using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;
using XRMultiplayer;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Hands;
using UnityEngine.UI;
using System.Collections;

public class PlayerHealth : NetworkBehaviour
{
    [Header("Health Settings")]
    [SerializeField] private int maxHealth = 100;
    [SerializeField] private float minPunchSpeed = 1.2f;
    [SerializeField] private float maxPunchSpeed = 5f;
    [SerializeField] private float damageMultiplier = 2f;
    [SerializeField] private float hitCooldown = 0.25f;
    [SerializeField] private float comboResetTime = 1.25f;
    [SerializeField] private float comboHit1Multiplier = 1.2f;
    [SerializeField] private float comboHit2Multiplier = 1.25f;
    [SerializeField] private float comboHit3Multiplier = 1.33f;
    [SerializeField] private float bodyHeight = 1.8f;
    [SerializeField] private float bodyRadius = 0.32f;
    [SerializeField, Min(0.5f)] private float boxingSpawnHalfSpacing = 1.5f;
    [SerializeField, Min(1f)] private float deathScreenSeconds = 5f;

    private static readonly Color[] PlayerColors =
    {
        new Color(0.12f, 0.55f, 1f), new Color(1f, 0.25f, 0.18f),
        new Color(0.25f, 0.85f, 0.38f), new Color(1f, 0.68f, 0.12f),
        new Color(0.72f, 0.32f, 1f), new Color(0.12f, 0.85f, 0.82f)
    };
    private static readonly List<PlayerHealth> ActivePlayers = new();
    private static PlayerHealth localPlayer;

    private NetworkVariable<Color> playerColor = new(Color.white,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private Transform headPose;
    private Transform leftHandPose;
    private Transform rightHandPose;
    private XROrigin xrOrigin;
    private XRInputModalityManager modalityManager;
    private Renderer capsuleRenderer;
    private GameObject healthHud;
    private GameObject deathScreen;
    private Text deathTitle;
    private Text deathDetails;
    private Text combatFeedbackText;
    private Coroutine combatFeedbackRoutine;
    private AudioSource combatAudioSource;
    private static AudioClip punchHitClip;
    private static AudioClip multiplierClip;
    private static AudioClip knockoutClip;
    private readonly List<Image> healthBars = new();
    private readonly List<Text> healthLabels = new();

    private readonly Dictionary<ulong,float> lastHitTimes = new();
    private readonly Dictionary<ulong,int> attackerComboCounts = new();
    private XRHandPoseReplicator localHandPoseReplicator;
    private Transform previousLeftHandOrigin;
    private Transform previousRightHandOrigin;
    private Transform sceneLeftGlove;
    private Transform sceneRightGlove;
    private bool roundResetScheduled;
    private static bool hasBoxingSpawnCenter;
    private static Vector3 boxingSpawnCenter;
    private static Quaternion boxingSpawnOrientation;

    // Synchronized variable across all connected clients
    public NetworkVariable<int> currentHealth = new NetworkVariable<int>(
        100,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public int MaxHealth => maxHealth;
    public Color PlayerColor => playerColor.Value;

    public override void OnNetworkSpawn()
    {
        if (!ActivePlayers.Contains(this)) ActivePlayers.Add(this);
        currentHealth.OnValueChanged += OnHealthChanged;
        playerColor.OnValueChanged += OnPlayerColorChanged;
        BuildCapsulePlayer();
        if (IsOwner) PositionAtBoxingSpawn();
        AddGloveHitboxes();
        EnsureCombatAudio();
        if (IsOwner)
        {
            localPlayer = this;
            CreateHealthHud();
            CreateDeathScreen();
            localHandPoseReplicator = GetComponent<XRHandPoseReplicator>();
        }
        if (IsServer)
        {
            currentHealth.Value = maxHealth;
            playerColor.Value = PlayerColors[(int)(OwnerClientId % (ulong)PlayerColors.Length)];
        }
        ApplyPlayerColor(playerColor.Value);
        RefreshHealthHud();
    }

    public override void OnNetworkDespawn()
    {
        currentHealth.OnValueChanged -= OnHealthChanged;
        playerColor.OnValueChanged -= OnPlayerColorChanged;
        ActivePlayers.Remove(this);
        if (localPlayer == this)
        {
            localPlayer = null;
            if (healthHud != null) Destroy(healthHud);
            if (deathScreen != null) Destroy(deathScreen);
        }
        RefreshHealthHud();
    }

    private void OnHealthChanged(int previousValue, int newValue)
    {
        RefreshHealthHud();
    }

    private void OnPlayerColorChanged(Color previous, Color current) => ApplyPlayerColor(current);

    private void BuildCapsulePlayer()
    {
        // Keep the existing hand/glove hierarchies, colliders, and punch scripts intact.
        Transform modelRoot = transform.Find("Midans mdol");
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            bool isModelRenderer = modelRoot != null &&
                (renderer.transform == modelRoot || renderer.transform.IsChildOf(modelRoot));
            bool keepVisible = isModelRenderer || (modelRoot == null && renderer.name == "Capsule Player Body");
            Transform child = renderer.transform;
            while (child != null && child != transform)
            {
                if (child.tag == "PlayerHand" || child.tag == "Glove" || child.name == "Left Hand" || child.name == "Right Hand")
                {
                    keepVisible = true;
                    break;
                }
                child = child.parent;
            }
            if (!keepVisible) renderer.enabled = false;
        }

        Transform existingBody = transform.Find("Capsule Player Body");
        GameObject body;
        if (existingBody != null)
        {
            body = existingBody.gameObject;
            capsuleRenderer = existingBody.GetComponent<Renderer>();
        }
        else
        {
            body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Capsule Player Body";
            body.transform.SetParent(transform, false);
            Collider bodyCollider = body.GetComponent<Collider>();
            if (bodyCollider != null) Destroy(bodyCollider); // Existing network hit collider stays in place.
            capsuleRenderer = body.GetComponent<Renderer>();
        }

        body.transform.localPosition = Vector3.up * (bodyHeight * 0.5f);
        body.transform.localScale = new Vector3(bodyRadius * 2f, bodyHeight * 0.5f, bodyRadius * 2f);

        headPose = FindChildNamed(transform, "Head");
        leftHandPose = FindChildNamed(transform, "Left Hand");
        rightHandPose = FindChildNamed(transform, "Right Hand");
        xrOrigin = FindFirstObjectByType<XROrigin>();
        if (xrOrigin != null) modalityManager = xrOrigin.GetComponent<XRInputModalityManager>();
    }

    private static Transform FindChildNamed(Transform root, string childName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == childName) return child;
        return null;
    }

    private void ApplyPlayerColor(Color color)
    {
        if (capsuleRenderer == null) return;
        bool usingModel = transform.Find("Midans mdol") != null;
        capsuleRenderer.enabled = !usingModel;
        if (!usingModel) capsuleRenderer.material.color = color;
    }

    private void UpdateNetworkAvatarPose()
    {
        if (!IsSpawned || !IsOwner || xrOrigin == null) return;
        if (transform.position != xrOrigin.transform.position || transform.rotation != xrOrigin.transform.rotation)
            transform.SetPositionAndRotation(xrOrigin.transform.position, xrOrigin.transform.rotation);

        if (headPose != null && xrOrigin.Camera != null)
            headPose.SetPositionAndRotation(xrOrigin.Camera.transform.position, xrOrigin.Camera.transform.rotation);

        if (modalityManager == null) return;
        bool handTracking = XRInputModalityManager.currentInputMode.Value == XRInputModalityManager.InputMode.TrackedHand;
        Transform left = handTracking ? GetHandSkeleton(modalityManager.leftHand) : GetTransform(modalityManager.leftController);
        Transform right = handTracking ? GetHandSkeleton(modalityManager.rightHand) : GetTransform(modalityManager.rightController);
        if (left != null && leftHandPose != null) leftHandPose.SetPositionAndRotation(left.position, left.rotation);
        if (right != null && rightHandPose != null) rightHandPose.SetPositionAndRotation(right.position, right.rotation);
    }

    private void PositionAtBoxingSpawn()
    {
        if (xrOrigin == null) return;

        // Cache the scene's starting pose once so reconnects don't keep pushing
        // the local rig farther away from the ring.
        if (!hasBoxingSpawnCenter)
        {
            boxingSpawnCenter = xrOrigin.transform.position;
            boxingSpawnOrientation = xrOrigin.transform.rotation;
            hasBoxingSpawnCenter = true;
        }

        Vector3 ringRight = boxingSpawnOrientation * Vector3.right;
        float side = OwnerClientId % 2 == 0 ? -1f : 1f;
        Vector3 spawnPosition = boxingSpawnCenter + ringRight * (side * boxingSpawnHalfSpacing);
        Vector3 towardCenter = boxingSpawnCenter - spawnPosition;
        Quaternion facing = towardCenter.sqrMagnitude > 0.001f
            ? Quaternion.LookRotation(towardCenter.normalized, Vector3.up)
            : boxingSpawnOrientation;

        xrOrigin.transform.SetPositionAndRotation(spawnPosition, facing);
        transform.SetPositionAndRotation(spawnPosition, facing);
    }

    private static Transform GetTransform(GameObject obj) => obj != null ? obj.transform : null;

    private static Transform GetHandSkeleton(GameObject obj)
    {
        XRHandSkeletonDriver driver = obj != null ? obj.GetComponentInChildren<XRHandSkeletonDriver>() : null;
        return driver != null ? driver.rootTransform : null;
    }

    private void CreateHealthHud()
    {
        if (healthHud != null) return;
        healthHud = new GameObject("Player Health HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = healthHud.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 120;
        CanvasScaler scaler = healthHud.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);

        GameObject panel = new GameObject("Health Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(healthHud.transform, false);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0, 1); panelRect.anchorMax = new Vector2(0, 1);
        panelRect.pivot = new Vector2(0, 1); panelRect.anchoredPosition = new Vector2(18, -18);
        panelRect.sizeDelta = new Vector2(320, 555);
        panel.GetComponent<Image>().color = new Color(0.03f, 0.04f, 0.08f, 0.82f);

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        for (int i = 0; i < 20; i++)
        {
            GameObject row = new GameObject($"Player {i} Health", typeof(RectTransform));
            row.transform.SetParent(panel.transform, false);
            RectTransform rowRect = row.GetComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0, 1); rowRect.anchorMax = new Vector2(1, 1);
            rowRect.pivot = new Vector2(0, 1); rowRect.anchoredPosition = new Vector2(12, -12 - i * 26);
            rowRect.sizeDelta = new Vector2(-24, 22);

            GameObject barBg = new GameObject("Bar Background", typeof(RectTransform), typeof(Image));
            barBg.transform.SetParent(row.transform, false);
            RectTransform bgRect = barBg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero; bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero; bgRect.offsetMax = Vector2.zero;
            barBg.GetComponent<Image>().color = new Color(0.12f, 0.14f, 0.18f, 1f);

            GameObject fillObj = new GameObject("Health Fill", typeof(RectTransform), typeof(Image));
            fillObj.transform.SetParent(barBg.transform, false);
            RectTransform fillRect = fillObj.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero; fillRect.anchorMax = new Vector2(1, 1);
            fillRect.pivot = new Vector2(0, 0.5f); fillRect.offsetMin = Vector2.zero; fillRect.offsetMax = Vector2.zero;
            Image fill = fillObj.GetComponent<Image>();
            fill.color = Color.green;
            healthBars.Add(fill);

            GameObject labelObj = new GameObject("Health Label", typeof(RectTransform), typeof(Text));
            labelObj.transform.SetParent(barBg.transform, false);
            RectTransform textRect = labelObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(5, 0); textRect.offsetMax = new Vector2(-5, 0);
            Text label = labelObj.GetComponent<Text>();
            label.font = font; label.fontSize = 16; label.alignment = TextAnchor.MiddleLeft; label.color = Color.white;
            healthLabels.Add(label);
        }

        GameObject feedbackObject = new GameObject("Combat Feedback", typeof(RectTransform), typeof(Text));
        feedbackObject.transform.SetParent(healthHud.transform, false);
        RectTransform feedbackRect = feedbackObject.GetComponent<RectTransform>();
        feedbackRect.anchorMin = new Vector2(0.15f, 0.58f);
        feedbackRect.anchorMax = new Vector2(0.85f, 0.72f);
        feedbackRect.offsetMin = Vector2.zero;
        feedbackRect.offsetMax = Vector2.zero;
        combatFeedbackText = feedbackObject.GetComponent<Text>();
        combatFeedbackText.font = font;
        combatFeedbackText.fontSize = 40;
        combatFeedbackText.fontStyle = FontStyle.Bold;
        combatFeedbackText.alignment = TextAnchor.MiddleCenter;
        combatFeedbackText.color = new Color(1f, 0.82f, 0.28f, 0f);
        combatFeedbackText.horizontalOverflow = HorizontalWrapMode.Wrap;
        combatFeedbackText.verticalOverflow = VerticalWrapMode.Overflow;
        combatFeedbackText.text = string.Empty;
        RefreshHealthHud();
    }

    private void EnsureCombatAudio()
    {
        if (combatAudioSource == null) combatAudioSource = GetComponent<AudioSource>();
        if (combatAudioSource == null) combatAudioSource = gameObject.AddComponent<AudioSource>();
        combatAudioSource.playOnAwake = false;
        combatAudioSource.spatialBlend = 0.65f;
        combatAudioSource.dopplerLevel = 0f;
        combatAudioSource.minDistance = 1.2f;
        combatAudioSource.maxDistance = 24f;
        combatAudioSource.rolloffMode = AudioRolloffMode.Logarithmic;

        if (punchHitClip == null) punchHitClip = Resources.Load<AudioClip>("BoxingAudio/PunchHeavy");
        if (multiplierClip == null) multiplierClip = Resources.Load<AudioClip>("BoxingAudio/MultiplierImpact");
        if (knockoutClip == null) knockoutClip = Resources.Load<AudioClip>("BoxingAudio/KnockoutThud");
    }

    private void PlayCombatOneShot(AudioClip clip, float volume)
    {
        if (clip == null) return;
        EnsureCombatAudio();
        combatAudioSource.PlayOneShot(clip, volume);
    }

    private void ShowCombatFeedback(string message, Color color)
    {
        if (combatFeedbackText == null) return;
        if (combatFeedbackRoutine != null) StopCoroutine(combatFeedbackRoutine);
        combatFeedbackText.text = message;
        color.a = 1f;
        combatFeedbackText.color = color;
        combatFeedbackRoutine = StartCoroutine(FadeCombatFeedback(color, 0.9f));
    }

    private IEnumerator FadeCombatFeedback(Color color, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration && combatFeedbackText != null)
        {
            elapsed += Time.deltaTime;
            color.a = 1f - Mathf.Clamp01(elapsed / duration);
            combatFeedbackText.color = color;
            yield return null;
        }
        if (combatFeedbackText != null) combatFeedbackText.text = string.Empty;
        combatFeedbackRoutine = null;
    }

    private void CreateDeathScreen()
    {
        if (deathScreen != null) return;

        deathScreen = new GameObject("Death Screen", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = deathScreen.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        CanvasScaler scaler = deathScreen.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);

        GameObject overlay = new GameObject("Overlay", typeof(RectTransform), typeof(Image));
        overlay.transform.SetParent(deathScreen.transform, false);
        RectTransform overlayRect = overlay.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        overlay.GetComponent<Image>().color = new Color(0.015f, 0.02f, 0.035f, 0.88f);

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        deathTitle = CreateDeathText("Result", overlay.transform, font, 54, FontStyle.Bold, new Vector2(0.1f, 0.53f), new Vector2(0.9f, 0.72f));
        deathDetails = CreateDeathText("Criteria", overlay.transform, font, 24, FontStyle.Normal, new Vector2(0.1f, 0.34f), new Vector2(0.9f, 0.52f));
        deathScreen.SetActive(false);
    }

    private static Text CreateDeathText(string objectName, Transform parent, Font font, int size, FontStyle style, Vector2 anchorMin, Vector2 anchorMax)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        Text text = textObject.GetComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private void ShowDeathScreen(bool defeated)
    {
        if (deathScreen == null) CreateDeathScreen();
        if (deathTitle != null) deathTitle.text = defeated ? "KNOCKED OUT" : "YOU WIN";
        if (deathDetails != null)
            deathDetails.text = defeated
                ? $"Knockout: health reached 0 from valid punches (1.2–5.0 m/s).\nRound restarts in {deathScreenSeconds:0} seconds."
                : $"Opponent reached 0 health from valid punches.\nRound restarts in {deathScreenSeconds:0} seconds.";
        deathScreen.SetActive(true);
    }

    private void HideDeathScreen()
    {
        if (deathScreen != null) deathScreen.SetActive(false);
    }

    private static void RefreshHealthHud()
    {
        if (localPlayer == null || localPlayer.healthBars.Count == 0) return;
        List<PlayerHealth> players = ActivePlayers.FindAll(player => player != null && player.IsSpawned);
        for (int i = 0; i < localPlayer.healthBars.Count; i++)
        {
            bool visible = i < players.Count;
            localPlayer.healthBars[i].transform.parent.gameObject.SetActive(visible);
            if (!visible) continue;
            PlayerHealth player = players[i];
            float fraction = player.maxHealth > 0 ? Mathf.Clamp01((float)player.currentHealth.Value / player.maxHealth) : 0f;
            RectTransform fillRect = localPlayer.healthBars[i].rectTransform;
            fillRect.anchorMax = new Vector2(fraction, 1f);
            localPlayer.healthBars[i].color = Color.Lerp(Color.red, Color.green, fraction);
            localPlayer.healthLabels[i].text = $"P{player.OwnerClientId}  {player.currentHealth.Value}/{player.maxHealth} HP";
        }
    }

    private int AddGloveHitboxes()
    {
        int hitboxCount = 0;
        foreach (Collider playerCollider in GetComponentsInChildren<Collider>(true))
        {
            if (!playerCollider.CompareTag("PlayerHand"))
            {
                continue;
            }

            if (playerCollider.GetComponent<GloveHitbox>() == null)
            {
                playerCollider.gameObject.AddComponent<GloveHitbox>();
            }

            hitboxCount++;
        }

        return hitboxCount;
    }

    private void LateUpdate()
    {
        if (!IsSpawned || !IsOwner)
        {
            return;
        }

        UpdateNetworkAvatarPose();

        if (localHandPoseReplicator == null)
        {
            localHandPoseReplicator = GetComponent<XRHandPoseReplicator>();
        }

        Transform leftHandOrigin = localHandPoseReplicator != null ? localHandPoseReplicator.LeftHandOrigin : null;
        Transform rightHandOrigin = localHandPoseReplicator != null ? localHandPoseReplicator.RightHandOrigin : null;

        bool usingExistingGloveColliders = false;
        if (leftHandOrigin == null && rightHandOrigin == null)
        {
            XRInputModalityManager modalityManager = null;
            XROrigin xrOrigin = FindFirstObjectByType<XROrigin>();
            if (xrOrigin != null)
            {
                modalityManager = xrOrigin.GetComponent<XRInputModalityManager>();
            }

            if (modalityManager != null && XRInputModalityManager.currentInputMode.Value == XRInputModalityManager.InputMode.TrackedHand)
            {
                leftHandOrigin = modalityManager.leftHand != null ? modalityManager.leftHand.transform : null;
                rightHandOrigin = modalityManager.rightHand != null ? modalityManager.rightHand.transform : null;
            }

            if (leftHandOrigin == null && rightHandOrigin == null && modalityManager != null)
            {
                leftHandOrigin = modalityManager.leftController != null ? modalityManager.leftController.transform : null;
                rightHandOrigin = modalityManager.rightController != null ? modalityManager.rightController.transform : null;
            }

            if (leftHandOrigin == null && rightHandOrigin == null)
            {
                FindSceneGloves(out leftHandOrigin, out rightHandOrigin);
                usingExistingGloveColliders = leftHandOrigin != null || rightHandOrigin != null;
            }
        }

        if (leftHandOrigin == null && rightHandOrigin == null)
        {
            return;
        }

        if (!usingExistingGloveColliders)
        {
            EnsureTrackedHandHitbox(ref previousLeftHandOrigin, leftHandOrigin, "Punch Hitbox Left");
            EnsureTrackedHandHitbox(ref previousRightHandOrigin, rightHandOrigin, "Punch Hitbox Right");
        }
        else
        {
            previousLeftHandOrigin = leftHandOrigin;
            previousRightHandOrigin = rightHandOrigin;
        }

    }

    private void FindSceneGloves(out Transform leftGlove, out Transform rightGlove)
    {
        if (sceneLeftGlove == null || sceneRightGlove == null)
        {
            sceneLeftGlove = null;
            sceneRightGlove = null;
            foreach (GameObject glove in GameObject.FindGameObjectsWithTag("Glove"))
            {
                if (glove.GetComponentInParent<XROrigin>() == null)
                {
                    continue;
                }

                string gloveName = glove.name.ToLowerInvariant();
                if (gloveName.Contains("left"))
                {
                    sceneLeftGlove = glove.transform;
                }
                else if (gloveName.Contains("right"))
                {
                    sceneRightGlove = glove.transform;
                }
            }
        }

        leftGlove = sceneLeftGlove;
        rightGlove = sceneRightGlove;
    }

    private static void EnsureTrackedHandHitbox(ref Transform previousOrigin, Transform currentOrigin, string hitboxName)
    {
        if (previousOrigin != currentOrigin)
        {
            if (previousOrigin != null)
            {
                Transform oldHitbox = previousOrigin.Find(hitboxName);
                if (oldHitbox != null)
                {
                    oldHitbox.gameObject.SetActive(false);
                }
            }

            previousOrigin = currentOrigin;
        }

        if (currentOrigin == null)
        {
            return;
        }

        Transform hitboxTransform = currentOrigin.Find(hitboxName);
        if (hitboxTransform == null)
        {
            GameObject hitboxObject = new GameObject(hitboxName, typeof(SphereCollider));
            hitboxTransform = hitboxObject.transform;
            hitboxTransform.SetParent(currentOrigin, false);
            hitboxTransform.localPosition = Vector3.zero;
            hitboxTransform.localRotation = Quaternion.identity;
            hitboxTransform.localScale = Vector3.one;

            SphereCollider hitboxCollider = hitboxObject.GetComponent<SphereCollider>();
            hitboxCollider.radius = 0.1f;
            hitboxCollider.isTrigger = true;
            hitboxObject.AddComponent<GloveHitbox>();
        }
        else if (!hitboxTransform.gameObject.activeSelf)
        {
            hitboxTransform.gameObject.SetActive(true);
        }
    }

    // Called by glove hit detection; executes only on the Server/Host
    [ServerRpc(RequireOwnership = false)]
    public void ReportPunchAttemptServerRpc(float reportedPunchSpeed, ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;

        ulong attackerClientId = rpcParams.Receive.SenderClientId;

        if (attackerClientId == OwnerClientId) return;

        float currentTime = Time.time;
        if (lastHitTimes.TryGetValue(attackerClientId, out float lastHitTime) && currentTime - lastHitTime < hitCooldown)
        {
            return;
        }

        if (currentHealth.Value <= 0)
        {
            return;
        }

        if (float.IsNaN(reportedPunchSpeed) ||
            float.IsInfinity(reportedPunchSpeed) ||
            reportedPunchSpeed < minPunchSpeed)
        {
            return;
        }

        if (reportedPunchSpeed > maxPunchSpeed)
        {
            return;
        }

        int comboCount = 1;
        if (attackerComboCounts.TryGetValue(attackerClientId, out int previousComboCount) &&
            lastHitTimes.TryGetValue(attackerClientId, out float previousHitTime) &&
            currentTime - previousHitTime <= comboResetTime)
        {
            comboCount = previousComboCount >= 3 ? 1 : previousComboCount + 1;
        }

        attackerComboCounts[attackerClientId] = comboCount;
        float comboMultiplier = comboCount == 1 ? comboHit1Multiplier :
            comboCount == 2 ? comboHit2Multiplier : comboHit3Multiplier;
        int damage = Mathf.RoundToInt(reportedPunchSpeed * damageMultiplier * comboMultiplier);
        lastHitTimes[attackerClientId] = currentTime;
        int previousHealth = currentHealth.Value;

        currentHealth.Value = Mathf.Max(0, currentHealth.Value - damage);
        bool knockedOut = previousHealth > 0 && currentHealth.Value == 0;
        HitFeedbackClientRpc(attackerClientId, comboCount, damage, comboMultiplier, reportedPunchSpeed, knockedOut);

        if (knockedOut)
        {
            TriggerKnockoutClientRpc(OwnerClientId);
            if (!roundResetScheduled)
            {
                roundResetScheduled = true;
                StartCoroutine(ResetRoundAfterKnockout());
            }
        }
    }

    [ClientRpc]
    private void HitFeedbackClientRpc(ulong attackerClientId, int comboCount, int damage, float comboMultiplier, float punchSpeed, bool knockedOut)
    {
        if (!knockedOut)
        {
            float hitVolume = Mathf.Lerp(0.55f, 0.9f, Mathf.InverseLerp(minPunchSpeed, maxPunchSpeed, punchSpeed));
            PlayCombatOneShot(punchHitClip, hitVolume);
        }

        if (localPlayer == null) return;
        if (localPlayer.OwnerClientId == OwnerClientId)
        {
            localPlayer.ShowCombatFeedback($"-{damage} HP", new Color(1f, 0.42f, 0.32f));
        }
        else if (localPlayer.OwnerClientId == attackerClientId && comboCount > 1)
        {
            float multiplierVolume = comboCount == 2 ? 0.68f : 0.95f;
            localPlayer.PlayCombatOneShot(multiplierClip, multiplierVolume);
            localPlayer.ShowCombatFeedback($"COMBO x{comboCount}  ·  {comboMultiplier:0.##}x DAMAGE", new Color(1f, 0.82f, 0.28f));
        }
    }

    [ClientRpc]
    private void TriggerKnockoutClientRpc(ulong defeatedClientId)
    {
        PlayCombatOneShot(knockoutClip, 0.95f);
        if (localPlayer != null)
            localPlayer.ShowDeathScreen(localPlayer.OwnerClientId == defeatedClientId);
    }

    private IEnumerator ResetRoundAfterKnockout()
    {
        yield return new WaitForSeconds(deathScreenSeconds);

        foreach (PlayerHealth player in ActivePlayers)
        {
            if (player == null || !player.IsSpawned) continue;
            player.currentHealth.Value = player.maxHealth;
            player.lastHitTimes.Clear();
            player.attackerComboCounts.Clear();
            player.roundResetScheduled = false;
        }

        ResetDeathScreensClientRpc();
    }

    [ClientRpc]
    private void ResetDeathScreensClientRpc()
    {
        if (localPlayer != null) localPlayer.HideDeathScreen();
    }
}

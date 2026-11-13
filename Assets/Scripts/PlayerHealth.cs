using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;
using XRMultiplayer;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Hands;
using UnityEngine.UI;
using System.Collections;
using TMPro;

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
    private Button restartRoundButton;
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
    private static bool roundOver;
    private static bool hasBoxingSpawnCenter;
    private static Vector3 boxingSpawnCenter;
    private static Quaternion boxingSpawnOrientation;

    private Transform localLeftHandSource;
    private Transform localRightHandSource;
    private Transform modelHeadBone;
    private Image overheadHealthBarFill;
    private TMP_Text overheadHealthBarTMP;
    private Text overheadHealthBarText;

    // Synchronized variable across all connected clients
    public NetworkVariable<int> currentHealth = new NetworkVariable<int>(
        100,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public int MaxHealth => maxHealth;
    public Color PlayerColor => playerColor.Value;
    public static bool RoundOver => roundOver;

    private void Awake()
    {
        EnsureAvatarHierarchyIntegrity();
    }

    private void EnsureAvatarHierarchyIntegrity()
    {
        Transform leftHand = transform.Find("Left Hand") ?? FindChildNamed(transform, "Left Hand");
        if (leftHand != null && leftHand.parent != transform)
        {
            leftHand.SetParent(transform, true);
            leftHand.localScale = Vector3.one;
        }

        Transform rightHand = transform.Find("Right Hand") ?? FindChildNamed(transform, "Right Hand");
        if (rightHand != null && rightHand.parent != transform)
        {
            rightHand.SetParent(transform, true);
            rightHand.localScale = Vector3.one;
        }
    }

    public override void OnNetworkSpawn()
    {
        EnsureAvatarHierarchyIntegrity();
        if (ActivePlayers.Count == 0)
            roundOver = false;
        if (!ActivePlayers.Contains(this)) ActivePlayers.Add(this);
        currentHealth.OnValueChanged += OnHealthChanged;
        playerColor.OnValueChanged += OnPlayerColorChanged;
        BuildCapsulePlayer();
        SetupFirstPersonView();
        SetupOverheadHealthBar();
        if (IsOwner) PositionAtBoxingSpawn();
        AddGloveHitboxes();
        EnsureCombatAudio();
        if (IsOwner)
        {
            localPlayer = this;
            CreateHealthHud();
            CreateDeathScreen();
            localHandPoseReplicator = GetComponent<XRHandPoseReplicator>();
            desktopController = GetComponent<DesktopPlayerController>();
            if (desktopController != null)
            {
                desktopController.EvaluateActivation();
            }
        }
        if (IsServer)
        {
            currentHealth.Value = maxHealth;
            playerColor.Value = PlayerColors[(int)(OwnerClientId % (ulong)PlayerColors.Length)];
        }
        ApplyPlayerColor(playerColor.Value);
        RefreshHealthHud();
        UpdateOverheadHealthBar();
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
        UpdateOverheadHealthBar();
        RefreshHealthHud();
    }

    private void OnPlayerColorChanged(Color previous, Color current) => ApplyPlayerColor(current);

    private void BuildCapsulePlayer()
    {
        EnsureAvatarHierarchyIntegrity();

        // 1. Permanently hide and disable template network hands to eliminate purple missing-shader hands
        string[] templateHandNames = new string[] {
            "Hand_Left_Networked", "Hand_Right_Networked",
            "Left Hand Networked", "Right Hand Networked",
            "LeftHand", "RightHand",
            "L_Wrist", "R_Wrist", "L_Wrist - Quest", "R_Wrist - Quest",
            "Quest", "AXR"
        };

        foreach (string tName in templateHandNames)
        {
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t != transform && t.name.Equals(tName, System.StringComparison.OrdinalIgnoreCase))
                {
                    // Never disable the character model or its bones
                    if (t.name.StartsWith("mixamorig") || t.name.Contains("Midans")) continue;
                    foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
                    {
                        r.enabled = false;
                    }
                    t.gameObject.SetActive(false);
                }
            }
        }

        // 2. Only show Midans mdol character model (and gloves if present)
        Transform modelRoot = transform.Find("Midans mdol");
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            bool isModelRenderer = modelRoot != null &&
                (renderer.transform == modelRoot || renderer.transform.IsChildOf(modelRoot));
            bool isGloveRenderer = renderer.CompareTag("Glove") && (modelRoot == null || !renderer.transform.IsChildOf(modelRoot));

            // Ensure no template hand renderers slip through
            bool isTemplateHand = false;
            Transform p = renderer.transform;
            while (p != null && p != transform)
            {
                string pName = p.name.ToLowerInvariant();
                if (pName.Contains("networked") || pName.Contains("wrist") || pName.Contains("quest") || pName == "axr")
                {
                    isTemplateHand = true;
                    break;
                }
                p = p.parent;
            }

            if (isTemplateHand)
            {
                renderer.enabled = false;
            }
            else
            {
                renderer.enabled = isModelRenderer || isGloveRenderer;
            }
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
        ScaleModelToTargetHeight(modelRoot);
        AlignModelFeetToOrigin(modelRoot);
        AlignBodyHitboxToModel(modelRoot);

        headPose = FindChildNamed(transform, "Head");
        leftHandPose = FindChildNamed(transform, "Left Hand");
        rightHandPose = FindChildNamed(transform, "Right Hand");
        xrOrigin = FindFirstObjectByType<XROrigin>();
        if (xrOrigin != null) modalityManager = xrOrigin.GetComponent<XRInputModalityManager>();
        ResolveLocalHandSources();
        SetupFirstPersonView();
        SetupOverheadHealthBar();
    }

    private void AlignBodyHitboxToModel(Transform modelRoot)
    {
        CapsuleCollider bodyHitbox = GetComponent<CapsuleCollider>();
        if (bodyHitbox == null) return;

        bodyHitbox.direction = 1;
        if (modelRoot == null)
        {
            bodyHitbox.height = bodyHeight;
            bodyHitbox.center = Vector3.up * (bodyHeight * 0.5f);
            return;
        }

        if (!TryGetModelBounds(modelRoot, out Bounds modelBounds) || modelBounds.size.y <= 0.01f) return;

        float modelHeight = transform.InverseTransformVector(Vector3.up * modelBounds.size.y).magnitude;
        Vector3 modelCenter = transform.InverseTransformPoint(modelBounds.center);
        bodyHitbox.height = modelHeight;
        bodyHitbox.center = modelCenter;
        bodyHitbox.radius = Mathf.Min(bodyRadius, modelHeight * 0.5f);
    }

    private void AlignModelFeetToOrigin(Transform modelRoot)
    {
        if (modelRoot == null || !TryGetModelBounds(modelRoot, out Bounds modelBounds)) return;

        float floorY = transform.position.y;
        float feetOffset = floorY - modelBounds.min.y;
        if (Mathf.Abs(feetOffset) > 0.001f)
            modelRoot.position += Vector3.up * feetOffset;
    }

    private void ScaleModelToTargetHeight(Transform modelRoot)
    {
        if (modelRoot == null || !TryGetModelBounds(modelRoot, out Bounds modelBounds) || modelBounds.size.y <= 0.01f)
            return;

        float scaleFactor = bodyHeight / modelBounds.size.y;
        if (Mathf.Abs(scaleFactor - 1f) > 0.001f)
            modelRoot.localScale *= scaleFactor;
    }

    private static bool TryGetModelBounds(Transform modelRoot, out Bounds modelBounds)
    {
        modelBounds = default;
        if (modelRoot == null) return false;

        bool hasBounds = false;
        foreach (Renderer modelRenderer in modelRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (!modelRenderer.enabled) continue;
            if (!hasBounds)
            {
                modelBounds = modelRenderer.bounds;
                hasBounds = true;
            }
            else
            {
                modelBounds.Encapsulate(modelRenderer.bounds);
            }
        }

        return hasBounds;
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

    private DesktopPlayerController desktopController;

    private void UpdateNetworkAvatarPose()
    {
        if (!IsSpawned || !IsOwner) return;

        if (desktopController == null) desktopController = GetComponent<DesktopPlayerController>();
        if (desktopController != null && desktopController.IsActive) return;

        if (xrOrigin == null) return;

        // 1. First-Person Avatar Alignment:
        // Position avatar body directly on the floor below the VR headset camera
        if (xrOrigin.Camera != null)
        {
            Vector3 camPos = xrOrigin.Camera.transform.position;
            Vector3 groundPos = new Vector3(camPos.x, xrOrigin.transform.position.y, camPos.z);
            Vector3 camForward = Vector3.ProjectOnPlane(xrOrigin.Camera.transform.forward, Vector3.up).normalized;
            if (camForward.sqrMagnitude > 0.001f)
            {
                transform.SetPositionAndRotation(groundPos, Quaternion.LookRotation(camForward, Vector3.up));
            }
            else
            {
                transform.position = groundPos;
            }

            if (headPose != null)
            {
                headPose.SetPositionAndRotation(camPos, xrOrigin.Camera.transform.rotation);
            }
        }

        // 2. Real-time Hand / Punch Tracking from VR controllers
        if (localLeftHandSource == null || localRightHandSource == null)
        {
            ResolveLocalHandSources();
        }

        if (localLeftHandSource != null && leftHandPose != null)
        {
            leftHandPose.SetPositionAndRotation(localLeftHandSource.position, localLeftHandSource.rotation);
        }
        if (localRightHandSource != null && rightHandPose != null)
        {
            rightHandPose.SetPositionAndRotation(localRightHandSource.position, localRightHandSource.rotation);
        }
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
        combatAudioSource.spatialBlend = 0.15f; // Mostly 2D stereo in VR headset for high loudness and clarity
        combatAudioSource.dopplerLevel = 0f;
        combatAudioSource.minDistance = 3.5f;
        combatAudioSource.maxDistance = 35f;
        combatAudioSource.rolloffMode = AudioRolloffMode.Logarithmic;

        if (punchHitClip == null) punchHitClip = Resources.Load<AudioClip>("BoxingAudio/PunchHeavy");
        if (multiplierClip == null) multiplierClip = Resources.Load<AudioClip>("BoxingAudio/MultiplierImpact");
        if (knockoutClip == null) knockoutClip = Resources.Load<AudioClip>("BoxingAudio/KnockoutThud");
    }

    private void PlayCombatOneShot(AudioClip clip, float volume)
    {
        if (clip == null) return;
        EnsureCombatAudio();
        float boostedVolume = Mathf.Clamp01(volume * 1.5f);
        combatAudioSource.PlayOneShot(clip, boostedVolume);
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

        GameObject restartButtonObject = new GameObject("Restart Round", typeof(RectTransform), typeof(Image), typeof(Button));
        restartButtonObject.transform.SetParent(overlay.transform, false);
        RectTransform restartButtonRect = restartButtonObject.GetComponent<RectTransform>();
        restartButtonRect.anchorMin = new Vector2(0.35f, 0.16f);
        restartButtonRect.anchorMax = new Vector2(0.65f, 0.27f);
        restartButtonRect.offsetMin = Vector2.zero;
        restartButtonRect.offsetMax = Vector2.zero;
        Image restartButtonImage = restartButtonObject.GetComponent<Image>();
        restartButtonImage.color = new Color(0.12f, 0.55f, 1f, 1f);
        restartRoundButton = restartButtonObject.GetComponent<Button>();
        restartRoundButton.onClick.AddListener(RequestRestartRound);

        GameObject restartLabelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
        restartLabelObject.transform.SetParent(restartButtonObject.transform, false);
        RectTransform restartLabelRect = restartLabelObject.GetComponent<RectTransform>();
        restartLabelRect.anchorMin = Vector2.zero;
        restartLabelRect.anchorMax = Vector2.one;
        restartLabelRect.offsetMin = Vector2.zero;
        restartLabelRect.offsetMax = Vector2.zero;
        Text restartLabel = restartLabelObject.GetComponent<Text>();
        restartLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        restartLabel.fontSize = 26;
        restartLabel.fontStyle = FontStyle.Bold;
        restartLabel.alignment = TextAnchor.MiddleCenter;
        restartLabel.color = Color.white;
        restartLabel.text = "RESTART ROUND";

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
        if (VRBoxingUI.Instance != null)
        {
            VRBoxingUI.Instance.ShowKnockoutAnnouncement(!defeated, deathScreenSeconds);
        }

        if (deathScreen == null) CreateDeathScreen();
        if (deathTitle != null) deathTitle.text = defeated ? "KNOCKED OUT" : "YOU WIN";
        if (deathDetails != null)
            deathDetails.text = defeated
                ? "Knockout: health reached 0 from a valid punch."
                : "Opponent reached 0 health from a valid punch.";
        if (restartRoundButton != null)
            restartRoundButton.interactable = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        deathScreen.SetActive(true);
    }

    private void HideDeathScreen()
    {
        if (deathScreen != null) deathScreen.SetActive(false);
    }

    public void RequestRestartRound()
    {
        if (!IsOwner || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            return;

        RestartRoundServerRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RestartRoundServerRpc()
    {
        if (!IsServer || !roundOver)
            return;

        foreach (PlayerHealth player in ActivePlayers)
        {
            if (player == null || !player.IsSpawned)
                continue;

            player.currentHealth.Value = player.maxHealth;
            player.lastHitTimes.Clear();
            player.attackerComboCounts.Clear();
        }

        roundOver = false;
        SetLocomotionEnabled(true);
        ResetRoundClientRpc();
    }

    [ClientRpc]
    private void ResetRoundClientRpc()
    {
        roundOver = false;
        SetLocomotionEnabled(true);
        if (localPlayer != null)
        {
            localPlayer.HideDeathScreen();
            if (VRBoxingUI.Instance != null)
                VRBoxingUI.Instance.HideKnockoutAnnouncement();
        }
    }

    private static void SetLocomotionEnabled(bool enabled)
    {
        var moveProvider = FindFirstObjectByType<UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets.DynamicMoveProvider>();
        if (moveProvider != null)
            moveProvider.enabled = enabled;
    }

    private static void RefreshHealthHud()
    {
        List<PlayerHealth> players = ActivePlayers.FindAll(player => player != null && player.IsSpawned);
        foreach (PlayerHealth p in players)
        {
            if (VRBoxingUI.Instance != null)
            {
                VRBoxingUI.Instance.UpdatePlayerHealth(p.OwnerClientId, p.currentHealth.Value, p.maxHealth);
            }
        }

        if (localPlayer == null || localPlayer.healthBars.Count == 0) return;
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

        if (roundOver)
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
            if (localLeftHandSource != null || localRightHandSource != null)
            {
                leftHandOrigin = localLeftHandSource;
                rightHandOrigin = localRightHandSource;
            }
            else
            {
                FindSceneGloves(out leftHandOrigin, out rightHandOrigin);
            }
            usingExistingGloveColliders = leftHandOrigin != null || rightHandOrigin != null;
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
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ReportPunchAttemptServerRpc(float reportedPunchSpeed, RpcParams rpcParams = default)
    {
        if (!IsServer) return;

        ulong attackerClientId = rpcParams.Receive.SenderClientId;

        if (attackerClientId == OwnerClientId) return;
        if (roundOver) return;

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
            roundOver = true;
            SetLocomotionEnabled(false);
            TriggerKnockoutClientRpc(OwnerClientId);
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
            VRHaptics.TriggerDamageImpulse(damage);
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
        roundOver = true;
        SetLocomotionEnabled(false);
        PlayCombatOneShot(knockoutClip, 0.95f);
        if (localPlayer != null)
        {
            if (localPlayer.OwnerClientId == defeatedClientId)
            {
                VRHaptics.TriggerKnockoutImpulse();
            }
            localPlayer.ShowDeathScreen(localPlayer.OwnerClientId == defeatedClientId);
        }
    }

    private void ResolveLocalHandSources()
    {
        if (xrOrigin == null) xrOrigin = FindFirstObjectByType<XROrigin>();
        if (xrOrigin == null) return;

        foreach (Transform t in xrOrigin.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name.ToLowerInvariant();
            if (localLeftHandSource == null)
            {
                if (n == "glove_left" || n == "left controller" || n == "left hand" || n.Contains("lefthand"))
                {
                    localLeftHandSource = t;
                }
            }
            if (localRightHandSource == null)
            {
                if (n == "glove_right" || n == "right controller" || n == "right hand" || n.Contains("righthand"))
                {
                    localRightHandSource = t;
                }
            }
        }
    }

    private void SetupFirstPersonView()
    {
        if (modelHeadBone == null)
        {
            Transform modelRoot = transform.Find("Midans mdol");
            if (modelRoot != null)
            {
                modelHeadBone = FindChildRecursive(modelRoot, "mixamorig:Head");
            }
            if (modelHeadBone == null)
            {
                modelHeadBone = FindChildRecursive(transform, "mixamorig:Head");
            }
        }

        if (modelHeadBone != null)
        {
            if (IsOwner)
            {
                // Local player: hide head bone mesh so it doesn't block the VR camera view
                modelHeadBone.localScale = Vector3.zero;
            }
            else
            {
                // Remote player: ensure head is visible to opponents
                modelHeadBone.localScale = Vector3.one;
            }
        }
    }

    private void SetupOverheadHealthBar()
    {
        Transform headCanvas = transform.Find("Head/Canvas");
        if (headCanvas == null)
        {
            Transform head = FindChildNamed(transform, "Head");
            if (head != null) headCanvas = head.GetComponentInChildren<Canvas>(true)?.transform;
        }

        if (headCanvas == null) return;

        // Deactivate useless elements
        string[] uselessNames = new string[] {
            "Voice Particles Left",
            "Voice Particles Right",
            "Profile",
            "Mute Button",
            "Voice Chat Icon"
        };
        foreach (string uName in uselessNames)
        {
            Transform uselessObj = FindChildRecursive(headCanvas, uName);
            if (uselessObj != null) uselessObj.gameObject.SetActive(false);
        }

        // Look for Health Bar under Layout Group or Canvas
        Transform healthBarTransform = FindChildRecursive(headCanvas, "Health Bar") ?? FindChildRecursive(headCanvas, "Overhead_HealthBar");
        if (healthBarTransform != null)
        {
            healthBarTransform.gameObject.SetActive(true);
            Transform fillTransform = FindChildRecursive(healthBarTransform, "Fill");
            if (fillTransform != null) overheadHealthBarFill = fillTransform.GetComponent<Image>();

            Transform textTransform = FindChildRecursive(healthBarTransform, "HP Text") ?? FindChildRecursive(healthBarTransform, "HP_Text");
            if (textTransform != null)
            {
                overheadHealthBarTMP = textTransform.GetComponent<TMP_Text>();
                overheadHealthBarText = textTransform.GetComponent<Text>();
            }
        }
        else
        {
            // Dynamically construct health bar as fallback
            Transform layoutGroup = FindChildRecursive(headCanvas, "Layout Group");
            Transform parentForBar = layoutGroup != null ? layoutGroup : headCanvas;

            GameObject barObj = new GameObject("Overhead_HealthBar", typeof(RectTransform));
            barObj.transform.SetParent(parentForBar, false);
            RectTransform barRect = barObj.GetComponent<RectTransform>();
            barRect.sizeDelta = new Vector2(200f, 26f);

            GameObject bgObj = new GameObject("Bg", typeof(RectTransform), typeof(Image));
            bgObj.transform.SetParent(barObj.transform, false);
            RectTransform bgRect = bgObj.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero; bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;
            Image bgImg = bgObj.GetComponent<Image>();
            bgImg.color = new Color(0.08f, 0.1f, 0.14f, 0.9f);

            GameObject fillObj = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillObj.transform.SetParent(bgObj.transform, false);
            RectTransform fillRect = fillObj.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero; fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(2, 2); fillRect.offsetMax = new Vector2(-2, -2);
            fillRect.pivot = new Vector2(0f, 0.5f);
            overheadHealthBarFill = fillObj.GetComponent<Image>();
            overheadHealthBarFill.type = Image.Type.Filled;
            overheadHealthBarFill.fillMethod = Image.FillMethod.Horizontal;
            overheadHealthBarFill.fillAmount = 1f;
            overheadHealthBarFill.color = new Color(0.18f, 0.85f, 0.35f);

            GameObject textObj = new GameObject("HP_Text", typeof(RectTransform), typeof(Text));
            textObj.transform.SetParent(barObj.transform, false);
            RectTransform textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            overheadHealthBarText = textObj.GetComponent<Text>();
            overheadHealthBarText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            overheadHealthBarText.fontSize = 15;
            overheadHealthBarText.fontStyle = FontStyle.Bold;
            overheadHealthBarText.alignment = TextAnchor.MiddleCenter;
            overheadHealthBarText.color = Color.white;
        }

        UpdateOverheadHealthBar();
    }

    private void UpdateOverheadHealthBar()
    {
        float fraction = maxHealth > 0 ? Mathf.Clamp01((float)currentHealth.Value / maxHealth) : 0f;
        Color barColor = Color.Lerp(Color.red, Color.green, fraction);

        if (overheadHealthBarFill != null)
        {
            overheadHealthBarFill.fillAmount = fraction;
            overheadHealthBarFill.color = barColor;
        }
        string hpStr = $"{currentHealth.Value} / {maxHealth} HP";
        if (overheadHealthBarTMP != null) overheadHealthBarTMP.text = hpStr;
        if (overheadHealthBarText != null) overheadHealthBarText.text = hpStr;
    }

    private static Transform FindChildRecursive(Transform parent, string name)
    {
        if (parent == null) return null;
        if (parent.name == name) return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform found = FindChildRecursive(parent.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }
}

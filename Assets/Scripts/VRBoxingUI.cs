using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

/// <summary>
/// World-Space VR UI designed specifically for VR headsets.
/// Provides:
/// 1. 3D World-Space Start Menu (Host / Join) punchable with boxing gloves or clickable with VR controllers.
/// 2. 3D World-Space Health Display (Player 1 vs Player 2 health bars & HP counters).
/// 3. 3D World-Space Knockout / Winner Announcement Banner.
/// </summary>
public class VRBoxingUI : MonoBehaviour
{
    public static VRBoxingUI Instance { get; private set; }

    [Header("Network UI References")]
    [SerializeField] private SimpleNetworkUI simpleNetworkUI;

    [Header("Menu Positioning")]
    [SerializeField] private Vector3 menuOffset = new Vector3(0f, 1.35f, 1.25f);
    [SerializeField] private Vector3 scoreboardOffset = new Vector3(0f, 1.65f, 1.8f);
    [SerializeField] private Vector3 knockoutBannerOffset = new Vector3(0f, 1.45f, 1.4f);

    private GameObject vrMenuRoot;
    private Text vrStatusText;
    private GameObject hostBtnObj;
    private GameObject joinBtnObj;

    private GameObject scoreboardRoot;
    private Image p1HealthBar;
    private Text p1HealthText;
    private Image p2HealthBar;
    private Text p2HealthText;

    private GameObject wristCanvasObj;
    private Image wristFill;
    private Text wristHpText;

    private GameObject knockoutRoot;
    private Text knockoutTitle;
    private Text knockoutSubtitle;
    private Button knockoutRestartButton;

    private bool isSessionActive;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureVRBoxingUI()
    {
        if (FindFirstObjectByType<VRBoxingUI>() == null)
        {
            GameObject uiObj = new GameObject("VR Boxing UI");
            uiObj.AddComponent<VRBoxingUI>();
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (simpleNetworkUI == null)
        {
            simpleNetworkUI = FindFirstObjectByType<SimpleNetworkUI>();
        }
    }

    private void Start()
    {
        BuildVRStartMenu();
        BuildVRScoreboard();
        BuildVRKnockoutBanner();
    }

    private void Update()
    {
        CheckInputShortcuts();
        UpdateMenuState();
        UpdateScoreboardBillboard();
        UpdateWristHealthDisplay();
    }

    private void CheckInputShortcuts()
    {
        // Keyboard shortcuts for quick PC / Editor testing
        if (!isSessionActive)
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.hKey.wasPressedThisFrame || keyboard.digit1Key.wasPressedThisFrame)
                {
                    OnHostButtonClicked();
                }
                else if (keyboard.jKey.wasPressedThisFrame || keyboard.digit2Key.wasPressedThisFrame)
                {
                    OnJoinButtonClicked();
                }
            }
#endif
            // VR controller trigger shortcut
            CheckVRControllerTriggers();
        }
    }

    private void CheckVRControllerTriggers()
    {
        InputDevice rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (rightHand.isValid && rightHand.TryGetFeatureValue(CommonUsages.triggerButton, out bool rightTrigger) && rightTrigger)
        {
            // Default right trigger to Host if not started
            OnHostButtonClicked();
            return;
        }

        InputDevice leftHand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        if (leftHand.isValid && leftHand.TryGetFeatureValue(CommonUsages.triggerButton, out bool leftTrigger) && leftTrigger)
        {
            // Left trigger to Join
            OnJoinButtonClicked();
        }
    }

    private void UpdateMenuState()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null) return;

        bool listening = nm.IsListening;
        isSessionActive = listening;
        bool matchReady = listening && nm.ConnectedClientsIds.Count >= 2;
        if (vrMenuRoot != null) vrMenuRoot.SetActive(!matchReady);

        if (scoreboardRoot != null && !scoreboardRoot.activeSelf)
        {
            scoreboardRoot.SetActive(true);
        }

        if (vrStatusText != null && !isSessionActive)
        {
            vrStatusText.text = "PUNCH A BUTTON OR USE TRIGGER TO START";
        }
        else if (vrStatusText != null && isSessionActive)
        {
            string role = nm.IsServer ? (nm.IsClient ? "HOST" : "SERVER") : "CLIENT";
            vrStatusText.text = $"{role} RUNNING  |  {nm.ConnectedClientsIds.Count} PLAYER(S) CONNECTED";
        }
    }

    #region VR Start Menu

    private void BuildVRStartMenu()
    {
        vrMenuRoot = new GameObject("VR Start Menu Canvas");
        vrMenuRoot.transform.SetParent(transform, false);

        Transform headCam = GetHeadCamera();
        Vector3 spawnPos = headCam != null ? headCam.position : Vector3.zero;
        Vector3 forward = headCam != null ? Vector3.ProjectOnPlane(headCam.forward, Vector3.up).normalized : Vector3.forward;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;

        vrMenuRoot.transform.position = spawnPos + forward * menuOffset.z + Vector3.up * menuOffset.y;
        vrMenuRoot.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);

        Canvas canvas = vrMenuRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = headCam != null ? headCam.GetComponent<Camera>() : Camera.main;

        RectTransform rect = vrMenuRoot.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(800, 520);
        rect.localScale = Vector3.one * 0.0016f;

        // Background panel
        GameObject panel = CreateUIObject("Panel", vrMenuRoot.transform);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.sizeDelta = new Vector2(800, 520);
        Image bgImg = panel.AddComponent<Image>();
        bgImg.color = new Color(0.04f, 0.06f, 0.12f, 0.94f);

        // Header Title
        Text title = CreateText("Title", panel.transform, 46, FontStyle.Bold, Color.white);
        title.text = "LAN BOXING VR";
        title.rectTransform.anchoredPosition = new Vector2(0, 190);
        title.rectTransform.sizeDelta = new Vector2(760, 60);

        // Subtitle / Instructions
        vrStatusText = CreateText("Status", panel.transform, 22, FontStyle.Normal, new Color(0.45f, 0.85f, 0.68f));
        vrStatusText.text = "PUNCH A BUTTON OR PULL TRIGGER TO START";
        vrStatusText.rectTransform.anchoredPosition = new Vector2(0, 130);
        vrStatusText.rectTransform.sizeDelta = new Vector2(760, 45);

        // Host Button (Punchable / Clickable)
        hostBtnObj = CreateVRButton(
            "HostButton",
            panel.transform,
            new Vector2(0, 40),
            new Vector2(620, 85),
            new Color(0.12f, 0.45f, 0.95f),
            "START HOST (PLAYER 1)",
            OnHostButtonClicked
        );

        // Join Button (Punchable / Clickable)
        joinBtnObj = CreateVRButton(
            "JoinButton",
            panel.transform,
            new Vector2(0, -65),
            new Vector2(620, 85),
            new Color(0.92f, 0.35f, 0.18f),
            "JOIN GAME (PLAYER 2)",
            OnJoinButtonClicked
        );

        // Bottom hint
        Text hint = CreateText("Hint", panel.transform, 19, FontStyle.Italic, new Color(0.7f, 0.75f, 0.85f));
        hint.text = "Hit the button with your glove or press Right Trigger (Host) / Left Trigger (Join)";
        hint.rectTransform.anchoredPosition = new Vector2(0, -170);
        hint.rectTransform.sizeDelta = new Vector2(760, 50);
    }

    private GameObject CreateVRButton(string name, Transform parent, Vector2 pos, Vector2 size, Color btnColor, string labelText, System.Action onClick)
    {
        GameObject btnObj = CreateUIObject(name, parent);
        RectTransform rt = btnObj.GetComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        Image img = btnObj.AddComponent<Image>();
        img.color = btnColor;

        Button btn = btnObj.AddComponent<Button>();
        btn.onClick.AddListener(() => onClick?.Invoke());

        Text label = CreateText("Label", btnObj.transform, 28, FontStyle.Bold, Color.white);
        label.text = labelText;
        label.rectTransform.sizeDelta = size;

        // Add 3D BoxCollider so physical gloves can punch the button!
        BoxCollider box = btnObj.AddComponent<BoxCollider>();
        box.size = new Vector3(size.x, size.y, 60f);
        box.isTrigger = true;

        // Punch detector component
        VRPunchableButton punchable = btnObj.AddComponent<VRPunchableButton>();
        punchable.onPunched = onClick;

        return btnObj;
    }

    public void OnHostButtonClicked()
    {
        if (isSessionActive) return;
        VRHaptics.TriggerPunchImpulse(false, 3.5f);
        if (vrStatusText != null) vrStatusText.text = "Starting Host on port 7777...";

        if (simpleNetworkUI != null)
        {
            simpleNetworkUI.StartHostGame();
        }
        else if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.StartHost();
        }
    }

    public void OnJoinButtonClicked()
    {
        if (isSessionActive) return;
        VRHaptics.TriggerPunchImpulse(true, 3.5f);
        if (vrStatusText != null) vrStatusText.text = "Connecting to Host on LAN...";

        if (simpleNetworkUI != null)
        {
            simpleNetworkUI.StartClientGame();
        }
        else if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.StartClient();
        }
    }

    #endregion

    #region VR In-Game Scoreboard & Health Display

    private void BuildVRScoreboard()
    {
        scoreboardRoot = new GameObject("VR Boxing Scoreboard Canvas");
        scoreboardRoot.transform.SetParent(transform, false);

        Transform headCam = GetHeadCamera();
        Vector3 spawnPos = headCam != null ? headCam.position : Vector3.zero;
        Vector3 forward = headCam != null ? Vector3.ProjectOnPlane(headCam.forward, Vector3.up).normalized : Vector3.forward;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;

        // Position scoreboard floating at ring wall height
        scoreboardRoot.transform.position = spawnPos + forward * scoreboardOffset.z + Vector3.up * scoreboardOffset.y;
        scoreboardRoot.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);

        Canvas canvas = scoreboardRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = headCam != null ? headCam.GetComponent<Camera>() : Camera.main;

        RectTransform rect = scoreboardRoot.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(850, 240);
        rect.localScale = Vector3.one * 0.0022f;

        GameObject panel = CreateUIObject("ScoreboardPanel", scoreboardRoot.transform);
        RectTransform pRect = panel.GetComponent<RectTransform>();
        pRect.sizeDelta = new Vector2(850, 240);
        Image bgImg = panel.AddComponent<Image>();
        bgImg.color = new Color(0.02f, 0.035f, 0.07f, 0.92f);

        // Header
        Text title = CreateText("MatchHeader", panel.transform, 26, FontStyle.Bold, new Color(0.9f, 0.85f, 0.4f));
        title.text = "BOXING CHAMPIONSHIP - ROUND 1";
        title.rectTransform.anchoredPosition = new Vector2(0, 85);
        title.rectTransform.sizeDelta = new Vector2(800, 40);

        // Player 1 (Blue Corner)
        GameObject p1Group = CreateUIObject("Player1Group", panel.transform);
        RectTransform p1Rect = p1Group.GetComponent<RectTransform>();
        p1Rect.anchoredPosition = new Vector2(-210, -10);
        p1Rect.sizeDelta = new Vector2(360, 120);

        Text p1Label = CreateText("P1Label", p1Group.transform, 24, FontStyle.Bold, new Color(0.35f, 0.68f, 1f));
        p1Label.text = "PLAYER 1";
        p1Label.rectTransform.anchoredPosition = new Vector2(0, 36);

        p1HealthBar = CreateHealthBar("P1Bar", p1Group.transform, new Vector2(0, -6), new Vector2(340, 30), new Color(0.12f, 0.55f, 1f));
        p1HealthText = CreateText("P1HP", p1Group.transform, 20, FontStyle.Bold, Color.white);
        p1HealthText.text = "100 / 100 HP";
        p1HealthText.rectTransform.anchoredPosition = new Vector2(0, -42);

        // VS Divider
        Text vs = CreateText("VS", panel.transform, 32, FontStyle.Bold, new Color(0.85f, 0.85f, 0.85f));
        vs.text = "VS";
        vs.rectTransform.anchoredPosition = new Vector2(0, -10);

        // Player 2 (Red Corner)
        GameObject p2Group = CreateUIObject("Player2Group", panel.transform);
        RectTransform p2Rect = p2Group.GetComponent<RectTransform>();
        p2Rect.anchoredPosition = new Vector2(210, -10);
        p2Rect.sizeDelta = new Vector2(360, 120);

        Text p2Label = CreateText("P2Label", p2Group.transform, 24, FontStyle.Bold, new Color(1f, 0.38f, 0.3f));
        p2Label.text = "PLAYER 2";
        p2Label.rectTransform.anchoredPosition = new Vector2(0, 36);

        p2HealthBar = CreateHealthBar("P2Bar", p2Group.transform, new Vector2(0, -6), new Vector2(340, 30), new Color(1f, 0.28f, 0.22f));
        p2HealthText = CreateText("P2HP", p2Group.transform, 20, FontStyle.Bold, Color.white);
        p2HealthText.text = "100 / 100 HP";
        p2HealthText.rectTransform.anchoredPosition = new Vector2(0, -42);

        scoreboardRoot.SetActive(true);
    }

    private Image CreateHealthBar(string name, Transform parent, Vector2 pos, Vector2 size, Color fillColor)
    {
        GameObject barBg = CreateUIObject(name + "_Bg", parent);
        RectTransform bgRect = barBg.GetComponent<RectTransform>();
        bgRect.anchoredPosition = pos;
        bgRect.sizeDelta = size;
        Image bgImg = barBg.AddComponent<Image>();
        bgImg.color = new Color(0.12f, 0.15f, 0.2f, 1f);

        GameObject fillObj = CreateUIObject(name + "_Fill", barBg.transform);
        RectTransform fillRect = fillObj.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        fillRect.pivot = new Vector2(0f, 0.5f);

        Image fillImg = fillObj.AddComponent<Image>();
        fillImg.color = fillColor;
        fillImg.type = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Horizontal;
        fillImg.fillAmount = 1f;

        return fillImg;
    }

    public void UpdatePlayerHealth(ulong clientId, int currentHp, int maxHp)
    {
        float fraction = maxHp > 0 ? Mathf.Clamp01((float)currentHp / maxHp) : 0f;

        if (clientId == 0) // Host / P1
        {
            if (p1HealthBar != null) p1HealthBar.fillAmount = fraction;
            if (p1HealthText != null) p1HealthText.text = $"{currentHp} / {maxHp} HP";
        }
        else // Client / P2
        {
            if (p2HealthBar != null) p2HealthBar.fillAmount = fraction;
            if (p2HealthText != null) p2HealthText.text = $"{currentHp} / {maxHp} HP";
        }

        NetworkManager nm = NetworkManager.Singleton;
        if (nm != null && clientId == nm.LocalClientId)
        {
            if (wristFill != null)
            {
                wristFill.fillAmount = fraction;
                wristFill.color = Color.Lerp(Color.red, Color.green, fraction);
            }
            if (wristHpText != null)
            {
                wristHpText.text = $"HP: {currentHp} / {maxHp}";
            }
        }
    }

    private void UpdateScoreboardBillboard()
    {
        if (scoreboardRoot == null) return;
        Transform headCam = GetHeadCamera();
        if (headCam == null) return;

        Vector3 toCam = headCam.position - scoreboardRoot.transform.position;
        toCam.y = 0;
        if (toCam.sqrMagnitude > 0.01f)
        {
            scoreboardRoot.transform.rotation = Quaternion.Slerp(
                scoreboardRoot.transform.rotation,
                Quaternion.LookRotation(-toCam.normalized, Vector3.up),
                Time.deltaTime * 5f
            );
        }
    }

    private void UpdateWristHealthDisplay()
    {
        if (wristCanvasObj != null) return;

        GameObject leftGlove = GameObject.Find("Glove_left");
        Transform attachParent = leftGlove != null ? leftGlove.transform : null;
        if (attachParent == null)
        {
            GameObject leftController = GameObject.Find("Left Controller");
            if (leftController != null) attachParent = leftController.transform;
        }

        if (attachParent == null) return;

        wristCanvasObj = new GameObject("Wrist_Health_Display", typeof(RectTransform), typeof(Canvas));
        wristCanvasObj.transform.SetParent(attachParent, false);
        wristCanvasObj.transform.localPosition = new Vector3(0f, 0.07f, -0.04f);
        wristCanvasObj.transform.localRotation = Quaternion.Euler(50f, 0f, 0f);
        wristCanvasObj.transform.localScale = Vector3.one * 0.001f;

        Canvas c = wristCanvasObj.GetComponent<Canvas>();
        c.renderMode = RenderMode.WorldSpace;
        c.worldCamera = Camera.main;

        RectTransform rt = wristCanvasObj.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(200, 60);

        GameObject panel = CreateUIObject("Panel", wristCanvasObj.transform);
        RectTransform prt = panel.GetComponent<RectTransform>();
        prt.sizeDelta = new Vector2(200, 60);
        Image bg = panel.AddComponent<Image>();
        bg.color = new Color(0.04f, 0.06f, 0.1f, 0.92f);

        wristHpText = CreateText("HPText", panel.transform, 20, FontStyle.Bold, Color.white);
        wristHpText.rectTransform.anchoredPosition = new Vector2(0, 14);
        wristHpText.text = "HP: 100 / 100";

        wristFill = CreateHealthBar("WristBar", panel.transform, new Vector2(0, -14), new Vector2(170, 16), new Color(0.18f, 0.85f, 0.35f));
    }

    #endregion

    #region VR Knockout & Winner Banner

    private void BuildVRKnockoutBanner()
    {
        knockoutRoot = new GameObject("VR Knockout Banner Canvas");
        knockoutRoot.transform.SetParent(transform, false);

        Transform headCam = GetHeadCamera();
        Vector3 spawnPos = headCam != null ? headCam.position : Vector3.zero;
        Vector3 forward = headCam != null ? Vector3.ProjectOnPlane(headCam.forward, Vector3.up).normalized : Vector3.forward;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;

        knockoutRoot.transform.position = spawnPos + forward * knockoutBannerOffset.z + Vector3.up * knockoutBannerOffset.y;
        knockoutRoot.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);

        Canvas canvas = knockoutRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = headCam != null ? headCam.GetComponent<Camera>() : Camera.main;

        RectTransform rect = knockoutRoot.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(900, 360);
        rect.localScale = Vector3.one * 0.0022f;

        GameObject panel = CreateUIObject("KnockoutPanel", knockoutRoot.transform);
        RectTransform pRect = panel.GetComponent<RectTransform>();
        pRect.sizeDelta = new Vector2(900, 360);
        Image bgImg = panel.AddComponent<Image>();
        bgImg.color = new Color(0.015f, 0.02f, 0.04f, 0.95f);

        knockoutTitle = CreateText("Title", panel.transform, 56, FontStyle.Bold, new Color(1f, 0.85f, 0.2f));
        knockoutTitle.text = "YOU WIN! KNOCKOUT!";
        knockoutTitle.rectTransform.anchoredPosition = new Vector2(0, 50);
        knockoutTitle.rectTransform.sizeDelta = new Vector2(860, 90);

        knockoutSubtitle = CreateText("Subtitle", panel.transform, 26, FontStyle.Normal, Color.white);
        knockoutSubtitle.text = "Press Restart Round to continue.";
        knockoutSubtitle.rectTransform.anchoredPosition = new Vector2(0, -60);
        knockoutSubtitle.rectTransform.sizeDelta = new Vector2(860, 80);

        GameObject restartButton = CreateUIObject("Restart Round", panel.transform);
        restartButton.transform.localPosition = new Vector3(0f, -125f, 0f);
        RectTransform restartButtonRect = restartButton.GetComponent<RectTransform>();
        restartButtonRect.sizeDelta = new Vector2(360f, 70f);
        Image restartButtonImage = restartButton.AddComponent<Image>();
        restartButtonImage.color = new Color(0.12f, 0.55f, 1f, 1f);
        knockoutRestartButton = restartButton.AddComponent<Button>();
        knockoutRestartButton.onClick.AddListener(RequestRestartRound);

        Text restartLabel = CreateText("Label", restartButton.transform, 26, FontStyle.Bold, Color.white);
        restartLabel.text = "RESTART ROUND";
        restartLabel.rectTransform.sizeDelta = restartButtonRect.sizeDelta;

        knockoutRoot.SetActive(false);
    }

    public void ShowKnockoutAnnouncement(bool isWinner, float countdownDuration)
    {
        if (knockoutRoot == null) BuildVRKnockoutBanner();

        // Reposition banner directly in front of current camera view
        Transform headCam = GetHeadCamera();
        if (headCam != null)
        {
            Vector3 forward = Vector3.ProjectOnPlane(headCam.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            knockoutRoot.transform.position = headCam.position + forward * knockoutBannerOffset.z + Vector3.up * (knockoutBannerOffset.y - 0.2f);
            knockoutRoot.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        if (knockoutTitle != null)
        {
            if (isWinner)
            {
                knockoutTitle.text = "👑 YOU WIN! 👑\nKNOCKOUT VICTORY!";
                knockoutTitle.color = new Color(1f, 0.88f, 0.22f); // Gold
            }
            else
            {
                knockoutTitle.text = "💥 KNOCKED OUT! 💥\nDEFEATED";
                knockoutTitle.color = new Color(1f, 0.25f, 0.22f); // Red
            }
        }

        knockoutRoot.SetActive(true);
    }

    private void RequestRestartRound()
    {
        foreach (PlayerHealth player in FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None))
        {
            if (player.IsOwner)
            {
                player.RequestRestartRound();
                return;
            }
        }
    }

    public void HideKnockoutAnnouncement()
    {
        if (knockoutRoot != null)
            knockoutRoot.SetActive(false);
    }

    #endregion

    #region Helper Utilities

    private static Transform GetHeadCamera()
    {
        if (Camera.main != null) return Camera.main.transform;
        Camera anyCam = FindFirstObjectByType<Camera>();
        return anyCam != null ? anyCam.transform : null;
    }

    private static GameObject CreateUIObject(string name, Transform parent)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        return obj;
    }

    private static Text CreateText(string name, Transform parent, int fontSize, FontStyle style, Color color)
    {
        GameObject obj = CreateUIObject(name, parent);
        Text text = obj.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    #endregion
}

/// <summary>
/// Attached to VR 3D buttons so players can physically punch them with their gloves.
/// </summary>
public class VRPunchableButton : MonoBehaviour
{
    public System.Action onPunched;
    private float lastPunchTime = float.NegativeInfinity;

    private void OnTriggerEnter(Collider other)
    {
        if (Time.time - lastPunchTime < 0.5f) return;

        // Check if collision is from a player hand or glove
        if (other.CompareTag("PlayerHand") || other.GetComponent<GloveHitbox>() != null || other.name.ToLower().Contains("glove") || other.name.ToLower().Contains("hand"))
        {
            lastPunchTime = Time.time;
            onPunched?.Invoke();
        }
    }
}

using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// First-person Keyboard and Mouse controller for Desktop PC players.
/// Automatically activates on PC (when no VR headset is detected, or for Host).
/// Provides WASD locomotion, mouse-look aiming, and LMB/RMB punch extension
/// that drives GloveHitbox and Two-Bone IK cleanly.
/// </summary>
public sealed class DesktopPlayerController : NetworkBehaviour
{
    [Header("Controller Configuration")]
    [Tooltip("If true, activates on PC when no VR headset is present or when hosting.")]
    [SerializeField] private bool autoDetectDesktop = true;
    [SerializeField] private float walkSpeed = 3.2f;
    [SerializeField] private float sprintSpeed = 5.0f;
    [SerializeField] private float mouseSensitivity = 1.8f;
    [SerializeField] private float eyeHeight = 1.48f;
    [SerializeField] private float ringRadius = 4.2f;

    [Header("Guard Tuning (Proportional to Midans Model)")]
    [SerializeField] private float leftGuardForward = 0.28f;
    [SerializeField] private float leftGuardInward = 0.12f;
    [SerializeField] private float rightGuardForward = 0.38f;
    [SerializeField] private float rightGuardInward = 0.06f;
    [SerializeField] private float guardHeightOffset = 0.08f;

    [Header("Punch Tuning (Straight Arm Extension)")]
    [SerializeField] private float punchOutDuration = 0.08f;
    [SerializeField] private float punchHoldDuration = 0.03f;
    [SerializeField] private float punchReturnDuration = 0.12f;

    [Header("Target References")]
    [SerializeField] private Transform leftHandTarget;
    [SerializeField] private Transform rightHandTarget;
    [SerializeField] private Transform headTarget;

    private Transform leftUpperArm;
    private Transform rightUpperArm;

    public bool IsActive { get; private set; }

    private Camera pcCamera;
    private UnityEngine.InputSystem.XR.TrackedPoseDriver cameraPoseDriver;
    private float yaw;
    private float pitch;
    private GameObject crosshairCanvas;

    // Punch state
    private float leftPunchTimer = -1f;
    private float rightPunchTimer = -1f;
    private bool isLeftPunching;
    private bool isRightPunching;
    private bool isBlocking;

    public override void OnNetworkSpawn()
    {
        ResolveTargets();
        EvaluateActivation();
    }

    public override void OnNetworkDespawn()
    {
        DeactivateDesktopMode();
    }

    public void ResolveTargets()
    {
        if (leftHandTarget == null) leftHandTarget = transform.Find("Left Hand");
        if (rightHandTarget == null) rightHandTarget = transform.Find("Right Hand");
        if (headTarget == null) headTarget = transform.Find("Head");

        if (leftUpperArm == null) leftUpperArm = FindBone(transform, "mixamorig:LeftArm");
        if (rightUpperArm == null) rightUpperArm = FindBone(transform, "mixamorig:RightArm");
    }

    private static Transform FindBone(Transform root, string boneName)
    {
        if (root == null) return null;
        if (root.name == boneName) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindBone(root.GetChild(i), boneName);
            if (found != null) return found;
        }
        return null;
    }

    public void EvaluateActivation()
    {
        if (!IsOwner)
        {
            DeactivateDesktopMode();
            return;
        }

        // Check if VR headset is active on this device
        bool hasVRHeadset = XRSettings.isDeviceActive;
        bool shouldBeDesktop = !hasVRHeadset || (IsServer && autoDetectDesktop);

        if (shouldBeDesktop)
        {
            ActivateDesktopMode();
        }
        else
        {
            DeactivateDesktopMode();
        }
    }

    public void ActivateDesktopMode()
    {
        IsActive = true;
        yaw = transform.eulerAngles.y;
        pitch = 0f;

        // Take control of the scene camera for first-person desktop view
        pcCamera = Camera.main;
        if (pcCamera == null)
        {
            foreach (Camera cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (cam.enabled)
                {
                    pcCamera = cam;
                    break;
                }
            }
        }

        if (pcCamera != null)
        {
            cameraPoseDriver = pcCamera.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            if (cameraPoseDriver != null)
            {
                cameraPoseDriver.enabled = false;
            }
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        CreateCrosshair();
    }

    public void DeactivateDesktopMode()
    {
        IsActive = false;
        if (cameraPoseDriver != null)
        {
            cameraPoseDriver.enabled = true;
            cameraPoseDriver = null;
        }

        if (crosshairCanvas != null)
        {
            Destroy(crosshairCanvas);
            crosshairCanvas = null;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void Update()
    {
        if (!IsSpawned || !IsOwner) return;
        if (PlayerHealth.RoundOver) return;

        // Hotkey 'P' to toggle Desktop PC vs VR mode on the fly
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.pKey.wasPressedThisFrame)
        {
            if (IsActive) DeactivateDesktopMode();
            else ActivateDesktopMode();
            return;
        }

        if (!IsActive) return;

        HandleCursorLock();
        HandleMouseLook();
        HandleMovement();
        HandleCombatInput();
        UpdatePunchTimers();
        UpdateHandTargetTransforms();
    }

    private void LateUpdate()
    {
        if (!IsSpawned || !IsOwner || !IsActive) return;

        UpdateCameraAndHeadPose();
        UpdateHandTargetTransforms();
    }

    private void HandleCursorLock()
    {
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;

        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = Cursor.lockState == CursorLockMode.Locked
                ? CursorLockMode.None
                : CursorLockMode.Locked;
            Cursor.visible = Cursor.lockState != CursorLockMode.Locked;
        }

        // Click screen to lock cursor
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void HandleMouseLook()
    {
        if (Cursor.lockState != CursorLockMode.Locked) return;

        var mouse = Mouse.current;
        if (mouse != null)
        {
            Vector2 delta = mouse.delta.ReadValue() * (mouseSensitivity * 0.1f);
            yaw += delta.x;
            pitch = Mathf.Clamp(pitch - delta.y, -65f, 75f);

            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }
    }

    private void HandleMovement()
    {
        float moveX = 0f;
        float moveZ = 0f;
        bool isSprinting = false;

        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) moveZ += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) moveZ -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) moveX += 1f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) moveX -= 1f;
            isSprinting = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
        }

        Vector3 moveInput = new Vector3(moveX, 0f, moveZ);
        if (moveInput.sqrMagnitude > 0.001f)
        {
            Vector3 worldDirection = (transform.forward * moveZ + transform.right * moveX).normalized;
            float speed = isSprinting ? sprintSpeed : walkSpeed;
            transform.position += worldDirection * (speed * Time.deltaTime);

            // Ring perimeter boundary clamp
            Vector3 centerOffset = transform.position;
            centerOffset.y = 0f;
            if (centerOffset.magnitude > ringRadius)
            {
                transform.position = centerOffset.normalized * ringRadius + Vector3.up * transform.position.y;
            }
        }
    }

    private void HandleCombatInput()
    {
        if (Cursor.lockState != CursorLockMode.Locked) return;

        var mouse = Mouse.current;
        var keyboard = Keyboard.current;

        // Block with Spacebar or Middle Mouse Click
        isBlocking = (keyboard != null && keyboard.spaceKey.isPressed) ||
                     (mouse != null && mouse.middleButton.isPressed);

        if (isBlocking) return;

        // Left Click = Left Jab / Punch
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && !isLeftPunching)
        {
            isLeftPunching = true;
            leftPunchTimer = 0f;
        }

        // Right Click = Right Cross / Heavy Punch
        if (mouse != null && mouse.rightButton.wasPressedThisFrame && !isRightPunching)
        {
            isRightPunching = true;
            rightPunchTimer = 0f;
        }
    }

    private void UpdatePunchTimers()
    {
        float totalPunchTime = punchOutDuration + punchHoldDuration + punchReturnDuration;

        if (isLeftPunching)
        {
            leftPunchTimer += Time.deltaTime;
            if (leftPunchTimer >= totalPunchTime)
            {
                isLeftPunching = false;
                leftPunchTimer = -1f;
            }
        }

        if (isRightPunching)
        {
            rightPunchTimer += Time.deltaTime;
            if (rightPunchTimer >= totalPunchTime)
            {
                isRightPunching = false;
                rightPunchTimer = -1f;
            }
        }
    }

    private void UpdateHandTargetTransforms()
    {
        if (leftUpperArm == null || rightUpperArm == null)
        {
            ResolveTargets();
        }

        Vector3 forward = transform.forward;
        Vector3 right = transform.right;
        Vector3 up = transform.up;

        // Shoulder world positions (or reliable stance fallback if not yet resolved)
        Vector3 lShoulderPos = leftUpperArm != null
            ? leftUpperArm.position
            : transform.TransformPoint(new Vector3(-0.268f, 1.334f, -0.051f));
        Vector3 rShoulderPos = rightUpperArm != null
            ? rightUpperArm.position
            : transform.TransformPoint(new Vector3(0.160f, 1.354f, -0.174f));

        Vector3 eyePos = transform.position + up * eyeHeight;
        Vector3 aimDir = Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
        Vector3 crosshairTarget = eyePos + aimDir * 2.5f;

        // Guard positions in front of the chin/eyes, framing the lower screen
        float blockMod = isBlocking ? 0.05f : 0f;
        float pitchGuardTilt = Mathf.Sin(pitch * Mathf.Deg2Rad) * 0.10f;

        Vector3 leftGuard = lShoulderPos
            + forward * (leftGuardForward - blockMod)
            + right * (leftGuardInward + (isBlocking ? 0.08f : 0f))
            + up * (guardHeightOffset + (isBlocking ? 0.08f : 0f) + pitchGuardTilt);

        Vector3 rightGuard = rShoulderPos
            + forward * (rightGuardForward - blockMod)
            - right * (rightGuardInward + (isBlocking ? 0.08f : 0f))
            + up * (guardHeightOffset + (isBlocking ? 0.08f : 0f) + pitchGuardTilt);

        // Maximum straight arm reach (close to 100% of physical arm reach for a straight punch)
        float maxLReach = 0.524f; // Left arm reach is 0.528m
        float maxRReach = 0.518f; // Right arm reach is 0.522m

        // Straight punch targets: vector from shoulder aimed directly toward crosshair target
        Vector3 lPunchTarget = lShoulderPos + (crosshairTarget - lShoulderPos).normalized * maxLReach;
        Vector3 rPunchTarget = rShoulderPos + (crosshairTarget - rShoulderPos).normalized * maxRReach;

        // Interpolate smoothly between guard and full extension using punch curve
        float leftExt = isLeftPunching ? EvaluatePunchCurve(leftPunchTimer) : 0f;
        float rightExt = isRightPunching ? EvaluatePunchCurve(rightPunchTimer) : 0f;

        Vector3 leftPos = Vector3.Lerp(leftGuard, lPunchTarget, leftExt);
        Vector3 rightPos = Vector3.Lerp(rightGuard, rPunchTarget, rightExt);

        // Natural boxing punch rotation
        Quaternion camFacingRot = Quaternion.Euler(pitch, yaw, 0f) * Quaternion.Euler(15f, 0f, 0f);

        if (leftHandTarget != null)
        {
            leftHandTarget.SetPositionAndRotation(leftPos, camFacingRot);
        }

        if (rightHandTarget != null)
        {
            rightHandTarget.SetPositionAndRotation(rightPos, camFacingRot);
        }
    }

    private float EvaluatePunchCurve(float timer)
    {
        if (timer < 0f) return 0f;
        if (timer <= punchOutDuration)
        {
            // Rapid outward punch
            float t = timer / punchOutDuration;
            return Mathf.Sin(t * Mathf.PI * 0.5f);
        }
        else if (timer <= punchOutDuration + punchHoldDuration)
        {
            return 1f;
        }
        else
        {
            // Smooth return
            float t = (timer - punchOutDuration - punchHoldDuration) / punchReturnDuration;
            return 1f - Mathf.Clamp01(t);
        }
    }

    private void UpdateCameraAndHeadPose()
    {
        Vector3 eyePos = transform.position + Vector3.up * eyeHeight;
        Quaternion camRot = Quaternion.Euler(pitch, yaw, 0f);

        if (pcCamera != null)
        {
            pcCamera.transform.SetPositionAndRotation(eyePos, camRot);
        }

        if (headTarget != null)
        {
            headTarget.SetPositionAndRotation(eyePos, camRot);
        }
    }

    private void CreateCrosshair()
    {
        if (crosshairCanvas != null) return;

        crosshairCanvas = new GameObject("Desktop_Crosshair_Canvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
        Canvas canvas = crosshairCanvas.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;

        GameObject crosshair = new GameObject("Reticle", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        crosshair.transform.SetParent(crosshairCanvas.transform, false);
        RectTransform rect = crosshair.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(8f, 8f);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);

        UnityEngine.UI.Image img = crosshair.GetComponent<UnityEngine.UI.Image>();
        img.color = new Color(1f, 1f, 1f, 0.75f);
    }
}

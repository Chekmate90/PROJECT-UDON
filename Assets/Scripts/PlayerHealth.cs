using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;
using XRMultiplayer;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

public class PlayerHealth : NetworkBehaviour
{
    [Header("Health Settings")]
    [SerializeField] private int maxHealth = 100;
    [SerializeField] private float minPunchSpeed = 1.2f;
    [SerializeField] private float maxPunchSpeed = 5f;
    [SerializeField] private float damageMultiplier = 8f;
    [SerializeField] private float hitCooldown = 0.25f;

    private readonly Dictionary<ulong,float> lastHitTimes = new();
    private XRHandPoseReplicator localHandPoseReplicator;
    private Transform previousLeftHandOrigin;
    private Transform previousRightHandOrigin;
    private Vector3 previousLeftHandPosition;
    private Vector3 previousRightHandPosition;
    private bool hasPreviousHandPositions;
    private string lastTrackingStatus;
    private Transform sceneLeftGlove;
    private Transform sceneRightGlove;

    // Synchronized variable across all connected clients
    public NetworkVariable<int> currentHealth = new NetworkVariable<int>(
        100,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public override void OnNetworkSpawn()
    {
        currentHealth.OnValueChanged += OnHealthChanged;
        int handHitboxCount = AddGloveHitboxes();
        if (IsOwner)
        {
            localHandPoseReplicator = GetComponent<XRHandPoseReplicator>();
            PunchDebugDisplay.Show($"Local player ready | Avatar hand hitboxes: {handHitboxCount}\nWaiting for XR hand tracking...");
        }
        if (IsServer)
        {
            currentHealth.Value = maxHealth;
        }
    }

    public override void OnNetworkDespawn()
    {
        currentHealth.OnValueChanged -= OnHealthChanged;
    }

    private void OnHealthChanged(int previousValue, int newValue)
    {
        Debug.Log($"[Player {OwnerClientId}] Health changed from {previousValue} to {newValue}");
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

        if (localHandPoseReplicator == null)
        {
            localHandPoseReplicator = GetComponent<XRHandPoseReplicator>();
        }

        Transform leftHandOrigin = localHandPoseReplicator != null ? localHandPoseReplicator.LeftHandOrigin : null;
        Transform rightHandOrigin = localHandPoseReplicator != null ? localHandPoseReplicator.RightHandOrigin : null;

        string trackingSource = "network hand tracker";
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
                trackingSource = "tracked hand anchors";
            }

            if (leftHandOrigin == null && rightHandOrigin == null && modalityManager != null)
            {
                leftHandOrigin = modalityManager.leftController != null ? modalityManager.leftController.transform : null;
                rightHandOrigin = modalityManager.rightController != null ? modalityManager.rightController.transform : null;
                trackingSource = "controller transforms";
            }

            if (leftHandOrigin == null && rightHandOrigin == null)
            {
                FindSceneGloves(out leftHandOrigin, out rightHandOrigin);
                trackingSource = "scene glove colliders";
                usingExistingGloveColliders = leftHandOrigin != null || rightHandOrigin != null;
            }
        }

        if (leftHandOrigin == null && rightHandOrigin == null)
        {
            ShowTrackingStatus("XR input references unavailable");
            return;
        }

        if (previousLeftHandOrigin != leftHandOrigin || previousRightHandOrigin != rightHandOrigin)
        {
            hasPreviousHandPositions = false;
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

        ShowTrackingStatus($"Tracking: {trackingSource} | Contact: waiting | Damage: --");

        if (!hasPreviousHandPositions || Time.deltaTime <= 0f)
        {
            previousLeftHandPosition = leftHandOrigin != null ? leftHandOrigin.position : Vector3.zero;
            previousRightHandPosition = rightHandOrigin != null ? rightHandOrigin.position : Vector3.zero;
            hasPreviousHandPositions = true;
            return;
        }

        float punchSpeed = 0f;
        if (leftHandOrigin != null)
        {
            punchSpeed = Mathf.Max(punchSpeed, Vector3.Distance(leftHandOrigin.position, previousLeftHandPosition) / Time.deltaTime);
            previousLeftHandPosition = leftHandOrigin.position;
        }

        if (rightHandOrigin != null)
        {
            punchSpeed = Mathf.Max(punchSpeed, Vector3.Distance(rightHandOrigin.position, previousRightHandPosition) / Time.deltaTime);
            previousRightHandPosition = rightHandOrigin.position;
        }

        PunchDebugDisplay.UpdateSpeed(punchSpeed);
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

    private void ShowTrackingStatus(string message)
    {
        if (lastTrackingStatus == message)
        {
            return;
        }

        lastTrackingStatus = message;
        PunchDebugDisplay.Show(message);
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
            PunchDebugClientRpc(attackerClientId, reportedPunchSpeed, 0, "BLOCKED: server cooldown");
            return;
        }

        lastHitTimes[attackerClientId] = currentTime;

        if (currentHealth.Value <= 0)
        {
            PunchDebugClientRpc(attackerClientId, reportedPunchSpeed, 0, "BLOCKED: target is knocked out");
            return;
        }

        if (float.IsNaN(reportedPunchSpeed) ||
            float.IsInfinity(reportedPunchSpeed) ||
            reportedPunchSpeed < minPunchSpeed)
        {
            PunchDebugClientRpc(attackerClientId, reportedPunchSpeed, 0, $"TOO SLOW: minimum {minPunchSpeed:F1} m/s");
            return;
        }

        if (reportedPunchSpeed > maxPunchSpeed)
        {
            PunchDebugClientRpc(attackerClientId, reportedPunchSpeed, 0, $"TOO FAST: maximum {maxPunchSpeed:F1} m/s");
            return;
        }

        int damage = Mathf.RoundToInt(reportedPunchSpeed*damageMultiplier);
        int previousHealth = currentHealth.Value;

        currentHealth.Value = Mathf.Max(0, currentHealth.Value - damage);

        PunchDebugClientRpc(attackerClientId, reportedPunchSpeed, damage, "DETECTED: server accepted");

        Debug.Log(
            $"Punch from {attackerClientId}"+$"Damage : {damage} and Health : {previousHealth} ---> {currentHealth.Value}"
        );

        if (previousHealth > 0 && currentHealth.Value == 0)
        {
            TriggerKnockoutClientRpc();
        }
    }

    [ClientRpc]
    private void PunchDebugClientRpc(ulong attackerClientId, float punchSpeed, int damage, string result)
    {
        string speedText = float.IsNaN(punchSpeed) || float.IsInfinity(punchSpeed)
            ? "invalid"
            : $"{punchSpeed:F2} m/s";
        PunchDebugDisplay.Show($"Attacker {attackerClientId} | Speed: {speedText}\nContact: YES | {result} | Damage: {damage}");
    }

    [ClientRpc]
    private void TriggerKnockoutClientRpc()
    {
        Debug.Log($"Player {OwnerClientId} was knocked out!");
        // Add sound effects, screen fade, or round-end triggers here
    }
}

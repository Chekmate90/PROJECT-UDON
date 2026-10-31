using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

/// <summary>
/// Drives the networked avatar's head and hands from local XR tracking.
/// When no XR tracking is available, it provides a visible desktop idle pose.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public sealed class NetworkAvatarPoseSync : NetworkBehaviour
{
    [Header("Networked avatar pose anchors")]
    [SerializeField] private Transform headPose;
    [SerializeField] private Transform leftHandPose;
    [SerializeField] private Transform rightHandPose;

    [Header("Desktop fallback")]
    [SerializeField, Min(0.5f)] private float fallbackHeadHeight = 1.6f;
    [SerializeField, Min(0f)] private float fallbackPlayerSpacing = 1.5f;
    [SerializeField, Min(0f)] private float fallbackHandSideOffset = 0.25f;
    [SerializeField] private float fallbackHandHeightOffset = -0.35f;
    [SerializeField] private float fallbackHandForwardOffset = 0.35f;

    private XROrigin localOrigin;
    private XRInputModalityManager modalityManager;
    private Vector3 fallbackHeadPosition;
    private Quaternion fallbackHeadRotation;
    private Vector3 desktopSpawnOffset;
    private bool desktopSpawnOffsetApplied;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsOwner)
            return;

        localOrigin = FindFirstObjectByType<XROrigin>();
        if (localOrigin != null)
            modalityManager = localOrigin.GetComponent<XRInputModalityManager>();

        Vector3 spawnPosition = transform.position;
        if (localOrigin != null)
        {
            spawnPosition = localOrigin.transform.position;
            fallbackHeadRotation = localOrigin.transform.rotation;
        }
        else
        {
            fallbackHeadRotation = transform.rotation;
        }

        // Give each non-VR client a distinct place in the arena instead of
        // stacking every desktop avatar at the same default spawn point.
        float playerOffset = (float)OwnerClientId * fallbackPlayerSpacing;
        desktopSpawnOffset = fallbackHeadRotation * Vector3.right * playerOffset;
        bool hasTrackedInput = modalityManager != null &&
                               XRInputModalityManager.currentInputMode.Value != XRInputModalityManager.InputMode.None;
        if (!hasTrackedInput && localOrigin != null && playerOffset > 0f)
        {
            localOrigin.transform.position += desktopSpawnOffset;
            desktopSpawnOffsetApplied = true;
        }

        spawnPosition += desktopSpawnOffset;
        fallbackHeadPosition = spawnPosition + Vector3.up * fallbackHeadHeight;

        DrivePose();
    }

    private void LateUpdate()
    {
        if (IsOwner)
            DrivePose();
    }

    private void DrivePose()
    {
        if (headPose == null || leftHandPose == null || rightHandPose == null)
            return;

        Transform cameraTransform = localOrigin != null && localOrigin.Camera != null
            ? localOrigin.Camera.transform
            : null;

        bool hasTrackedInput = modalityManager != null &&
                               XRInputModalityManager.currentInputMode.Value != XRInputModalityManager.InputMode.None;

        if (cameraTransform != null)
        {
            headPose.SetPositionAndRotation(cameraTransform.position, cameraTransform.rotation);
        }
        else
        {
            headPose.SetPositionAndRotation(fallbackHeadPosition, fallbackHeadRotation);
        }

        if (hasTrackedInput && TryGetTrackedHand(true, out Transform leftTrackedHand))
        {
            leftHandPose.SetPositionAndRotation(leftTrackedHand.position, leftTrackedHand.rotation);
        }
        else
        {
            SetFallbackHand(leftHandPose, -fallbackHandSideOffset);
        }

        if (hasTrackedInput && TryGetTrackedHand(false, out Transform rightTrackedHand))
        {
            rightHandPose.SetPositionAndRotation(rightTrackedHand.position, rightTrackedHand.rotation);
        }
        else
        {
            SetFallbackHand(rightHandPose, fallbackHandSideOffset);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner && desktopSpawnOffsetApplied && localOrigin != null)
            localOrigin.transform.position -= desktopSpawnOffset;

        base.OnNetworkDespawn();
    }

    private bool TryGetTrackedHand(bool left, out Transform handTransform)
    {
        handTransform = null;
        if (modalityManager == null)
            return false;

        if (XRInputModalityManager.currentInputMode.Value == XRInputModalityManager.InputMode.TrackedHand)
        {
            GameObject handObject = left ? modalityManager.leftHand : modalityManager.rightHand;
            XRHandSkeletonDriver skeleton = handObject != null
                ? handObject.GetComponentInChildren<XRHandSkeletonDriver>()
                : null;
            if (skeleton != null)
                handTransform = skeleton.rootTransform;
        }
        else
        {
            GameObject controller = left ? modalityManager.leftController : modalityManager.rightController;
            if (controller != null)
                handTransform = controller.transform;
        }

        return handTransform != null;
    }

    private void SetFallbackHand(Transform hand, float sideOffset)
    {
        Vector3 offset = new Vector3(sideOffset, fallbackHandHeightOffset, fallbackHandForwardOffset);
        hand.SetPositionAndRotation(
            headPose.position + headPose.rotation * offset,
            headPose.rotation);
    }
}

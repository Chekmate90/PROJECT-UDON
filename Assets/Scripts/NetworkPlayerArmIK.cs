using UnityEngine;

/// <summary>
/// Analytical Two-Bone Inverse Kinematics solver for player arm rigging.
/// Solves upper arms, forearms (elbows), and hands to track VR controllers cleanly
/// without accumulated roll twist, gimbal drift, or network desynchronization.
/// </summary>
public sealed class NetworkPlayerArmIK : MonoBehaviour
{
    [Header("Left Arm")]
    [SerializeField] private Transform leftUpperArm;
    [SerializeField] private Transform leftForearm;
    [SerializeField] private Transform leftHand;
    [SerializeField] private Transform leftTarget;

    [Header("Right Arm")]
    [SerializeField] private Transform rightUpperArm;
    [SerializeField] private Transform rightForearm;
    [SerializeField] private Transform rightHand;
    [SerializeField] private Transform rightTarget;

    [Header("IK Weights")]
    [SerializeField, Range(0f, 1f)] private float positionWeight = 1f;
    [SerializeField, Range(0f, 1f)] private float rotationWeight = 1f;

    // Cached rest-pose local rotations to avoid cumulative twist error
    private Quaternion leftUpperArmRestRot;
    private Quaternion leftForearmRestRot;
    private Quaternion leftHandRestRot;

    private Quaternion rightUpperArmRestRot;
    private Quaternion rightForearmRestRot;
    private Quaternion rightHandRestRot;

    private bool hasCachedRestPose;

    private void Awake()
    {
        EnsureHierarchyIntegrity();
        ResolveMissingReferences();
        CacheRestPose();
    }

    private void OnEnable()
    {
        EnsureHierarchyIntegrity();
        ResolveMissingReferences();
        CacheRestPose();
    }

    /// <summary>
    /// Defensive check: targets must never be parented under the model bones,
    /// which would cause a circular dependency loop between IK solver and target.
    /// </summary>
    private void EnsureHierarchyIntegrity()
    {
        if (leftTarget != null && leftTarget.parent != transform)
        {
            Transform boneParent = FindBone(transform, "mixamorig:LeftHand");
            if (boneParent != null && (leftTarget == boneParent || leftTarget.IsChildOf(boneParent)))
            {
                leftTarget.SetParent(transform, true);
                leftTarget.localScale = Vector3.one;
            }
        }

        if (rightTarget != null && rightTarget.parent != transform)
        {
            Transform boneParent = FindBone(transform, "mixamorig:RightHand");
            if (boneParent != null && (rightTarget == boneParent || rightTarget.IsChildOf(boneParent)))
            {
                rightTarget.SetParent(transform, true);
                rightTarget.localScale = Vector3.one;
            }
        }
    }

    public void ResolveMissingReferences()
    {
        Transform model = transform.Find("Midans mdol");
        if (model == null)
        {
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (child.name.Contains("Midans") || child.name.Contains("mixamorig"))
                {
                    model = child;
                    break;
                }
            }
        }

        if (leftTarget == null) leftTarget = transform.Find("Left Hand") ?? FindChildNamed(transform, "Left Hand");
        if (rightTarget == null) rightTarget = transform.Find("Right Hand") ?? FindChildNamed(transform, "Right Hand");

        if (model != null)
        {
            if (leftUpperArm == null) leftUpperArm = FindBone(model, "mixamorig:LeftArm");
            if (leftForearm == null) leftForearm = FindBone(model, "mixamorig:LeftForeArm");
            if (leftHand == null) leftHand = FindBone(model, "mixamorig:LeftHand");

            if (rightUpperArm == null) rightUpperArm = FindBone(model, "mixamorig:RightArm");
            if (rightForearm == null) rightForearm = FindBone(model, "mixamorig:RightForeArm");
            if (rightHand == null) rightHand = FindBone(model, "mixamorig:RightHand");
        }
    }

    public void CacheRestPose()
    {
        if (leftUpperArm != null) leftUpperArmRestRot = leftUpperArm.localRotation;
        if (leftForearm != null) leftForearmRestRot = leftForearm.localRotation;
        if (leftHand != null) leftHandRestRot = leftHand.localRotation;

        if (rightUpperArm != null) rightUpperArmRestRot = rightUpperArm.localRotation;
        if (rightForearm != null) rightForearmRestRot = rightForearm.localRotation;
        if (rightHand != null) rightHandRestRot = rightHand.localRotation;

        hasCachedRestPose = leftUpperArm != null && rightUpperArm != null;
    }

    private void LateUpdate()
    {
        if (positionWeight <= 0.001f) return;

        if (!hasCachedRestPose)
        {
            ResolveMissingReferences();
            CacheRestPose();
            if (!hasCachedRestPose) return;
        }

        // Solve Left Arm with elbow pointing back-down-left
        if (leftUpperArm != null && leftForearm != null && leftHand != null && leftTarget != null)
        {
            Vector3 leftPole = -transform.forward * 0.35f - transform.right * 0.65f - transform.up * 0.25f;
            SolveArmIK(leftUpperArm, leftForearm, leftHand, leftTarget, leftPole,
                leftUpperArmRestRot, leftForearmRestRot, leftHandRestRot);
        }

        // Solve Right Arm with elbow pointing back-down-right
        if (rightUpperArm != null && rightForearm != null && rightHand != null && rightTarget != null)
        {
            Vector3 rightPole = -transform.forward * 0.35f + transform.right * 0.65f - transform.up * 0.25f;
            SolveArmIK(rightUpperArm, rightForearm, rightHand, rightTarget, rightPole,
                rightUpperArmRestRot, rightForearmRestRot, rightHandRestRot);
        }
    }

    private void SolveArmIK(
        Transform upperArm,
        Transform forearm,
        Transform hand,
        Transform target,
        Vector3 poleVector,
        Quaternion upperRestRot,
        Quaternion forearmRestRot,
        Quaternion handRestRot)
    {
        // 1. Reset to rest local rotation to prevent cumulative twist errors across frames
        upperArm.localRotation = upperRestRot;
        forearm.localRotation = forearmRestRot;
        hand.localRotation = handRestRot;

        Vector3 aPos = upperArm.position;
        Vector3 bPos = forearm.position;
        Vector3 cPos = hand.position;
        Vector3 tPos = target.position;

        float aLen = Vector3.Distance(aPos, bPos);
        float bLen = Vector3.Distance(bPos, cPos);
        if (aLen < 0.001f || bLen < 0.001f) return;

        Vector3 targetDir = tPos - aPos;
        float targetDist = targetDir.magnitude;
        if (targetDist < 0.001f) return;

        // Clamp reach within physical limits of the arm
        float maxReach = (aLen + bLen) * 0.999f;
        float minReach = Mathf.Max(0.02f, Mathf.Abs(aLen - bLen) * 1.05f);
        targetDist = Mathf.Clamp(targetDist, minReach, maxReach);
        Vector3 targetDirNorm = targetDir.normalized;

        // Law of Cosines for shoulder angle
        float cosA = (aLen * aLen + targetDist * targetDist - bLen * bLen) / (2f * aLen * targetDist);
        cosA = Mathf.Clamp(cosA, -1f, 1f);
        float angleA = Mathf.Acos(cosA) * Mathf.Rad2Deg;

        // Calculate bend plane normal using pole direction
        Vector3 normal = Vector3.Cross(targetDirNorm, poleVector.normalized);
        if (normal.sqrMagnitude < 0.0001f)
        {
            normal = Vector3.Cross(targetDirNorm, transform.up);
            if (normal.sqrMagnitude < 0.0001f)
            {
                normal = Vector3.Cross(targetDirNorm, transform.right);
            }
        }
        normal.Normalize();

        // 1. Solve Upper Arm rotation
        Vector3 desiredUpperDir = (Quaternion.AngleAxis(angleA, normal) * targetDirNorm).normalized;
        Vector3 currentUpperDir = (bPos - aPos).normalized;
        Quaternion upperRotDelta = Quaternion.FromToRotation(currentUpperDir, desiredUpperDir);
        upperArm.rotation = Quaternion.Slerp(upperArm.rotation, upperRotDelta * upperArm.rotation, positionWeight);

        // 2. Solve Forearm rotation
        Vector3 currentForearmDir = (hand.position - forearm.position).normalized;
        Vector3 desiredForearmDir = (tPos - forearm.position).normalized;
        Quaternion forearmRotDelta = Quaternion.FromToRotation(currentForearmDir, desiredForearmDir);
        forearm.rotation = Quaternion.Slerp(forearm.rotation, forearmRotDelta * forearm.rotation, positionWeight);

        // 3. Align Hand rotation
        if (rotationWeight > 0.001f)
        {
            hand.rotation = Quaternion.Slerp(hand.rotation, target.rotation, rotationWeight);
        }
    }

    private static Transform FindBone(Transform root, string boneName)
    {
        if (root == null) return null;
        if (root.name == boneName) return root;
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == boneName) return child;
        }
        return null;
    }

    private static Transform FindChildNamed(Transform root, string childName)
    {
        if (root == null) return null;
        if (root.name == childName) return root;
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == childName) return child;
        }
        return null;
    }
}

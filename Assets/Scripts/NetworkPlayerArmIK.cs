using UnityEngine;

/// <summary>Drives only the shoulder, elbow, and wrist bones from the replicated hand targets.</summary>
public sealed class NetworkPlayerArmIK : MonoBehaviour
{
    [SerializeField] private Transform leftUpperArm;
    [SerializeField] private Transform leftForearm;
    [SerializeField] private Transform leftHand;
    [SerializeField] private Transform leftTarget;
    [SerializeField] private Transform rightUpperArm;
    [SerializeField] private Transform rightForearm;
    [SerializeField] private Transform rightHand;
    [SerializeField] private Transform rightTarget;
    [SerializeField, Range(0f, 1f)] private float positionWeight = 1f;
    [SerializeField, Range(0f, 1f)] private float rotationWeight = 1f;

    private float leftUpperLength;
    private float leftLowerLength;
    private float rightUpperLength;
    private float rightLowerLength;

    private void Awake()
    {
        leftUpperLength = BoneLength(leftUpperArm, leftForearm);
        leftLowerLength = BoneLength(leftForearm, leftHand);
        rightUpperLength = BoneLength(rightUpperArm, rightForearm);
        rightLowerLength = BoneLength(rightForearm, rightHand);
    }

    private void LateUpdate()
    {
        SolveArm(leftUpperArm, leftForearm, leftHand, leftTarget, leftUpperLength, leftLowerLength, -1f);
        SolveArm(rightUpperArm, rightForearm, rightHand, rightTarget, rightUpperLength, rightLowerLength, 1f);
    }

    private void SolveArm(Transform upper, Transform lower, Transform hand, Transform target,
        float upperLength, float lowerLength, float side)
    {
        if (upper == null || lower == null || hand == null || target == null || upperLength <= 0f || lowerLength <= 0f)
            return;

        Vector3 shoulder = upper.position;
        Vector3 toTarget = target.position - shoulder;
        float reach = upperLength + lowerLength;
        float distance = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(upperLength - lowerLength) + 0.001f, reach - 0.001f);
        Vector3 direction = toTarget.sqrMagnitude > 0.000001f ? toTarget.normalized : transform.forward;

        // Bend the elbows toward the front of the avatar, with a slight outward bias.
        Vector3 pole = transform.forward + transform.right * (0.12f * side);
        pole = Vector3.ProjectOnPlane(pole, direction).normalized;
        if (pole.sqrMagnitude < 0.0001f)
            pole = Vector3.ProjectOnPlane(transform.up, direction).normalized;

        float along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) / (2f * distance);
        float height = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
        Vector3 desiredElbow = shoulder + direction * along + pole * height;

        Quaternion upperDelta = Quaternion.FromToRotation(lower.position - shoulder, desiredElbow - shoulder);
        upper.rotation = Quaternion.Slerp(Quaternion.identity, upperDelta, positionWeight) * upper.rotation;

        Quaternion lowerDelta = Quaternion.FromToRotation(hand.position - lower.position, target.position - lower.position);
        lower.rotation = Quaternion.Slerp(Quaternion.identity, lowerDelta, positionWeight) * lower.rotation;
        hand.rotation = Quaternion.Slerp(hand.rotation, target.rotation, rotationWeight);
    }

    private static float BoneLength(Transform start, Transform end)
    {
        return start != null && end != null ? Vector3.Distance(start.position, end.position) : 0f;
    }
}

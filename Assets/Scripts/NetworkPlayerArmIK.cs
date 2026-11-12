using UnityEngine;

/// <summary>Moves only the model's hand bones to the replicated VR hand targets.</summary>
public sealed class NetworkPlayerArmIK : MonoBehaviour
{
    [SerializeField] private Transform leftHand;
    [SerializeField] private Transform leftTarget;
    [SerializeField] private Transform rightHand;
    [SerializeField] private Transform rightTarget;
    [SerializeField, Range(0f, 1f)] private float positionWeight = 1f;
    [SerializeField, Range(0f, 1f)] private float rotationWeight = 1f;

    private void LateUpdate()
    {
        FollowTarget(leftHand, leftTarget);
        FollowTarget(rightHand, rightTarget);
    }

    private void FollowTarget(Transform hand, Transform target)
    {
        if (hand == null || target == null) return;

        hand.position = Vector3.Lerp(hand.position, target.position, positionWeight);
        hand.rotation = Quaternion.Slerp(hand.rotation, target.rotation, rotationWeight);
    }
}

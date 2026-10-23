using Unity.Netcode;
using UnityEngine;

public class GloveHitbox : NetworkBehaviour
{
    [Header("Punch Settings")]
    [Tooltip("Minimum punch velocity (m/s) required to register damage")]
    [SerializeField] private float minPunchSpeed = 1.2f;
    [SerializeField] private float damageMultiplier = 8.0f;
    [SerializeField] private float hitCooldown = 0.25f;

    private Vector3 previousPosition;
    private float currentPunchSpeed;
    private float lastHitTime;

    private void Start()
    {
        previousPosition = transform.position;
    }

    private void Update()
    {
        // Track glove velocity in world meters per second
        if (Time.deltaTime > 0f)
        {
            currentPunchSpeed = (transform.position - previousPosition).magnitude / Time.deltaTime;
        }
        previousPosition = transform.position;
    }

    private void OnTriggerEnter(Collider other)
    {
        // Only the owner of this controller calculates their own hits to eliminate local input lag
        if (!IsOwner) return;

        // Prevent multiple hit registrations in the same swing
        if (Time.time - lastHitTime < hitCooldown) return;

        // Check if we struck an opponent's head/torso trigger
        if (other.CompareTag("Hurtbox") && currentPunchSpeed >= minPunchSpeed)
        {
            PlayerHealth targetHealth = other.GetComponentInParent<PlayerHealth>();

            // Confirm the target is an opponent, not our own avatar
            if (targetHealth != null && targetHealth.OwnerClientId != OwnerClientId)
            {
                lastHitTime = Time.time;
                int damage = Mathf.RoundToInt(currentPunchSpeed * damageMultiplier);

                Debug.Log($"Punch landed at {currentPunchSpeed:F2} m/s dealing {damage} damage!");
                targetHealth.ApplyDamageServerRpc(damage);
            }
        }
    }
}
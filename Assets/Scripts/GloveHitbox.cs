using Unity.Netcode;
using UnityEngine;

public class GloveHitbox : MonoBehaviour
{
    [Header("Punch Settings")]
    [Tooltip("Minimum forward punch velocity (m/s) required to register damage")]
    [SerializeField] private float minPunchSpeed = 1.8f;
    [Tooltip("Maximum distance from glove center to the target body center")]
    [SerializeField] private float maxHitDistance = 0.65f;
    [Tooltip("Minimum amount of movement toward the target (0 to 1)")]
    [SerializeField, Range(0f, 1f)] private float minForwardAlignment = 0.65f;
    [SerializeField] private float hitCooldown = 0.25f;

    private Vector3 previousPosition;
    private float currentPunchSpeed;
    private Vector3 currentVelocity;
    private float lastHitTime = float.NegativeInfinity;

    private void Awake()
    {
        Collider gloveCollider = GetComponent<Collider>();
        gloveCollider.isTrigger = true;

        Rigidbody gloveBody = GetComponent<Rigidbody>();
        if (gloveBody == null)
        {
            gloveBody = gameObject.AddComponent<Rigidbody>();
        }

        gloveBody.isKinematic = true;
        gloveBody.useGravity = false;
    }

    private void Start()
    {
        previousPosition = transform.position;
    }

    private void Update()
    {
        // Track glove velocity in world meters per second
        if (Time.deltaTime > 0f)
        {
            currentVelocity = (transform.position - previousPosition) / Time.deltaTime;
            currentPunchSpeed = currentVelocity.magnitude;
        }
        previousPosition = transform.position;

        PlayerHealth playerHealth = GetComponentInParent<PlayerHealth>();
        if ((playerHealth != null && playerHealth.IsOwner) || CompareTag("Glove"))
        {
            PunchDebugDisplay.UpdateSpeed(currentPunchSpeed);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        PlayerHealth targetHealth = other.GetComponentInParent<PlayerHealth>();
        if (targetHealth == null)
        {
            return;
        }

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsClient || !networkManager.IsListening)
        {
            PunchDebugDisplay.Show($"Contact detected: YES | Speed: {currentPunchSpeed:F2} m/s\nDamage: 0 | Network unavailable");
            return;
        }

        if (!targetHealth.IsSpawned || targetHealth.OwnerClientId == networkManager.LocalClientId)
        {
            return;
        }

        Vector3 toTarget = targetHealth.transform.position - transform.position;
        float targetDistance = toTarget.magnitude;
        if (targetDistance > maxHitDistance)
        {
            PunchDebugDisplay.Show($"Contact detected: YES | Reach: {targetDistance:F2} m\nDamage: 0 | Out of reach");
            return;
        }

        float forwardSpeed = targetDistance > 0.001f
            ? Vector3.Dot(currentVelocity, toTarget / targetDistance)
            : 0f;
        float forwardAlignment = currentPunchSpeed > 0.001f ? forwardSpeed / currentPunchSpeed : 0f;
        if (forwardSpeed < minPunchSpeed || forwardAlignment < minForwardAlignment)
        {
            PunchDebugDisplay.Show($"Contact detected: YES | Forward: {forwardSpeed:F2} m/s\nDamage: 0 | Not a punch");
            return;
        }

        if (Time.time - lastHitTime < hitCooldown)
        {
            PunchDebugDisplay.Show($"Contact detected: YES | Speed: {currentPunchSpeed:F2} m/s\nDamage: 0 | Local cooldown");
            return;
        }

        lastHitTime = Time.time;
        PunchDebugDisplay.Show($"Contact detected: YES | Speed: {currentPunchSpeed:F2} m/s\nDamage: waiting for server...");
        targetHealth.ReportPunchAttemptServerRpc(forwardSpeed, targetDistance);
    }
}

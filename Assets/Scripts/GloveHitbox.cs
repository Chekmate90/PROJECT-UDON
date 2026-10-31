using Unity.Netcode;
using UnityEngine;

public class GloveHitbox : MonoBehaviour
{
    [Header("Punch Settings")]
    [Tooltip("Minimum punch velocity (m/s) required to register damage")]
    [SerializeField] private float hitCooldown = 0.25f;

    private Vector3 previousPosition;
    private float currentPunchSpeed;
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
            currentPunchSpeed = (transform.position - previousPosition).magnitude / Time.deltaTime;
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

        if (Time.time - lastHitTime < hitCooldown)
        {
            PunchDebugDisplay.Show($"Contact detected: YES | Speed: {currentPunchSpeed:F2} m/s\nDamage: 0 | Local cooldown");
            return;
        }

        lastHitTime = Time.time;
        PunchDebugDisplay.Show($"Contact detected: YES | Speed: {currentPunchSpeed:F2} m/s\nDamage: waiting for server...");
        targetHealth.ReportPunchAttemptServerRpc(currentPunchSpeed);
    }
}

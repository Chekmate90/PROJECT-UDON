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

    }

    private void OnTriggerEnter(Collider other)
    {
        TestDummy testDummy = other.GetComponentInParent<TestDummy>();
        if (testDummy != null)
        {
            PlayerHealth attackerHealth = GetComponentInParent<PlayerHealth>();
            if (attackerHealth != null && !attackerHealth.IsOwner)
            {
                return;
            }

            testDummy.RegisterPunch(currentPunchSpeed);
            return;
        }

        PlayerHealth targetHealth = other.GetComponentInParent<PlayerHealth>();
        if (targetHealth == null)
        {
            return;
        }

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsClient || !networkManager.IsListening)
        {
            return;
        }

        if (!targetHealth.IsSpawned || targetHealth.OwnerClientId == networkManager.LocalClientId)
        {
            return;
        }

        if (Time.time - lastHitTime < hitCooldown)
        {
            return;
        }

        lastHitTime = Time.time;
        targetHealth.ReportPunchAttemptServerRpc(currentPunchSpeed);
    }
}

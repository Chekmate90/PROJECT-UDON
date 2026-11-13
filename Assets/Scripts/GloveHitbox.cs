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
    private bool isLeftGlove;

    private void Awake()
    {
        Collider gloveCollider = GetComponent<Collider>();
        if (gloveCollider != null)
        {
            gloveCollider.isTrigger = true;
        }

        Rigidbody gloveBody = GetComponent<Rigidbody>();
        if (gloveBody == null)
        {
            gloveBody = gameObject.AddComponent<Rigidbody>();
        }

        gloveBody.isKinematic = true;
        gloveBody.useGravity = false;

        isLeftGlove = gameObject.name.ToLower().Contains("left") ||
                      (transform.parent != null && transform.parent.name.ToLower().Contains("left"));
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

            VRHaptics.TriggerPunchImpulse(isLeftGlove, currentPunchSpeed);
            PlayLocalPunchSound();
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
        VRHaptics.TriggerPunchImpulse(isLeftGlove, currentPunchSpeed);
        PlayLocalPunchSound();
        targetHealth.ReportPunchAttemptServerRpc(currentPunchSpeed);
    }

    private void PlayLocalPunchSound()
    {
        AudioSource audio = GetComponent<AudioSource>();
        if (audio == null)
        {
            audio = gameObject.AddComponent<AudioSource>();
            audio.spatialBlend = 0.15f;
            audio.minDistance = 2.5f;
            audio.maxDistance = 25f;
            audio.playOnAwake = false;
        }

        AudioClip clip = Resources.Load<AudioClip>("BoxingAudio/PunchHeavy");
        if (clip != null && audio != null)
        {
            audio.PlayOneShot(clip, 1.0f);
        }
    }
}

using UnityEngine;

/// <summary>
/// Synchronizes the player's locomotion speed with the Animator.
/// Switches smoothly between the Mixamo Boxing Idle state and the Short Step Forward walk state
/// based on real-time horizontal velocity across both local VR and replicated network players.
/// </summary>
public sealed class PlayerLocomotionAnimation : MonoBehaviour
{
    [Header("Animator Settings")]
    [SerializeField] private Animator animator;
    [SerializeField, Min(0.01f)] private float walkThreshold = 0.15f;
    [SerializeField, Min(1f)] private float speedSmoothing = 8f;

    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int IsWalkingHash = Animator.StringToHash("IsWalking");

    private Vector3 lastPosition;
    private float currentSpeed;

    private void Awake()
    {
        EnsureAnimator();
        lastPosition = transform.position;
    }

    private void OnEnable()
    {
        lastPosition = transform.position;
        currentSpeed = 0f;
    }

    public void EnsureAnimator()
    {
        if (animator == null)
        {
            Transform model = transform.Find("Midans mdol");
            if (model != null)
            {
                animator = model.GetComponent<Animator>();
            }
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
        }

        if (animator != null)
        {
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
    }

    private void Update()
    {
        if (animator == null)
        {
            EnsureAnimator();
            if (animator == null) return;
        }

        Vector3 currentPosition = transform.position;
        Vector3 delta = currentPosition - lastPosition;
        delta.y = 0f; // Horizontal movement only

        float deltaTime = Mathf.Max(0.0001f, Time.deltaTime);
        float rawSpeed = delta.magnitude / deltaTime;
        lastPosition = currentPosition;

        // Smooth speed to filter tracking jitter and network interpolation steps
        currentSpeed = Mathf.Lerp(currentSpeed, rawSpeed, Time.deltaTime * speedSmoothing);
        if (currentSpeed < 0.04f)
        {
            currentSpeed = 0f;
        }

        bool isWalking = currentSpeed > walkThreshold;

        animator.SetFloat(SpeedHash, currentSpeed);
        animator.SetBool(IsWalkingHash, isWalking);
    }
}

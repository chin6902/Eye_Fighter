using KinematicCharacterController;
using UnityEngine;

[RequireComponent(typeof(Animator))]
public class PlayerAnimationController : MonoBehaviour
{
    [Tooltip("Your kinematic character motor (to read current velocity)")]
    public KinematicCharacterMotor Motor;

    [Tooltip("Your player health component (to listen for hits)")]
    public Health PlayerHealth;
    [SerializeField] private Animator _animator;

    [Header("Blend‑Tree Parameter Names")]
    public string MoveXParam = "MoveX";
    public string MoveYParam = "MoveY";

    [Header("Hit Reaction")]
    [Tooltip("Trigger name in your Animator for hit reaction")]
    public string HitTriggerParam = "GetHit";

    [Header("Parry")]
    [Tooltip("Trigger name for parry animation")]
    public string ParryTriggerParam = "Parry";

    [Header("Speed Normalization")]
    [Tooltip("Divide your local velocity by this to get -1…+1 range")]
    public float MaxMoveSpeed = 4f;

    [Header("Footstep Settings")]
    [Tooltip("Minimum speed (0–1) to start playing footsteps.")]
    [Range(0, 0.5f)] public float stepSpeedThreshold = 0.1f;
    [Tooltip("Number of footsteps per unit of movement (higher = faster cadence)")]
    public float stepsPerUnit = 1.2f;
    [Tooltip("Volume of the footstep SFX (0–1)")]
    [Range(0, 1)] public float footstepVolume = 0.8f;

    [Header("Jump SFX")]
    [Tooltip("Volume of the jump SFX (0–1)")]
    [Range(0, 1)] public float jumpVolume = 1f;
    private bool _wasGrounded = true;

    private float _stepTimer;

    private void Start()
    {
        if (PlayerHealth != null)
        {
            PlayerHealth.OnTakeDamage += SetGetHitTrigger;
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.onParryPerformed += OnParryPerformed;
        }
    }

    private void SetGetHitTrigger()
    {
        _animator.SetTrigger(HitTriggerParam);
    }

    private void OnParryPerformed()
    {
        _animator.SetTrigger(ParryTriggerParam);
    }

    private void OnDestroy()
    {
        if (PlayerHealth != null)
        {
            PlayerHealth.OnTakeDamage -= SetGetHitTrigger;
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.onParryPerformed -= OnParryPerformed;
        }
    }

    private void Update()
    {
        if (Motor == null || _animator == null) return;

        Vector3 worldVel = Motor.Velocity;
        Vector3 localVel = transform.InverseTransformDirection(worldVel);

        float vx = Mathf.Clamp(localVel.x / MaxMoveSpeed, -1f, 1f);
        float vy = Mathf.Clamp(localVel.z / MaxMoveSpeed, -1f, 1f);
        float speedNorm = new Vector2(vx, vy).magnitude;

        float speed = new Vector2(vx, vy).magnitude;
        _animator.SetFloat("Speed", speed);

        _animator.SetFloat(MoveXParam, vx);
        _animator.SetFloat(MoveYParam, vy);

        bool isGrounded = Motor.GroundingStatus.IsStableOnGround;
        _animator.SetBool("IsGrounded", isGrounded);

        if (_wasGrounded && !isGrounded)
        {
            SoundManager.PlaySound(SoundType.Jump, jumpVolume);
        }

        if (isGrounded && speedNorm > stepSpeedThreshold)
        {
            float distanceThisFrame = speedNorm * Time.deltaTime;
            _stepTimer += distanceThisFrame;

            if (_stepTimer >= 1f / stepsPerUnit)
            {
                _stepTimer = 0f;
                SoundManager.PlaySound(SoundType.PlayerWalk, footstepVolume);
            }
        }
        else
        {
            _stepTimer = 0f;
        }

        _wasGrounded = isGrounded;
    }

    public void PlayFootstep()
    {
        SoundManager.PlaySound(SoundType.PlayerWalk, footstepVolume);
    }
}

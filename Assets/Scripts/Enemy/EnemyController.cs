using System;
using UnityEngine;

public enum EnemyType { Mushroom, Cactus }

public class EnemyController : MonoBehaviour
{
    IState currentState;
    public EnemyType Type = EnemyType.Mushroom;

    public Transform Player;
    public Animator Animator;

    // Ranges
    public float ChaseRange = 15f;
    public float CirculateRange = 6f;
    public float AttackRange = 1.75f;

    // Speeds
    public float WanderSpeed = 2f;
    public float ChaseSpeed = 4f;
    public float CirculateSpeed = 2f;
    public float AttackSpeed = 3f;
    public float ReturnSpeed = 4.5f;

    // Attack cooldown and slot
    public float AttackCooldown = 2f;
    private float _attackCooldownTimer = 0f;
    private bool attackSlotGranted = false;

    // Knockback & parry
    [Header("Knockback Settings")]
    public bool IsParryable = false;
    public float KnockbackForce = 5f;
    public float KnockbackDuration = 0.5f;
    private bool isKnockedBack = false;
    public Coroutine currentAttackCoroutine;
    public GameObject parryWarningPrefab;

    [Header("GroupSpawn Settings")]
    public bool InCombat { get; private set; } = false;

    public EnemyGroup CurrentGroup;
    public Transform RestrictedArea => CurrentGroup.restrictedArea.transform;


    public bool IsDead { get; private set; } = false;

    void Start()
    {
        Player = GameObject.FindGameObjectWithTag("Player").transform;
        Animator = GetComponentInChildren<Animator>();
        GetComponent<Health>().OnDie += OnDie;

        currentState = new IdleState(this);
        currentState.Enter();
    }

    void Update()
    {
        if (IsDead) return;

        if (_attackCooldownTimer > 0f)
            _attackCooldownTimer -= Time.deltaTime;

        if (isKnockedBack) return; // FSM paused during knockback

        currentState?.Execute();
    }

    private void OnDie()
    {
        IsDead = true;
        EnemyManager.Instance.OnAttackEnd(this);
    }

    public void ChangeState(IState next)
    {
        if (IsDead || isKnockedBack) return; // Don't switch states when dead or knocked back

        currentState?.Exit();
        currentState = next;
        currentState?.Enter();
    }

    public bool HasSlot() => attackSlotGranted;

    public void OnAttackGranted()
    {
        attackSlotGranted = true;
    }

    public bool CanAttack()
    {
        if (attackSlotGranted) return true;
        if (_attackCooldownTimer > 0f) return false;

        bool gotSlot = EnemyManager.Instance.TryRequestAttack(this);
        attackSlotGranted = gotSlot;
        return gotSlot;
    }

    public void FinishAttack()
    {
        EnemyManager.Instance.OnAttackEnd(this);
        attackSlotGranted = false;
        _attackCooldownTimer = AttackCooldown;

        ExitCombat();

        Vector3 flatCenter = RestrictedArea.position;
        flatCenter.y = transform.position.y;

        float dist = Vector3.Distance(transform.position, flatCenter);
        float limit = RestrictedArea.GetComponent<RestrictedAreaController>().areaRadius * 1.5f;

        if (dist > limit)
        {
            ChangeState(new ReturnToGroupState(this));
        }
        else
        {
            ChangeState(new CirculateState(this));
        }
    }

    public void EnterCombat()
    {
        InCombat = true;
    }

    public void ExitCombat()
    {
        InCombat = false;
    }


    public void ApplyKnockback(Vector3 sourcePosition)
    {
        if (IsDead || isKnockedBack) return;

        StartCoroutine(KnockbackRoutine(sourcePosition));
    }

    private System.Collections.IEnumerator KnockbackRoutine(Vector3 sourcePosition)
    {
        isKnockedBack = true;

        Vector3 dir = (transform.position - sourcePosition).normalized;
        dir.y = 0f;

        float timer = KnockbackDuration;

        while (timer > 0f)
        {
            transform.position += dir * KnockbackForce * Time.deltaTime;
            timer -= Time.deltaTime;
            yield return null;
        }

        isKnockedBack = false;

        if (!IsDead)
        {
            ChangeState(new IdleState(this));
        }
    }

    public void InterruptAttack()
    {
        if (currentAttackCoroutine == null)
        {
            Debug.Log("No active attack to interrupt");
            return;
        }

        StopCoroutine(currentAttackCoroutine);
        currentAttackCoroutine = null;

        IsParryable = false;

        var attackCol = GetComponentInChildren<EnemyAttackCollider>(true);
        if (attackCol != null)
        {
            attackCol.GetComponent<Collider>().enabled = false;
        }

        Animator.SetTrigger("Parried");

        EnemyManager.Instance.OnAttackEnd(this);
        attackSlotGranted = false;
        _attackCooldownTimer = AttackCooldown;

        ChangeState(new IdleState(this));
    }


    // Movement helpers
    public void MoveTowards(Vector3 target, float speed)
    {
        Vector3 flatTarget = new Vector3(target.x, transform.position.y, target.z);
        Vector3 dir = (flatTarget - transform.position).normalized;
        if (dir != Vector3.zero)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 10f);

        transform.position += dir * speed * Time.deltaTime;

    }

    public void Move(Vector3 direction, float speed)
    {
        Vector3 dir = new Vector3(direction.x, 0, direction.z).normalized;
        if (dir != Vector3.zero)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 10f);

        transform.position += dir * speed * Time.deltaTime;
    }

    public bool IsOutsideRestrictedArea()
    {
        if (InCombat) return false;

        if (RestrictedArea == null) return false;

        Vector3 flatCenter = RestrictedArea.position;
        flatCenter.y = transform.position.y;

        float dist = Vector3.Distance(transform.position, flatCenter);
        return dist > RestrictedArea.GetComponent<RestrictedAreaController>().areaRadius;
    }

    public void OnAttackSlotRevoked()
    {
       // Debug.Log($"[EnemyController] Attack slot revoked for {name}");

        // Stop any active attack coroutine
        if (currentAttackCoroutine != null)
        {
            try
            {
                StopCoroutine(currentAttackCoroutine);
            }
            catch { }
            currentAttackCoroutine = null;
        }

        // Disable attack hitbox if present
        var attackCol = GetComponentInChildren<EnemyAttackCollider>(true);
        if (attackCol != null)
        {
            var col = attackCol.GetComponent<Collider>();
            if (col != null) col.enabled = false;
        }

        // Clear attackable / parryable state
        IsParryable = false;

        // Clear the granted slot flag and start cooldown so it won't spam requests
        attackSlotGranted = false;
        _attackCooldownTimer = AttackCooldown;

        try
        {
            // If the enemy is too far from its group center, return; else idle/circulate
            if (CurrentGroup != null)
            {
                Vector3 flatCenter = RestrictedArea.position;
                flatCenter.y = transform.position.y;
                float dist = Vector3.Distance(transform.position, flatCenter);
                float limit = RestrictedArea.GetComponent<RestrictedAreaController>().areaRadius * 1.5f;
                if (dist > limit)
                {
                    ChangeState(new ReturnToGroupState(this));
                    return;
                }
            }
        }
        catch { /* defensive: fallthrough to idle if something goes wrong */ }

        // Default fallback state
        ChangeState(new IdleState(this));
    }


    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, ChaseRange);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, CirculateRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, AttackRange);
    }
}

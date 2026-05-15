using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// BossController (updated):
/// - subscribes to BossHealth.OnChargedHit(element, duration)
/// - spawns element.chargedHitEffect for the provided duration and cleans it up
/// - uses SpeedMultiplier (exposed by BossController) — states read ctx.SpeedMultiplier
/// </summary>
public class BossController : MonoBehaviour
{
    [Header("References")]
    public Transform player;
    public BarrierController barrierController;

    [Header("Missile spawn roots (assign 0..N transforms). If empty, missileSpawnRoot or boss transform is used.")]
    public List<Transform> MissileSpawnRoots = new List<Transform>();
    public Transform missileSpawnRoot; 
    public GameObject missilePrefab;

    [Header("Attack collider / timing")]
    public BossAttackCollider AttackCollider; 
    [Tooltip("Animation damage window start time (seconds after animation starts)")]
    public float ThrustAttackWindowStart = 0.2f;
    [Tooltip("Animation damage window end time (seconds after animation starts)")]
    public float ThrustAttackWindowEnd = 0.8f;

    [Header("Movement / Ranges")]
    public float WalkSpeed = 2.5f;
    public float ChaseRange = 30f;
    public float AttackRange = 6f;

    [Header("Timing / Wait")]
    public float MinWait = 0.7f;
    public float MaxWait = 2.0f;

    [Header("Thrust Attack")]
    public float ThrustDistance = 6f;
    public float ThrustDuration = 0.45f;
    public float ThrustSpeed = 12f;
    public int MaxThrustCombo = 3;
    public float ThrustComboDelay = 1f;

    [Header("Missile Attack")]
    public int MissileCount = 4; // legacy default
    public float MissileInterval = 0.25f;
    public float MissileSpawnHeight = 4f;
    public float MissileHorizontalSpread = 1.2f;
    public float MissileSpeed = 8f;
    public float MissileHomingStrength = 6f;

    [Header("Missile Spawn Settings")]
    public int MinMissileCount = 2;
    public int MaxMissileCount = 6;
    public int MissileDamage = 10;

    [Header("General")]
    public float DecisionCooldown = 0.8f;

    [Header("Attack cooldown & barrier recovery")]
    [Tooltip("Time (seconds) after performing an attack during which boss will not start a new attack.")]
    public float AttackCooldownDuration = 60f;
    [Tooltip("Interval (seconds) at which the boss will attempt to recover the barrier if it is down.")]
    public float BarrierRecoverInterval = 60f;

    // runtime
    private bool alive = true;
    [SerializeField] private Animator animator;

    // state instances (public so state classes can reference them)
    public BossIdleState IdleState { get; private set; }
    public BossWalkState WalkState { get; private set; }
    public BossWaitState WaitState { get; private set; }
    public BossThrustState ThrustState { get; private set; }
    public BossMissileState MissileState { get; private set; }
    public BossRecoverBarrierState RecoverBarrierState { get; private set; }

    private IState currentState;

    // cooldown timers
    private float attackCooldownTimer = 0f;

    // recovery accumulator (fires every BarrierRecoverInterval)
    private float recoverAccumulator = 0f;

    // cached boss health for stun/slow/charged hooks
    private BossHealth bossHealth;

    // movement speed multiplier (1 = normal). modified by OnSlowed.
    private float speedMultiplier = 1f;
    private Coroutine slowCoroutine = null;

    // active charged effect instance and cleanup coroutine
    private GameObject activeChargedEffect;
    private Coroutine chargedEffectCleaner;

    // accessors
    public Transform Player => player;
    public Animator Animator => animator;
    public BarrierController BarrierController => barrierController;
    public Transform MissileSpawnRoot => missileSpawnRoot;
    public GameObject MissilePrefab => missilePrefab;

    // Expose multiplier so states can read if needed (e.g. to slow thrust movement)
    public float SpeedMultiplier => speedMultiplier;

    private bool _isAwaitingMiniGameState = false;

    public bool IsAwaitingMiniGameState => _isAwaitingMiniGameState;

    private void Awake()
    {
        animator = GetComponentInChildren<Animator>();

        if (player == null)
        {
            var pgo = GameObject.FindGameObjectWithTag("Player");
            if (pgo != null) player = pgo.transform;
        }

        // instantiate reusable state objects
        IdleState = new BossIdleState(this);
        WalkState = new BossWalkState(this);
        WaitState = new BossWaitState(this);
        ThrustState = new BossThrustState(this);
        MissileState = new BossMissileState(this);
        RecoverBarrierState = new BossRecoverBarrierState(this);

        // find boss health and subscribe to its special-effect events
        bossHealth = GetComponent<BossHealth>();
        if (bossHealth != null)
        {
            bossHealth.OnStunned += HandleBossStunned;
            bossHealth.OnSlowed += HandleBossSlowed;
            // subscribe to charged-hit (element + duration)
            bossHealth.OnChargedHit += HandleChargedHit;
            bossHealth.OnTakeDamagePopUp += HandleBossDamagePopup;

            bossHealth.OnReachedZero += HandleReachedZero;
            bossHealth.OnRecoveredFromMiniGame += HandleRecoveredFromMiniGame;
            bossHealth.OnDie += HandleFinalDeath;
        }
    }

    private void Start()
    {
        if (barrierController != null)
            barrierController.OnBarrierFullyBroken += OnBarrierFullyBroken;

        ChangeState(IdleState);
    }

    private void Update()
    {
        if (!alive || _isAwaitingMiniGameState) return;

        // update attack cooldown
        if (attackCooldownTimer > 0f)
        {
            attackCooldownTimer -= Time.deltaTime;
            if (attackCooldownTimer < 0f) attackCooldownTimer = 0f;
        }

        // update recover accumulator only when barrier controller exists
        if (barrierController != null)
        {
            // if barrier is active, reset accumulator
            if (barrierController.IsBarrierActive())
            {
                recoverAccumulator = 0f;
            }
            else
            {
                // accumulate time while barrier is down
                recoverAccumulator += Time.deltaTime;

                // if accumulated enough, and boss is not currently attacking, initiate recover
                if (recoverAccumulator >= BarrierRecoverInterval)
                {
                    bool isInAttackState = (currentState is BossThrustState) || (currentState is BossMissileState);

                    if (!isInAttackState && currentState != RecoverBarrierState)
                    {
                        // reset accumulator and request recover
                        recoverAccumulator = 0f;
                        ChangeState(RecoverBarrierState);
                    }
                }
            }
        }

        // normal state execution
        currentState?.Execute();
    }

    private void OnDestroy()
    {
        if (barrierController != null)
            barrierController.OnBarrierFullyBroken -= OnBarrierFullyBroken;

        if (bossHealth != null)
        {
            bossHealth.OnStunned -= HandleBossStunned;
            bossHealth.OnSlowed -= HandleBossSlowed;
            bossHealth.OnChargedHit -= HandleChargedHit;
            bossHealth.OnTakeDamagePopUp -= HandleBossDamagePopup;

            bossHealth.OnReachedZero -= HandleReachedZero;
            bossHealth.OnRecoveredFromMiniGame -= HandleRecoveredFromMiniGame;
            bossHealth.OnDie -= HandleFinalDeath;
        }
    }

    private void OnBarrierFullyBroken()
    {
        // Don't force immediate recover here; recoverAccumulator will handle it.
    }

    private void HandleBossStunned(float duration)
    {
        if (currentState is BossStunnedState) return;
        ChangeState(new BossStunnedState(this, duration));
    }

    private void HandleBossSlowed(float multiplier, float duration)
    {
        multiplier = Mathf.Clamp(multiplier, 0.01f, 10f);
        if (slowCoroutine != null) StopCoroutine(slowCoroutine);
        slowCoroutine = StartCoroutine(SlowRoutine(multiplier, duration));
    }

    private IEnumerator SlowRoutine(float multiplier, float duration)
    {
        speedMultiplier = multiplier;
        if (animator != null) animator.speed = multiplier;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            yield return null;
        }

        speedMultiplier = 1f;
        if (animator != null) animator.speed = 1f;
        slowCoroutine = null;
    }

    // This is the immediate visual hook for charged hits.
    // element = which element, duration = how long the charged visual should persist.
    private void HandleChargedHit(GameManager.ElementType element, float duration)
    {
        // cancel any previous charged effect
        if (chargedEffectCleaner != null)
        {
            StopCoroutine(chargedEffectCleaner);
            chargedEffectCleaner = null;
        }
        if (activeChargedEffect != null)
        {
            Destroy(activeChargedEffect);
            activeChargedEffect = null;
        }

        // spawn element-specific charged effect if available on ElementData
        var data = ElementDatabase.Instance?.Get(element);
        if (data != null && data.chargedHitEffect != null)
        {
            // instantiate as child so it follows boss position (looping VFX)
            activeChargedEffect = Instantiate(data.chargedHitEffect, transform);
            // optionally position a bit above boss
            activeChargedEffect.transform.localPosition = Vector3.up * 1.0f;
            // start cleanup coroutine
            chargedEffectCleaner = StartCoroutine(ChargedEffectRoutine(activeChargedEffect, duration));
        }
    }

    private IEnumerator ChargedEffectRoutine(GameObject effectGO, float duration)
    {
        // If the effect has particle systems, ensure they play
        var systems = effectGO.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var ps in systems) ps.Play(true);

        // wait duration (if zero or negative, just play one frame and cleanup immediately)
        if (duration > 0f) yield return new WaitForSeconds(duration);
        else yield return null;

        // stop particle systems gracefully
        foreach (var ps in systems) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        // give it a short grace so stop clears
        yield return new WaitForSeconds(0.25f);

        if (effectGO != null) Destroy(effectGO);
        if (activeChargedEffect == effectGO) activeChargedEffect = null;
        chargedEffectCleaner = null;
    }

    private void HandleBossDamagePopup(int dmg)
    {
        // no-op by default
    }

    public void ChangeState(IState next)
    {
        if (!alive) return;
        if (next == null) return;
        if (next == currentState) return;

        currentState?.Exit();
        currentState = next;
        currentState?.Enter();
    }

    // Movement helpers using the same style as your enemy but multiplied by speedMultiplier
    public void MoveTowards(Vector3 target, float speed)
    {
        float effectiveSpeed = speed * Mathf.Max(0f, speedMultiplier);
        Vector3 flatTarget = new Vector3(target.x, transform.position.y, target.z);
        Vector3 dir = (flatTarget - transform.position).normalized;
        if (dir != Vector3.zero)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 10f);

        transform.position += dir * effectiveSpeed * Time.deltaTime;
    }

    public void Move(Vector3 direction, float speed)
    {
        float effectiveSpeed = speed * Mathf.Max(0f, speedMultiplier);
        Vector3 dir = new Vector3(direction.x, 0, direction.z).normalized;
        if (dir != Vector3.zero)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 10f);

        transform.position += dir * effectiveSpeed * Time.deltaTime;
    }

    public void Die()
    {
        alive = false;
        if (animator != null) animator.SetBool("Die", true);
    }

    // --- attack cooldown API used by states ---
    public bool CanPerformAttack()
    {
        return attackCooldownTimer <= 0f;
    }

    public bool IsOnAttackCooldown() => attackCooldownTimer > 0f;

    /// <summary> Call this when the boss performs any attack. Starts the attack cooldown. </summary>
    public void StartAttackCooldown()
    {
        attackCooldownTimer = AttackCooldownDuration;
        // intentionally DO NOT reset recoverAccumulator here so barrier attempts still happen on fixed interval.
    }

    /*
    // --- Gizmos ---
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.35f);
        Gizmos.DrawSphere(transform.position, ChaseRange);
        Gizmos.color = new Color(1f, 0.4f, 0.4f, 0.45f);
        Gizmos.DrawSphere(transform.position, AttackRange);

        // visualize spawn roots
        Gizmos.color = Color.yellow;
        if (MissileSpawnRoots != null && MissileSpawnRoots.Count > 0)
        {
            foreach (var r in MissileSpawnRoots)
            {
                if (r == null) continue;
                Vector3 spawnPos = r.position + Vector3.up * MissileSpawnHeight;
                Gizmos.DrawSphere(spawnPos, 0.15f);
                Gizmos.DrawLine(transform.position, spawnPos);
            }
        }
        else
        {
            Transform root = missileSpawnRoot != null ? missileSpawnRoot : transform;
            Vector3 spawnPos = root.position + Vector3.up * MissileSpawnHeight;
            Gizmos.DrawSphere(spawnPos, 0.15f);
            Gizmos.DrawLine(transform.position, spawnPos);
        }
    }
    */

    private void OnValidate()
    {
        ChaseRange = Mathf.Max(0.1f, ChaseRange);
        AttackRange = Mathf.Max(0.1f, AttackRange);
        WalkSpeed = Mathf.Max(0f, WalkSpeed);
        MinMissileCount = Mathf.Max(0, MinMissileCount);
        MaxMissileCount = Mathf.Max(MinMissileCount, MaxMissileCount);
    }

    private void HandleReachedZero()
    {
        // mark internal flag
        _isAwaitingMiniGameState = true;

        // switch to death/awaiting state (stops movement/attacks)
        ChangeState(new BossDeathState(this));
    }

    private void HandleRecoveredFromMiniGame()
    {
        if (!_isAwaitingMiniGameState)
        {
            Debug.LogWarning("[BossController] Ignored OnRecoveredFromMiniGame: not awaiting mini-game.");
            return;
        }

        // play revive animation and return to idle behavior
        StartCoroutine(ReviveRoutine());
    }

    private System.Collections.IEnumerator ReviveRoutine()
    {
        // clear the awaiting flag so controller can return to normal once idle state is entered
        _isAwaitingMiniGameState = false;

        if (animator != null)
        {
            // If you used a bool for death pose, clear it so revive animation can play cleanly
            animator.SetBool("Die", false);

            // Trigger the revive animation (ensure "Revive" trigger exists in your animator)
            animator.SetTrigger("Revive");
        }

        // Wait a short moment to let revive animation start (adjust if you want to wait until full animation)
        yield return new WaitForSeconds(2f);

        // return to the idle behavior
        ChangeState(WaitState);
    }

    // Called when boss is finally dead (mini-game cleared -> boss.FinalizeDeath() -> OnDie fired)
    private void HandleFinalDeath()
    {
        _isAwaitingMiniGameState = false;

        Die();

        ChangeState(new BossDeadPermanentState(this));
    }
}

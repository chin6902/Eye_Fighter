using System.Collections;
using UnityEngine;

/// <summary>
/// Pooled projectile with homing/upward arc, lifetime explosion, and robust parry/clear handling.
/// Works with ProjectilePool if present; otherwise falls back to Destroy.
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class Projectile : MonoBehaviour
{
    public GameManager.ElementType Element = GameManager.ElementType.None;

    [Header("Lifetime")]
    public float lifeTime = 6f;

    [Header("Movement / Homing")]
    public float speed = 14f;
    public float homingStrength = 5f;

    [Header("Upward arc")]
    public float upwardStrength = 2f;
    public float upwardDecayTime = 1f;

    [Header("Explosion (on lifetime end or parry)")]
    public bool explodeOnLifeEnd = true;
    public float explosionRadius = 2f;
    public int explosionDamage = 10;
    public LayerMask explosionHitMask = ~0;
    public GameObject explosionVfx = null;

    [Header("Parry options")]
    [Range(0f, 1f)]
    public float parryExplosionChance = 0.5f;
    public float parryRecoverGaugeAmount = 20f;

    [Header("Collision / Spawn")]
    [Tooltip("Small time to ignore immediate collisions after spawn (avoid ground/spawn overlap).")]
    public float spawnGraceTime = 0.08f;

    // pooling runtime hooks (set by ProjectilePool)
    [HideInInspector] public bool IsPooled = false;
    [HideInInspector] public ProjectilePool pool;

    // runtime state
    private Rigidbody _rb;
    private int _damage;
    private EnemyController _owner;
    private Transform _targetTransform;
    private Vector3 _initialTargetPosition;
    private float _spawnTime;
    private bool _initialized;
    private bool _wasHandled = false;

    private Coroutine _lifeCoroutine;

    private const float DESTROY_DELAY = 0.02f;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (_rb == null) Debug.LogError("Projectile requires a Rigidbody.");
    }

    /// <summary>
    /// Called by pool on spawn (or by Instantiate path). Initializes projectile runtime variables.
    /// </summary>
    public void Initialize(Vector3 startVelocity, int dmg, EnemyController ownerEnemy, Transform targetTransform, Vector3 initialTargetPosition)
    {
        _damage = dmg;
        _owner = ownerEnemy;
        _targetTransform = targetTransform;
        _initialTargetPosition = initialTargetPosition;
        _spawnTime = Time.time;
        _initialized = true;
        _wasHandled = false;

        if (_rb == null) _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = false;
        _rb.detectCollisions = true;
        _rb.linearVelocity = startVelocity;

        // start lifetime coroutine (clear any previous)
        if (_lifeCoroutine != null) StopCoroutine(_lifeCoroutine);
        _lifeCoroutine = StartCoroutine(LifeRoutine(lifeTime));
    }

    /// <summary>
    /// Reset visual/physics state before spawn so pooled object behaves like a fresh one.
    /// </summary>
    public void ResetForSpawn()
    {
        _wasHandled = false;
        _initialized = false;
        _owner = null;
        _targetTransform = null;

        // re-enable colliders
        foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = true;

        // re-enable renderers
        foreach (var mr in GetComponentsInChildren<MeshRenderer>(true)) mr.enabled = true;
        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.enabled = true;

        // re-enable particle systems (clear/stop)
        foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
        {
            ps.Clear(true);
            ps.Play(true);
        }

        if (_rb != null)
        {
            _rb.isKinematic = false;
            _rb.detectCollisions = true;
            _rb.angularVelocity = Vector3.zero;
            _rb.linearVelocity = Vector3.zero;
        }
    }

    private IEnumerator LifeRoutine(float lifetime)
    {
        float t = 0f;
        while (t < lifetime)
        {
            t += Time.deltaTime;
            yield return null;
        }

        // lifetime expired
        if (!_wasHandled)
        {
            if (explodeOnLifeEnd) Explode();
            else SafeDestroy("LifeExpired_NoExplode");
        }
    }

    // ---- Explosion / destroy ----

    private void Explode()
    {
        if (_wasHandled) return;
        PrepareForImmediateRemove();

        if (explosionVfx != null) Instantiate(explosionVfx, transform.position, Quaternion.identity);

        Collider[] hits = Physics.OverlapSphere(transform.position, explosionRadius, explosionHitMask);
        foreach (var h in hits)
        {
            if (_owner != null)
            {
                if (h.transform.IsChildOf(_owner.transform) || h.transform == _owner.transform) continue;
            }

            var health = h.GetComponentInParent<Health>();
            if (health != null && health.isPlayer)
            {
                health.DealDamage(explosionDamage);
            }
        }

        ReturnToPoolOrDestroy(DESTROY_DELAY);
    }

    private void Explode(int damageOverride)
    {
        if (_wasHandled) return;
        PrepareForImmediateRemove();

        if (explosionVfx != null) Instantiate(explosionVfx, transform.position, Quaternion.identity);

        int dmg = damageOverride > 0 ? damageOverride : explosionDamage;
        Collider[] hits = Physics.OverlapSphere(transform.position, explosionRadius, explosionHitMask);
        foreach (var h in hits)
        {
            if (_owner != null)
            {
                if (h.transform.IsChildOf(_owner.transform) || h.transform == _owner.transform) continue;
            }

            var health = h.GetComponentInParent<Health>();
            if (health != null && health.isPlayer)
            {
                health.DealDamage(dmg);
            }
        }

        ReturnToPoolOrDestroy(DESTROY_DELAY);
    }

    /// <summary>
    /// Make the object inert and unregister UI; used before destruction or returning to pool.
    /// </summary>
    private void PrepareForImmediateRemove()
    {
        if (!_wasHandled) _wasHandled = true;

        // disable colliders & stop physics
        foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = false;

        if (_rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.isKinematic = true;
            _rb.detectCollisions = false;
        }

        // hide visuals
        foreach (var mr in GetComponentsInChildren<MeshRenderer>(true)) mr.enabled = false;
        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.enabled = false;

        foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        // cancel lifetime coroutine
        if (_lifeCoroutine != null) StopCoroutine(_lifeCoroutine);
        _lifeCoroutine = null;

        // unregister UI
        if (DefensiveMiniGame.Instance != null)
            DefensiveMiniGame.Instance.UnregisterProjectile(this);

        // stop other coroutines on projectile
        StopAllCoroutines();
    }

    private void SafeDestroy(string reason = "SafeDestroy")
    {
        PrepareForImmediateRemove();
        ReturnToPoolOrDestroy(DESTROY_DELAY);
    }

    private void ReturnToPoolOrDestroy(float delay = 0f)
    {
        if (IsPooled && pool != null)
        {
            // delay a tiny bit so physics callbacks finish; we used PrepareForImmediateRemove to make it inert
            if (delay <= 0f)
            {
                pool.Despawn(this);
            }
            else
            {
                StartCoroutine(DespawnDelayed(delay));
            }
        }
        else
        {
            Destroy(gameObject, delay);
        }
    }

    private IEnumerator DespawnDelayed(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (pool != null) pool.Despawn(this);
        else Destroy(gameObject);
    }

    // ---- Player interactions ----

    /// <summary>
    /// Melee parry called by player parry logic (recoverGauge awarded regardless).
    /// explosionChance = probability it still explodes.
    /// </summary>
    public bool ParryByMelee(float recoverGauge, float explosionChance, int explosionDamageOverride, Transform parryOrigin)
    {
        if (_wasHandled) return false;

        PrepareForImmediateRemove();

        if (GameManager.Instance != null && recoverGauge > 0f)
            GameManager.Instance.RecoverDefensiveGauge(recoverGauge);

        bool explodeNow = Random.value < Mathf.Clamp01(explosionChance);
        if (explodeNow)
        {
            if (explosionDamageOverride > 0) Explode(explosionDamageOverride);
            else Explode();
            return true;
        }

        SafeDestroy("Parried_NoExplode");
        return true;
    }

    /// <summary>
    /// Gaze clear — awards gauge and stores one charge
    /// </summary>
    public bool ClearByGaze(float awardAmount = 20f, float explosionChanceAfterClear = 0f)
    {
        if (_wasHandled) return false;

        PrepareForImmediateRemove();

        if (awardAmount > 0f && GameManager.Instance != null)
            GameManager.Instance.RecoverDefensiveGauge(awardAmount);

        // award one charge
        ProjectileChargeManager.Instance?.AddCharge();

        SafeDestroy("GazeCleared");
        return true;
    }



    // ---- Movement ----

    private void FixedUpdate()
    {
        if (!_initialized || _rb == null) return;

        float elapsed = Time.time - _spawnTime;
        float upwardFactor = (upwardDecayTime > 0f) ? Mathf.Clamp01(1f - (elapsed / upwardDecayTime)) : 0f;

        Vector3 currentTargetPos = (_targetTransform != null) ? _targetTransform.position : _initialTargetPosition;
        Vector3 toTarget = currentTargetPos - transform.position;
        if (toTarget.sqrMagnitude <= 0.0001f) return;

        Vector3 toTargetDir = toTarget.normalized;
        Vector3 biasedDir = (toTargetDir + Vector3.up * upwardStrength * upwardFactor).normalized;
        Vector3 desiredVel = biasedDir * speed;

        float t = Mathf.Clamp01(homingStrength * Time.fixedDeltaTime);
        _rb.linearVelocity = Vector3.Lerp(_rb.linearVelocity, desiredVel, t);

        if (_rb.linearVelocity.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(_rb.linearVelocity.normalized);
    }

    // ---- Collisions ----

    private void OnTriggerEnter(Collider other)
    {
        if (_wasHandled) return;

        if (_owner != null)
        {
            if (other.transform.IsChildOf(_owner.transform) || other.transform == _owner.transform)
                return;
        }

        var health = other.GetComponentInParent<Health>();
        if (health != null && health.isPlayer)
        {
            health.DealDamage(_damage);
            SafeDestroy("HitPlayer");
            return;
        }

        if (other.isTrigger) return;

        if (Time.time - _spawnTime < spawnGraceTime) return;

        Explode();
    }

    private void OnEnable()
    {
        // projectile was (re)activated → show its segment if the mini-game already registered it
        if (DefensiveMiniGame.Instance != null)
            DefensiveMiniGame.Instance.ShowSegmentForProjectile(this);
    }

    private void OnDisable()
    {
        // projectile was deactivated → hide its associated UI so it won't linger
        if (DefensiveMiniGame.Instance != null)
            DefensiveMiniGame.Instance.HideSegmentForProjectile(this);
    }
}

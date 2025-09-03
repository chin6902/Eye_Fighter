using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Simple pool for Projectile instances. Prefab must have a Projectile component.
/// Call Spawn(...) to get a projectile, and the projectile will return itself to the pool when it Explodes / is Cleared / destroyed.
/// </summary>
public class ProjectilePool : MonoBehaviour
{
    public static ProjectilePool Instance { get; private set; }

    [Tooltip("Projectile prefab (must contain Projectile component).")]
    public Projectile projectilePrefab;

    [Tooltip("Initial pool size (prewarm).")]
    public int initialSize = 20;

    [Tooltip("Parent transform for pooled objects (optional).")]
    public Transform poolParent;

    private readonly Queue<Projectile> _queue = new();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        if (poolParent == null) poolParent = this.transform;

        // prewarm
        for (int i = 0; i < Mathf.Max(0, initialSize); i++)
            AddNewToPool();
    }

    private Projectile AddNewToPool()
    {
        if (projectilePrefab == null) return null;
        var go = Instantiate(projectilePrefab.gameObject, poolParent);
        go.SetActive(false);
        var proj = go.GetComponent<Projectile>();
        proj.IsPooled = true;
        proj.pool = this;
        _queue.Enqueue(proj);
        return proj;
    }

    /// <summary>
    /// Spawn a projectile. This sets transform, activates the GO, calls Initialize and registers with DefensiveMiniGame.
    /// </summary>
    public Projectile Spawn(Vector3 position, Quaternion rotation, Vector3 startVelocity, int damage, EnemyController owner,
                             Transform targetTransform = null, Vector3? initialTargetPosition = null, float lifeTimeOverride = -1f)
    {
        Projectile p;
        if (_queue.Count > 0)
        {
            p = _queue.Dequeue();
            if (p == null) return Spawn(position, rotation, startVelocity, damage, owner, targetTransform, initialTargetPosition, lifeTimeOverride);
        }
        else
        {
            p = AddNewToPool();
            if (p == null) return null;
        }

        // prepare and activate
        p.gameObject.SetActive(true);
        p.transform.SetParent(null);
        p.transform.position = position;
        p.transform.rotation = rotation;

        // reset internal state & components
        p.ResetForSpawn();

        // apply lifeTime override if requested
        if (lifeTimeOverride > 0f) p.lifeTime = lifeTimeOverride;

        // choose initial target pos
        Vector3 initPos = initialTargetPosition ?? (targetTransform != null ? targetTransform.position : position + p.transform.forward * 5f);
        p.Initialize(startVelocity, damage, owner, targetTransform, initPos);

        // register with defensive mini game UI (persistent while projectile exists)
        DefensiveMiniGame.RegisterProjectileStatic(p);

        return p;
    }

    /// <summary>
    /// Return projectile to pool. If pool doesn't exist or GO destroyed, falls back to Destroy.
    /// </summary>
    public void Despawn(Projectile p)
    {
        if (p == null) return;

        // safety: fully deactivate the object and put back under poolParent
        p.gameObject.SetActive(false);
        p.transform.SetParent(poolParent, false);

        // reset optional visual state (ResetForSpawn will be called next time)
        _queue.Enqueue(p);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}

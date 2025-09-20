using System.Collections;
using UnityEngine;

/// <summary>
/// Boss missile attack state:
/// - picks a random number of missiles between MinMissileCount and MaxMissileCount (inclusive)
/// - selects spawn roots in order from MissileSpawnRoots (falls back to single root)
/// - spawns each missile with slight delay (MissileInterval)
/// - uses ProjectilePool if available (and configures returned Projectile)
/// - 1 second pre-delay before spawning missiles (telegraph)
/// </summary>
public class BossMissileState : IState
{
    private BossController ctx;
    private Coroutine routine;

    public BossMissileState(BossController controller)
    {
        ctx = controller;
    }

    public void Enter()
    {
        if (ctx.Animator != null)
            ctx.Animator.SetTrigger("AttackMissile");

        routine = ctx.StartCoroutine(DoMissiles());
    }

    public void Execute() { }

    public void Exit()
    {
        if (routine != null)
            ctx.StopCoroutine(routine);
        routine = null;
    }

    private IEnumerator DoMissiles()
    {
        // 1s pre-delay (telegraph)
        yield return new WaitForSeconds(1f);

        // determine count
        int minCount = Mathf.Max(0, ctx.MinMissileCount);
        int maxCount = Mathf.Max(minCount, ctx.MaxMissileCount);
        int count = (maxCount > minCount) ? Random.Range(minCount, maxCount + 1) : minCount;
        if (count == 0) count = Mathf.Max(1, ctx.MissileCount);

        // decide roots array in order
        Transform[] roots;
        if (ctx.MissileSpawnRoots != null && ctx.MissileSpawnRoots.Count > 0)
        {
            roots = ctx.MissileSpawnRoots.ToArray();
        }
        else
        {
            roots = new Transform[] { ctx.MissileSpawnRoot != null ? ctx.MissileSpawnRoot : ctx.transform };
        }

        // spawn in order from the roots list (wrap around if count > roots.Length)
        int rootIndex = 0;
        for (int i = 0; i < count; i++)
        {
            Transform root = roots[rootIndex];
            if (root == null) root = ctx.transform;

            Vector3 offset = new Vector3(
                Random.Range(-ctx.MissileHorizontalSpread, ctx.MissileHorizontalSpread),
                ctx.MissileSpawnHeight,
                Random.Range(-ctx.MissileHorizontalSpread, ctx.MissileHorizontalSpread)
            );

            Vector3 spawnPos = root.position + offset;
            Quaternion spawnRot = Quaternion.identity;
            Vector3 startVelocity = Vector3.zero;
            Vector3 initialTarget = ctx.Player != null ? ctx.Player.position : spawnPos + Vector3.forward * 5f;

            if (ProjectilePool.Instance != null && ctx.MissilePrefab != null)
            {
                // use pool and configure the returned projectile
                var proj = ProjectilePool.Instance.Spawn(spawnPos, spawnRot, startVelocity, ctx.MissileDamage, null, ctx.Player, initialTarget, -1f);
                if (proj != null)
                {
                    proj.speed = ctx.MissileSpeed;
                    proj.homingStrength = ctx.MissileHomingStrength;
                    // set element if appropriate:
                    // proj.Element = GameManager.ElementType.None;
                }
            }
            else
            {
                // fallback: instantiate prefab
                if (ctx.MissilePrefab != null)
                {
                    GameObject go = Object.Instantiate(ctx.MissilePrefab, spawnPos, spawnRot);
                    var proj = go.GetComponent<Projectile>();
                    if (proj == null) proj = go.AddComponent<Projectile>();

                    proj.speed = ctx.MissileSpeed;
                    proj.homingStrength = ctx.MissileHomingStrength;
                    proj.Element = GameManager.ElementType.None;
                    proj.Initialize(startVelocity, ctx.MissileDamage, null, ctx.Player, initialTarget);
                }
            }

            // increment root index (order), wrap around
            rootIndex = (rootIndex + 1) % roots.Length;

            // interval before next missile
            yield return new WaitForSeconds(ctx.MissileInterval);
        }

        // optionally trigger attack cooldown here (if missile volley counts as a performed attack)
        // ctx.StartAttackCooldown();

        // small pause then decide next state
        yield return new WaitForSeconds(ctx.DecisionCooldown);

        if (ctx.Player != null && Vector3.Distance(ctx.transform.position, ctx.Player.position) <= ctx.AttackRange)
            ctx.ChangeState(ctx.WaitState);
        else
            ctx.ChangeState(ctx.WalkState);
    }
}

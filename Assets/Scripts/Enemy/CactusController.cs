using System.Collections;
using Unity.AppUI.Core;
using UnityEngine;

public class CactusController : EnemyController
{
    [Header("Ranged Attack - Tuning")]
    [Tooltip("Projectile prefab (requires Projectile.cs or similar).")]
    public GameObject projectilePrefab = null;

    [Tooltip("Spawn point for projectile (child transform).")]
    public Transform projectileSpawn = null;

    [Tooltip("Projectile speed in world units/second.")]
    public float projectileSpeed = 14f;

    [Tooltip("Damage dealt by projectile.")]
    public int projectileDamage = 10;

    [Tooltip("Lifetime of projectile in seconds.")]
    public float projectileLifeTime = 6f;

    [Tooltip("Vertical offset added to aim target (useful for arcs).")]
    public float aimVerticalOffset = 0.3f;

    [Tooltip("Time (seconds) of wind-up before spawning the projectile (telegraph).")]
    public float rangedWindup = 0.35f;

    [Tooltip("How long to wait after firing before calling FinishAttack.")]
    public float postAttackDelay = 0.5f;

    [Tooltip("If true, projectiles can be parried (player can reflect).")]
    public bool projectileParryable = false;

    [Header("Optional Movement Overrides")]
    [Tooltip("Speed used while approaching to attack (if different).")]
    public float approachSpeedOverride = 3f;

    // Start ranged attack entry point (keeps pattern same as mushroom attack)
    public void StartRangedAttack()
    {
        if (currentAttackCoroutine != null) return;
        currentAttackCoroutine = StartCoroutine(RangedAttackRoutine());
    }

    private IEnumerator RangedAttackRoutine()
    {
        // Face the player
        Vector3 lookDir = (Player.position - transform.position).normalized;
        lookDir.y = 0f;
        if (lookDir != Vector3.zero) transform.rotation = Quaternion.LookRotation(lookDir);

        // Trigger same "Attack" trigger so the animator can reuse clip (or override on prefab)
        if (Animator != null) Animator.SetTrigger("Attack");

        // Optional short parry warning (keeps compatibility with mushroom actions)
        if (projectileSpawn != null && parryWarningPrefab != null)
        {
            var warn = Instantiate(parryWarningPrefab, projectileSpawn.position + Vector3.up * 1.3f, projectileSpawn.rotation);
            Destroy(warn, 0.35f);
        }

        // Windup (allow animation/telegraph)
        float t = 0f;
        while (t < rangedWindup)
        {
            t += Time.deltaTime;
            yield return null;
        }

        // Spawn projectile aimed at player
        if (ProjectilePool.Instance != null && projectilePrefab != null)
        {
            Vector3 spawnPos = projectileSpawn.position;
            Vector3 targetPos = Player.position + Vector3.up * aimVerticalOffset;
            Vector3 dir = (targetPos - spawnPos).normalized;

            // spawn from pool (pool must have projectilePrefab assigned in inspector)
            var proj = ProjectilePool.Instance.Spawn(spawnPos, Quaternion.LookRotation(dir), dir * projectileSpeed, projectileDamage, this, Player.transform, targetPos, projectileLifeTime);

            // returned projectile is already registered with DefensiveMiniGame by pool
        }

        // Post attack delay to finish animation
        float elapsed = 0f;
        while (elapsed < postAttackDelay)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        // Finish attack (release slot, set cooldown etc.)
        FinishAttack();

        currentAttackCoroutine = null;
    }
}

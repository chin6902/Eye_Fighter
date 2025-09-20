using UnityEngine;

public class PlayerCombat : MonoBehaviour
{
    [Header("Parry Settings")]
    public float ParryRadius = 3f;
    [SerializeField] private GameObject parryEffectPrefab;

    [Header("Parry Feedback")]
    [Tooltip("Player becomes invincible for this duration after a successful parry.")]
    [SerializeField] private float ParryInvincibilityDuration = 1f;

    private void Update()
    {
        if (GameManager.Instance.isPaused || GameManager.Instance.IsGazeModeActive())
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.F))
        {
            TryParry();
        }
    }

    private void TryParry()
    {
        if (!GameManager.Instance.TryUseParry())
        {
            return;
        }

        if (parryEffectPrefab != null)
        {
            Instantiate(
                parryEffectPrefab,
                transform.position + Vector3.up * 1f,
                Quaternion.identity,
                transform
            );
            SoundManager.PlaySound(SoundType.Barrier, 0.5f);
        }

        Collider[] hits = Physics.OverlapSphere(transform.position, ParryRadius);

        bool parrySucceeded = false;

        foreach (var hit in hits)
        {
            // Normal enemy: interrupt + knockback
            var enemy = hit.GetComponentInParent<EnemyController>();
            if (enemy != null && enemy.IsParryable && enemy.currentAttackCoroutine != null)
            {
                enemy.InterruptAttack();
                enemy.ApplyKnockback(transform.position);
                GameManager.Instance.GrantUnlimitedSkill();
                GameManager.Instance.RecoverDefensiveGauge(20f);

                //parrySucceeded = true;
                continue;
            }

            // Boss: interrupt only, NO knockback — keep this block minimal and explicit
            var boss = hit.GetComponentInParent<BossController>();
            if (boss != null)
            {
                GameManager.Instance.GrantUnlimitedSkill();
                GameManager.Instance.RecoverDefensiveGauge(20f);

                parrySucceeded = true;
                continue;
            }

            // Projectiles in radius: try melee-parry them
            var proj = hit.GetComponentInParent<Projectile>();
            if (proj != null)
            {
                float recoverAmount = 20f;
                float explosionChance = 0.50f;
                int explosionDamage = 8;

                bool handled = proj.ParryByMelee(recoverAmount, explosionChance, explosionDamage, transform);
                if (handled)
                {
                    GameManager.Instance.RecoverDefensiveGauge(20f);
                    parrySucceeded = true;
                }
            }
        }

        // If any successful parry occurred for boss and projectiles, make the player invincible for the configured duration
        if (parrySucceeded)
        {
            var health = GetComponent<Health>();
            if (health != null)
            {
                health.SetInvincibleFor(ParryInvincibilityDuration);
            }
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, ParryRadius);
    }
#endif
}

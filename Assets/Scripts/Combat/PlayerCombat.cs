using UnityEngine;

public class PlayerCombat : MonoBehaviour
{
    [Header("Parry Settings")]
    public float ParryRadius = 3f;
    [SerializeField] private GameObject parryEffectPrefab;

    private void Update()
    {
        if(GameManager.Instance.isPaused || GameManager.Instance.IsGazeModeActive())
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
        if(!GameManager.Instance.TryUseParry())
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

        foreach (var hit in hits)
        {
            var enemy = hit.GetComponentInParent<EnemyController>();
            if (enemy != null && enemy.IsParryable && enemy.currentAttackCoroutine != null)
            {
                enemy.InterruptAttack();
                enemy.ApplyKnockback(transform.position);
                GameManager.Instance.GrantUnlimitedSkill();
                GameManager.Instance.RecoverDefensiveGauge(20f); // existing reward
            }

            // Projectiles in radius: try melee-parry them
            var proj = hit.GetComponentInParent<Projectile>();
            if (proj != null)
            {
                // parameters: recover 20 gauge on success, 50% chance to still explode and deal 8 damage (tune as needed)
                float recoverAmount = 20f;
                float explosionChance = 0.50f;
                int explosionDamage = 8;

                bool handled = proj.ParryByMelee(recoverAmount, explosionChance, explosionDamage, transform);
                if (handled)
                {
                    
                }
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

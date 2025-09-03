using UnityEngine;

[RequireComponent(typeof(Collider))]
public class EnemyAttackCollider : MonoBehaviour
{
    [Tooltip("Damage dealt by this attack")] public int damage = 20;
    private EnemyController enemy;

    private void Awake()
    {
        enemy = GetComponentInParent<EnemyController>();
        var col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            var playerHealth = other.GetComponentInParent<Health>();
            if (playerHealth != null)
            {
                playerHealth.DealDamage(damage);
            }
        }
    }
}
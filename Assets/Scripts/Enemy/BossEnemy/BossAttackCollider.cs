using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class BossAttackCollider : MonoBehaviour
{
    [Tooltip("Damage dealt by this attack")]
    public int damage = 20;

    private Collider _collider;
    private bool _canDamage;

    private void Awake()
    {
        _collider = GetComponent<Collider>();
        _collider.isTrigger = true;
        _collider.enabled = false;
        _canDamage = false;
    }

    // Public API ----------------------------------------------------------
    public void EnableDamage()
    {
        _canDamage = true;
        _collider.enabled = true;
    }

    public void DisableDamage()
    {
        _canDamage = false;
        _collider.enabled = false;
    }

    public IEnumerator EnableDamageFor(float seconds)
    {
        EnableDamage();
        yield return new WaitForSeconds(seconds);
        DisableDamage();
    }

    // startDelay = how long to wait before enabling, windowDuration = how long collider stays active
    public IEnumerator EnableWindow(float startDelay, float windowDuration)
    {
        if (startDelay > 0f)
            yield return new WaitForSeconds(startDelay);

        EnableDamage();

        if (windowDuration > 0f)
            yield return new WaitForSeconds(windowDuration);

        DisableDamage();
    }

    // Methods you can call directly from an animation event
    public void AttackHitStart() => EnableDamage();
    public void AttackHitEnd() => DisableDamage();

    // Collision handling -------------------------------------------------
    private void OnTriggerEnter(Collider other)
    {
        if (!_canDamage) return;
        if (!other.CompareTag("Player")) return;

        var playerHealth = other.GetComponentInParent<Health>();
        if (playerHealth != null)
        {
            playerHealth.DealDamage(damage);
        }
    }
}

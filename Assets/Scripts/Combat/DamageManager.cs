using UnityEngine;

public class DamageManager : MonoBehaviour
{
    public static DamageManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// Calculate and apply damage to the given Health component.
    /// </summary>
    /// <param name="targetHealth">The Health on the enemy.</param>
    /// <param name="attackerElement">The player’s chosen element.</param>
    /// <param name="defenderElement">The enemy’s element (set on their Health or another component).</param>
    /// <param name="accuracy">0–1 accuracy from your gaze trace.</param>
    public void DealElementalDamage(
        Health targetHealth,
        GameManager.ElementType attackerElement,
        GameManager.ElementType defenderElement,
        float accuracy)
    {
        if (targetHealth == null) return;

        int baseDamage;
        // 1) Effective matchup: full base 100
        if (IsEffective(attackerElement, defenderElement))
        {
            baseDamage = 100;
            // scale by accuracy
            float dmg = baseDamage * Mathf.Clamp01(accuracy);
            targetHealth.DealDamage(Mathf.RoundToInt(dmg));
        }
        // 2) Same element: half base
        else if (attackerElement == defenderElement)
        {
            baseDamage = 50;
            targetHealth.DealDamage(baseDamage);
        }
        // 3) Ineffective matchup: tiny 1
        else
        {
            baseDamage = 1;
            targetHealth.DealDamage(baseDamage);
        }
    }

    /// <summary>
    /// Returns true if attackerElement is strong vs defenderElement.
    /// Fire > Electric, Electric > Water, Water > Fire.
    /// </summary>
    private bool IsEffective(
        GameManager.ElementType attacker,
        GameManager.ElementType defender)
    {
        return (attacker == GameManager.ElementType.Fire && defender == GameManager.ElementType.Electric)
            || (attacker == GameManager.ElementType.Electric && defender == GameManager.ElementType.Water)
            || (attacker == GameManager.ElementType.Water && defender == GameManager.ElementType.Fire);
    }
}

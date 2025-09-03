using System.Collections;
using UnityEngine;

public class RangedAttackState : IState
{
    EnemyController ctx;

    public RangedAttackState(EnemyController c)
    {
        ctx = c;
    }

    public void Enter()
    {
        // Face player immediately
        Vector3 lookDir = (ctx.Player.position - ctx.transform.position).normalized;
        lookDir.y = 0;
        if (lookDir != Vector3.zero)
            ctx.transform.rotation = Quaternion.LookRotation(lookDir);

        // Start the cactus ranged attack if the enemy is a cactus
        // If ctx is not a cactus, fall back (safety)
        if (ctx is CactusController cactus)
        {
            cactus.StartRangedAttack();
        }
        else
        {
            // Fallback: if accidentally used for non-cactus, trigger melee attack to avoid breaking flow.
            ctx.Animator.SetTrigger("Attack");
            ctx.currentAttackCoroutine = ctx.StartCoroutine(DefaultFallbackAttack());
        }
    }

    private IEnumerator DefaultFallbackAttack()
    {
        // simple fallback to mimic melee timing so game doesn't hang if misassigned
        yield return new WaitForSeconds(0.5f);
        ctx.FinishAttack();
    }

    public void Execute()
    {
        if (ctx.IsDead) return;
        // Ranged attack coroutine handles the flow; nothing to roam here.
    }

    public void Exit() { }
}

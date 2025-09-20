using UnityEngine;

public class BossWalkState : IState
{
    private BossController ctx;

    public BossWalkState(BossController controller)
    {
        ctx = controller;
    }

    public void Enter()
    {
        if (ctx.Animator != null)
        {
            ctx.Animator.SetBool("Idle", false);
            ctx.Animator.SetBool("Walk", true);
        }
    }

    public void Execute()
    {
        if (ctx.Player == null)
        {
            ctx.ChangeState(ctx.IdleState);
            return;
        }

        float dist = Vector3.Distance(ctx.transform.position, ctx.Player.position);

        // If we are inside attack range, decide what to do depending on cooldown/intent
        if (dist <= ctx.AttackRange)
        {
            // If boss can attack right away, pick an attack
            if (ctx.CanPerformAttack())
            {
                if (Random.value < 0.5f)
                    ctx.ChangeState(ctx.ThrustState);
                else
                    ctx.ChangeState(ctx.MissileState);
                return;
            }

            // If still cooling down, decide to idle (if close) or close the gap (if mid-range)
            float stopDistance = ctx.AttackRange * 0.7f; // close-enough threshold
            if (dist <= stopDistance)
            {
                ctx.ChangeState(ctx.IdleState);
            }
            else
            {
                ctx.MoveTowards(ctx.Player.position, ctx.WalkSpeed);
            }
            return;
        }

        // Not in attack range but within chase range
        if (dist <= ctx.ChaseRange)
        {
            ctx.MoveTowards(ctx.Player.position, ctx.WalkSpeed);
            return;
        }

        // Out of chase range -> idle
        ctx.ChangeState(ctx.IdleState);
    }

    public void Exit()
    {
        if (ctx.Animator != null)
            ctx.Animator.SetBool("Walk", false);
    }
}

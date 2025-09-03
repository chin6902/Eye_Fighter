using UnityEngine;

public class ApproachState : IState
{
    EnemyController ctx;

    public ApproachState(EnemyController c) { ctx = c; }

    public void Enter() { }

    public void Execute()
    {
        if (ctx.IsDead) return;

        float dist = Vector3.Distance(ctx.transform.position, ctx.Player.position);

        if (dist > ctx.ChaseRange)
        {
            ctx.ChangeState(new ChaseState(ctx));
            return;
        }

        if (dist <= ctx.AttackRange)
        {
            if (ctx is CactusController)
            {
                ctx.ChangeState(new RangedAttackState(ctx));
            }
            else
            {
                ctx.ChangeState(new AttackState(ctx));
            }

            return;
        }

        ctx.MoveTowards(ctx.Player.position, ctx.AttackSpeed);

        if (ctx.IsOutsideRestrictedArea())
        {
            ctx.ChangeState(new ReturnToGroupState(ctx));
            return;
        }
    }

    public void Exit() { }
}

using UnityEngine;

public class ChaseState : IState
{
    EnemyController ctx;

    public ChaseState(EnemyController c)
    {
        ctx = c;
    }

    public void Enter() 
    {
        ctx.EnterCombat();
    }

    public void Execute()
    {
        if (ctx.IsDead) return;

        float distToPlayer = Vector3.Distance(ctx.transform.position, ctx.Player.position);

        // ✅ If player out of chase range → idle
        if (distToPlayer > ctx.ChaseRange)
        {
            ctx.ChangeState(new IdleState(ctx));
            return;
        }

        // ✅ If within circulate → try to attack
        if (distToPlayer <= ctx.CirculateRange)
        {
            if (ctx.CanAttack())
            {
                ctx.ChangeState(new CirculateState(ctx));
            }
            else
            {
                ctx.ChangeState(new CirculateState(ctx));
            }
            return;
        }

        // ✅ NEW: If too far from group → give up and return
        Vector3 flatCenter = ctx.RestrictedArea.position;
        flatCenter.y = ctx.transform.position.y;
        float distToCenter = Vector3.Distance(ctx.transform.position, flatCenter);

        float limit = ctx.RestrictedArea.GetComponent<RestrictedAreaController>().areaRadius * 1.5f;

        if (distToCenter > limit)
        {
            ctx.ExitCombat();
            ctx.ChangeState(new ReturnToGroupState(ctx));
            return;
        }

        // ✅ Else chase
        ctx.MoveTowards(ctx.Player.position, ctx.ChaseSpeed);
    }


    public void Exit() { }
}

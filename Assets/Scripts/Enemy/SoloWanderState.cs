using UnityEngine;

public class SoloWanderState : IState
{
    EnemyController ctx;
    float timer;
    Vector3 target;

    public SoloWanderState(EnemyController c)
    {
        ctx = c;
    }

    public void Enter()
    {
        timer = Random.Range(2f, 4f);
        Vector3 dir = Random.insideUnitSphere;
        dir.y = 0;
        dir.Normalize();
        float radius = ctx.RestrictedArea.GetComponent<RestrictedAreaController>().areaRadius;

        target = ctx.transform.position + dir * Random.Range(0.5f, radius * 0.8f);
    }

    public void Execute()
    {
        if (ctx.IsDead) return;


        // If player enters ChaseRange Å® chase immediately
        if (Vector3.Distance(ctx.transform.position, ctx.Player.position) <= ctx.ChaseRange)
        {
            ctx.ChangeState(new ChaseState(ctx));
            return;
        }

        if (ctx.IsOutsideRestrictedArea())
        {
            ctx.ChangeState(new ReturnToGroupState(ctx));
            return;
        }

        // Otherwise, wander towards target
        ctx.MoveTowards(target, ctx.WanderSpeed);
        timer -= Time.deltaTime;

        if (timer <= 0f || Vector3.Distance(ctx.transform.position, target) < 0.5f)
        {
            ctx.ChangeState(new IdleState(ctx));
        }
    }

    public void Exit() { }
}

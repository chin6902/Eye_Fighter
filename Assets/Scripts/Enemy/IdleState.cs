using UnityEngine;

public class IdleState : IState
{
    EnemyController ctx;
    float idleTime;

    public IdleState(EnemyController c) => ctx = c;

    public void Enter() => idleTime = Random.Range(1f, 3f);

    public void Execute()
    {
        if (ctx.IsDead) return;


        idleTime -= Time.deltaTime;

        if (Vector3.Distance(ctx.transform.position, ctx.Player.position) <= ctx.ChaseRange)
            ctx.ChangeState(new ChaseState(ctx));

        if (ctx.IsOutsideRestrictedArea())
        {
            ctx.ChangeState(new ReturnToGroupState(ctx));
            return;
        }

        if (idleTime <= 0f)
        {
            ctx.ChangeState(new SoloWanderState(ctx));
        }
    }

    public void Exit() { }
}

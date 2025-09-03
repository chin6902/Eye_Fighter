using UnityEngine;

public class ReturnToGroupState : IState
{
    EnemyController ctx;

    public ReturnToGroupState(EnemyController c) { ctx = c; }

    public void Enter() { }

    public void Execute()
    {
        if (ctx.IsDead) return;

        Vector3 center = ctx.RestrictedArea.transform.position;
        Vector3 toCenter = (center - ctx.transform.position).normalized;
        float angleOffset = Random.Range(-15f, 15f);
        Vector3 randomDir = Quaternion.Euler(0, angleOffset, 0) * toCenter;

        ctx.Move(randomDir, ctx.ReturnSpeed);

        float dist = Vector3.Distance(ctx.transform.position, center);
        if (dist <= 10f)
        {
            ctx.ChangeState(new IdleState(ctx));
        }
    }

    public void Exit() { }
}

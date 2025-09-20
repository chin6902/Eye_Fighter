using UnityEngine;

public class BossIdleState : IState
{
    private BossController ctx;

    public BossIdleState(BossController controller)
    {
        ctx = controller;
    }

    public void Enter()
    {
        if (ctx.Animator != null)
        {
            // Ensure idle active, walk disabled
            ctx.Animator.ResetTrigger("AttackThrust");
            ctx.Animator.ResetTrigger("AttackMissile");
            ctx.Animator.SetBool("Walk", false);
            ctx.Animator.SetBool("Idle", true);
        }
    }

    public void Execute()
    {
        if (ctx.Player == null) return;

        float dist = Vector3.Distance(ctx.transform.position, ctx.Player.position);

        if (dist <= ctx.AttackRange)
        {
            ctx.ChangeState(ctx.WaitState);
        }
        else if (dist <= ctx.ChaseRange)
        {
            ctx.ChangeState(ctx.WalkState);
        }
    }

    public void Exit()
    {
        if (ctx.Animator != null)
        {
            ctx.Animator.SetBool("Idle", false);
        }
    }
}

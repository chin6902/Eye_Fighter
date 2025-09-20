using UnityEngine;

public class BossWaitState : IState
{
    private BossController ctx;
    private float endTime;

    // Alternation toggle (persists because state object is reused)
    // false -> this Enter = idle wait; true -> this Enter = walk-while-wait
    private bool nextShouldWalk = false;

    // runtime walk data
    private bool willWalkToPoint;
    private Vector3 walkTarget;
    private float walkStopRadius = 0.5f;

    // tuning for how far from player the boss will walk
    private float walkChooseRadiusMin = 1.0f;
    private float walkChooseRadiusMax = 3.0f;

    // minimum guaranteed wait so player has time to react
    private const float MIN_WAIT_SECONDS = 1.5f;

    public BossWaitState(BossController controller)
    {
        ctx = controller;
    }

    public void Enter()
    {
        // default animator -> idle; may flip to walk below
        if (ctx.Animator != null)
        {
            ctx.Animator.SetBool("Walk", false);
            ctx.Animator.SetBool("Idle", true);
        }

        // choose wait time but ensure minimum
        float randomWait = Random.Range(ctx.MinWait, ctx.MaxWait);
        float wait = Mathf.Max(MIN_WAIT_SECONDS, randomWait);
        endTime = Time.time + wait;

        // decide whether this entry will walk (alternating)
        willWalkToPoint = nextShouldWalk;
        nextShouldWalk = !nextShouldWalk; // flip for next Enter

        walkTarget = Vector3.zero;

        if (willWalkToPoint && ctx.Player != null)
        {
            // choose a random point near the player
            float r = Random.Range(walkChooseRadiusMin, walkChooseRadiusMax);
            float ang = Random.Range(0f, Mathf.PI * 2f);
            Vector3 offset = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r;
            walkTarget = ctx.Player.position + offset;
            walkTarget.y = ctx.transform.position.y;

            // start walking animation
            if (ctx.Animator != null)
            {
                ctx.Animator.SetBool("Idle", false);
                ctx.Animator.SetBool("Walk", true);
            }
        }
    }

    public void Execute()
    {
        // If player becomes attackable AND is within attack range, attack immediately
        if (ctx.Player != null && ctx.CanPerformAttack() && Vector3.Distance(ctx.transform.position, ctx.Player.position) <= ctx.AttackRange)
        {
            // choose attack
            if (Random.value < 0.5f)
                ctx.ChangeState(ctx.ThrustState);
            else
                ctx.ChangeState(ctx.MissileState);
            return;
        }

        // always face player while waiting / walking
        if (ctx.Player != null)
        {
            Vector3 toPlayer = ctx.Player.position - ctx.transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > 0.0001f)
            {
                Quaternion desired = Quaternion.LookRotation(toPlayer.normalized);
                ctx.transform.rotation = Quaternion.Slerp(ctx.transform.rotation, desired, Time.deltaTime * 8f);
            }
        }

        // If chosen to walk, move toward the target until reached
        if (willWalkToPoint && walkTarget != Vector3.zero)
        {
            float distToTarget = Vector3.Distance(new Vector3(ctx.transform.position.x, 0f, ctx.transform.position.z),
                                                  new Vector3(walkTarget.x, 0f, walkTarget.z));
            if (distToTarget > walkStopRadius)
            {
                // move while still facing player
                ctx.MoveTowards(walkTarget, ctx.WalkSpeed);
            }
            else
            {
                // reached early -> stop walking and become idle for remaining wait time
                willWalkToPoint = false;
                if (ctx.Animator != null)
                {
                    ctx.Animator.SetBool("Walk", false);
                    ctx.Animator.SetBool("Idle", true);
                }
            }
        }

        // wait expiration logic
        if (Time.time < endTime) return;

        // when wait over, if can attack, pick an attack
        if (ctx.CanPerformAttack())
        {
            if (Random.value < 0.65f)
                ctx.ChangeState(ctx.ThrustState);
            else
                ctx.ChangeState(ctx.MissileState);
        }
        else
        {
            // fallback: either chase (walk) or stay idle based on distance to player
            if (ctx.Player != null && Vector3.Distance(ctx.transform.position, ctx.Player.position) > ctx.AttackRange)
                ctx.ChangeState(ctx.WalkState);
            else
                ctx.ChangeState(ctx.IdleState);
        }
    }

    public void Exit()
    {
        // clear animator flags — next state will set its own animation
        if (ctx.Animator != null)
        {
            ctx.Animator.SetBool("Idle", false);
            ctx.Animator.SetBool("Walk", false);
        }
        willWalkToPoint = false;
        walkTarget = Vector3.zero;
    }
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Temporary stunned state that pauses only the boss animator for a duration.
/// Does not modify global timeScale. Restores animator speed on exit.
/// </summary>
public class BossStunnedState : IState
{
    private BossController ctx;
    private float endTime;
    private float duration;
    private float prevAnimatorSpeed;
    private List<Collider> disabledColliders;

    public BossStunnedState(BossController controller, float durationSeconds)
    {
        ctx = controller;
        duration = Mathf.Max(0f, durationSeconds);
    }

    public void Enter()
    {
        // store & pause animator
        if (ctx.Animator != null)
        {
            prevAnimatorSpeed = ctx.Animator.speed;
            ctx.Animator.speed = 0f;
        }
        else
        {
            prevAnimatorSpeed = 1f;
        }

        // Optionally disable attack colliders during stun to avoid accidental hits
        disabledColliders = new List<Collider>();
        var attackCols = ctx.GetComponentsInChildren<Collider>(true);
        foreach (var c in attackCols)
        {
            if (c != null && c.enabled)
            {
                c.enabled = false;
                disabledColliders.Add(c);
            }
        }

        endTime = Time.time + duration;
    }

    public void Execute()
    {
        // You can optionally slowly rotate to face player while stunned:
        /*
        if (ctx.Player != null)
        {
            Vector3 dir = ctx.Player.position - ctx.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion target = Quaternion.LookRotation(dir.normalized);
                ctx.transform.rotation = Quaternion.Slerp(ctx.transform.rotation, target, Time.deltaTime * 4f);
            }
        }
        */

        if (Time.time >= endTime)
        {
            ctx.ChangeState(ctx.IdleState);
        }
    }

    public void Exit()
    {
        if (ctx.Animator != null)
            ctx.Animator.speed = prevAnimatorSpeed;

        /*
        if (disabledColliders != null)
        {
            foreach (var c in disabledColliders)
                if (c != null) c.enabled = true;
            disabledColliders = null;
        }
        */
    }
}

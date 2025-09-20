using System.Collections;
using UnityEngine;

public class BossThrustState : IState
{
    private BossController ctx;
    private Coroutine routine;

    public BossThrustState(BossController controller)
    {
        ctx = controller;
    }

    public void Enter()
    {
        if (!ctx.CanPerformAttack())
        {
            ctx.ChangeState(ctx.WalkState);
            return;
        }

        routine = ctx.StartCoroutine(DoThrustCombo());
    }

    public void Execute() { }

    public void Exit()
    {
        if (routine != null)
            ctx.StopCoroutine(routine);
        routine = null;
    }

    private IEnumerator DoThrustCombo()
    {
        if (ctx.Player == null)
        {
            ctx.ChangeState(ctx.IdleState);
            yield break;
        }

        int combo = Random.Range(1, ctx.MaxThrustCombo + 1);
        combo = Mathf.Clamp(combo, 1, ctx.MaxThrustCombo);

        for (int i = 0; i < combo; i++)
        {
            // face player before thrust
            yield return ctx.StartCoroutine(SmoothFacePlayer(0.15f));

            if (ctx.Animator != null)
            {
                ctx.Animator.SetTrigger("AttackThrust");
            }

            Vector3 targetPos = ctx.Player != null ? ctx.Player.position : ctx.transform.position;
            targetPos.y = ctx.transform.position.y;

            Vector3 start = ctx.transform.position;
            Vector3 dir = (targetPos - start).normalized;
            if (dir.sqrMagnitude < 0.001f) dir = ctx.transform.forward;

            Vector3 goal = start + dir * ctx.ThrustDistance;

            var attackCollider = ctx.AttackCollider;
            if (attackCollider != null)
            {
                float startTiming = ctx.ThrustAttackWindowStart;
                float end = ctx.ThrustAttackWindowEnd; 
                float windowDuration = Mathf.Max(0f, end - startTiming);

                ctx.StartCoroutine(attackCollider.EnableWindow(startTiming, windowDuration));
            }

            float elapsed = 0f;
            // slow movement when speed multiplier < 1 (divide duration by multiplier so movement is slower when multiplier<1)
            float duration = Mathf.Max(0.05f, ctx.ThrustDuration / Mathf.Max(0.01f, ctx.SpeedMultiplier));

            while (elapsed < duration)
            {
                float t = elapsed / duration;
                float ease = Mathf.SmoothStep(0f, 1f, t);
                ctx.transform.position = Vector3.Lerp(start, goal, ease);
                elapsed += Time.deltaTime;
                yield return null;
            }

            ctx.transform.position = goal;

            //after each thrust, go to idle pose briefly
            if (ctx.Animator != null)
            {
                ctx.Animator.SetBool("Idle", true);
            }

            if (i < combo - 1)
            {
                // pause between thrusts — scale delay by speed multiplier (slower multiplier => longer delay)
                float comboDelay = ctx.ThrustComboDelay / Mathf.Max(0.01f, ctx.SpeedMultiplier);
                yield return new WaitForSeconds(comboDelay);
            }

            // return to non-idle pose before next thrust
            if (ctx.Animator != null)
            {
                ctx.Animator.SetBool("Idle", false);
            }
        }

        // start attack cooldown
        ctx.StartAttackCooldown();

        // after attack, continue chasing player (no long idle)
        yield return new WaitForSeconds(0.05f);
        ctx.ChangeState(ctx.WalkState);
    }

    private IEnumerator SmoothFacePlayer(float duration)
    {
        if (ctx.Player == null) yield break;

        Vector3 dir = (ctx.Player.position - ctx.transform.position);
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) yield break;

        Quaternion from = ctx.transform.rotation;
        Quaternion to = Quaternion.LookRotation(dir.normalized);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            float t = elapsed / duration;
            ctx.transform.rotation = Quaternion.Slerp(from, to, t);
            elapsed += Time.deltaTime;
            yield return null;
        }
        ctx.transform.rotation = to;
    }
}

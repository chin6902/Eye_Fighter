using System.Collections;
using UnityEngine;

/// <summary>
/// Recover state: play recover animation fully, then re-enable barrier and return to Idle.
/// This implementation waits for the animator clip that's actually played and waits for the remaining duration.
/// </summary>
public class BossRecoverBarrierState : IState
{
    private BossController ctx;
    private Coroutine routine;

    // Safety timeout if the animator doesn't provide clip info (seconds)
    private const float ANIM_CLIP_WAIT_TIMEOUT = 2.2f;
    // Fallback wait if we can't determine clip length
    private const float FALLBACK_WAIT = 2.2f;

    public BossRecoverBarrierState(BossController controller)
    {
        ctx = controller;
    }

    public void Enter()
    {
        if (ctx.Animator != null)
            ctx.Animator.SetTrigger("Recover");

        if (ctx.BarrierController == null)
        {
            ctx.ChangeState(ctx.IdleState);
            return;
        }

        routine = ctx.StartCoroutine(DoRecover());
    }

    public void Execute() { }

    public void Exit()
    {
        if (routine != null)
        {
            ctx.StopCoroutine(routine);
            routine = null;
        }
    }

    private IEnumerator DoRecover()
    {
        // Wait for the animator to start playing an animation clip (or timeout)
        float waited = 0f;
        float clipLength = 0f;
        Animator anim = ctx.Animator;

        // Wait a frame to allow animator to process the trigger
        yield return null;

        // Activate barrier
        ctx.BarrierController.ActivateBarrier();

        // Poll for clip info up to timeout
        while (waited < ANIM_CLIP_WAIT_TIMEOUT)
        {
            if (anim != null)
            {
                var clipInfos = anim.GetCurrentAnimatorClipInfo(0);
                if (clipInfos != null && clipInfos.Length > 0 && clipInfos[0].clip != null)
                {
                    clipLength = clipInfos[0].clip.length;
                    break;
                }
            }

            waited += Time.deltaTime;
            yield return null;
        }

        // If we found a clip, compute remaining time based on animator normalizedTime
        if (clipLength > 0f && anim != null)
        {
            var state = anim.GetCurrentAnimatorStateInfo(0);
            // normalizedTime can be >1 if looping; take fractional part
            float norm = state.normalizedTime;
            float frac = norm - Mathf.Floor(norm);
            // remaining portion of this clip (in seconds)
            float remaining = clipLength * (1f - frac);
            // safety clamp
            remaining = Mathf.Max(0f, remaining);
            if (remaining > 0f)
                yield return new WaitForSeconds(remaining);
        }
        else
        {
            // Fallback: wait a short time to ensure the animation played somewhat
            yield return new WaitForSeconds(FALLBACK_WAIT);
        }

        // cleanup
        routine = null;

        // return to idle
        ctx.ChangeState(ctx.IdleState);
    }
}

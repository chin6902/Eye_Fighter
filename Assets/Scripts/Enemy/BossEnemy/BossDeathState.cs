using System.Collections;
using UnityEngine;

public class BossDeathState : IState
{
    private BossController ctx;
    private Coroutine waitCoroutine;

    public BossDeathState(BossController c) => ctx = c;

    public void Enter()
    {
        // Play death animation. Use a bool so the Animator may keep the death pose.
        if (ctx.Animator != null)
        {
            ctx.Animator.SetBool("Die", true);
        }

        waitCoroutine = ctx.StartCoroutine(DeathAnimationRoutine());
    }

    public void Execute()
    {
        // Intentionally do nothing: stop movement & attack while in death state.
    }

    public void Exit()
    {
        if (waitCoroutine != null)
        {
            ctx.StopCoroutine(waitCoroutine);
            waitCoroutine = null;
        }
    }

    private IEnumerator DeathAnimationRoutine()
    {
        // Wait a frame so animator can switch states
        yield return null;

        if (ctx.Animator == null)
        {
            yield return new WaitForSeconds(1f);
        }
        else
        {
            var info = ctx.Animator.GetCurrentAnimatorStateInfo(0);
            float length = Mathf.Max(0.5f, info.length); // safe fallback
            yield return new WaitForSeconds(length);
        }

        // After playing the death clip once, keep "Die" == true so boss appears dead.
        // Do not change back to idle here — recovery handler will manage revive & returning to IdleState.
        yield break;
    }
}

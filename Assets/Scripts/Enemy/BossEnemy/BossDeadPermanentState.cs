public class BossDeadPermanentState : IState
{
    private BossController ctx;

    public BossDeadPermanentState(BossController c) => ctx = c;

    public void Enter()
    {
        // Ensure animator is in death pose
        if (ctx.Animator != null)
            ctx.Animator.SetBool("Die", true);

        // mark controller as not alive (just to be safe)
        // ctx.Die() already sets alive=false, but we ensure no logic resumes
    }

    public void Execute()
    {
    }

    public void Exit()
    {
    }
}

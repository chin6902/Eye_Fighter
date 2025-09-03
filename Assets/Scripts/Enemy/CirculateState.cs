using UnityEngine;

public class CirculateState : IState
{
    EnemyController ctx;
    int dir;                    // +1 for CW, -1 for CCW
    float desiredRadius;        // target orbit radius
    float switchCooldown;       // time until next allowed flip on bump
    float randomSwitchTimer;    // time until next random direction flip

    public CirculateState(EnemyController c) { ctx = c; }

    public void Enter()
    {
        desiredRadius = Random.Range(ctx.AttackRange + 0.5f, ctx.CirculateRange);
        dir = Random.value < 0.5f ? 1 : -1;

        switchCooldown = 0f;
        randomSwitchTimer = Random.Range(2f, 5f); // every 2-5 seconds randomly flip
    }

    public void Execute()
    {
        if (ctx.IsDead) return;

        float dist = Vector3.Distance(ctx.transform.position, ctx.Player.position);

        // 1) Out of chase range? → back to chase
        if (dist > ctx.ChaseRange)
        {
            ctx.ChangeState(new ChaseState(ctx));
            return;
        }

        // 2) Check for slot → Approach or Attack
        if (ctx.CanAttack())
        {
            if (dist <= ctx.AttackRange)
            {
                if (ctx is CactusController)
                {
                    ctx.ChangeState(new RangedAttackState(ctx));
                }
                else
                {
                    ctx.ChangeState(new AttackState(ctx));
                }
            }
            else
            {
                ctx.ChangeState(new ApproachState(ctx));
            }

            return;
        }

        // 3) Orbit: add slight jitter to radius
        desiredRadius += Random.Range(-0.2f, 0.2f) * Time.deltaTime;

        // Calculate orbit path
        Vector3 flatSelf = new Vector3(ctx.transform.position.x, 0, ctx.transform.position.z);
        Vector3 flatPlayer = new Vector3(ctx.Player.position.x, 0, ctx.Player.position.z);
        Vector3 toEnemy = (flatSelf - flatPlayer).normalized;

        Vector3 orbit = Quaternion.Euler(0, 90 * dir, 0) * toEnemy;
        Vector3 desiredPos = flatPlayer + toEnemy * desiredRadius + orbit * 1.0f;
        Vector3 moveDir = (desiredPos - flatSelf).normalized;

        ctx.Move(moveDir, ctx.CirculateSpeed);

        if (ctx.IsOutsideRestrictedArea())
        {
            ctx.ChangeState(new ReturnToGroupState(ctx));
            return;
        }

        // 4) Handle bump flip
        switchCooldown -= Time.deltaTime;
        if (switchCooldown <= 0f)
        {
            Collider[] hits = Physics.OverlapSphere(ctx.transform.position, 0.5f, LayerMask.GetMask("Enemy"));
            foreach (var hit in hits)
            {
                if (hit.transform == ctx.transform) continue;

                dir *= -1;                // flip orbit direction
                switchCooldown = 1f;    // prevent instant flip back
                break;
            }
        }

        // 5) Random flip
        randomSwitchTimer -= Time.deltaTime;
        if (randomSwitchTimer <= 0f)
        {
            dir *= -1;
            randomSwitchTimer = Random.Range(1f, 8f);
        }
    }

    public void Exit() { }
}

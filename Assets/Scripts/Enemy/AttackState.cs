using UnityEngine;

public class AttackState : IState
{
    EnemyController ctx;

    public AttackState(EnemyController c)
    {
        ctx = c;
    }

    public void Enter()
    {
        Vector3 lookDir = (ctx.Player.position - ctx.transform.position).normalized;
        lookDir.y = 0;
        if (lookDir != Vector3.zero)
            ctx.transform.rotation = Quaternion.LookRotation(lookDir);

        ctx.Animator.SetTrigger("Attack");
        ctx.currentAttackCoroutine = ctx.StartCoroutine(AttackRoutine());
    }

    private System.Collections.IEnumerator AttackRoutine()
    {
        var attackCol = ctx.transform.GetComponentInChildren<EnemyAttackCollider>(true);
        yield return new WaitForSeconds(0.075f);
        if (attackCol != null && ctx.parryWarningPrefab != null)
        {
            Transform spawnPoint = attackCol.transform;
            GameObject warning = Object.Instantiate(
                ctx.parryWarningPrefab,
                spawnPoint.position + Vector3.up * 1.3f,
                spawnPoint.rotation
            );
            Object.Destroy(warning, 0.35f);
        }

        ctx.IsParryable = true;

        yield return new WaitForSeconds(0.35f);

        ctx.IsParryable = false;

        if (attackCol != null)
        {
            var col = attackCol.GetComponent<Collider>();
            col.enabled = true;

            yield return new WaitForSeconds(0.2f);

            col.enabled = false;
        }
        else
        {
            Debug.LogWarning("No EnemyAttackCollider found on child!");
        }

        yield return new WaitForSeconds(0.3f);

        ctx.FinishAttack();
    }


    public void Execute() 
    {
        if (ctx.IsDead) return;
    }

    public void Exit() { }
}

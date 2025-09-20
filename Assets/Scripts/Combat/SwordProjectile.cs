using System;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class SwordProjectile : MonoBehaviour
{
    private Action onImpact;
    private bool hasHit = false;

    // cached target comps (may be null)
    private Health targetHealth;
    private BossHealth targetBossHealth;
    private BarrierSpot targetBarrierSpot;

    private GameManager.ElementType attackerElement;
    private float accuracy;

    private Transform target;

    private Vector3 start;
    private Vector3 lateral;
    private float curveHeight;
    private float travelTime;
    private float timer;

    public float targetHeightOffset = 0.5f;
    public float impactDistance = 0.5f;

    /// <summary>
    /// Initialize projectile. (unchanged signature from your current usage)
    /// </summary>
    public void Initialize(
        Transform target,
        GameManager.ElementType attackerElement,
        float accuracy,
        Vector3 start,
        Vector3 lateral,
        float curveHeight,
        float travelTime,
        Action impactCallback)
    {
        this.target = target;
        this.attackerElement = attackerElement;
        this.accuracy = accuracy;
        this.start = start;
        this.lateral = lateral;
        this.curveHeight = curveHeight;
        this.travelTime = travelTime;
        this.onImpact = impactCallback;

        // cache possible components on the target
        if (target != null)
        {
            targetHealth = target.GetComponent<Health>();
            targetBossHealth = target.GetComponent<BossHealth>();
            targetBarrierSpot = target.GetComponent<BarrierSpot>();
        }
        else
        {
            targetHealth = null;
            targetBossHealth = null;
            targetBarrierSpot = null;
        }

        transform.position = start;
        timer = 0f;

        var rb = GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;
    }

    private void Update()
    {
        if (target == null || hasHit) return;

        timer += Time.deltaTime;
        float t = Mathf.Clamp01(timer / travelTime);

        Vector3 end = target.position + Vector3.up * targetHeightOffset;
        Vector3 mid = (start + end) * 0.5f + Vector3.up * curveHeight;

        Vector3 p1 = Vector3.Lerp(start, mid, t);
        Vector3 p2 = Vector3.Lerp(mid, end, t);
        Vector3 bezierPos = Vector3.Lerp(p1, p2, t);

        Vector3 pos = bezierPos + lateral * Mathf.Sin(Mathf.PI * t);

        float nextT = Mathf.Clamp01(t + 0.01f);
        Vector3 p1_next = Vector3.Lerp(start, mid, nextT);
        Vector3 p2_next = Vector3.Lerp(mid, end, nextT);
        Vector3 bezierPosNext = Vector3.Lerp(p1_next, p2_next, nextT);
        Vector3 posNext = bezierPosNext + lateral * Mathf.Sin(Mathf.PI * nextT);

        Vector3 lookDir = (posNext - pos).normalized;
        if (lookDir.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.LookRotation(lookDir);
        }

        transform.position = pos;

        if (Vector3.Distance(pos, end) < impactDistance)
        {
            OnHit();
        }
    }

    private void OnHit()
    {
        if (hasHit) return;
        hasHit = true;

        if (targetBossHealth != null)
        {
            // BossHealth will consume the charge internally and apply charged effects.
            targetBossHealth.ReceiveElementalDamage(attackerElement, accuracy);

            var dataB = ElementDatabase.Instance.Get(attackerElement);
            if (dataB != null && dataB.HitSFX != null)
                SoundManager.PlaySFX(dataB.HitSFX, 0.4f);
        }
        else if (targetHealth != null)
        {
            // normal enemy
            targetHealth.ReceiveElementalDamage(attackerElement, accuracy);

            var dataH = ElementDatabase.Instance.Get(attackerElement);
            if (dataH != null && dataH.HitSFX != null)
                SoundManager.PlaySFX(dataH.HitSFX, 0.5f);
        }
        else if (targetBarrierSpot != null)
        {
            // barrier spot (existing behavior)
            targetBarrierSpot.ReceiveElementalDamage(attackerElement, accuracy);

            var dataS = ElementDatabase.Instance.Get(attackerElement);
            if (dataS != null && dataS.HitSFX != null)
                SoundManager.PlaySFX(dataS.HitSFX, 0.5f);
        }
        else
        {
            // nothing to hit
        }

        onImpact?.Invoke();
        Destroy(gameObject);
    }
}

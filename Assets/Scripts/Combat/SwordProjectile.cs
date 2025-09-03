using System;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class SwordProjectile : MonoBehaviour
{
    private Action onImpact;
    private bool hasHit = false;
    private Health targetHealth;
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
        this.targetHealth = target.GetComponent<Health>();
        this.attackerElement = attackerElement;
        this.accuracy = accuracy;
        this.start = start;
        this.lateral = lateral;
        this.curveHeight = curveHeight;
        this.travelTime = travelTime;
        this.onImpact = impactCallback;

        transform.position = start;

        timer = 0f;

        GetComponent<Rigidbody>().isKinematic = true;
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
        if (targetHealth != null)
        {
            targetHealth.ReceiveElementalDamage(attackerElement, accuracy);

            var data = ElementDatabase.Instance.Get(attackerElement);
            if (data != null && data.HitSFX != null)
            {
                SoundManager.PlaySFX(data.HitSFX, 0.5f);
            }
        }

        onImpact?.Invoke();
        Destroy(gameObject);
    }
}

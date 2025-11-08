using System.Collections;
using UnityEngine;

/// <summary>
/// Scales the target repeatedly (pulse) while enabled. Non-destructive; stops when disabled/destroyed.
/// </summary>
public class StartMarkerPulse : MonoBehaviour
{
    public float pulseScaleMin = 0.9f;
    public float pulseScaleMax = 1.25f;
    public float pulseSpeed = 2.5f;

    private RectTransform rt;

    private void Awake()
    {
        rt = GetComponent<RectTransform>();
        if (rt == null)
            rt = gameObject.AddComponent<RectTransform>();
    }

    private void OnEnable()
    {
        StopAllCoroutines();
        StartCoroutine(PulseRoutine());
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        if (rt != null) rt.localScale = Vector3.one;
    }

    private IEnumerator PulseRoutine()
    {
        float t = 0f;
        while (true)
        {
            t += Time.deltaTime * pulseSpeed;
            float s = Mathf.Lerp(pulseScaleMin, pulseScaleMax, (Mathf.Sin(t) + 1f) * 0.5f);
            if (rt != null) rt.localScale = new Vector3(s, s, 1f);
            yield return null;
        }
    }
}

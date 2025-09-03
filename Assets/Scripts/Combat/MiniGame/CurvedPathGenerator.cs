using System.Collections.Generic;
using UnityEngine;

public class CurvedPathGenerator : MonoBehaviour
{
    public enum Pattern
    {
        TopLeft_To_BottomRight,
        TopRight_To_BottomLeft,
        Cross
    }

    [Header("References")]
    public RectTransform canvasRect;
    public GameObject segmentPrefab;
    public GameObject endpointPrefab;
    public GameObject startPointPrefab;
    public int segmentCount = 40;

    [Header("Distance Settings")]
    [Tooltip("Minimum distance (in canvas local units) between the start and end points")]
    [SerializeField] private float minDistance = 10f;
    [Tooltip("How many tries before giving up (avoids infinite loops)")]
    [SerializeField] private int maxAttempts = 20;

    [Header("Pattern defaults")]
    [Tooltip("Default length (canvas-local units) used for barrier spot patterns if caller passes <= 0")]
    public float defaultPatternLength = 100f;

    [HideInInspector] public List<RectTransform> segments;
    [HideInInspector] public RectTransform endPointRect;
    [HideInInspector] public RectTransform startPointRect;

    // Flag: tells caller/tracker whether the last generation was a pattern (barrier spot) or a normal curve.
    [HideInInspector] public bool lastGeneratedWasPattern = false;

    // Grouped data for patterns (and used by tracker). Generator populates these.
    [HideInInspector] public List<List<RectTransform>> segmentGroups = new List<List<RectTransform>>();
    [HideInInspector] public List<RectTransform> startPointRects = new List<RectTransform>();
    [HideInInspector] public List<RectTransform> endPointRects = new List<RectTransform>();

    // ---------------- existing curve generation (UNCHANGED behaviour) ----------------
    public void GenerateCurve(Vector2 start, Vector2 end)
    {
        // Mark this generation explicitly as a curve (not a pattern)
        lastGeneratedWasPattern = false;

        ClearExisting();
        segments = new List<RectTransform>();
        segmentGroups = new List<List<RectTransform>>();
        startPointRects = new List<RectTransform>();
        endPointRects = new List<RectTransform>();

        List<RectTransform> thisGroup = new List<RectTransform>();

        Vector2 control1 = start + Vector2.up * 200f;
        Vector2 control2 = end + Vector2.down * 200f;

        for (int i = 1; i <= segmentCount; i++)
        {
            float t = i / (float)segmentCount;
            Vector2 pos = Mathf.Pow(1 - t, 3) * start
                        + 3 * Mathf.Pow(1 - t, 2) * t * control1
                        + 3 * (1 - t) * t * t * control2
                        + Mathf.Pow(t, 3) * end;

            GameObject prefab = (i == segmentCount)
                ? endpointPrefab
                : segmentPrefab;

            GameObject segObj = Instantiate(prefab, canvasRect);
            RectTransform rt = segObj.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            segments.Add(rt);
            thisGroup.Add(rt);

            if (i == segmentCount)
                endPointRect = rt;
        }

        // record the single group used by the curve
        segmentGroups.Add(thisGroup);
        endPointRects.Add(endPointRect);

        // original start marker creation (kept)
        if (startPointPrefab != null && canvasRect != null)
        {
            GameObject startObj = Instantiate(startPointPrefab, canvasRect);
            startPointRect = startObj.GetComponent<RectTransform>();
            startPointRect.anchoredPosition = start;
            startPointRect.transform.SetAsLastSibling();

            startPointRects.Add(startPointRect);
        }
    }

    public void GenerateCurveFromWorldObject(Transform targetObject, Canvas canvas, Camera cam)
    {
        // compute end
        Vector2 endPoint = WorldToCanvasPosition(canvas, cam, targetObject.position);

        // pick a random start at least minDistance away
        RectTransform canvasRT = canvas.GetComponent<RectTransform>();
        Vector2 startPoint = Vector2.zero;
        int attempts = 0;

        do
        {
            startPoint = GetRandomCanvasPoint(canvasRT);
            attempts++;
        }
        while (Vector2.Distance(startPoint, endPoint) < minDistance
               && attempts < maxAttempts);

        if (attempts >= maxAttempts)
            Debug.LogWarning($"Could not find start point ≥{minDistance} units from end after {maxAttempts} tries.");

        GenerateCurve(startPoint, endPoint);
    }

    public void ClearExisting()
    {
        // destroy flat segments
        if (segments != null)
        {
            for (int i = segments.Count - 1; i >= 0; i--)
                if (segments[i] != null)
                    Destroy(segments[i].gameObject);
            segments.Clear();
            segments = null;
        }

        // destroy groups
        if (segmentGroups != null)
        {
            foreach (var g in segmentGroups)
            {
                if (g == null) continue;
                for (int i = g.Count - 1; i >= 0; i--)
                    if (g[i] != null)
                        Destroy(g[i].gameObject);
                g.Clear();
            }
            segmentGroups.Clear();
        }

        // destroy end points
        if (endPointRects != null)
        {
            for (int i = endPointRects.Count - 1; i >= 0; i--)
                if (endPointRects[i] != null)
                    Destroy(endPointRects[i].gameObject);
            endPointRects.Clear();
        }

        if (endPointRect != null)
        {
            Destroy(endPointRect.gameObject);
            endPointRect = null;
        }

        // destroy start markers
        if (startPointRects != null)
        {
            for (int i = startPointRects.Count - 1; i >= 0; i--)
                if (startPointRects[i] != null)
                    Destroy(startPointRects[i].gameObject);
            startPointRects.Clear();
        }

        if (startPointRect != null)
        {
            Destroy(startPointRect.gameObject);
            startPointRect = null;
        }

        // NOTE: do not modify lastGeneratedWasPattern here — generation methods control it explicitly.
    }

    Vector2 WorldToCanvasPosition(Canvas canvas, Camera cam, Vector3 worldPos)
    {
        Vector2 screenPos = cam != null ? cam.WorldToScreenPoint(worldPos) : RectTransformUtility.WorldToScreenPoint(null, worldPos);
        RectTransform canvasRT = canvas.GetComponent<RectTransform>();
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRT,
            screenPos,
            canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : cam,
            out Vector2 canvasPos
        );
        return canvasPos;
    }

    Vector2 GetRandomCanvasPoint(RectTransform canvasRect, float margin = 100f)
    {
        float halfW = canvasRect.sizeDelta.x * 0.5f - margin;
        float halfH = canvasRect.sizeDelta.y * 0.5f - margin;
        return new Vector2(
            Random.Range(-halfW, halfW),
            Random.Range(-halfH, halfH)
        );
    }

    // ---------------- NEW: anchored-centered pattern generation for barrier spots ----------------

    public void GeneratePatternAtSpot(Transform spot, Canvas canvas, Camera cam, Pattern pattern, float length, int singleLineSegments = -1)
    {
        if (spot == null || canvas == null)
        {
            Debug.LogWarning("CurvedPathGenerator.GeneratePatternAtSpot: spot or canvas is null.");
            return;
        }

        if (length <= 0f) length = defaultPatternLength;

        if (canvasRect == null)
            canvasRect = canvas.GetComponent<RectTransform>();

        Vector2 center = WorldToCanvasPosition(canvas, cam, spot.position);

        GeneratePatternAtCenter(center, length, pattern, singleLineSegments);

        // mark that we built a pattern (set AFTER ClearExisting and after segments exist)
        lastGeneratedWasPattern = true;

        // debug to help you see correct state
        //Debug.Log($"CurvedPathGenerator: Generated PATTERN for '{spot.name}' -> lastGeneratedWasPattern={lastGeneratedWasPattern}, groups={segmentGroups.Count}, segments(flat)={(segments != null ? segments.Count : 0)}, startPoints={startPointRects.Count}");
    }

    public void GeneratePatternAtCenter(Vector2 center, float length, Pattern pattern, int singleLineSegments = -1)
    {
        ClearExisting();

        segments = new List<RectTransform>();
        segmentGroups = new List<List<RectTransform>>();
        startPointRects = new List<RectTransform>();
        endPointRects = new List<RectTransform>();

        int segs = (singleLineSegments > 0) ? singleLineSegments : Mathf.Max(3, segmentCount);
        if (segs % 2 == 0) segs++;
        float spacing = (segs > 1) ? (length / (segs - 1)) : 0f;

        Vector2 dirTLBR = new Vector2(-1f, 1f).normalized;
        Vector2 dirTRBL = new Vector2(1f, 1f).normalized;

        switch (pattern)
        {
            case Pattern.TopLeft_To_BottomRight:
                {
                    bool endpointAtPositive = (Random.value < 0.5f);
                    var group = GenerateCenteredLineGroup(center, dirTLBR, segs, spacing, endpointAtPositive);
                    segmentGroups.Add(group);
                }
                break;

            case Pattern.TopRight_To_BottomLeft:
                {
                    bool endpointAtPositive = (Random.value < 0.5f);
                    var group = GenerateCenteredLineGroup(center, dirTRBL, segs, spacing, endpointAtPositive);
                    segmentGroups.Add(group);
                }
                break;

            case Pattern.Cross:
                {
                    bool endpointA = (Random.value < 0.5f);
                    bool endpointB = (Random.value < 0.5f);
                    var groupA = GenerateCenteredLineGroup(center, dirTLBR, segs, spacing, endpointA);
                    var groupB = GenerateCenteredLineGroup(center, dirTRBL, segs, spacing, endpointB);
                    segmentGroups.Add(groupA);
                    segmentGroups.Add(groupB);
                }
                break;
        }

        // start markers (one per group), instantiate after segments so they render on top
        if (startPointPrefab != null && canvasRect != null && segmentGroups.Count > 0)
        {
            foreach (var group in segmentGroups)
            {
                if (group != null && group.Count > 0)
                {
                    Vector2 startPos = group[0].anchoredPosition;
                    GameObject startObj = Instantiate(startPointPrefab, canvasRect);
                    RectTransform rt = startObj.GetComponent<RectTransform>();
                    rt.anchoredPosition = startPos;
                    startPointRects.Add(rt);
                    startPointRect = rt; // legacy single ref points to last created
                    startObj.transform.SetAsLastSibling();
                }
            }
        }

        // Set the pattern flag here as well to be safe if someone calls GeneratePatternAtCenter directly
        lastGeneratedWasPattern = true;
    }

    private List<RectTransform> GenerateCenteredLineGroup(Vector2 center, Vector2 dirNormalized, int segCount, float spacing, bool endpointAtPositive)
    {
        List<RectTransform> group = new List<RectTransform>();

        if (canvasRect == null)
        {
            Debug.LogWarning("CurvedPathGenerator.GenerateCenteredLineGroup: canvasRect is null.");
            return group;
        }

        int midIndex = segCount / 2;
        Vector2[] positions = new Vector2[segCount];
        for (int i = 0; i < segCount; i++)
        {
            int offset = i - midIndex;
            positions[i] = center + dirNormalized * (offset * spacing);
        }

        int startIndex = endpointAtPositive ? 0 : (segCount - 1);
        int dir = endpointAtPositive ? 1 : -1;

        for (int k = 0; k < segCount; k++)
        {
            int i = startIndex + k * dir;
            if (i < 0 || i >= segCount) continue;

            Vector2 pos = positions[i];
            bool isEndpointIndexInOriginal = (endpointAtPositive && i == segCount - 1) || (!endpointAtPositive && i == 0);
            GameObject prefab = isEndpointIndexInOriginal ? endpointPrefab : segmentPrefab;

            GameObject segObj = Instantiate(prefab, canvasRect);
            RectTransform rt = segObj.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;

            if (segments == null) segments = new List<RectTransform>();
            segments.Add(rt);
            group.Add(rt);

            if (isEndpointIndexInOriginal)
            {
                endPointRect = rt;
                endPointRects.Add(rt);
            }
        }

        if ((endPointRects.Count == 0 || endPointRects[endPointRects.Count - 1] == null) && group.Count > 0)
        {
            endPointRect = group[group.Count - 1];
            endPointRects.Add(endPointRect);
        }

        return group;
    }

    public void GenerateLine(Vector2 start, Vector2 end, int segCount)
    {
        if (segCount < 2) segCount = 2;
        if (canvasRect == null)
        {
            Debug.LogWarning("CurvedPathGenerator.GenerateLine: canvasRect is null, cannot instantiate segments.");
            return;
        }

        if (segments == null) segments = new List<RectTransform>();

        List<RectTransform> thisGroup = new List<RectTransform>();

        for (int i = 1; i <= segCount; i++)
        {
            float t = i / (float)segCount;
            Vector2 pos = Vector2.Lerp(start, end, t);

            GameObject prefab = (i == segCount) ? endpointPrefab : segmentPrefab;
            GameObject segObj = Instantiate(prefab, canvasRect);
            RectTransform rt = segObj.GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            segments.Add(rt);
            thisGroup.Add(rt);

            if (i == segCount)
                endPointRect = rt;
        }

        segmentGroups.Add(thisGroup);
        endPointRects.Add(endPointRect);
    }
}

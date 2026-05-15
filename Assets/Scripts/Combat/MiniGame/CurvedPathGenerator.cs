using System.Collections.Generic;
using UnityEngine;

public class CurvedPathGenerator : MonoBehaviour
{
    [Header("Curve Shape")]
    [Tooltip("If <= 0, uses the old fixed height of 200. Larger values give more arc for the same distance.")]
    public float curveHeightFactor = 0.5f;

    [Tooltip("Minimum vertical arc height, in canvas units.")]
    public float minArcHeight = 50f;

    [Tooltip("Maximum vertical arc height, in canvas units.")]
    public float maxArcHeight = 400f;

    [Header("Segment Size Scaling")]
    [Tooltip("If true, mid segments on the curve scale between min and max size based on distance to end.")]
    public bool scaleSegmentsByDistance = true;

    [Tooltip("Minimum segment size (square side).")]
    public float segmentMinSize = 30f;

    [Tooltip("Maximum segment size (square side).")]
    public float segmentMaxSize = 40f;

    [Tooltip("Remaps distance → size. Input 0 = at end, 1 = far from end. Output 0..1.")]
    public AnimationCurve sizeDistanceCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    // cached default sizes from the prefabs (used to reset pooled objects)
    private Vector2 _segmentPrefabSize;
    private Vector2 _endpointPrefabSize;

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

    [Header("Pool (only middle segments use this)")]
    public UIPool segmentPool;

    [HideInInspector] public List<RectTransform> segments;
    [HideInInspector] public RectTransform endPointRect;
    [HideInInspector] public RectTransform startPointRect;

    // Flag: tells caller/tracker whether the last generation was a pattern (barrier spot) or a normal curve.
    [HideInInspector] public bool lastGeneratedWasPattern = false;

    // Grouped data for patterns (and used by tracker). Generator populates these.
    [HideInInspector] public List<List<RectTransform>> segmentGroups = new List<List<RectTransform>>();
    [HideInInspector] public List<RectTransform> startPointRects = new List<RectTransform>();
    [HideInInspector] public List<RectTransform> endPointRects = new List<RectTransform>();

    // Remember only the middle segments that came from the pool
    private HashSet<GameObject> _pooledSegmentInstances = new HashSet<GameObject>();

    private void Awake()
    {
        CachePrefabSizes();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        CachePrefabSizes();
    }
#endif

    private void CachePrefabSizes()
    {
        if (segmentPrefab != null)
        {
            RectTransform rt = segmentPrefab.GetComponent<RectTransform>();
            if (rt != null)
            {
                _segmentPrefabSize = rt.sizeDelta;
            }
        }

        if (endpointPrefab != null)
        {
            RectTransform rt = endpointPrefab.GetComponent<RectTransform>();
            if (rt != null)
            {
                _endpointPrefabSize = rt.sizeDelta;
            }
        }
    }

    // ---------------- curve generation ----------------
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

        // Arc height scales with distance between start and end
        float dist = Vector2.Distance(start, end);

        float arcHeight;
        if (curveHeightFactor > 0f)
        {
            arcHeight = dist * curveHeightFactor;
        }
        else
        {
            // Fallback to the old behavior if factor <= 0
            arcHeight = 200f;
        }

        arcHeight = Mathf.Clamp(arcHeight, minArcHeight, maxArcHeight);

        Vector2 control1 = start + Vector2.up * arcHeight;
        Vector2 control2 = end + Vector2.down * arcHeight;

        // Used for segment size scaling
        float totalDist = Mathf.Max(dist, 0.001f);

        for (int i = 1; i <= segmentCount; i++)
        {
            float t = i / (float)segmentCount;
            Vector2 pos =
                Mathf.Pow(1 - t, 3) * start +
                3 * Mathf.Pow(1 - t, 2) * t * control1 +
                3 * (1 - t) * t * t * control2 +
                Mathf.Pow(t, 3) * end;

            bool isEndpoint = (i == segmentCount);

            RectTransform rt = CreateSegmentAt(pos, isEndpoint);
            if (rt == null)
            {
                continue;
            }

            // Only middle segments get size scaling
            if (!isEndpoint)
            {
                ApplySegmentSize(rt, pos, end, totalDist);
            }

            segments.Add(rt);
            thisGroup.Add(rt);

            if (isEndpoint)
            {
                endPointRect = rt;
            }
        }

        // Record the single group used by the curve
        segmentGroups.Add(thisGroup);
        if (endPointRect != null)
        {
            endPointRects.Add(endPointRect);
        }

        // Start marker
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
        // Compute end point in canvas space
        Vector2 endPoint = WorldToCanvasPosition(canvas, cam, targetObject.position);

        // Pick a random start at least minDistance away
        RectTransform canvasRT = canvas.GetComponent<RectTransform>();
        Vector2 startPoint = Vector2.zero;
        int attempts = 0;

        do
        {
            startPoint = GetRandomCanvasPoint(canvasRT);
            attempts++;
        }
        while (Vector2.Distance(startPoint, endPoint) < minDistance && attempts < maxAttempts);

        if (attempts >= maxAttempts)
        {
            Debug.LogWarning($"Could not find start point ≥{minDistance} units from end after {maxAttempts} tries.");
        }

        // Force the start onto a circle of radius >= minDistance, so it never gets too close.
        float currentDist = Vector2.Distance(startPoint, endPoint);

        if (currentDist < minDistance)
        {
            Vector2 dir = startPoint - endPoint;

            if (dir.sqrMagnitude < 0.0001f)
            {
                // If by chance start == end, pick a random direction
                float angle = Random.Range(0f, Mathf.PI * 2f);
                dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }
            else
            {
                dir.Normalize();
            }

            startPoint = endPoint + dir * minDistance;
        }

        GenerateCurve(startPoint, endPoint);
    }

    public void ClearExisting()
    {
        // For everything in segments, either return to pool or destroy
        if (segments != null)
        {
            for (int i = segments.Count - 1; i >= 0; i--)
            {
                if (segments[i] != null)
                {
                    ReleaseOrDestroySegment(segments[i].gameObject);
                }
            }
            segments.Clear();
            segments = null;
        }

        // For segmentGroups, just clear references (actual segment objects already handled above)
        if (segmentGroups != null)
        {
            foreach (var g in segmentGroups)
            {
                if (g == null) continue;
                g.Clear();
            }
            segmentGroups.Clear();
        }

        // End points: clear references only (objects already handled above)
        if (endPointRects != null)
        {
            endPointRects.Clear();
        }

        endPointRect = null;

        // Start markers are not pooled, so just Destroy them
        if (startPointRects != null)
        {
            for (int i = startPointRects.Count - 1; i >= 0; i--)
            {
                if (startPointRects[i] != null)
                {
                    Destroy(startPointRects[i].gameObject);
                }
            }
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
        Vector2 screenPos = cam != null
            ? cam.WorldToScreenPoint(worldPos)
            : RectTransformUtility.WorldToScreenPoint(null, worldPos);

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

    // ---------------- anchored-centered pattern generation for barrier spots ----------------

    public void GeneratePatternAtSpot(Transform spot, Canvas canvas, Camera cam, Pattern pattern, float length, int singleLineSegments = -1)
    {
        if (spot == null || canvas == null)
        {
            Debug.LogWarning("CurvedPathGenerator.GeneratePatternAtSpot: spot or canvas is null.");
            return;
        }

        if (length <= 0f) length = defaultPatternLength;

        if (canvasRect == null)
        {
            canvasRect = canvas.GetComponent<RectTransform>();
        }

        Vector2 center = WorldToCanvasPosition(canvas, cam, spot.position);

        GeneratePatternAtCenter(center, length, pattern, singleLineSegments);

        // Mark that we built a pattern (set AFTER ClearExisting and after segments exist)
        lastGeneratedWasPattern = true;
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
                    break;
                }

            case Pattern.TopRight_To_BottomLeft:
                {
                    bool endpointAtPositive = (Random.value < 0.5f);
                    var group = GenerateCenteredLineGroup(center, dirTRBL, segs, spacing, endpointAtPositive);
                    segmentGroups.Add(group);
                    break;
                }

            case Pattern.Cross:
                {
                    bool endpointA = (Random.value < 0.5f);
                    bool endpointB = (Random.value < 0.5f);
                    var groupA = GenerateCenteredLineGroup(center, dirTLBR, segs, spacing, endpointA);
                    var groupB = GenerateCenteredLineGroup(center, dirTRBL, segs, spacing, endpointB);
                    segmentGroups.Add(groupA);
                    segmentGroups.Add(groupB);
                    break;
                }
        }

        // Start markers (one per group), instantiate after segments so they render on top
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
                    // For legacy callers, this single reference points to the last created start marker
                    startPointRect = rt;
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

            RectTransform rt = CreateSegmentAt(pos, isEndpointIndexInOriginal);
            if (rt == null) continue;

            if (segments == null)
            {
                segments = new List<RectTransform>();
            }

            segments.Add(rt);
            group.Add(rt);

            if (isEndpointIndexInOriginal)
            {
                endPointRect = rt;
                endPointRects.Add(rt);
            }
        }

        // If for some reason we didn't record an endpoint, use the last element in this group
        if ((endPointRects.Count == 0 || endPointRects[endPointRects.Count - 1] == null) && group.Count > 0)
        {
            endPointRect = group[group.Count - 1];
            endPointRects.Add(endPointRect);
        }

        return group;
    }

    // ------- helpers -------

    private RectTransform CreateSegmentAt(Vector2 anchoredPos, bool isEndpoint)
    {
        if (canvasRect == null)
        {
            Debug.LogWarning("[CurvedPathGenerator] canvasRect is null.");
            return null;
        }

        GameObject prefab = isEndpoint ? endpointPrefab : segmentPrefab;
        if (prefab == null)
        {
            Debug.LogWarning("[CurvedPathGenerator] prefab is null.");
            return null;
        }

        GameObject segObj;

        if (!isEndpoint && segmentPool != null && segmentPool.prefab != null)
        {
            // Use the pool only for middle segments
            segObj = segmentPool.Get(canvasRect);
            _pooledSegmentInstances.Add(segObj);
        }
        else
        {
            // Endpoint or no pool configured → just instantiate
            segObj = Instantiate(prefab, canvasRect);
        }

        RectTransform rt = segObj.GetComponent<RectTransform>();
        if (rt == null)
        {
            Debug.LogWarning("[CurvedPathGenerator] segment object missing RectTransform.");
            return null;
        }

        rt.anchoredPosition = anchoredPos;

        // Reset size to prefab default so patterns stay consistent
        if (isEndpoint)
        {
            if (_endpointPrefabSize != Vector2.zero)
            {
                rt.sizeDelta = _endpointPrefabSize;
            }
        }
        else
        {
            if (_segmentPrefabSize != Vector2.zero)
            {
                rt.sizeDelta = _segmentPrefabSize;
            }
        }

        return rt;
    }

    /// <summary>
    /// If this is a middle segment, return it to the pool. Otherwise, destroy it.
    /// </summary>
    public void ReleaseOrDestroySegment(GameObject go)
    {
        if (go == null) return;

        if (segmentPool != null && _pooledSegmentInstances.Contains(go))
        {
            _pooledSegmentInstances.Remove(go);
            segmentPool.Release(go);
        }
        else
        {
            Destroy(go);
        }
    }

    private void ApplySegmentSize(RectTransform rt, Vector2 segmentPos, Vector2 endPos, float totalDist)
    {
        if (!scaleSegmentsByDistance || rt == null)
        {
            return;
        }

        if (totalDist <= 0.0001f)
        {
            return;
        }

        // Distance from this segment to the endpoint
        float distToEnd = Vector2.Distance(segmentPos, endPos);

        // 0 → at the end, 1 → as far as the start (roughly)
        float normalized = Mathf.Clamp01(distToEnd / totalDist);

        // Non-linear remap using curve
        float curved = normalized;

        if (sizeDistanceCurve != null && sizeDistanceCurve.length > 0)
        {
            // sizeDistanceCurve: x = normalized distance, y = remapped factor
            curved = Mathf.Clamp01(sizeDistanceCurve.Evaluate(normalized));
        }
        else
        {
            // Fallback: SmoothStep (ease in/out)
            curved = normalized * normalized * (3f - 2f * normalized);
        }

        // Ensure min/max ordering
        float minSize = Mathf.Min(segmentMinSize, segmentMaxSize);
        float maxSize = Mathf.Max(segmentMinSize, segmentMaxSize);

        // Farther from the end → closer to maxSize, with non-linear curve
        float size = Mathf.Lerp(minSize, maxSize, curved);

        rt.sizeDelta = new Vector2(size, size);
    }
}

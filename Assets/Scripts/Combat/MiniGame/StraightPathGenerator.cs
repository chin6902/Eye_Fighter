using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates a straight line of UI segments between two canvas positions.
/// Used by the Boss mini-game to spawn the "connector" line.
///
/// Middle segments use a UIPool (segmentPool) if assigned.
/// Start marker and endpoint marker are always instantiated / destroyed normally.
/// </summary>
[AddComponentMenu("BossMiniGame/StraightPathGenerator")]
public class StraightPathGenerator : MonoBehaviour
{
    [Header("References")]
    public RectTransform canvasRect;
    public GameObject segmentPrefab;     // middle pieces
    public GameObject endpointPrefab;    // last piece (visually end of connector)
    public GameObject startPointPrefab;  // separate green "START" marker

    [Header("Optional pool (boss mini-game segments only)")]
    [Tooltip("Pool used ONLY for boss mini-game middle segments.")]
    public UIPool segmentPool;

    [HideInInspector] public List<RectTransform> segments;
    [HideInInspector] public List<List<RectTransform>> segmentGroups = new List<List<RectTransform>>();
    [HideInInspector] public List<RectTransform> startPointRects = new List<RectTransform>();
    [HideInInspector] public List<RectTransform> endPointRects = new List<RectTransform>();

    // Track which instances actually came from the pool so we don't accidentally
    // pool endpoints or non-pooled objects.
    private readonly HashSet<GameObject> _pooledSegmentInstances = new HashSet<GameObject>();

    /// <summary>
    /// Generate a straight connector from start to end with the given segment count.
    /// segmentsPerConnector includes the endpoint segment.
    /// </summary>
    public void GenerateLine(Vector2 start, Vector2 end, int segmentsPerConnector)
    {
        ClearExisting();

        if (canvasRect == null)
        {
            Debug.LogWarning("[StraightPathGenerator] canvasRect is not assigned.");
            return;
        }

        if (segmentsPerConnector < 2)
        {
            // At least one middle + one endpoint
            segmentsPerConnector = 2;
        }

        segments = new List<RectTransform>();
        segmentGroups = new List<List<RectTransform>>();
        startPointRects = new List<RectTransform>();
        endPointRects = new List<RectTransform>();
        _pooledSegmentInstances.Clear();

        List<RectTransform> group = new List<RectTransform>();
        segmentGroups.Add(group);

        for (int i = 0; i < segmentsPerConnector; i++)
        {
            float t = (float)i / (segmentsPerConnector - 1);
            Vector2 pos = Vector2.Lerp(start, end, t);

            bool isEndpoint = (i == segmentsPerConnector - 1);
            GameObject prefab = isEndpoint ? endpointPrefab : segmentPrefab;

            if (prefab == null)
            {
                Debug.LogWarning("[StraightPathGenerator] Missing prefab for segment generation.");
                continue;
            }

            GameObject segObj;

            // Middle segments -> pool; endpoint -> always Instantiate
            if (!isEndpoint && segmentPool != null && segmentPool.prefab != null)
            {
                segObj = segmentPool.Get(canvasRect);
                _pooledSegmentInstances.Add(segObj);
            }
            else
            {
                segObj = Instantiate(prefab, canvasRect, false);
            }

            RectTransform rt = segObj.GetComponent<RectTransform>();
            if (rt == null)
            {
                Debug.LogWarning("[StraightPathGenerator] Segment object has no RectTransform.");
                continue;
            }

            rt.anchoredPosition = pos;

            segments.Add(rt);
            group.Add(rt);

            if (isEndpoint)
            {
                endPointRects.Add(rt);
            }
        }

        // Separate START marker at the start position (not pooled)
        if (startPointPrefab != null)
        {
            GameObject startObj = Instantiate(startPointPrefab, canvasRect, false);
            RectTransform startRt = startObj.GetComponent<RectTransform>();
            if (startRt != null)
            {
                startRt.anchoredPosition = start;
                startRt.transform.SetAsLastSibling();
                startPointRects.Add(startRt);
            }
        }
    }

    /// <summary>
    /// Clears all currently generated UI (segments + markers).
    /// Middle segments are released to the pool when possible.
    /// </summary>
    public void ClearExisting()
    {
        // segments
        if (segments != null)
        {
            for (int i = segments.Count - 1; i >= 0; i--)
            {
                RectTransform seg = segments[i];
                if (seg != null)
                {
                    ReleaseOrDestroySegment(seg.gameObject);
                }
            }
            segments.Clear();
        }

        // groups
        if (segmentGroups != null)
        {
            segmentGroups.Clear();
        }

        // start markers (never pooled)
        if (startPointRects != null)
        {
            foreach (RectTransform rt in startPointRects)
            {
                if (rt != null)
                {
                    Destroy(rt.gameObject);
                }
            }
            startPointRects.Clear();
        }

        // endpoints are just references; they were already destroyed or pooled
        if (endPointRects != null)
        {
            endPointRects.Clear();
        }

        _pooledSegmentInstances.Clear();
    }

    /// <summary>
    /// Used by the tracker and by ClearExisting:
    /// middle segments go back to pool, everything else gets Destroy().
    /// </summary>
    public void ReleaseOrDestroySegment(GameObject go)
    {
        if (go == null) return;

        if (segmentPool != null && segmentPool.prefab != null && _pooledSegmentInstances.Contains(go))
        {
            _pooledSegmentInstances.Remove(go);
            segmentPool.Release(go); // pooled middle segment
        }
        else
        {
            Destroy(go); // endpoints or anything not from the pool
        }
    }
}

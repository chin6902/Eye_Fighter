using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// StraightPathGenerator (pattern-friendly)
/// - Produces straight-line segmented UI between two canvas-local positions.
/// - Ensures segmentGroups are ordered start -> ... -> endpoint so sequential clearing works.
/// - Keeps the same public fields used by GazePathTracker.
/// </summary>
public class StraightPathGenerator : MonoBehaviour
{
    [Header("References")]
    public RectTransform canvasRect;
    public GameObject segmentPrefab;
    public GameObject endpointPrefab;
    public GameObject startPointPrefab;

    [Header("Defaults")]
    [Tooltip("Default number of segments created for a line")]
    public int defaultSegmentCount = 8;

    [HideInInspector] public List<RectTransform> segments;
    [HideInInspector] public RectTransform endPointRect;
    [HideInInspector] public RectTransform startPointRect;

    [HideInInspector] public bool lastGeneratedWasPattern = false;

    [HideInInspector] public List<List<RectTransform>> segmentGroups = new List<List<RectTransform>>();
    [HideInInspector] public List<RectTransform> startPointRects = new List<RectTransform>();
    [HideInInspector] public List<RectTransform> endPointRects = new List<RectTransform>();

    /// <summary>
    /// Generate a straight segmented line between start and end in canvas-local coordinates.
    /// segCount includes the endpoint; minimum 2.
    /// Ensures the group's element order is front-to-back: group[0] is the first segment the player must clear.
    /// </summary>
    public void GenerateLine(Vector2 start, Vector2 end, int segCount)
    {
        if (segCount < 2) segCount = Mathf.Max(2, defaultSegmentCount);
        if (canvasRect == null)
        {
            Debug.LogWarning("[StraightPathGenerator] canvasRect is null. Cannot generate segments.");
            return;
        }

        // Clear previous data
        ClearExisting();

        segments = new List<RectTransform>();
        segmentGroups = new List<List<RectTransform>>();
        startPointRects = new List<RectTransform>();
        endPointRects = new List<RectTransform>();

        // We want positions from start -> end (inclusive) and then create group entries
        List<Vector2> positions = new List<Vector2>(segCount);
        for (int i = 1; i <= segCount; i++)
        {
            float t = i / (float)segCount;
            Vector2 pos = Vector2.Lerp(start, end, t);
            positions.Add(pos);
        }

        // Instantiate UI elements in the same order as positions so group[0] = nearest to start
        List<RectTransform> thisGroup = new List<RectTransform>();

        for (int i = 0; i < positions.Count; i++)
        {
            GameObject prefab = (i == positions.Count - 1) ? endpointPrefab : segmentPrefab;
            if (prefab == null)
            {
                Debug.LogWarning("[StraightPathGenerator] segment or endpoint prefab is not assigned.");
                continue;
            }

            GameObject segObj = Instantiate(prefab, canvasRect);
            segObj.name = $"SP_Segment_{i}"; // helpful for debugging in hierarchy
            RectTransform rt = segObj.GetComponent<RectTransform>();
            rt.anchoredPosition = positions[i];

            // ensure the segment renders above existing canvas content (keep UI visible)
            segObj.transform.SetAsLastSibling();

            // Add to flat list and to group in the same forward order
            segments.Add(rt);
            thisGroup.Add(rt);

            if (i == positions.Count - 1)
            {
                endPointRect = rt;
                endPointRects.Add(rt);
            }
        }


        // The first element of the group should be the one the player clears first (closest to start).
        // We already added in start->end order so group[0] is correct.
        segmentGroups.Add(thisGroup);

        // create a start marker (optional) — keep single reference for legacy systems
        if (startPointPrefab != null)
        {
            GameObject startObj = Instantiate(startPointPrefab, canvasRect);
            RectTransform rtStart = startObj.GetComponent<RectTransform>();
            rtStart.anchoredPosition = start;
            startPointRects.Add(rtStart);
            startPointRect = rtStart;
            startObj.transform.SetAsLastSibling();
        }

        // mark pattern-mode true so GazePathTracker will use sequential clearing
        lastGeneratedWasPattern = true;
    }

    /// <summary>
    /// Clears/destroys any UI created by this generator and resets state.
    /// </summary>
    public void ClearExisting()
    {
        if (segments != null)
        {
            for (int i = segments.Count - 1; i >= 0; i--)
            {
                if (segments[i] != null)
                    Destroy(segments[i].gameObject);
            }
            segments.Clear();
            segments = null;
        }

        if (segmentGroups != null)
        {
            foreach (var g in segmentGroups)
            {
                if (g == null) continue;
                for (int i = g.Count - 1; i >= 0; i--)
                {
                    if (g[i] != null)
                        Destroy(g[i].gameObject);
                }
                g.Clear();
            }
            segmentGroups.Clear();
        }

        if (endPointRects != null)
        {
            for (int i = endPointRects.Count - 1; i >= 0; i--)
            {
                if (endPointRects[i] != null)
                    Destroy(endPointRects[i].gameObject);
            }
            endPointRects.Clear();
        }

        if (endPointRect != null)
        {
            Destroy(endPointRect.gameObject);
            endPointRect = null;
        }

        if (startPointRects != null)
        {
            for (int i = startPointRects.Count - 1; i >= 0; i--)
            {
                if (startPointRects[i] != null)
                    Destroy(startPointRects[i].gameObject);
            }
            startPointRects.Clear();
        }

        if (startPointRect != null)
        {
            Destroy(startPointRect.gameObject);
            startPointRect = null;
        }

        lastGeneratedWasPattern = false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        defaultSegmentCount = Mathf.Max(2, defaultSegmentCount);
    }
#endif
}

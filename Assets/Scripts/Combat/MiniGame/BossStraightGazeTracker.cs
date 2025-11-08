using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// BossStraightGazeTracker
/// - Minimal tracker for StraightPathGenerator only (pattern / sequential clearing).
/// - Uses an explicit owner (BossMiniGame) to call ConnectorCleared() when a connector finishes.
/// - Defensive: waits if generator hasn't produced UI yet and falls back to using flat segments as a group.
/// </summary>
public class BossStraightGazeTracker : MonoBehaviour
{
    [Tooltip("The StraightPathGenerator instance that produced the UI for the current connector.")]
    public StraightPathGenerator straightGenerator;   // required

    [Tooltip("The gaze dot (must have dotRect assigned).")]
    public GazeDot gazeDot;                           // required (dotRect must be set)

    [Tooltip("Owner to notify when the connector is cleared.")]
    public BossMiniGame owner;

    // runtime
    private bool tracking = false;

    private List<bool> groupStarted;
    private int patternHitCount = 0;
    private int patternTotalCount = 0;

    private void Awake()
    {
        if (straightGenerator == null)
            Debug.LogWarning("[BossStraightGazeTracker] straightGenerator is not assigned.");
        if (gazeDot == null)
            Debug.LogWarning("[BossStraightGazeTracker] gazeDot is not assigned.");
        if (owner == null)
            Debug.LogWarning("[BossStraightGazeTracker] owner is not assigned (BossMiniGame).");
    }

    /// <summary>
    /// Start tracking the current straightGenerator UI.
    /// Safe to call even if generator hasn't produced UI yet; tracker will wait.
    /// </summary>
    public void StartTracking()
    {
        if (straightGenerator == null)
        {
            Debug.LogWarning("[BossStraightGazeTracker] StartTracking called but straightGenerator is null.");
            return;
        }

        if (gazeDot == null)
        {
            Debug.LogWarning("[BossStraightGazeTracker] StartTracking called but gazeDot is null.");
            return;
        }

        tracking = true;
        groupStarted = null;
        patternHitCount = 0;
        patternTotalCount = 0;

        //Debug.Log("[BossStraightGazeTracker] StartTracking: waiting for generator data...");
    }

    private void Update()
    {
        if (!tracking || straightGenerator == null || gazeDot == null) return;

        // If the generator hasn't produced segmentGroups yet, but segments exist, build fallback group.
        if ((straightGenerator.segmentGroups == null || straightGenerator.segmentGroups.Count == 0)
            && (straightGenerator.segments != null && straightGenerator.segments.Count > 0))
        {
            // Build single group from flat segments (start -> end)
            straightGenerator.segmentGroups = new List<List<RectTransform>>();
            var fallback = new List<RectTransform>(straightGenerator.segments);
            straightGenerator.segmentGroups.Add(fallback);
            Debug.Log("[BossStraightGazeTracker] Fallback: created single segmentGroup from flat segments.");
        }

        // If generator still hasn't produced anything, wait (avoid false clear).
        int groupCount = (straightGenerator.segmentGroups != null) ? straightGenerator.segmentGroups.Count : 0;
        int flatCount = (straightGenerator.segments != null) ? straightGenerator.segments.Count : 0;
        if (groupCount == 0 && flatCount == 0)
        {
            // still nothing created - wait another frame
            return;
        }

        // Ensure our groupStarted list is initialized only once (after generator has data)
        if (groupStarted == null)
        {
            groupStarted = new List<bool>(groupCount);
            patternTotalCount = 0;
            for (int g = 0; g < groupCount; g++)
            {
                groupStarted.Add(false);
                var grp = straightGenerator.segmentGroups[g];
                if (grp != null) patternTotalCount += grp.Count;
            }

            //Debug.LogFormat("[BossStraightGazeTracker] StartTracking: groups={0} totalSegments={1}", groupCount, patternTotalCount);
        }

        // standard per-frame overlap checks using the gaze dot rect
        RectTransform dotRect = gazeDot.dotRect;
        if (dotRect == null) return;

        Vector3[] dotWorldCorners = new Vector3[4];
        dotRect.GetWorldCorners(dotWorldCorners);
        Rect dotScreenRect = new Rect(dotWorldCorners[0], dotWorldCorners[2] - dotWorldCorners[0]);

        var groups = straightGenerator.segmentGroups;
        var startRects = straightGenerator.startPointRects;
        var endRects = straightGenerator.endPointRects;

        // 1) start markers
        for (int g = 0; g < groupCount; g++)
        {
            if (groupStarted == null || g >= groupStarted.Count) break;
            if (groupStarted[g]) continue;

            RectTransform startRect = null;
            if (startRects != null && g < startRects.Count) startRect = startRects[g];
            if (startRect == null) continue;

            Vector3[] startCorners = new Vector3[4];
            startRect.GetWorldCorners(startCorners);
            Rect startScreenRect = new Rect(startCorners[0], startCorners[2] - startCorners[0]);

            if (dotScreenRect.Overlaps(startScreenRect))
            {
                groupStarted[g] = true;
                Destroy(startRect.gameObject);
                if (straightGenerator.startPointRects != null && g < straightGenerator.startPointRects.Count)
                    straightGenerator.startPointRects[g] = null;
            }
        }

        // 2) sequential clearing per active group
        for (int g = 0; g < groupCount; g++)
        {
            if (groupStarted == null || g >= groupStarted.Count) continue;
            if (!groupStarted[g]) continue;

            var group = groups[g];
            if (group == null || group.Count == 0) continue;

            RectTransform nextSeg = group[0];
            if (nextSeg == null)
            {
                // remove null entries defensively
                group.RemoveAt(0);
                continue;
            }

            Vector3[] segCorners = new Vector3[4];
            nextSeg.GetWorldCorners(segCorners);
            Rect segScreenRect = new Rect(segCorners[0], segCorners[2] - segCorners[0]);

            if (dotScreenRect.Overlaps(segScreenRect))
            {
                patternHitCount++;

                // remove from flat list if present
                if (straightGenerator.segments != null)
                {
                    for (int s = straightGenerator.segments.Count - 1; s >= 0; s--)
                    {
                        if (straightGenerator.segments[s] == nextSeg)
                        {
                            straightGenerator.segments.RemoveAt(s);
                            break;
                        }
                    }
                }

                if (nextSeg != null && nextSeg.gameObject != null)
                {
                    Destroy(nextSeg.gameObject);
                }

                group.RemoveAt(0);
            }
        }

        // 3) finish checks
        bool allGroupsEmpty = true;
        if (groups != null)
        {
            foreach (var g in groups)
            {
                if (g != null && g.Count > 0) { allGroupsEmpty = false; break; }
            }
        }

        // endpoints: only allow finishing if all groups empty
        if (endRects != null)
        {
            for (int e = 0; e < endRects.Count; e++)
            {
                var endRect = endRects[e];
                if (endRect == null) continue;

                Vector3[] endCorners = new Vector3[4];
                endRect.GetWorldCorners(endCorners);
                Rect endScreenRect = new Rect(endCorners[0], endCorners[2] - endCorners[0]);

                if (dotScreenRect.Overlaps(endScreenRect))
                {
                    if (allGroupsEmpty)
                    {
                        tracking = false;
                        StartCoroutine(DelayedNotifyAndReset());
                        return;
                    }
                }
            }
        }

        if (allGroupsEmpty)
        {
            tracking = false;
            StartCoroutine(DelayedNotifyAndReset());
            return;
        }
    }

    /// <summary>
    /// Wait one frame to let the generator/mini-game cleanup complete, then notify owner and reset.
    /// </summary>
    private IEnumerator DelayedNotifyAndReset()
    {
        //Debug.Log("[BossStraightGazeTracker] Connector cleared — notifying owner in next frame.");
        yield return null;

        if (owner != null)
        {
            //Debug.Log("[BossStraightGazeTracker] Notifying owner.ConnectorCleared()");
            try { owner.ConnectorCleared(); }
            catch (System.Exception ex) { Debug.LogWarning("[BossStraightGazeTracker] owner.ConnectorCleared() threw: " + ex.Message); }
        }
        else
        {
            Debug.LogWarning("[BossStraightGazeTracker] No owner assigned to notify.");
        }

        Reset();
    }

    public void Reset()
    {
        tracking = false;

        if (straightGenerator != null)
            straightGenerator.ClearExisting();

        groupStarted = null;
        patternHitCount = 0;
        patternTotalCount = 0;
    }

    public void StopTracking()
    {
        tracking = false;
        Reset();
    }
}

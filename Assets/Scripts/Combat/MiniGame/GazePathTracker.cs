using System.Collections.Generic;
using UnityEngine;

public class GazePathTracker : MonoBehaviour
{
    public static GazePathTracker Instance;

    public GazeDot gazeDot;                
    public CurvedPathGenerator pathGenerator;

    // runtime
    private bool tracking = false;

    // normal-curve mode
    private bool curveStarted = false;
    private int curveHitCount = 0;
    private int curveTotalCount = 0;

    // pattern-mode (barrier) state
    private List<bool> groupStarted;   
    private int patternHitCount = 0;
    private int patternTotalCount = 0;

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    /// <summary>
    /// Called by game code to begin tracking the currently generated path.
    /// The tracker inspects pathGenerator.lastGeneratedWasPattern to decide behaviour.
    /// </summary>
    public void StartTracking()
    {
        if (pathGenerator == null)
        {
            Debug.LogWarning("GazePathTracker: StartTracking called but pathGenerator is null.");
            return;
        }

        tracking = true;

        if (pathGenerator.lastGeneratedWasPattern)
        {
            // Pattern mode
            curveStarted = false;
            curveHitCount = 0;
            curveTotalCount = 0;

            int groupCount = (pathGenerator.segmentGroups != null) ? pathGenerator.segmentGroups.Count : 0;
            groupStarted = new List<bool>(groupCount);

            patternHitCount = 0;
            patternTotalCount = 0;

            for (int g = 0; g < groupCount; g++)
            {
                groupStarted.Add(false);

                var grp = pathGenerator.segmentGroups[g];
                if (grp != null) patternTotalCount += grp.Count;
            }
        }
        else
        {
            // Curve mode (original behaviour)
            curveStarted = false;
            curveHitCount = 0;
            curveTotalCount = (pathGenerator.segments != null) ? pathGenerator.segments.Count : 0;

            // clear any pattern state
            groupStarted = null;
            patternHitCount = 0;
            patternTotalCount = 0;
        }
    }

    private void Update()
    {
        if (!tracking || pathGenerator == null || gazeDot == null) return;

        RectTransform dotRect = gazeDot.dotRect;
        if (dotRect == null) return;

        Vector3[] dotWorldCorners = new Vector3[4];
        dotRect.GetWorldCorners(dotWorldCorners);
        Rect dotScreenRect = new Rect(dotWorldCorners[0], dotWorldCorners[2] - dotWorldCorners[0]);

        if (!pathGenerator.lastGeneratedWasPattern)
        {
            // ---------- Curve mode (original behaviour) ----------
            if (!curveStarted)
            {
                RectTransform startRect = null;
                if (pathGenerator.startPointRects != null && pathGenerator.startPointRects.Count > 0)
                    startRect = pathGenerator.startPointRects[0];
                else
                    startRect = pathGenerator.startPointRect;

                if (startRect != null)
                {
                    Vector3[] startCorners = new Vector3[4];
                    startRect.GetWorldCorners(startCorners);
                    Rect startScreenRect = new Rect(startCorners[0], startCorners[2] - startCorners[0]);

                    if (dotScreenRect.Overlaps(startScreenRect))
                    {
                        curveStarted = true;
                        Destroy(startRect.gameObject);
                        if (pathGenerator.startPointRect == startRect) pathGenerator.startPointRect = null;
                        if (pathGenerator.startPointRects != null && pathGenerator.startPointRects.Count > 0)
                            pathGenerator.startPointRects[0] = null;

                        // do not clear segments this frame
                        return;
                    }
                }

                return; // waiting for start touch
            }

            // After start touched: clear *any* segment the dot overlaps (original behavior).
            if (pathGenerator.segments != null)
            {
                for (int i = pathGenerator.segments.Count - 1; i >= 0; i--)
                {
                    var seg = pathGenerator.segments[i];
                    if (seg == null) continue;

                    Vector3[] segCorners = new Vector3[4];
                    seg.GetWorldCorners(segCorners);
                    Rect segRect = new Rect(segCorners[0], segCorners[2] - segCorners[0]);

                    if (dotScreenRect.Overlaps(segRect))
                    {
                        curveHitCount++;
                        Destroy(seg.gameObject);
                        pathGenerator.segments.RemoveAt(i);
                        break; // remove only one segment per frame
                    }
                }
            }

            // check endpoint overlap to complete
            RectTransform endRect = pathGenerator.endPointRect;
            if (endRect != null)
            {
                Vector3[] endCorners = new Vector3[4];
                endRect.GetWorldCorners(endCorners);
                Rect endRectScreen = new Rect(endCorners[0], endCorners[2] - endCorners[0]);

                if (dotScreenRect.Overlaps(endRectScreen))
                {
                    float accuracy = (curveTotalCount == 0) ? 1f : ((float)curveHitCount / curveTotalCount);
                    tracking = false;
                    FinishAndCleanup(accuracy);
                }
            }
        }
        else
        {
            // ---------- Pattern mode (per-line sequential clearing) ----------
            int groupCount = (pathGenerator.segmentGroups != null) ? pathGenerator.segmentGroups.Count : 0;

            // 1) check start markers (per group). touching a start activates that group.
            for (int g = 0; g < groupCount; g++)
            {
                if (groupStarted == null || g >= groupStarted.Count) break;
                if (groupStarted[g]) continue; // already activated

                RectTransform startRect = null;
                if (pathGenerator.startPointRects != null && g < pathGenerator.startPointRects.Count)
                    startRect = pathGenerator.startPointRects[g];

                if (startRect == null) continue;

                Vector3[] startCorners = new Vector3[4];
                startRect.GetWorldCorners(startCorners);
                Rect startScreenRect = new Rect(startCorners[0], startCorners[2] - startCorners[0]);

                if (dotScreenRect.Overlaps(startScreenRect))
                {
                    groupStarted[g] = true;
                    Destroy(startRect.gameObject);
                    pathGenerator.startPointRects[g] = null;
                    //Debug.Log($"GazePathTracker: Group {g} started.");
                    // do not clear segments this frame (user must touch start first)
                }
            }

            // 2) for each group that has been activated, allow clearing only the next segment (front of group)
            for (int g = 0; g < groupCount; g++)
            {
                if (groupStarted == null || g >= groupStarted.Count) continue;
                if (!groupStarted[g]) continue;

                var group = pathGenerator.segmentGroups[g];
                if (group == null || group.Count == 0) continue;

                // next required segment (ordered front)
                RectTransform nextSeg = group[0];
                if (nextSeg == null) continue;

                Vector3[] segCorners = new Vector3[4];
                nextSeg.GetWorldCorners(segCorners);
                Rect segScreenRect = new Rect(segCorners[0], segCorners[2] - segCorners[0]);

                if (dotScreenRect.Overlaps(segScreenRect))
                {
                    // clear it (one-per-frame)
                    patternHitCount++;

                    // remove from flat segments list (without double-destroy)
                    if (pathGenerator.segments != null)
                    {
                        for (int s = pathGenerator.segments.Count - 1; s >= 0; s--)
                        {
                            if (pathGenerator.segments[s] == nextSeg)
                            {
                                // remove reference but DO NOT destroy here (we will destroy below)
                                pathGenerator.segments.RemoveAt(s);
                                break;
                            }
                        }
                    }

                    // Destroy the GameObject for nextSeg (if still exists)
                    if (nextSeg != null && nextSeg.gameObject != null)
                    {
                        Destroy(nextSeg.gameObject);
                    }

                    // remove from the group's list (this enforces sequential clearing)
                    group.RemoveAt(0);

                    // only clear one segment per frame per group, but continue loop to allow other groups to also clear one each frame
                }
            }

            // compute whether ALL groups are empty (used for endpoint finishing)
            bool allGroupsEmpty = true;
            if (pathGenerator.segmentGroups != null)
            {
                foreach (var g in pathGenerator.segmentGroups)
                {
                    if (g != null && g.Count > 0) { allGroupsEmpty = false; break; }
                }
            }

            // 2.5) End-point touches: only allow finishing if ALL groups are empty.
            if (pathGenerator.endPointRects != null)
            {
                for (int e = 0; e < pathGenerator.endPointRects.Count; e++)
                {
                    var endRect = pathGenerator.endPointRects[e];
                    if (endRect == null) continue;

                    Vector3[] endCorners = new Vector3[4];
                    endRect.GetWorldCorners(endCorners);
                    Rect endScreenRect = new Rect(endCorners[0], endCorners[2] - endCorners[0]);

                    if (dotScreenRect.Overlaps(endScreenRect))
                    {
                        if (allGroupsEmpty)
                        {
                            float accuracy = (patternTotalCount == 0) ? 1f : ((float)patternHitCount / patternTotalCount);
                            tracking = false;
                            //Debug.Log($"GazePathTracker: Endpoint {e} touched and all groups empty -> finishing. accuracy={accuracy}");
                            FinishAndCleanup(accuracy);
                            return;
                        }
                        else
                        {
                            //Debug.Log($"GazePathTracker: Endpoint {e} touched but not all groups empty yet -> ignoring (remaining groups).");
                        }
                    }
                }
            }

            // 3) check completion: all groups empty (this would handle case where last segment removed and no endpoint is required to be touched)
            if (allGroupsEmpty)
            {
                float accuracy = (patternTotalCount == 0) ? 1f : ((float)patternHitCount / patternTotalCount);
                tracking = false;
                FinishAndCleanup(accuracy);
            }
        }
    }

    /// <summary>
    /// Stop tracking and destroy generated UI. Report attack accuracy and exit gaze mode.
    /// </summary>
    private void FinishAndCleanup(float accuracy)
    {
        // ensure we remove any remaining UI created by the pathGenerator
        Reset();

        // notify game (same as existing flow)
        GameManager.Instance.onAttack?.Invoke(accuracy);

        // exit gaze mode (same as existing flow)
        GameManager.Instance.ExitGazeMode();
    }

    /// <summary>
    /// Reset / destroy generated path UI without triggering onAttack.
    /// </summary>
    public void Reset()
    {
        tracking = false;

        if (pathGenerator != null)
        {
            // destroy group's segments
            if (pathGenerator.segmentGroups != null)
            {
                foreach (var g in pathGenerator.segmentGroups)
                {
                    if (g == null) continue;
                    for (int i = g.Count - 1; i >= 0; i--)
                        if (g[i] != null)
                            Destroy(g[i].gameObject);
                    g.Clear();
                }
                pathGenerator.segmentGroups.Clear();
            }

            // destroy flat segments
            if (pathGenerator.segments != null)
            {
                for (int i = pathGenerator.segments.Count - 1; i >= 0; i--)
                    if (pathGenerator.segments[i] != null)
                        Destroy(pathGenerator.segments[i].gameObject);
                pathGenerator.segments.Clear();
            }

            // destroy end points
            if (pathGenerator.endPointRects != null)
            {
                for (int i = pathGenerator.endPointRects.Count - 1; i >= 0; i--)
                    if (pathGenerator.endPointRects[i] != null)
                        Destroy(pathGenerator.endPointRects[i].gameObject);
                pathGenerator.endPointRects.Clear();
            }

            if (pathGenerator.endPointRect != null)
            {
                Destroy(pathGenerator.endPointRect.gameObject);
                pathGenerator.endPointRect = null;
            }

            // destroy start markers
            if (pathGenerator.startPointRects != null)
            {
                for (int i = pathGenerator.startPointRects.Count - 1; i >= 0; i--)
                    if (pathGenerator.startPointRects[i] != null)
                        Destroy(pathGenerator.startPointRects[i].gameObject);
                pathGenerator.startPointRects.Clear();
            }

            if (pathGenerator.startPointRect != null)
            {
                Destroy(pathGenerator.startPointRect.gameObject);
                pathGenerator.startPointRect = null;
            }

            // reset generator state
            pathGenerator.segmentGroups = new List<List<RectTransform>>();
            pathGenerator.segments = new List<RectTransform>();
            pathGenerator.startPointRects = new List<RectTransform>();
            pathGenerator.endPointRects = new List<RectTransform>();
            pathGenerator.lastGeneratedWasPattern = false;
        }

        // reset tracker state
        curveStarted = false;
        curveHitCount = 0;
        curveTotalCount = 0;
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

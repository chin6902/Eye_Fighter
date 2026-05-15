using System.Collections.Generic;
using UnityEngine;

public class GazePathTracker : MonoBehaviour
{
    public static GazePathTracker Instance;

    public GazeDot gazeDot;
    public CurvedPathGenerator pathGenerator;

    [Header("Line SFX (per-line pitch ramp)")]
    public AudioClip lineClearClip;           // inspector: normal per-segment sound
    [Tooltip("Base pitch for the first cleared segment (usually 1.0)")]
    public float lineBasePitch = 1.0f;
    [Tooltip("Pitch step added for each subsequent cleared segment")]
    public float linePitchStep = 0.05f;
    [Tooltip("Maximum allowed pitch")]
    public float lineMaxPitch = 2.0f;
    [Range(0f, 1f)]
    public float lineSfxVolume = 0.8f;

    [Header("Final SFX (play on endpoint touch)")]
    public AudioClip finalSegmentClip;        // inspector: special sound played when endpoint is touched
    [Tooltip("Pitch used for finalSegmentClip (if used)")]
    public float finalSegmentPitch = 1.25f;
    [Range(0f, 1f)]
    public float finalSegmentVolume = 1f;

    // runtime
    private bool tracking = false;
    public bool trackingPaused = false;

    // normal-curve mode
    private bool curveStarted = false;
    private int curveHitCount = 0;
    private int curveTotalCount = 0;

    // pattern-mode (barrier) state
    private List<bool> groupStarted;
    private int patternHitCount = 0;
    private int patternTotalCount = 0;

    // per-group cleared counts (for pitch ramp per-line)
    private List<int> groupClearCounts;

    // キャッシュして GC を減らす（ロジックには影響なし）
    private readonly Vector3[] _cachedCornersDot = new Vector3[4];
    private readonly Vector3[] _cachedCornersA = new Vector3[4];

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

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

            // initialize per-group clear counters
            groupClearCounts = new List<int>(groupCount);
            patternTotalCount = 0;
            for (int g = 0; g < groupCount; g++)
            {
                groupStarted.Add(false);
                groupClearCounts.Add(0);

                var grp = pathGenerator.segmentGroups[g];
                if (grp != null) patternTotalCount += grp.Count;
            }

            patternHitCount = 0;
        }
        else
        {
            // Curve mode 
            curveStarted = false;
            curveHitCount = 0;
            curveTotalCount = (pathGenerator.segments != null) ? pathGenerator.segments.Count : 0;

            // clear any pattern state
            groupStarted = null;
            patternHitCount = 0;
            patternTotalCount = 0;
            groupClearCounts = null;
        }
    }

    private void Update()
    {
        if (!tracking || pathGenerator == null || gazeDot == null) return;
        if (trackingPaused) return;

        RectTransform dotRect = gazeDot.dotRect;
        if (dotRect == null) return;

        dotRect.GetWorldCorners(_cachedCornersDot);
        Rect dotScreenRect = new Rect(_cachedCornersDot[0], _cachedCornersDot[2] - _cachedCornersDot[0]);

        if (!pathGenerator.lastGeneratedWasPattern)
        {
            // ---------- Curve mode ----------

            if (!curveStarted)
            {
                RectTransform startRect = null;
                if (pathGenerator.startPointRects != null && pathGenerator.startPointRects.Count > 0)
                    startRect = pathGenerator.startPointRects[0];
                else
                    startRect = pathGenerator.startPointRect;

                if (startRect != null)
                {
                    startRect.GetWorldCorners(_cachedCornersA);
                    Rect startScreenRect = new Rect(_cachedCornersA[0], _cachedCornersA[2] - _cachedCornersA[0]);

                    if (dotScreenRect.Overlaps(startScreenRect))
                    {
                        curveStarted = true;
                        Destroy(startRect.gameObject); // start marker はプールしない

                        if (pathGenerator.startPointRect == startRect) pathGenerator.startPointRect = null;
                        if (pathGenerator.startPointRects != null && pathGenerator.startPointRects.Count > 0)
                            pathGenerator.startPointRects[0] = null;

                        // do not clear segments this frame
                        return;
                    }
                }

                return; // waiting for start touch
            }

            // After start touched: clear *any* segment the dot overlaps
            if (pathGenerator.segments != null)
            {
                for (int i = pathGenerator.segments.Count - 1; i >= 0; i--)
                {
                    var seg = pathGenerator.segments[i];
                    if (seg == null) continue;

                    seg.GetWorldCorners(_cachedCornersA);
                    Rect segRect = new Rect(_cachedCornersA[0], _cachedCornersA[2] - _cachedCornersA[0]);

                    if (dotScreenRect.Overlaps(segRect))
                    {
                        // clear
                        curveHitCount++;

                        // remove from list
                        pathGenerator.segments.RemoveAt(i);

                        // 中間セグメントならプールに返す (エンドポイントなら Destroy)
                        pathGenerator.ReleaseOrDestroySegment(seg.gameObject);

                        // compute pitch for normal ramp (based on hits so far)
                        float pitch = lineBasePitch + linePitchStep * (curveHitCount - 1);
                        pitch = Mathf.Min(pitch, lineMaxPitch);

                        if (lineClearClip != null)
                        {
                            SoundManager.PlaySFXWithPitch(lineClearClip, pitch, lineSfxVolume);
                        }
                        else
                        {
                            SoundManager.PlaySFX(SoundType.Touch, lineSfxVolume);
                        }

                        break; // remove only one segment per frame
                    }
                }
            }

            // check endpoint overlap to complete -> play final clip here (on endpoint touch)
            RectTransform endRect = pathGenerator.endPointRect;
            if (endRect != null)
            {
                endRect.GetWorldCorners(_cachedCornersA);
                Rect endRectScreen = new Rect(_cachedCornersA[0], _cachedCornersA[2] - _cachedCornersA[0]);

                if (dotScreenRect.Overlaps(endRectScreen))
                {
                    // play final clip on endpoint touch
                    if (finalSegmentClip != null)
                    {
                        SoundManager.PlaySFXWithPitch(finalSegmentClip, finalSegmentPitch, finalSegmentVolume);
                    }
                    else
                    {
                        SoundManager.PlaySFX(SoundType.Touch, finalSegmentVolume);
                    }

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

                startRect.GetWorldCorners(_cachedCornersA);
                Rect startScreenRect = new Rect(_cachedCornersA[0], _cachedCornersA[2] - _cachedCornersA[0]);

                if (dotScreenRect.Overlaps(startScreenRect))
                {
                    groupStarted[g] = true;
                    Destroy(startRect.gameObject); // start marker は Destroy

                    pathGenerator.startPointRects[g] = null;

                    // reset per-group clear counter when group activated
                    if (groupClearCounts != null && g < groupClearCounts.Count)
                        groupClearCounts[g] = 0;

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

                nextSeg.GetWorldCorners(_cachedCornersA);
                Rect segScreenRect = new Rect(_cachedCornersA[0], _cachedCornersA[2] - _cachedCornersA[0]);

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
                                pathGenerator.segments.RemoveAt(s);
                                break;
                            }
                        }
                    }

                    // 中間セグメントならプールに返す (エンドポイントなら Destroy)
                    pathGenerator.ReleaseOrDestroySegment(nextSeg.gameObject);

                    // update per-group clear counter and compute pitch for normal ramp
                    if (groupClearCounts != null && g < groupClearCounts.Count)
                    {
                        groupClearCounts[g]++; // 1-based cleared count for that group
                        int cleared = groupClearCounts[g];
                        float pitch = lineBasePitch + linePitchStep * (cleared - 1);
                        pitch = Mathf.Min(pitch, lineMaxPitch);

                        if (lineClearClip != null)
                        {
                            SoundManager.PlaySFXWithPitch(lineClearClip, pitch, lineSfxVolume);
                        }
                        else
                        {
                            SoundManager.PlaySFX(SoundType.Touch, lineSfxVolume);
                        }
                    }
                    else
                    {
                        if (lineClearClip != null)
                        {
                            SoundManager.PlaySFXWithPitch(lineClearClip, lineBasePitch, lineSfxVolume);
                        }
                        else
                        {
                            SoundManager.PlaySFX(SoundType.Touch, lineSfxVolume);
                        }
                    }

                    // remove from the group's list (this enforces sequential clearing)
                    group.RemoveAt(0);

                    // only clear one segment per frame per group
                }
            }

            // compute whether ALL groups are empty (used for endpoint finishing)
            bool allGroupsEmpty = true;
            if (pathGenerator.segmentGroups != null)
            {
                foreach (var g in pathGenerator.segmentGroups)
                {
                    if (g != null && g.Count > 0)
                    {
                        allGroupsEmpty = false;
                        break;
                    }
                }
            }

            // 2.5) End-point touches: only allow finishing if ALL groups are empty.
            if (pathGenerator.endPointRects != null)
            {
                for (int e = 0; e < pathGenerator.endPointRects.Count; e++)
                {
                    var endRect = pathGenerator.endPointRects[e];
                    if (endRect == null) continue;

                    endRect.GetWorldCorners(_cachedCornersA);
                    Rect endRectScreen = new Rect(_cachedCornersA[0], _cachedCornersA[2] - _cachedCornersA[0]);

                    if (dotScreenRect.Overlaps(endRectScreen))
                    {
                        if (allGroupsEmpty)
                        {
                            if (finalSegmentClip != null)
                            {
                                SoundManager.PlaySFXWithPitch(finalSegmentClip, finalSegmentPitch, finalSegmentVolume);
                            }
                            else
                            {
                                SoundManager.PlaySFX(SoundType.Touch, finalSegmentVolume);
                            }

                            float accuracy = (patternTotalCount == 0) ? 1f : ((float)patternHitCount / patternTotalCount);
                            tracking = false;
                            FinishAndCleanup(accuracy);
                            return;
                        }
                    }
                }
            }

            // 3) check completion: all groups empty (no endpoint touch)
            if (allGroupsEmpty)
            {
                float accuracy = (patternTotalCount == 0) ? 1f : ((float)patternHitCount / patternTotalCount);
                tracking = false;
                FinishAndCleanup(accuracy);
            }
        }
    }

    private void FinishAndCleanup(float accuracy)
    {
        // ensure we remove any remaining UI created by the pathGenerator
        Reset();

        // notify game (same as existing flow)
        GameManager.Instance.onAttack?.Invoke(accuracy);

        // exit gaze mode (same as existing flow)
        GameManager.Instance.ExitGazeMode();
    }

    public void Reset()
    {
        tracking = false;

        if (pathGenerator != null)
        {
            // 片付けはジェネレーター側の ClearExisting に任せる
            pathGenerator.ClearExisting();

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
        groupClearCounts = null;
        patternHitCount = 0;
        patternTotalCount = 0;
    }

    public void StopTracking()
    {
        tracking = false;
        Reset();
    }
}

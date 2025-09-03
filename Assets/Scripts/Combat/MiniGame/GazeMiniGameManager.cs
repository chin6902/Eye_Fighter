using UnityEngine;

/// <summary>
/// Generates the visual path when gaze-minigame enters. 
/// - Uses curved path for normal enemies (unchanged).
/// - Uses centered straight pattern for BarrierSpot targets and notifies the spot with NotifyAimed(true/false).
/// </summary>
public class GazeMiniGameManager : MonoBehaviour
{
    public static GazeMiniGameManager Instance;

    [Header("References")]
    public CurvedPathGenerator pathGenerator;
    public GazePathTracker tracker;
    public Canvas canvas;
    public Camera cam;

    [Header("Barrier pattern settings")]
    [Tooltip("Length (canvas-local units) used when generating barrier spot patterns. If <=0, generator.defaultPatternLength is used.")]
    public float patternLength = 100f;
    [Tooltip("Segments used for pattern generation (<=0 uses pathGenerator.segmentCount).")]
    public int patternSegments = -1;

    private bool hasGeneratedOnEnter = false;

    // currently aimed barrier spot (if any) so we can toggle NotifyAimed(false) later
    private BarrierSpot _currentAimedSpot = null;

    private void Start()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(this);
            return;
        }
    }

    private void Update()
    {
        // If gaze mode isn't active, ensure we clear state and return
        if (!GameManager.Instance.IsGazeModeActive())
        {
            hasGeneratedOnEnter = false;
            ClearAimedSpot();
            return;
        }

        // Only generate on entering the Gaze Trace Phase
        if (GameManager.Instance.currentGamePhase == GameManager.MiniGamePhase.GazeTracePhase)
        {
            if (!hasGeneratedOnEnter)
            {
                TryGenerateCurve();
                hasGeneratedOnEnter = true;
            }
        }
        else
        {
            // not in the trace phase -> reset generator state for next entry
            hasGeneratedOnEnter = false;
            ClearAimedSpot();
        }

        // debug / manual cycling (unchanged)
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            GameManager.Instance.enemyFinder.RefreshEnemies();
            GameManager.Instance.CycleGazeTarget();
            TryGenerateCurve();
        }
    }

    private void TryGenerateCurve()
    {
        Transform currentEnemy = GameManager.Instance.CurrentGazeTarget;

        // clear any previously drawn visuals first
        if (pathGenerator != null)
            pathGenerator.ClearExisting();

        if (currentEnemy == null)
        {
            Debug.Log("GazeMiniGameManager: No enemy in range to generate curve.");
            ClearAimedSpot();
            GameManager.Instance.ExitGazeMode();
            return;
        }

        // Try find a BarrierSpot on the target or its parents (safer)
        var spot = currentEnemy.GetComponent<BarrierSpot>();
        if (spot == null)
            spot = currentEnemy.GetComponentInParent<BarrierSpot>();

        if (spot != null)
        {
            // notify previously aimed spot if different
            if (_currentAimedSpot != spot)
            {
                ClearAimedSpot();
                _currentAimedSpot = spot;
            }

            _currentAimedSpot?.NotifyAimed(true);

            // generate barrier-style straight pattern centered on the spot
            if (pathGenerator != null && canvas != null)
            {
                float len = (patternLength > 0f) ? patternLength : pathGenerator.defaultPatternLength;
                pathGenerator.GeneratePatternAtSpot(spot.transform, canvas, cam, spot.SpotPattern, len, patternSegments);

                Debug.Log($"GazeMiniGameManager: Generated PATTERN for '{spot.name}' -> lastGeneratedWasPattern={pathGenerator.lastGeneratedWasPattern}, groups={pathGenerator.segmentGroups?.Count ?? 0}, segments(flat)={pathGenerator.segments?.Count ?? 0}, startPoints={pathGenerator.startPointRects?.Count ?? 0}");
            }
            else
            {
                Debug.LogWarning("GazeMiniGameManager: Missing pathGenerator or canvas when generating PATTERN.");
            }

            // now start tracker
            tracker?.StartTracking();
            return;
        }

        // Not a barrier spot -> normal enemy behaviour (unchanged)
        ClearAimedSpot();

        if (pathGenerator != null && canvas != null)
        {
            pathGenerator.GenerateCurveFromWorldObject(currentEnemy, canvas, cam);
            Debug.Log($"GazeMiniGameManager: Generated CURVE for '{currentEnemy.name}' -> lastGeneratedWasPattern={pathGenerator.lastGeneratedWasPattern}, segments={pathGenerator.segments?.Count ?? 0}");
        }
        else
        {
            Debug.LogWarning("GazeMiniGameManager: Missing pathGenerator or canvas when generating CURVE.");
        }

        tracker?.StartTracking();
    }


    /// <summary>
    /// Clears notification on previously aimed barrier spot (if any).
    /// Does NOT clear the path visuals (caller may call pathGenerator.ClearExisting separately).
    /// </summary>
    private void ClearAimedSpot()
    {
        if (_currentAimedSpot != null)
        {
            _currentAimedSpot.NotifyAimed(false);
            _currentAimedSpot = null;
        }
    }
}

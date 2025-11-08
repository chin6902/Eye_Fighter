using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static GameManager;

/// <summary>
/// BossMiniGame (plays cutscene after final connector cleared)
/// - Set 'bossTarget' at runtime (GameManager should call SetBossTarget) or assign in inspector.
/// - Set cutsceneController, sword/magic prefabs in inspector.
/// - When final connector cleared: plays cutscene, waits for it to finish, then finishes mini-game.
/// </summary>
public class BossMiniGame : MonoBehaviour
{
    [Header("Scene anchors")]
    public List<Transform> anchorPoints = new List<Transform>();

    [Header("References")]
    public StraightPathGenerator pathGenerator;
    public BossStraightGazeTracker gazePathTracker;

    [Header("Cutscene")]
    [Tooltip("CutsceneController that owns the PlayableDirector")]
    public CutsceneController cutsceneController;

    [Header("Canvas / camera (optional)")]
    public Camera uiCamera;
    public Canvas uiCanvas;

    [Header("Connector settings")]
    public int segmentsPerConnector = 8;

    // runtime
    private int currentConnectorIndex = -1; // connects anchor[i] -> anchor[i+1]
    private bool isRunning = false;
    private bool awaitingConnectorClear = false;

    // boss that invoked the mini-game (set by GameManager.StartBossMiniGame before calling StartMiniGame)
    [HideInInspector] public BossHealth bossTarget;

    private void Awake()
    {
        if (pathGenerator == null)
            Debug.LogError("[BossMiniGame] pathGenerator (StraightPathGenerator) reference is required.");

        if (pathGenerator != null && uiCanvas == null && pathGenerator.canvasRect != null)
            uiCanvas = pathGenerator.canvasRect.GetComponent<Canvas>();
    }

    private void OnDisable()
    {
        StopMiniGameImmediate();
    }

    /// <summary>
    /// Set the boss that this mini-game is for.
    /// Call this from GameManager before calling StartMiniGame:
    ///   mini.SetBossTarget(bossComponent);
    /// </summary>
    public void SetBossTarget(BossHealth boss)
    {
        bossTarget = boss;
    }

    public void StartMiniGame()
    {
        if (isRunning) return;
        if (anchorPoints == null || anchorPoints.Count < 2)
        {
            Debug.LogWarning("[BossMiniGame] Need at least 2 anchor points.");
            return;
        }
        if (pathGenerator == null || gazePathTracker == null)
        {
            Debug.LogWarning("[BossMiniGame] Missing pathGenerator or gazePathTracker.");
            return;
        }

        pathGenerator.ClearExisting();

        currentConnectorIndex = 0;
        isRunning = true;
        awaitingConnectorClear = false;

        SpawnConnectorForCurrentIndex();
    }

    private void SpawnConnectorForCurrentIndex()
    {
        if (!isRunning) return;
        if (currentConnectorIndex < 0 || currentConnectorIndex >= anchorPoints.Count - 1)
        {
            Debug.LogWarning("[BossMiniGame] invalid connector index.");
            return;
        }

        if (uiCanvas == null)
        {
            if (pathGenerator != null && pathGenerator.canvasRect != null)
                uiCanvas = pathGenerator.canvasRect.GetComponent<Canvas>();
            if (uiCanvas == null)
            {
                Debug.LogError("[BossMiniGame] No uiCanvas found. Assign uiCanvas or set pathGenerator.canvasRect.");
                return;
            }
        }

        Camera camToUse = ResolveCameraForCanvas(uiCanvas);

        Vector3 worldA = anchorPoints[currentConnectorIndex].position;
        Vector3 worldB = anchorPoints[currentConnectorIndex + 1].position;

        Vector2 startCanvas = WorldToCanvasPosition(uiCanvas, camToUse, worldA);
        Vector2 endCanvas = WorldToCanvasPosition(uiCanvas, camToUse, worldB);

        // generate the connector
        pathGenerator.ClearExisting();
        pathGenerator.GenerateLine(startCanvas, endCanvas, segmentsPerConnector);
        pathGenerator.lastGeneratedWasPattern = true;
        if (pathGenerator.segmentGroups == null) pathGenerator.segmentGroups = new List<List<RectTransform>>();

        // wire tracker to generator & this owner then start next-frame to avoid race conditions
        gazePathTracker.straightGenerator = pathGenerator;
        gazePathTracker.owner = this;

        StartCoroutine(StartTrackerNextFrame());

        awaitingConnectorClear = true;
    }

    private IEnumerator StartTrackerNextFrame()
    {
        yield return null;
        gazePathTracker.StartTracking();
    }

    /// <summary>
    /// Called by BossStraightGazeTracker to notify the mini-game that the connector has been cleared.
    /// (No accuracy param — minimal notification)
    /// </summary>
    public void ConnectorCleared()
    {
        if (!isRunning || !awaitingConnectorClear) return;

        awaitingConnectorClear = false;

        // defensive clear
        pathGenerator.ClearExisting();

        currentConnectorIndex++;

        if (currentConnectorIndex >= anchorPoints.Count - 1)
        {
            // final connector done -> instead of immediately ending, play the cutscene
            isRunning = false;

            if (cutsceneController != null && bossTarget != null)
            {
                // ensure tracker stopped and UI cleared
                if (gazePathTracker != null) gazePathTracker.StopTracking();
                if (pathGenerator != null) pathGenerator.ClearExisting();

                GameManager.Instance?.SetGazeCanvas(false, MiniGamePhase.BossMiniGamePhase);

                // Play cutscene and when finished tell GameManager to end mini-game.
                cutsceneController.PlayCutsceneForBoss(bossTarget, OnCutsceneComplete);
            }
            else
            {
                // fallback: end directly
                FinishAndEndMiniGame();
            }
        }
        else
        {
            // reset timer for next connector (GameManager resets boss-mini-game timer)
            if (GameManager.Instance != null)
                GameManager.Instance.ResetBossMiniGameTimer();

            // spawn next connector next frame to avoid race with tracker cleanup
            StartCoroutine(SpawnNextConnectorNextFrame());
        }
    }

    private IEnumerator SpawnNextConnectorNextFrame()
    {
        yield return null;
        SpawnConnectorForCurrentIndex();
    }

    private void OnCutsceneComplete()
    {
        // Cutscene finished. Now finalize the mini-game result.
        FinishAndEndMiniGame();
    }

    private void FinishAndEndMiniGame()
    {
        // Now call GameManager to end the boss mini-game.
        // IMPORTANT: GameManager.EndBossMiniGame should be robust (only call FinalizeDeath if needed).
        if (GameManager.Instance != null)
        {
            GameManager.Instance.EndBossMiniGame(true);
        }
    }

    public void StopMiniGameImmediate()
    {
        if (!isRunning && !awaitingConnectorClear) return;

        isRunning = false;
        awaitingConnectorClear = false;

        if (gazePathTracker != null) gazePathTracker.StopTracking();
        if (pathGenerator != null) pathGenerator.ClearExisting();

        currentConnectorIndex = -1;
    }

    private Camera ResolveCameraForCanvas(Canvas canvas)
    {
        if (canvas == null) return uiCamera ?? Camera.main;
        if (canvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;
        if (canvas.renderMode == RenderMode.ScreenSpaceCamera)
        {
            if (canvas.worldCamera != null) return canvas.worldCamera;
            if (uiCamera != null) return uiCamera;
            return Camera.main;
        }
        return uiCamera != null ? uiCamera : Camera.main;
    }

    private Vector2 WorldToCanvasPosition(Canvas canvas, Camera cam, Vector3 worldPos)
    {
        if (canvas == null)
        {
            Debug.LogWarning("[BossMiniGame] WorldToCanvasPosition: canvas is null.");
            return Vector2.zero;
        }

        Vector2 screenPoint;
        if (cam != null)
        {
            Vector3 sp = cam.WorldToScreenPoint(worldPos);
            screenPoint = new Vector2(sp.x, sp.y);
        }
        else
        {
            Vector3 sp = RectTransformUtility.WorldToScreenPoint(null, worldPos);
            screenPoint = new Vector2(sp.x, sp.y);
        }

        RectTransform canvasRect = canvas.GetComponent<RectTransform>();
        Vector2 localPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect,
            screenPoint,
            (canvas.renderMode == RenderMode.ScreenSpaceOverlay) ? null : cam,
            out localPoint
        );
        return localPoint;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.K))
        {
            ConnectorCleared();
        }
    }
}

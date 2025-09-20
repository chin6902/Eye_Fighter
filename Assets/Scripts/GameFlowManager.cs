using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class GameFlowManager : MonoBehaviour
{
    public static GameFlowManager Instance { get; private set; }

    [Header("References")]
    [SerializeField] private Health playerHealth;
    [SerializeField] private List<EnemyController> enemies; // optional inspector list (not required)

    [Header("Pause Menu UI")]
    [SerializeField] private GameObject pauseMenuPanel;
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button retryButton1;

    [Header("End-Game UI")]
    [SerializeField] private GameObject endGamePanel;
    [SerializeField] private TextMeshProUGUI endGameTitleText;
    [SerializeField] private TextMeshProUGUI averageAccuracyText;
    [SerializeField] private Button retryButton2;
    [SerializeField] private Button homeButton;

    [Header("Spawner")]
    [Tooltip("Optional: assign your EnemySpawner (used to subscribe to boss spawn events).")]
    [SerializeField] private EnemySpawner spawner;

    [Header("Events (hook these from inspector or code)")]
    public UnityEvent<GameObject> OnBossStarted;  // passes spawned boss GameObject (may be null if spawner handled spawn)
    public UnityEvent OnBossDefeated;

    private bool isPaused = false;
    private bool gameEnded = false;
    private List<float> allAccuracies = new List<float>();

    // tracked bosses (we subscribe to their OnDie)
    private readonly HashSet<BossHealth> trackedBosses = new HashSet<BossHealth>();
    // stored handlers so we can unsubscribe cleanly
    private readonly Dictionary<BossHealth, Action> bossDeathHandlers = new Dictionary<BossHealth, Action>();

    // whether the boss-phase has started (boss spawn time reached)
    private bool bossPhaseStarted = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        // UI init
        if (pauseMenuPanel != null) pauseMenuPanel.SetActive(false);
        if (endGamePanel != null) endGamePanel.SetActive(false);

        if (resumeButton != null) resumeButton.onClick.AddListener(ResumeGame);
        if (retryButton1 != null) retryButton1.onClick.AddListener(RetryGame);
        if (retryButton2 != null) retryButton2.onClick.AddListener(RetryGame);
        if (homeButton != null) homeButton.onClick.AddListener(ReturnHome);

        if (GameManager.Instance != null)
            GameManager.Instance.onAttack += (acc) => allAccuracies.Add(acc);

        // register any bosses that already exist in the scene (e.g. placed in authoring)
        RegisterAllExistingBosses();

        // subscribe to spawner event if spawner assigned
        if (spawner != null)
        {
            spawner.OnBossSpawned += HandleSpawnerBossSpawned;
        }
    }

    private void OnDestroy()
    {
        // unsubscribe boss death handlers
        foreach (var kv in new List<KeyValuePair<BossHealth, Action>>(bossDeathHandlers))
        {
            var boss = kv.Key;
            var handler = kv.Value;
            if (boss != null && handler != null)
            {
                try { boss.OnDie -= handler; } catch { }
            }
        }

        bossDeathHandlers.Clear();
        trackedBosses.Clear();

        if (spawner != null)
            spawner.OnBossSpawned -= HandleSpawnerBossSpawned;

        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (gameEnded) return;

        // toggle pause
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (isPaused) ResumeGame();
            else PauseGame();
        }

        if (!isPaused)
        {
            // 1) Game Over?
            if (playerHealth != null && playerHealth.CurrentHealth <= 0)
            {
                EndGame("GAME OVER");
                return;
            }

            // 2) Game Clear?
            // Only declare game clear after boss phase started and all tracked bosses are gone.
            if (bossPhaseStarted)
            {
                if (trackedBosses.Count == 0)
                {
                    // Boss-phase finished -> fire event and end game as clear
                    OnBossDefeated?.Invoke();
                    EndGame("GAME CLEAR");
                    return;
                }
            }
            else
            {
                // Boss-phase not started yet:
                // Do NOT call Game Clear when normal enemies are temporarily 0 (spawner may respawn).
                // If you need a level type with no boss, add a design-time flag to allow early-clear.
            }
        }
    }

    // ---------------- Boss tracking ----------------

    /// <summary>
    /// Called by EnemySpawner when it spawns the boss (spawner invokes OnBossSpawned).
    /// </summary>
    private void HandleSpawnerBossSpawned(GameObject bossGO)
    {
        // mark that boss-phase started
        bossPhaseStarted = true;

        // notify external listeners (HUD/music/timeline)
        OnBossStarted?.Invoke(bossGO);

        // try to register boss health if present
        if (bossGO != null)
        {
            var bh = bossGO.GetComponent<BossHealth>();
            if (bh != null) RegisterBoss(bh);
            else Debug.LogWarning("[GameFlowManager] Boss spawned but no BossHealth found on the spawned GameObject.");
        }

        // defensive: ask spawner to reduce spawn rate for boss-phase (spawner may already do this)
        if (spawner != null)
        {
            try { spawner.ReduceSpawnRateForBoss(); } catch { }
        }
    }

    /// <summary>
    /// Register a boss instance so we can listen for its death.
    /// Call this for dynamically spawned bosses (or pre-placed ones).
    /// </summary>
    public void RegisterBoss(BossHealth boss)
    {
        if (boss == null) return;
        if (trackedBosses.Contains(boss)) return;

        trackedBosses.Add(boss);

        Action handler = () => OnBossDied(boss);
        boss.OnDie += handler;
        bossDeathHandlers[boss] = handler;
    }

    /// <summary>
    /// Unregister a boss (and unsubscribe).
    /// </summary>
    public void UnregisterBoss(BossHealth boss)
    {
        if (boss == null) return;
        if (!trackedBosses.Contains(boss)) return;

        if (bossDeathHandlers.TryGetValue(boss, out var handler) && handler != null)
        {
            try { boss.OnDie -= handler; } catch { }
            bossDeathHandlers.Remove(boss);
        }

        trackedBosses.Remove(boss);
    }

    private void OnBossDied(BossHealth boss)
    {
        if (boss != null) UnregisterBoss(boss);

        // If there are no more tracked bosses and the boss phase has started, handle defeat in Update (or do it here)
        if (bossPhaseStarted && trackedBosses.Count == 0)
        {
            OnBossDefeated?.Invoke();
            EndGame("GAME CLEAR");
        }
    }

    /// <summary>
    /// Find & register any BossHealth instances already present in the scene at start.
    /// </summary>
    private void RegisterAllExistingBosses()
    {
        var bosses = UnityEngine.Object.FindObjectsByType<BossHealth>(FindObjectsSortMode.None);
        if (bosses == null || bosses.Length == 0) return;

        foreach (var b in bosses)
            RegisterBoss(b);

        if (bosses.Length > 0) bossPhaseStarted = true; // if a boss already existed, mark boss-phase started
    }

    // ---------------- UI / end game ----------------

    private void PauseGame()
    {
        isPaused = true;
        Time.timeScale = 0f;
        if (GameManager.Instance != null) GameManager.Instance.isPaused = true;

        if (pauseMenuPanel != null) pauseMenuPanel.SetActive(true);
        if (endGamePanel != null) endGamePanel.SetActive(false);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void ResumeGame()
    {
        isPaused = false;
        if (GameManager.Instance != null)
        {
            Time.timeScale = GameManager.Instance.CurrentTimeScale;
            GameManager.Instance.isPaused = false;
        }
        else
        {
            Time.timeScale = 1f;
        }

        Time.fixedDeltaTime = 0.02f * Time.timeScale;

        if (pauseMenuPanel != null) pauseMenuPanel.SetActive(false);
        if (endGamePanel != null) endGamePanel.SetActive(false);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void EndGame(string message)
    {
        gameEnded = true;
        Time.timeScale = 0f;
        if (GameManager.Instance != null) GameManager.Instance.isPaused = true;

        if (endGameTitleText != null) endGameTitleText.text = message;
        if (endGamePanel != null) endGamePanel.SetActive(true);
        if (pauseMenuPanel != null) pauseMenuPanel.SetActive(false);

        if (averageAccuracyText != null)
        {
            averageAccuracyText.gameObject.SetActive(true);
            if (allAccuracies.Count > 0)
            {
                float sum = 0f;
                foreach (var a in allAccuracies) sum += a;
                float avg = sum / allAccuracies.Count;
                averageAccuracyText.text = $"Average Gaze Accuracy:\n{avg * 100f:F1}%";
            }
            else
            {
                averageAccuracyText.text = $"Average Gaze Accuracy:\n0.0%";
            }
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void RetryGame()
    {
        Time.timeScale = 1f;
        Loader.Load(Loader.Scene.GameScene);
    }

    private void ReturnHome()
    {
        Time.timeScale = 1f;
        Loader.Load(Loader.Scene.MainMenu);
    }
}

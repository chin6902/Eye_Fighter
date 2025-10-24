using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class GameFlowManager : MonoBehaviour
{
    public static GameFlowManager Instance { get; private set; }

    [Header("References")]
    [SerializeField] private Health playerHealth;

    [Header("Pause Menu UI")]
    [SerializeField] private GameObject pauseMenuPanel;
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button retryButton1;

    [Header("End-Game UI")]
    [SerializeField] private GameObject endGamePanel;
    [SerializeField] private TextMeshProUGUI endGameTitleText;
    [SerializeField] private TextMeshProUGUI averageAccuracyText;
    [SerializeField] private TextMeshProUGUI projectilesClearedText;
    [SerializeField] private Button retryButton2;
    [SerializeField] private Button homeButton;

    [Header("Spawner")]
    [Tooltip("Optional: assign your EnemySpawner (used to subscribe to boss spawn events).")]
    [SerializeField] private EnemySpawner spawner;

    [Header("Events (hook these from inspector or code)")]
    public UnityEvent<GameObject> OnBossStarted;  // passes spawned boss GameObject (may be null if spawner handled spawn)
    public UnityEvent OnBossDefeated;

    // runtime state
    private bool isPaused = false;
    private bool gameEnded = false;
    private List<float> allAccuracies = new List<float>();

    // projectile cleared counter
    private int projectilesCleared = 0;

    // tracked bosses (we subscribe to their OnDie)
    private readonly HashSet<BossHealth> trackedBosses = new HashSet<BossHealth>();
    // stored handlers so we can unsubscribe cleanly
    private readonly Dictionary<BossHealth, Action> bossDeathHandlers = new Dictionary<BossHealth, Action>();

    // whether the boss-phase has started (boss spawn time reached)
    private bool bossPhaseStarted = false;

    // Only declare "boss-phase active" after we've actually seen at least one boss.
    // This prevents declaring clear if bossPhaseStarted was true but no bosses exist.
    private bool bossPhaseConfirmed = false;

    // cached handler so we can remove it later
    private Action<float> attackHandler;

    private void Awake()
    {
        if (Instance == null) Instance = this;

        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Start()
    {
        // Reset runtime data and subscriptions to behave like a fresh run.
        ResetRuntimeState();

        // UI init
        if (pauseMenuPanel != null) pauseMenuPanel.SetActive(false);
        if (endGamePanel != null) endGamePanel.SetActive(false);

        // Defensive: remove previous listeners then add.
        if (resumeButton != null)
        {
            resumeButton.onClick.RemoveAllListeners();
            resumeButton.onClick.AddListener(ResumeGame);
        }

        if (retryButton1 != null)
        {
            retryButton1.onClick.RemoveAllListeners();
            retryButton1.onClick.AddListener(RetryGame);
        }

        if (retryButton2 != null)
        {
            retryButton2.onClick.RemoveAllListeners();
            retryButton2.onClick.AddListener(RetryGame);
        }

        if (homeButton != null)
        {
            homeButton.onClick.RemoveAllListeners();
            homeButton.onClick.AddListener(ReturnHome);
        }

        if (GameManager.Instance != null)
        {
            if (attackHandler != null)
                GameManager.Instance.onAttack -= attackHandler;

            attackHandler = (acc) => allAccuracies.Add(acc);
            GameManager.Instance.onAttack += attackHandler;
        }

        RegisterAllExistingBosses();

        if (spawner != null)
        {
            try { spawner.OnBossSpawned -= HandleSpawnerBossSpawned; } catch { }
            spawner.OnBossSpawned += HandleSpawnerBossSpawned;
        }

        // Subscribe to defensive mini-game projectile cleared event
        try
        {
            DefensiveMiniGame.OnProjectileCleared -= HandleProjectileCleared;
        }
        catch { }
        DefensiveMiniGame.OnProjectileCleared += HandleProjectileCleared;

        // ensure projectile text hidden initially
        if (projectilesClearedText != null)
            projectilesClearedText.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;

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
        {
            try { spawner.OnBossSpawned -= HandleSpawnerBossSpawned; } catch { }
        }

        if (GameManager.Instance != null && attackHandler != null)
        {
            try { GameManager.Instance.onAttack -= attackHandler; } catch { }
            attackHandler = null;
        }

        // Unsubscribe defensive mini-game event
        try
        {
            DefensiveMiniGame.OnProjectileCleared -= HandleProjectileCleared;
        }
        catch { }

        if (Instance == this) Instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Reset runtime state when a new scene is loaded (covers Retry from pause menu).
        ResetRuntimeState();

        // re-register bosses in the new scene
        RegisterAllExistingBosses();

        // re-hook spawner if inspector reference is still valid
        if (spawner != null)
        {
            try { spawner.OnBossSpawned -= HandleSpawnerBossSpawned; } catch { }
            spawner.OnBossSpawned += HandleSpawnerBossSpawned;
        }

        // re-hook attack handler
        if (GameManager.Instance != null)
        {
            if (attackHandler == null)
                attackHandler = (acc) => allAccuracies.Add(acc);

            try { GameManager.Instance.onAttack -= attackHandler; } catch { }
            GameManager.Instance.onAttack += attackHandler;
        }

        // re-subscribe defensive mini-game event (defensive)
        try
        {
            DefensiveMiniGame.OnProjectileCleared -= HandleProjectileCleared;
        }
        catch { }
        DefensiveMiniGame.OnProjectileCleared += HandleProjectileCleared;

        // ensure UI panels are hidden at scene start
        if (pauseMenuPanel != null) pauseMenuPanel.SetActive(false);
        if (endGamePanel != null) endGamePanel.SetActive(false);

        // ensure projectile text hidden at scene start
        if (projectilesClearedText != null)
            projectilesClearedText.gameObject.SetActive(false);
    }

    /// <summary>
    /// Reset runtime-only state and unsubscribe any previously registered handlers.
    /// Call this on initial start and on scene reloads (retry).
    /// </summary>
    private void ResetRuntimeState()
    {
        // clear paused / ended flags
        isPaused = false;
        gameEnded = false;

        // ensure time scale is normal (important if Pause set timeScale = 0)
        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;

        // clear accuracies from previous run
        allAccuracies.Clear();

        // reset projectile counter
        projectilesCleared = 0;

        // unsubscribe any boss death handlers previously registered
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

        // reset boss-phase flags (must confirm later by actually finding/seeing bosses)
        bossPhaseStarted = false;
        bossPhaseConfirmed = false;
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
            // Only declare game clear after boss phase started AND we've confirmed the boss phase by seeing at least one boss.
            if (bossPhaseStarted && bossPhaseConfirmed)
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
                // Boss-phase not started/confirmed yet:
                // Do NOT call Game Clear when normal enemies are temporarily 0 (spawner may respawn).
            }
        }
    }

    // ---------------- Boss tracking ----------------

    private void HandleSpawnerBossSpawned(GameObject bossGO)
    {
        // mark that boss-phase started and confirmed (spawner actually spawned a boss)
        bossPhaseStarted = true;
        bossPhaseConfirmed = true;

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

    public void RegisterBoss(BossHealth boss)
    {
        if (boss == null) return;
        if (trackedBosses.Contains(boss)) return;

        trackedBosses.Add(boss);

        // once we register at least one boss, confirm boss-phase
        bossPhaseStarted = true;
        bossPhaseConfirmed = true;

        Action handler = () => OnBossDied(boss);
        boss.OnDie += handler;
        bossDeathHandlers[boss] = handler;
    }

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

        // If there are no more tracked bosses and the boss phase has started/confirmed, handle defeat here.
        if (bossPhaseStarted && bossPhaseConfirmed && trackedBosses.Count == 0)
        {
            OnBossDefeated?.Invoke();
            EndGame("GAME CLEAR");
        }
    }

    private void RegisterAllExistingBosses()
    {
        // clear previous tracked data
        trackedBosses.Clear();
        foreach (var kv in new List<KeyValuePair<BossHealth, Action>>(bossDeathHandlers))
        {
            if (kv.Key != null && kv.Value != null)
            {
                try { kv.Key.OnDie -= kv.Value; } catch { }
            }
        }
        bossDeathHandlers.Clear();

        var bosses = UnityEngine.Object.FindObjectsByType<BossHealth>(FindObjectsSortMode.None);
        if (bosses == null || bosses.Length == 0)
        {
            bossPhaseStarted = false;
            bossPhaseConfirmed = false;
            return;
        }

        foreach (var b in bosses) RegisterBoss(b);

        if (bosses.Length > 0)
        {
            bossPhaseStarted = true;
            bossPhaseConfirmed = true;
        }
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

        // Show number of projectiles cleared below average accuracy
        if (projectilesClearedText != null)
        {
            projectilesClearedText.gameObject.SetActive(true);
            projectilesClearedText.text = $"Projectiles Cleared:\n{projectilesCleared}";
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void RetryGame()
    {
        // Reset state now to avoid race/leftover state before the scene reload happens.
        ResetRuntimeState();

        // Ensure time is running before reloading
        Time.timeScale = 1f;

        //Return to default BGM
        SoundManager.PlayDefaultBGM(0.5f);

        // Use your Loader helper as before
        Loader.Load(Loader.Scene.GameScene);
    }

    private void ReturnHome()
    {
        Time.timeScale = 1f;

        SoundManager.PlayDefaultBGM(0.5f);

        Loader.Load(Loader.Scene.MainMenu);
    }

    // ---------------- Defensive Mini-Game integration ----------------

    private void HandleProjectileCleared(Projectile projectile)
    {
        projectilesCleared++;
    }
}

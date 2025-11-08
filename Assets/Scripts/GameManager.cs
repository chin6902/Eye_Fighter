using KinematicCharacterController.Walkthrough.AddingImpulses;
using System;
using Unity.AppUI.UI;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    public Action<float> onAttack;
    public event Action onParryPerformed;
    public Action<float, float> onSkillCooldownChanged;
    public Action<float, float> onParryCooldownChanged;
    public Action<float, float, MiniGamePhase> onPhaseTimerChanged;

    [Header("Element System")]
    public ElementDatabase elementDatabase;

    [Header("Player & Systems")]
    [SerializeField] private MyCharacterController MyCharacterController;
    [SerializeField] public GetEnemy enemyFinder;

    [Header("UI")]
    [SerializeField] private GameObject GazeDotCanvas;
    [SerializeField] private GameObject Phase1;
    [SerializeField] private GameObject Phase2;
    [SerializeField] private GameObject BossMiniGame;

    [Header("Phase Durations")]
    [Tooltip("Seconds for Element Select Phase")]
    public float ElementSelectDuration = 10f;

    [Tooltip("Seconds for Gaze Trace Phase")]
    public float GazeTraceDuration = 13f;

    [Tooltip("Seconds for BossMiniGame Phase")]
    public float BossMiniGameDuration = 13f;

    [Header("Auto Exit Settings")]
    public float autoExitCheckDelay = 3f;

    [Header("Skill Settings")]
    [Tooltip("Cooldown (sec) after using Gaze Mode")]
    public float SkillCooldown = 3f;
    public float _skillTimer = 0f;

    [Header("Parry Settings")]
    [Tooltip("Seconds between parry attempts")]
    public float ParryCooldown = 5f;
    private float _parryTimer = 0f;

    [Header("UnlimitedSkill Settings")]
    [Tooltip("After a successful parry, unlimited use for this many seconds")]
    public float UnlimitedRemaining => _unlimitedTimer;
    public float UnlimitedDuration = 5f;
    public float _unlimitedTimer = 0f;

    [Header("GameFlow Settings")]
    public bool isPaused = false;
    public float CurrentTimeScale { get; private set; } = 1f;

    [Header("Defensive Gaze (Hold Q)")]
    [Tooltip("Max value of defensive gauge (starts full).")]
    public float DefensiveGaugeMax = 100f;
    [SerializeField] private float _defensiveGauge = 100f; // current gauge

    [Tooltip("How many gauge points recovered per second when NOT holding Q.")]
    public float DefensiveRecoverPerSecond = 2f;

    [Tooltip("How many gauge points consumed per second while holding Q.")]
    public float DefensiveConsumePerSecond = 5f;

    [Tooltip("Slow amount multiplier used when entering defensive gaze. (Passed into ApplySlowMotion)")]
    public float DefensiveSlowAmount = 0.5f;

    private bool _defensiveGazeActive = false;

    public Action<float> onDefensiveGaugeChanged;

    [Header("BossMiniGame")]
    private BossHealth _bossInMiniGame = null;

    public Transform CurrentGazeTarget { get; private set; }
    private int currentGazeTargetIndex = -1;

    private bool GazeMode;
    private float time;

    public enum MiniGamePhase
    {
        None,
        ElementSelectPhase,
        GazeTracePhase,
        BossMiniGamePhase
    }

    public MiniGamePhase currentGamePhase;

    public enum ElementType
    {
        None,
        Fire,
        Electric,
        Water
    }

    public ElementType selectedElement { get; private set; } = ElementType.None;


    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }

        onSkillCooldownChanged?.Invoke(_skillTimer, SkillCooldown);
        onParryCooldownChanged?.Invoke(_parryTimer, ParryCooldown);

        ElementDatabase.Init(elementDatabase);

        SetGazeCanvas(false, MiniGamePhase.None);

        _defensiveGauge = DefensiveGaugeMax;
    }

    private void Update()
    {
        if (isPaused)
        {
            return;
        }

        HandleTimers();
        HandleInput();
        UpdateSkillTimers();
        UpdateParryTimer();
        UpdateDefensiveGauge();
    }

    /// <summary>
    /// Checks for player input each frame.
    /// </summary>
    private void HandleInput()
    {
        if (Input.GetKeyDown(KeyCode.E) && currentGamePhase == MiniGamePhase.None)
        {
            // allow if unlimited OR timer expired
            if (_unlimitedTimer > 0f || _skillTimer <= 0f)
            {
                EnterElementSelectPhase();
                _skillTimer = SkillCooldown;
            }
        }

        // Don't allow defensive gaze to start while offensive gaze mode is active
        if (!GazeMode)
        {
            // Start/maintain while Q is held and there's gauge remaining
            if (Input.GetKey(KeyCode.Q))
            {
                if (_defensiveGauge > 0f)
                {
                    if (!_defensiveGazeActive)
                    {
                        EnterDefensiveGaze();
                    }
                }
                else
                {
                    // out of gauge -> force exit
                    if (_defensiveGazeActive)
                    {
                        ExitDefensiveGaze();
                    }
                }
            }

            // On Q release, exit defensive gaze
            if (Input.GetKeyUp(KeyCode.Q))
            {
                if (_defensiveGazeActive)
                    ExitDefensiveGaze();
            }
        }
    }


    /// <summary>
    /// Updates timers for active phases.
    /// </summary>
    private void HandleTimers()
    {
        if (currentGamePhase == MiniGamePhase.ElementSelectPhase || currentGamePhase == MiniGamePhase.GazeTracePhase || currentGamePhase == MiniGamePhase.BossMiniGamePhase)
        {
            time -= Time.unscaledDeltaTime;

            float max = (currentGamePhase == MiniGamePhase.ElementSelectPhase) ? ElementSelectDuration
                    : (currentGamePhase == MiniGamePhase.GazeTracePhase) ? GazeTraceDuration
                    : BossMiniGameDuration;

            onPhaseTimerChanged?.Invoke(time, max, currentGamePhase);

            if (time <= 0f)
            {
                Debug.Log($"{currentGamePhase} timed out.");
                if (currentGamePhase == MiniGamePhase.BossMiniGamePhase)
                {
                    EndBossMiniGame(false);
                }
                else
                {
                    ExitGazeMode();
                }
            }
        }
    }

    private void UpdateSkillTimers()
    {
        // Regular cooldown
        if (_skillTimer > 0f)
        {
            _skillTimer -= Time.deltaTime;
            if (_skillTimer < 0f) _skillTimer = 0f;
        }

        // Unlimited buff expires
        if (_unlimitedTimer > 0f)
        {
            _unlimitedTimer -= Time.deltaTime;
            if (_unlimitedTimer < 0f) _unlimitedTimer = 0f;
        }

        onSkillCooldownChanged?.Invoke(_skillTimer, SkillCooldown);
    }

    private void UpdateParryTimer()
    {
        if (_parryTimer > 0f)
        {
            _parryTimer -= Time.deltaTime;
            if (_parryTimer < 0f) _parryTimer = 0f;
            onParryCooldownChanged?.Invoke(_parryTimer, ParryCooldown);
        }
    }

    /// <summary>
    /// Starts the Element Select Phase.
    /// </summary>
    private void EnterElementSelectPhase()
    {
        Debug.Log("Element Select Phase started.");

        currentGamePhase = MiniGamePhase.ElementSelectPhase;
        GazeMode = true;
        time = ElementSelectDuration;

        enemyFinder.RefreshEnemies();

        if (enemyFinder.Enemies.Count > 0)
        {
            CurrentGazeTarget = enemyFinder.GetClosestEnemyInFront();
            currentGazeTargetIndex = enemyFinder.Enemies.IndexOf(CurrentGazeTarget);
        }
        else
        {
            Debug.Log("No enemies found. Cancelling Element Select.");
            ExitGazeMode();
            return;
        }

        ApplySlowMotion(true, 0.2f);
        SetGazeCanvas(true, MiniGamePhase.ElementSelectPhase);

        StartCoroutine(CheckAutoExitAfterDelay(autoExitCheckDelay));
    }

    /// <summary>
    /// Confirms the selected element & starts the Gaze Trace Phase.
    /// </summary>
    public void ConfirmElementSelection()
    {
        Debug.Log("Element confirmed. Starting Gaze Trace Phase.");

        if (CurrentGazeTarget == null)
        {
            Debug.Log("No valid enemy to trace.");
            ExitGazeMode();
            return;
        }

        currentGamePhase = MiniGamePhase.GazeTracePhase;
        time = GazeTraceDuration;

        ApplySlowMotion(true, 0.65f);
        SetGazeCanvas(true, MiniGamePhase.GazeTracePhase);

        StartCoroutine(CheckAutoExitAfterDelay(autoExitCheckDelay));
    }

    /// <summary>
    /// Checks after a delay whether to auto-exit if no valid enemies remain.
    /// </summary>
    private System.Collections.IEnumerator CheckAutoExitAfterDelay(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);

        if (!GazeMode) yield break;

        enemyFinder.RefreshEnemies();

        if (enemyFinder.Enemies.Count == 0)
        {
            Debug.Log("No enemies found in range. Auto exiting Gaze Mode.");
            ExitGazeMode();
        }
    }

    /// <summary>
    /// Cycles to next enemy in the list.
    /// </summary>
    public void CycleGazeTarget()
    {
        if (!GazeMode || enemyFinder.Enemies.Count <= 1)
            return;

        currentGazeTargetIndex = (currentGazeTargetIndex + 1) % enemyFinder.Enemies.Count;
        CurrentGazeTarget = enemyFinder.Enemies[currentGazeTargetIndex];
    }

    /// <summary>
    /// Exits any gaze mode & resets time scale.
    /// </summary>
    public void ExitGazeMode()
    {
        Debug.Log("Exiting Gaze Mode");

        GazeMode = false;
        ApplySlowMotion(false, 1);

        CurrentGazeTarget = null;
        currentGazeTargetIndex = -1;
        currentGamePhase = MiniGamePhase.None;
        selectedElement = ElementType.None;

        SetGazeCanvas(false, MiniGamePhase.None);

        if (GazePathTracker.Instance != null)
        {
            GazePathTracker.Instance.StopTracking();
        }
    }

    /// <summary>
    /// Enables/disables UI canvases for phases.
    /// </summary>
    public void SetGazeCanvas(bool isActive, MiniGamePhase phase)
    {
        GazeDotCanvas.SetActive(isActive);
        Phase1.SetActive(phase == MiniGamePhase.ElementSelectPhase && isActive);
        Phase2.SetActive(phase == MiniGamePhase.GazeTracePhase && isActive);
        BossMiniGame.SetActive(phase == MiniGamePhase.BossMiniGamePhase && isActive);
    }

    /// <summary>
    /// Sets time scale and gravity for slow motion effect.
    /// </summary>
    private void ApplySlowMotion(bool active, float slowAmount)
    {
        if (active)
        {
            CurrentTimeScale = 0.1f * slowAmount;
            Time.timeScale = CurrentTimeScale;
            Time.fixedDeltaTime = 0.02f * CurrentTimeScale;
            MyCharacterController.GravityMultiplier = 0.1f * slowAmount;
        }
        else
        {
            CurrentTimeScale = 1f;
            Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.02f;
            MyCharacterController.GravityMultiplier = 1f;
        }
    }

    public bool IsGazeModeActive() => GazeMode;

    public void SetSelectedElement(ElementType element)
    {
        selectedElement = element;
        Debug.Log($"Selected element is now: {selectedElement}");
    }

    public void GrantUnlimitedSkill()
    {
        _unlimitedTimer = UnlimitedDuration;
    }

    public bool TryUseParry()
    {
        if (_parryTimer > 0f) return false;
        _parryTimer = ParryCooldown;
        onParryCooldownChanged?.Invoke(_parryTimer, ParryCooldown);
        onParryPerformed?.Invoke();
        return true;
    }

    private void EnterDefensiveGaze()
    {
        _defensiveGazeActive = true;

        // Put the game into a slow-motion defensive state.
        // Reuse your ApplySlowMotion method. Use DefensiveSlowAmount as a parameter.
        ApplySlowMotion(true, DefensiveSlowAmount);

        // Enable gaze UI so the gaze dot system can be used (we pass MiniGamePhase.None so Phase1/2 stay off).
        SetGazeCanvas(true, MiniGamePhase.None);

        // If you want to cancel existing offensive tracking for the defensive mode, ensure CurrentGazeTarget is nulled:
        CurrentGazeTarget = null;
        currentGazeTargetIndex = -1;

        // Optionally notify UI that gauge changed (initial)
        onDefensiveGaugeChanged?.Invoke(_defensiveGauge);
    }

    private void ExitDefensiveGaze()
    {
        _defensiveGazeActive = false;

        // Revert slow motion.
        ApplySlowMotion(false, 1f);

        // Hide gaze UI
        SetGazeCanvas(false, MiniGamePhase.None);

        // Clear any defensive-specific tracking (if needed)
        if (GazePathTracker.Instance != null)
        {
            GazePathTracker.Instance.StopTracking();
        }
    }

    /* Called every frame from Update() to change gauge up/down and notify UI. */
    private void UpdateDefensiveGauge()
    {
        if (_defensiveGazeActive && Input.GetKey(KeyCode.Q))
        {
            // consume gauge while Q is held
            _defensiveGauge -= DefensiveConsumePerSecond * Time.unscaledDeltaTime;
            if (_defensiveGauge <= 0f)
            {
                _defensiveGauge = 0f;
                // Auto-exit if gauge depleted
                if (_defensiveGazeActive) ExitDefensiveGaze();
            }

            onDefensiveGaugeChanged?.Invoke(_defensiveGauge);
        }
        else
        {
            // recover gauge when not holding Q
            if (_defensiveGauge < DefensiveGaugeMax)
            {
                _defensiveGauge += DefensiveRecoverPerSecond * Time.deltaTime;
                if (_defensiveGauge > DefensiveGaugeMax) _defensiveGauge = DefensiveGaugeMax;
                onDefensiveGaugeChanged?.Invoke(_defensiveGauge);
            }
        }
    }

    /// Recover defensive gauge by `amount` (clamped to DefensiveGaugeMax) and notify listeners.
    public void RecoverDefensiveGauge(float amount)
    {
        if (amount <= 0f) return;
        _defensiveGauge += amount;
        if (_defensiveGauge > DefensiveGaugeMax) _defensiveGauge = DefensiveGaugeMax;
        onDefensiveGaugeChanged?.Invoke(_defensiveGauge);
    }

    /// Try to consume `amount` defensive gauge. Returns true if consumption succeeded.
    public bool TryConsumeDefensiveGauge(float amount)
    {
        if (amount <= 0f) return true;
        if (_defensiveGauge >= amount)
        {
            _defensiveGauge -= amount;
            onDefensiveGaugeChanged?.Invoke(_defensiveGauge);
            return true;
        }
        return false;
    }

    public float DefensiveGaugeNormalized => DefensiveGaugeMax > 0f ? (_defensiveGauge / DefensiveGaugeMax) : 0f;

    /// <summary>
    /// BossMiniGame start: puts player into boss mini-game mode targeting the specified boss.
    /// </summary>
    public void StartBossMiniGame(BossHealth boss)
    {
        if (boss == null) return;
        Debug.Log("Starting Boss MiniGame targeting: " + boss.name);

        currentGamePhase = MiniGamePhase.BossMiniGamePhase;
        GazeMode = true;
        time = BossMiniGameDuration;

        _bossInMiniGame = boss;

        // set gaze target to the boss transform so gaze systems point at it
        CurrentGazeTarget = boss.transform;
        currentGazeTargetIndex = -1;

        ApplySlowMotion(true, 0.3f);
        SetGazeCanvas(true, MiniGamePhase.BossMiniGamePhase);

        // Start the mini-game logic
        if (BossMiniGame != null)
        {
            var mini = BossMiniGame.GetComponent<BossMiniGame>();
            if (mini != null)
            {
                mini.SetBossTarget(boss);

                mini.StartMiniGame();
            }
        }
    }

    /// <summary>
    /// BossMiniGame end: called when mini-game is cleared or failed.
    /// <summary>
    public void EndBossMiniGame(bool cleared)
    {
        // If there was no boss tracked (defensive), just exit gaze mode.
        if (_bossInMiniGame == null)
        {
            ExitGazeMode();
            return;
        }

        var boss = _bossInMiniGame;
        _bossInMiniGame = null; // clear the stored reference immediately so other flows don't confuse it

        if (cleared)
        {
            //Boss mini-game cleared. Finalizing boss (if not already finalized by cutscene)

            try
            {
                if (!boss.IsFinalizedDead)
                {
                    boss.FinalizeDeath();
                }
                else
                {
                    Debug.Log("[GameManager] Boss already finalized by cutscene; skipping FinalizeDeath().");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GameManager] Failed to finalize boss death: " + ex);
            }
        }
        else
        {
            //Boss mini-game failed. Recovering boss HP and resuming combat.
            try
            {
                boss.RecoverFromMiniGame(0.2f);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GameManager] Failed boss.RecoverFromMiniGame(): " + ex);
            }
        }

        // Now that the boss is finalized or recovered, hide gaze UI and return to normal gameplay.
        ExitGazeMode();
    }


    public void ResetBossMiniGameTimer()
    {
        if (currentGamePhase != MiniGamePhase.BossMiniGamePhase)
        {
            // Only reset if we're actually in the boss mini-game.
            return;
        }

        time = BossMiniGameDuration;
        onPhaseTimerChanged?.Invoke(time, BossMiniGameDuration, currentGamePhase);
    }
}

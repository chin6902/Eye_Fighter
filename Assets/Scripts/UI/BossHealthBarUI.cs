using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BossHealthBarUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private EnemySpawner spawner;
    [SerializeField] private Image barImage;
    [SerializeField] private GameObject bossHPBarVisual;

    [Header("Countdown UI (new)")]
    [Tooltip("Root GameObject of the countdown UI. Starts inactive and will be enabled during countdown.")]
    [SerializeField] private GameObject countdownUI;
    [SerializeField] private TextMeshProUGUI countdownTextOverride;

    [Header("Smoothing")]
    [Tooltip("Fill speed in fill-units per second (higher = faster).")]
    [SerializeField] private float smoothFillSpeed = 6f;

    private BossHealth bossHealth;
    private float targetFill = 1f;
    private float currentFill = 1f;
    private Coroutine fillCoroutine;

    private TextMeshProUGUI countdownText;
    private Coroutine countdownCoroutine;

    private void Awake()
    {
        if (bossHPBarVisual != null)
        {
            bossHPBarVisual.SetActive(false);
        }

        if (countdownUI != null)
        {
            countdownUI.SetActive(false);

            countdownText = countdownTextOverride;
        }
    }

    private void Start()
    {
        if (spawner == null)
        {
            Debug.LogWarning("[BossHealthBarUI] Spawner reference is missing. Please assign it in inspector.");
            return;
        }

        spawner.OnBossSpawned += OnBossSpawned;

        // subscribe to new countdown / cutscene events
        spawner.OnBossCountdownStarted += HandleBossCountdownStarted;
        spawner.OnCutsceneStarted += HandleCutsceneStarted;
    }

    private void OnDestroy()
    {
        if (spawner != null)
        {
            spawner.OnBossSpawned -= OnBossSpawned;
            spawner.OnBossCountdownStarted -= HandleBossCountdownStarted;
            spawner.OnCutsceneStarted -= HandleCutsceneStarted;
        }

        UnsubscribeFromBoss();
    }

    private void OnBossSpawned(GameObject bossGO)
    {
        if (bossGO == null) return;

        var bh = bossGO.GetComponent<BossHealth>();
        if (bh == null)
        {
            Debug.LogWarning("[BossHealthBarUI] Spawned boss has no BossHealth component.");
            return;
        }

        Initialize(bh);
    }

    private void Initialize(BossHealth boss)
    {
        UnsubscribeFromBoss();

        bossHealth = boss;

        // Set initial fill instantly to current HP fraction (no visible pop)
        currentFill = targetFill = GetFillFromBoss();
        if (barImage != null)
        {
            barImage.fillAmount = currentFill;
        }

        // Subscribe to events
        bossHealth.OnTakeDamage += BossHealth_OnTakeDamage;
        bossHealth.OnDie += BossHealth_OnDie;
        bossHealth.OnRecoveredFromMiniGame += BossHealth_OnRecoveredFromMiniGame;

        if (bossHPBarVisual != null)
        {
            bossHPBarVisual.SetActive(true);
        }
    }

    private void UnsubscribeFromBoss()
    {
        if (bossHealth != null)
        {
            bossHealth.OnTakeDamage -= BossHealth_OnTakeDamage;
            bossHealth.OnDie -= BossHealth_OnDie;
            bossHealth.OnRecoveredFromMiniGame -= BossHealth_OnRecoveredFromMiniGame;
        }

        bossHealth = null;
    }

    private float GetFillFromBoss()
    {
        if (bossHealth == null) return 0f;
        return (float)bossHealth.CurrentHP / Mathf.Max(1, bossHealth.maxHP);
    }

    // Called for any damage event (including RecoverFromMiniGame because that raises OnTakeDamage)
    private void BossHealth_OnTakeDamage()
    {
        StartFillTo(GetFillFromBoss());
    }

    // Called when boss recovers from mini-game (explicit event)
    private void BossHealth_OnRecoveredFromMiniGame()
    {
        // Ensure fill animates to the recovered hp value
        StartFillTo(GetFillFromBoss());
    }

    private void BossHealth_OnDie()
    {
        UnsubscribeFromBoss();

        if (bossHPBarVisual != null)
        {
            bossHPBarVisual.SetActive(false);
        }
    }

    private void StartFillTo(float newTarget)
    {
        newTarget = Mathf.Clamp01(newTarget);

        // if a coroutine is active, stop it and start a new one to avoid jumps
        if (fillCoroutine != null) StopCoroutine(fillCoroutine);
        fillCoroutine = StartCoroutine(FillCoroutine(currentFill, newTarget));
    }

    private IEnumerator FillCoroutine(float from, float to)
    {
        // If speed is <= 0, snap instantly to avoid division by zero
        if (smoothFillSpeed <= 0f)
        {
            currentFill = to;
            if (barImage != null) barImage.fillAmount = currentFill;
            yield break;
        }

        float diff = Mathf.Abs(to - from);
        if (diff <= Mathf.Epsilon)
        {
            currentFill = to;
            if (barImage != null) barImage.fillAmount = currentFill;
            yield break;
        }

        // duration such that speed * duration = diff => duration = diff / speed
        float duration = diff / smoothFillSpeed;
        float t = 0f;

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float alpha = Mathf.Clamp01(t / duration);
            currentFill = Mathf.Lerp(from, to, alpha);
            if (barImage != null) barImage.fillAmount = currentFill;
            yield return null;
        }

        currentFill = to;
        if (barImage != null) barImage.fillAmount = currentFill;
        fillCoroutine = null;
    }

    // --- countdown handlers ---

    private void HandleBossCountdownStarted(float duration)
    {
        // start or restart countdown display
        if (countdownCoroutine != null)
        {
            StopCoroutine(countdownCoroutine);
        }
        countdownCoroutine = StartCoroutine(CountdownCoroutine(duration));
    }

    private void HandleCutsceneStarted()
    {
        // ensure countdown hidden when cutscene begins
        if (countdownCoroutine != null)
        {
            StopCoroutine(countdownCoroutine);
            countdownCoroutine = null;
        }

        if (countdownUI != null)
        {
            countdownUI.SetActive(false);
        }
    }

    private IEnumerator CountdownCoroutine(float duration)
    {
        if (countdownUI == null)
            yield break;

        float remaining = duration;
        countdownUI.SetActive(true);

        // initial text update
        UpdateCountdownText(remaining);

        while (remaining > 0f)
        {
            yield return null;
            remaining -= Time.deltaTime;
            UpdateCountdownText(remaining);
        }

        countdownUI.SetActive(false);
        countdownCoroutine = null;
    }

    private void UpdateCountdownText(float remaining)
    {
        if (countdownText == null) return;

        int seconds = Mathf.CeilToInt(Mathf.Max(0f, remaining));
        countdownText.text = seconds.ToString();
    }
}

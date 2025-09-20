using System;
using UnityEngine;
using UnityEngine.UI;

public class BossHealthBarUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private EnemySpawner spawner;          // Drag your EnemySpawner here
    [SerializeField] private Image barImage;               // Drag the UI Image (fill) here
    [SerializeField] private GameObject bossHPBarVisual;   // Drag the visual root here

    [Header("Smoothing")]
    [SerializeField] private float smoothFillSpeed = 6f;

    private BossHealth bossHealth;
    private float targetFill = 1f;
    private float currentFill = 1f;
    private bool initialized = false;

    private void Awake()
    {
        // Hide the bar at start
        if (bossHPBarVisual != null)
            bossHPBarVisual.SetActive(false);
    }

    private void Start()
    {
        if (spawner == null)
        {
            Debug.LogWarning("[BossHealthBarUI] Spawner reference is missing. Please assign it in inspector.");
            return;
        }

        // Subscribe to the spawner event
        spawner.OnBossSpawned += OnBossSpawned;
    }

    private void OnDestroy()
    {
        if (spawner != null)
            spawner.OnBossSpawned -= OnBossSpawned;

        UnsubscribeFromBoss();
    }

    private void Update()
    {
        if (!initialized)
            return;

        if (!Mathf.Approximately(currentFill, targetFill))
        {
            currentFill = Mathf.MoveTowards(currentFill, targetFill, smoothFillSpeed * Time.deltaTime);
            if (barImage != null)
                barImage.fillAmount = currentFill;
        }
    }

    private void OnBossSpawned(GameObject bossGO)
    {
        if (bossGO == null) return;

        BossHealth newBoss = bossGO.GetComponent<BossHealth>();
        if (newBoss == null)
        {
            Debug.LogWarning("[BossHealthBarUI] Spawned boss has no BossHealth component.");
            return;
        }

        Initialize(newBoss);
    }

    private void Initialize(BossHealth boss)
    {
        if (boss == null) return;

        UnsubscribeFromBoss(); // clean previous boss if any

        bossHealth = boss;

        // Set initial fill
        targetFill = currentFill = GetFillFromBoss();
        if (barImage != null)
            barImage.fillAmount = currentFill;

        // Subscribe to boss events
        bossHealth.OnTakeDamage += BossHealth_OnTakeDamage;
        bossHealth.OnDie += BossHealth_OnDie;

        initialized = true;

        // Show the UI
        if (bossHPBarVisual != null)
            bossHPBarVisual.SetActive(true);
    }

    private float GetFillFromBoss()
    {
        if (bossHealth == null)
            return 0f;

        return (float)bossHealth.CurrentHP / Mathf.Max(1, bossHealth.maxHP);
    }

    private void BossHealth_OnTakeDamage()
    {
        targetFill = GetFillFromBoss();
    }

    private void BossHealth_OnDie()
    {
        UnsubscribeFromBoss();

        if (bossHPBarVisual != null)
            bossHPBarVisual.SetActive(false);
    }

    private void UnsubscribeFromBoss()
    {
        if (bossHealth != null)
        {
            bossHealth.OnTakeDamage -= BossHealth_OnTakeDamage;
            bossHealth.OnDie -= BossHealth_OnDie;
        }

        bossHealth = null;
        initialized = false;
    }
}

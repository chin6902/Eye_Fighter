using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;

public class EnemySpawner : MonoBehaviour
{
    [Header("Spawner Settings")]
    public List<Transform> manualSpawnPoints = new List<Transform>();
    public GameObject groupPrefab;
    public int groupsToSpawn = 3;

    [Header("Continuous spawning (optional)")]
    public bool continuousSpawning = false;
    public float spawnInterval = 6f;
    public int maxConcurrentEnemies = 50;

    [Header("Group respawn (random per point)")]
    public float groupRespawnDelayMin = 15f;
    public float groupRespawnDelayMax = 30f;
    public float respawnCheckInterval = 3f;

    [Header("Boss spawn")]
    public GameObject bossPrefab;                 // the real boss (AI, combat logic)
    public Transform bossSpawnPoint;
    public float bossSpawnDelay = 30f;
    public float postBossSpawnIntervalMultiplier = 2f;
    public int postBossMaxEnemies = 25;

    [Header("Cutscene (Timeline) - keep simple")]
    [Tooltip("PlayableDirector with the Timeline that plays the cutscene (camera + visuals).")]
    public PlayableDirector cutsceneDirector;

    [Tooltip("Set player health to invisible during cutscene")]
    public Health playerHealth;

    [Tooltip("Small buffer after cutscene ends before spawning real boss.")]
    public float cutscenePostDelay = 0.25f;

    [Header("Global spawn timing (applies to all groups)")]
    public float minSpawnDelayPerMember = 0.05f;
    public float maxSpawnDelayPerMember = 0.35f;

    [Header("Obstacle Check (fallback)")]
    public float checkRadius = 1f;
    public LayerMask obstacleMask;

    [Header("Cutscene BGM")]
    [Tooltip("Direct AudioClip to fade into for the cutscene")]
    public int cutsceneBgmIndex = 1;

    [Tooltip("Fade time (seconds) used for crossfading into the cutscene BGM.")]
    public float bgmFadeDuration = 1f;

    // runtime bookkeeping
    private readonly List<EnemyGroup> activeGroups = new List<EnemyGroup>();
    private readonly Dictionary<Transform, EnemyGroup> groupByPoint = new Dictionary<Transform, EnemyGroup>();
    private readonly Dictionary<Transform, float> lastSpawnTimeByPoint = new Dictionary<Transform, float>();
    private readonly Dictionary<Transform, float> respawnDelayByPoint = new Dictionary<Transform, float>();

    private Coroutine continuousSpawnRoutine;
    private Coroutine respawnLoopRoutine;
    private bool bossHasSpawned = false;

    public event Action<GameObject> OnBossSpawned;

    // spawn queue for serialized global spawning
    private struct SpawnRequest
    {
        public GameObject prefab;
        public Vector3 pos;
        public Quaternion rot;
        public EnemyGroup group;
        public Action<GameObject> onSpawned;
    }

    private readonly Queue<SpawnRequest> spawnQueue = new Queue<SpawnRequest>();
    private Coroutine spawnProcessorCoroutine;

    // cutscene wait flag
    private bool directorStoppedFlag = false;

    private void Start()
    {
        if (manualSpawnPoints == null) manualSpawnPoints = new List<Transform>();

        foreach (var pt in manualSpawnPoints)
        {
            if (pt == null) continue;
            groupByPoint[pt] = null;
            lastSpawnTimeByPoint[pt] = -9999f;
            respawnDelayByPoint[pt] = UnityEngine.Random.Range(groupRespawnDelayMin, groupRespawnDelayMax);
        }

        int spawnCount = Mathf.Clamp(groupsToSpawn, 0, manualSpawnPoints.Count);
        for (int i = 0; i < spawnCount; i++)
        {
            var pt = manualSpawnPoints[i];
            if (pt == null) continue;
            SpawnGroupAtPoint(pt.position, pt.rotation, pt);
            lastSpawnTimeByPoint[pt] = Time.time;
            respawnDelayByPoint[pt] = UnityEngine.Random.Range(groupRespawnDelayMin, groupRespawnDelayMax);
        }

        if (continuousSpawning)
            continuousSpawnRoutine = StartCoroutine(ContinuousSpawnLoop());

        respawnLoopRoutine = StartCoroutine(ManualPointRespawnLoop());

        if (bossPrefab != null)
            StartCoroutine(BossSpawnTimer());
    }

    private IEnumerator BossSpawnTimer()
    {
        yield return new WaitForSeconds(bossSpawnDelay);

        // If a Timeline (PlayableDirector) is assigned, play it, wait for it to stop, then spawn the boss.
        if (cutsceneDirector != null)
        {
            // Make player invincible during cutscene.
            if (playerHealth != null)
            {
                playerHealth.invincible = true;
            }

            SoundManager.CrossfadeToBGMIndex(cutsceneBgmIndex, bgmFadeDuration);

            // Prepare director stopped flag and subscribe to stopped event.
            directorStoppedFlag = false;
            cutsceneDirector.stopped += OnDirectorStopped;

            // Ensure director's GameObject is active and start from beginning.
            cutsceneDirector.gameObject.SetActive(true);
            cutsceneDirector.time = 0;
            cutsceneDirector.Play();

            // Wait until the director stops (set by OnDirectorStopped).
            yield return new WaitUntil(() => directorStoppedFlag);

            // Unsubscribe (best-effort).
            try { cutsceneDirector.stopped -= OnDirectorStopped; } catch { }

            // small buffer
            yield return new WaitForSeconds(cutscenePostDelay);

            // spawn the real boss
            SpawnRealBossAtPoint();
        }
        else
        {
            // no cutscene director — spawn immediately
            SpawnRealBossAtPoint();
        }
    }

    private void OnDirectorStopped(PlayableDirector director)
    {
        directorStoppedFlag = true;
    }

    private void SpawnRealBossAtPoint()
    {
        if (bossHasSpawned) return;

        Vector3 pos = bossSpawnPoint != null ? bossSpawnPoint.position : transform.position;
        Quaternion rot = bossSpawnPoint != null ? bossSpawnPoint.rotation : Quaternion.identity;

        GameObject bossGO = Instantiate(bossPrefab, pos, rot);
        bossHasSpawned = true;

        // adjust global spawn pressure
        spawnInterval *= Mathf.Max(0.01f, postBossSpawnIntervalMultiplier);
        maxConcurrentEnemies = Mathf.Max(1, postBossMaxEnemies);

        OnBossSpawned?.Invoke(bossGO);

        // Make player vulnerable again.
        if (playerHealth != null)
        {
            playerHealth.invincible = false;
        }
    }

    private IEnumerator ContinuousSpawnLoop()
    {
        while (true)
        {
            // simple throttle while boss cutscene might be playing (optional)
            yield return new WaitForSeconds(spawnInterval);

            if (manualSpawnPoints.Count > 0 && activeGroups.Count < manualSpawnPoints.Count)
            {
                var shuffled = new List<Transform>(manualSpawnPoints);
                for (int i = shuffled.Count - 1; i > 0; i--)
                {
                    int j = UnityEngine.Random.Range(0, i + 1);
                    var tmp = shuffled[i]; shuffled[i] = shuffled[j]; shuffled[j] = tmp;
                }

                foreach (var pt in shuffled)
                {
                    if (pt == null) continue;
                    if (!groupByPoint.ContainsKey(pt) || groupByPoint[pt] == null)
                    {
                        SpawnGroupAtPoint(pt.position, pt.rotation, pt);
                        lastSpawnTimeByPoint[pt] = Time.time;
                        respawnDelayByPoint[pt] = UnityEngine.Random.Range(groupRespawnDelayMin, groupRespawnDelayMax);
                        break;
                    }
                }
            }
        }
    }

    private IEnumerator ManualPointRespawnLoop()
    {
        while (true)
        {
            foreach (var pt in manualSpawnPoints)
            {
                if (pt == null) continue;

                groupByPoint.TryGetValue(pt, out var existingGroup);
                float lastSpawn = lastSpawnTimeByPoint.ContainsKey(pt) ? lastSpawnTimeByPoint[pt] : -9999f;
                float delayForThisPoint = respawnDelayByPoint.ContainsKey(pt) ? respawnDelayByPoint[pt] : groupRespawnDelayMin;

                bool needRespawn = false;
                if (existingGroup == null)
                {
                    if (Time.time - lastSpawn >= delayForThisPoint)
                        needRespawn = true;
                }
                else
                {
                    if (existingGroup.SpawnCompleted && (existingGroup.Members == null || existingGroup.Members.Count == 0))
                    {
                        if (Time.time - lastSpawn >= delayForThisPoint)
                            needRespawn = true;
                    }
                }

                if (needRespawn)
                {
                    if (CanSpawnEnemy())
                    {
                        if (existingGroup != null)
                        {
                            try
                            {
                                existingGroup.ForceDestroyGroup();
                            }
                            catch
                            {
                                try { Destroy(existingGroup.gameObject); } catch { }
                                activeGroups.Remove(existingGroup);
                                groupByPoint[pt] = null;
                            }
                        }

                        SpawnGroupAtPoint(pt.position, pt.rotation, pt);
                        lastSpawnTimeByPoint[pt] = Time.time;
                        respawnDelayByPoint[pt] = UnityEngine.Random.Range(groupRespawnDelayMin, groupRespawnDelayMax);
                    }
                }
            }

            yield return new WaitForSeconds(respawnCheckInterval);
        }
    }

    private void SpawnGroupAtPoint(Vector3 pos, Quaternion rot, Transform manualPoint = null)
    {
        if (groupPrefab == null)
        {
            Debug.LogWarning("EnemySpawner: groupPrefab not assigned");
            return;
        }

        GameObject go = Instantiate(groupPrefab, pos, rot);
        var group = go.GetComponent<EnemyGroup>();
        if (group == null)
        {
            Debug.LogWarning("EnemySpawner: spawned groupPrefab has no EnemyGroup component");
            Destroy(go);
            return;
        }

        group.Initialize(this, 0f);
        activeGroups.Add(group);

        if (manualPoint != null)
        {
            groupByPoint[manualPoint] = group;
            lastSpawnTimeByPoint[manualPoint] = Time.time;
            respawnDelayByPoint[manualPoint] = UnityEngine.Random.Range(groupRespawnDelayMin, groupRespawnDelayMax);
        }
    }

    /// <summary>
    /// Enqueue a member spawn request to be processed by the spawner's global queue.
    /// </summary>
    public void EnqueueSpawn(GameObject prefab, Vector3 pos, Quaternion rot, EnemyGroup group, Action<GameObject> onSpawned)
    {
        if (prefab == null)
        {
            Debug.LogWarning("EnemySpawner.EnqueueSpawn: prefab null");
            onSpawned?.Invoke(null);
            return;
        }

        SpawnRequest req = new SpawnRequest
        {
            prefab = prefab,
            pos = pos,
            rot = rot,
            group = group,
            onSpawned = onSpawned
        };

        spawnQueue.Enqueue(req);

        if (spawnProcessorCoroutine == null)
            spawnProcessorCoroutine = StartCoroutine(ProcessSpawnQueue());
    }

    private IEnumerator ProcessSpawnQueue()
    {
        while (spawnQueue.Count > 0)
        {
            var req = spawnQueue.Dequeue();

            while (!CanSpawnEnemy())
            {
                yield return new WaitForSeconds(0.25f);
            }

            GameObject go = Instantiate(req.prefab, req.pos, req.rot);

            try { req.onSpawned?.Invoke(go); } catch { }

            float delay = UnityEngine.Random.Range(minSpawnDelayPerMember, maxSpawnDelayPerMember);
            yield return new WaitForSeconds(delay);
        }

        spawnProcessorCoroutine = null;
    }

    /// <summary>
    /// Called by groups to check global capacity before enqueuing spawning.
    /// </summary>
    public bool CanSpawnEnemy()
    {
        int current = GetCurrentEnemyCount();
        return current < maxConcurrentEnemies;
    }

    /// <summary>
    /// Helper to count current enemies in scene (uses modern API).
    /// </summary>
    public int GetCurrentEnemyCount()
    {
        var arr = UnityEngine.Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None);
        return arr != null ? arr.Length : 0;
    }

    /// <summary>
    /// Called by a group just before it destroys itself so spawner can clear bookkeeping.
    /// </summary>
    public void NotifyGroupDestroyed(EnemyGroup group)
    {
        if (group == null) return;

        activeGroups.Remove(group);

        foreach (var kv in new List<Transform>(groupByPoint.Keys))
        {
            if (groupByPoint[kv] == group)
            {
                groupByPoint[kv] = null;
                lastSpawnTimeByPoint[kv] = Time.time;
                respawnDelayByPoint[kv] = UnityEngine.Random.Range(groupRespawnDelayMin, groupRespawnDelayMax);
                break;
            }
        }
    }

    public void ReduceSpawnRateForBoss()
    {
        if (bossHasSpawned) return;
        bossHasSpawned = true;
        spawnInterval *= Mathf.Max(0.01f, postBossSpawnIntervalMultiplier);
        maxConcurrentEnemies = Mathf.Max(1, postBossMaxEnemies);
        Debug.Log("[EnemySpawner] ReduceSpawnRateForBoss called.");
    }

    public List<EnemyGroup> GetAllGroups() => new List<EnemyGroup>(activeGroups);
}

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemySpawner : MonoBehaviour
{
    [Header("Spawner Settings")]
    [Tooltip("Manual spawn points. A group will be created for each assigned manual spawn point.")]
    public List<Transform> manualSpawnPoints = new List<Transform>();
    [Tooltip("Prefab that contains EnemyGroup component.")]
    public GameObject groupPrefab;
    [Tooltip("How many groups to spawn initially. Clamped to manualSpawnPoints count.")]
    public int groupsToSpawn = 3;

    [Header("Continuous spawning (optional)")]
    public bool continuousSpawning = false;
    [Tooltip("Seconds between spawn attempts when continuousSpawning is enabled.")]
    public float spawnInterval = 6f;
    [Tooltip("Maximum total concurrent live enemies across all groups.")]
    public int maxConcurrentEnemies = 50;

    [Header("Group respawn (random per point)")]
    [Tooltip("Minimum seconds to wait before respawning a group at the same manual point after its members are gone.")]
    public float groupRespawnDelayMin = 15f;
    [Tooltip("Maximum seconds to wait before respawning a group at the same manual point after its members are gone.")]
    public float groupRespawnDelayMax = 30f;
    [Tooltip("How often (seconds) the spawner checks manual points for respawn opportunities.")]
    public float respawnCheckInterval = 3f;

    [Header("Boss spawn")]
    public GameObject bossPrefab;
    public Transform bossSpawnPoint;
    public float bossSpawnDelay = 30f;
    [Tooltip("After the boss spawns, multiply spawnInterval by this value (e.g. 2.0 -> half the spawn rate).")]
    public float postBossSpawnIntervalMultiplier = 2f;
    [Tooltip("After the boss spawns, set maxConcurrentEnemies to this value.")]
    public int postBossMaxEnemies = 25;

    [Header("Global spawn timing (applies to all groups)")]
    [Tooltip("Minimum delay between global successive member spawns.")]
    public float minSpawnDelayPerMember = 0.05f;
    [Tooltip("Maximum delay between global successive member spawns.")]
    public float maxSpawnDelayPerMember = 0.35f;

    [Header("Obstacle Check (fallback)")]
    public float checkRadius = 1f;
    public LayerMask obstacleMask;

    // runtime
    private readonly List<EnemyGroup> activeGroups = new();
    private readonly Dictionary<Transform, EnemyGroup> groupByPoint = new();
    private readonly Dictionary<Transform, float> lastSpawnTimeByPoint = new();
    private readonly Dictionary<Transform, float> respawnDelayByPoint = new();

    private Coroutine continuousSpawnRoutine;
    private Coroutine respawnLoopRoutine;
    private bool bossHasSpawned = false;

    public event Action<GameObject> OnBossSpawned;

    // --- spawn queue for serialised global spawning ---
    private struct SpawnRequest
    {
        public GameObject prefab;
        public Vector3 pos;
        public Quaternion rot;
        public EnemyGroup group;
        public Action<GameObject> onSpawned;
    }

    private readonly Queue<SpawnRequest> spawnQueue = new();
    private Coroutine spawnProcessorCoroutine;

    private void Start()
    {
        if (manualSpawnPoints == null) manualSpawnPoints = new List<Transform>();

        // initialize bookkeeping maps
        foreach (var pt in manualSpawnPoints)
        {
            if (pt == null) continue;
            groupByPoint[pt] = null;
            lastSpawnTimeByPoint[pt] = -9999f;
            respawnDelayByPoint[pt] = UnityEngine.Random.Range(groupRespawnDelayMin, groupRespawnDelayMax);
        }

        // initial spawn - spawn up to groupsToSpawn groups at manual points (first N points)
        int spawnCount = Mathf.Clamp(groupsToSpawn, 0, manualSpawnPoints.Count);
        for (int i = 0; i < spawnCount; i++)
        {
            var pt = manualSpawnPoints[i];
            if (pt == null) continue;
            SpawnGroupAtPoint(pt.position, pt.rotation, pt);
            lastSpawnTimeByPoint[pt] = Time.time;
            respawnDelayByPoint[pt] = UnityEngine.Random.Range(groupRespawnDelayMin, groupRespawnDelayMax);
        }

        // continuous spawning (optional)
        if (continuousSpawning)
            continuousSpawnRoutine = StartCoroutine(ContinuousSpawnLoop());

        // periodic respawn check for manual points
        respawnLoopRoutine = StartCoroutine(ManualPointRespawnLoop());

        // boss timer
        if (bossPrefab != null)
            StartCoroutine(BossSpawnTimer());
    }

    private IEnumerator BossSpawnTimer()
    {
        yield return new WaitForSeconds(bossSpawnDelay);

        Vector3 pos = bossSpawnPoint != null ? bossSpawnPoint.position : transform.position;
        Quaternion rot = bossSpawnPoint != null ? bossSpawnPoint.rotation : Quaternion.identity;
        var bossGO = Instantiate(bossPrefab, pos, rot);

        bossHasSpawned = true;

        // reduce spawn pressure
        spawnInterval *= Mathf.Max(0.01f, postBossSpawnIntervalMultiplier);
        maxConcurrentEnemies = Mathf.Max(1, postBossMaxEnemies);

        // invoke event with spawned GameObject (listeners can extract BossHealth)
        OnBossSpawned?.Invoke(bossGO);
    }

    private IEnumerator ContinuousSpawnLoop()
    {
        while (true)
        {
            // only spawn if we are below desired active groups count (clamped to manual points)
            if (manualSpawnPoints.Count > 0 && activeGroups.Count < manualSpawnPoints.Count)
            {
                // pick a random manual point to place a new group (that doesn't already have an active group)
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

            yield return new WaitForSeconds(spawnInterval);
        }
    }

    private IEnumerator ManualPointRespawnLoop()
    {
        while (true)
        {
            // iterate manual spawn points and respawn if needed
            foreach (var pt in manualSpawnPoints)
            {
                if (pt == null) continue;

                groupByPoint.TryGetValue(pt, out var existingGroup);
                float lastSpawn = lastSpawnTimeByPoint.ContainsKey(pt) ? lastSpawnTimeByPoint[pt] : -9999f;
                float delayForThisPoint = respawnDelayByPoint.ContainsKey(pt) ? respawnDelayByPoint[pt] : groupRespawnDelayMin;

                bool needRespawn = false;
                if (existingGroup == null)
                {
                    // nothing there -> spawn if respawn delay passed
                    if (Time.time - lastSpawn >= delayForThisPoint)
                        needRespawn = true;
                }
                else
                {
                    // group exists: if it finished spawning and has zero members -> consider respawn
                    if (existingGroup.SpawnCompleted && (existingGroup.Members == null || existingGroup.Members.Count == 0))
                    {
                        if (Time.time - lastSpawn >= delayForThisPoint)
                            needRespawn = true;
                    }
                }

                if (needRespawn)
                {
                    // check overall enemy capacity (avoid bursting when arena full)
                    if (CanSpawnEnemy())
                    {
                        // destroy old group object if it exists (clean up) - call group's ForceDestroyGroup()
                        if (existingGroup != null)
                        {
                            try
                            {
                                // allow the group to perform proper cleanup (detach restricted area etc.)
                                existingGroup.ForceDestroyGroup();
                            }
                            catch
                            {
                                // fallback to best-effort destroy
                                try { Destroy(existingGroup.gameObject); } catch { }
                                // ensure it's removed from our lists if fallback used
                                activeGroups.Remove(existingGroup);
                                groupByPoint[pt] = null;
                            }
                        }

                        // Spawn new group at this point
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
    /// group supplies an onSpawned callback to receive the instantiated GameObject.
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

            // wait until global capacity allows another enemy
            while (!CanSpawnEnemy())
            {
                yield return new WaitForSeconds(0.25f);
            }

            // instantiate
            GameObject go = Instantiate(req.prefab, req.pos, req.rot);

            // call back group for post-setup (adding to group.members, subscribing to OnDie, etc.)
            try { req.onSpawned?.Invoke(go); } catch { }

            // wait randomized global delay before next spawn
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

        // remove from activeGroups list
        activeGroups.Remove(group);

        // clear mapping of any manual spawn point that pointed to this group
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

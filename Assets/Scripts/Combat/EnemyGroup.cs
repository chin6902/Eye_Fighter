using System.Collections.Generic;
using UnityEngine;

public class EnemyGroup : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Default prefab used when not using separate type prefabs.")]
    public GameObject enemyPrefab;

    [Tooltip("Optional: mushroom prefab (use when using separate counts).")]
    public GameObject mushroomPrefab;

    [Tooltip("Optional: cactus prefab (use when using separate counts).")]
    public GameObject cactusPrefab;

    [Tooltip("If true, spawn using per-type min/max counts. If false, spawn using minMembers/maxMembers with `enemyPrefab`.")]
    public bool useSeparateCounts;

    [Tooltip("When not using separate counts: total members to spawn (random between min/max).")]
    public int minMembers;
    public int maxMembers;

    [Header("Per-type counts (used when useSeparateCounts = true)")]
    [Tooltip("How many mushrooms to spawn (random between).")]
    public int mushroomMin;
    public int mushroomMax;

    [Tooltip("How many cactuses to spawn (random between).")]
    public int cactusMin;
    public int cactusMax;

    [Header("Obstacle Check")]
    public float checkRadius;
    public LayerMask obstacleMask;
    public int maxAttempts = 20;

    [Header("Refs")]
    public RestrictedAreaController restrictedArea; // Assign in Inspector

    private EnemySpawner spawner;
    private float spawnRadius;

    private List<EnemyController> members = new();
    private Vector3 moveDirection;

    public float areaRadius => restrictedArea != null ? restrictedArea.areaRadius : 0f;

    /// <summary>
    /// Initialize the group and spawn members according to the configured mode.
    /// spawner and spawnRadius are kept for compatibility with existing code.
    /// </summary>
    public void Initialize(EnemySpawner spawner, float spawnRadius)
    {
        this.spawner = spawner;
        this.spawnRadius = spawnRadius;

        if (restrictedArea == null)
        {
            Debug.LogWarning("EnemyGroup.Initialize: restrictedArea not assigned. Aborting spawn.");
            return;
        }

        members.Clear();

        if (useSeparateCounts)
        {
            SpawnByType();
        }
        else
        {
            SpawnDefault();
        }
    }

    /// <summary>
    /// Legacy/default spawning: uses enemyPrefab and spawns between minMembers..maxMembers.
    /// </summary>
    private void SpawnDefault()
    {
        if (enemyPrefab == null)
        {
            Debug.LogWarning("EnemyGroup.SpawnDefault: enemyPrefab is null. Cannot spawn members.");
            return;
        }

        if (minMembers > maxMembers)
        {
            Debug.LogWarning("EnemyGroup.SpawnDefault: minMembers > maxMembers. Please set valid values.");
            return;
        }

        int numToSpawn = Random.Range(minMembers, maxMembers + 1);

        for (int i = 0; i < numToSpawn; i++)
        {
            Vector3 spawnPos;
            if (FindValidSpawnPosition(out spawnPos))
            {
                GameObject enemyGO = Instantiate(enemyPrefab, spawnPos, Quaternion.identity);
                var enemy = enemyGO.GetComponent<EnemyController>();
                if (enemy != null)
                {
                    enemy.CurrentGroup = this;
                    members.Add(enemy);
                }
                else
                {
                    Debug.LogWarning("EnemyGroup: spawned prefab has no EnemyController component. Destroying instance.");
                    Destroy(enemyGO);
                }
            }
            else
            {
                Debug.LogWarning($"EnemyGroup: Could not find valid spawn position for enemy {i} (default mode)");
            }
        }
    }

    /// <summary>
    /// Spawns mushrooms and cactuses using their respective prefabs and per-type counts.
    /// </summary>
    private void SpawnByType()
    {
        if (mushroomPrefab == null && cactusPrefab == null)
        {
            Debug.LogWarning("EnemyGroup.SpawnByType: both mushroomPrefab and cactusPrefab are null. Nothing to spawn.");
            return;
        }

        // Spawn mushrooms if configured correctly
        if (mushroomPrefab != null)
        {
            if (mushroomMin > mushroomMax)
            {
                Debug.LogWarning("EnemyGroup: mushroomMin > mushroomMax. Skipping mushroom spawn.");
            }
            else
            {
                int numMushrooms = Random.Range(mushroomMin, mushroomMax + 1);
                for (int i = 0; i < numMushrooms; i++)
                {
                    Vector3 spawnPos;
                    if (FindValidSpawnPosition(out spawnPos))
                    {
                        GameObject enemyGO = Instantiate(mushroomPrefab, spawnPos, Quaternion.identity);
                        var enemy = enemyGO.GetComponent<EnemyController>();
                        if (enemy != null)
                        {
                            enemy.Type = EnemyType.Mushroom;
                            enemy.CurrentGroup = this;
                            members.Add(enemy);
                        }
                        else
                        {
                            Debug.LogWarning("EnemyGroup: mushroomPrefab has no EnemyController component. Destroying instance.");
                            Destroy(enemyGO);
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"EnemyGroup: Could not find valid spawn position for mushroom {i}");
                    }
                }
            }
        }

        // Spawn cactuses if configured correctly
        if (cactusPrefab != null)
        {
            if (cactusMin > cactusMax)
            {
                Debug.LogWarning("EnemyGroup: cactusMin > cactusMax. Skipping cactus spawn.");
            }
            else
            {
                int numCactuses = Random.Range(cactusMin, cactusMax + 1);
                for (int i = 0; i < numCactuses; i++)
                {
                    Vector3 spawnPos;
                    if (FindValidSpawnPosition(out spawnPos))
                    {
                        GameObject enemyGO = Instantiate(cactusPrefab, spawnPos, Quaternion.identity);
                        var enemy = enemyGO.GetComponent<EnemyController>();
                        if (enemy != null)
                        {
                            enemy.Type = EnemyType.Cactus;
                            enemy.CurrentGroup = this;
                            members.Add(enemy);
                        }
                        else
                        {
                            Debug.LogWarning("EnemyGroup: cactusPrefab has no EnemyController component. Destroying instance.");
                            Destroy(enemyGO);
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"EnemyGroup: Could not find valid spawn position for cactus {i}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Attempts up to maxAttempts to find a valid spawn position inside the restricted area that isn't obstructed.
    /// Returns true and the position if successful.
    /// </summary>
    private bool FindValidSpawnPosition(out Vector3 result)
    {
        result = Vector3.zero;

        if (restrictedArea == null)
            return false;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            Vector3 offset = Random.insideUnitSphere * (areaRadius * 0.5f);
            offset.y = 0f;

            Vector3 candidatePos = restrictedArea.transform.position + offset;

            bool hasObstacle = Physics.CheckSphere(candidatePos, checkRadius, obstacleMask);

            if (!hasObstacle)
            {
                result = candidatePos;
                return true;
            }
        }

        return false;
    }

    void OnDrawGizmosSelected()
    {
        if (restrictedArea != null)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(restrictedArea.transform.position, restrictedArea.areaRadius);
        }
    }

    // Optional: expose members list for external use (read-only)
    public IReadOnlyList<EnemyController> Members => members.AsReadOnly();
}

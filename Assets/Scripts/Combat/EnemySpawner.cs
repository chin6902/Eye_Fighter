using System.Collections.Generic;
using UnityEngine;

public class EnemySpawner : MonoBehaviour
{
    [Header("Spawner Settings")]
    public float spawnRadius = 30f; // Half side length of square area
    public int groupsToSpawn = 3;
    public List<Transform> manualSpawnPoints;
    public GameObject groupPrefab;

    [Header("Obstacle Check")]
    public float checkRadius = 1f; // How big a sphere to check for overlaps
    public LayerMask obstacleMask; // What layers count as obstacles

    private List<EnemyGroup> activeGroups = new();

    private void Start()
    {
        for (int i = 0; i < groupsToSpawn; i++)
        {
            Vector3 spawnPos = Vector3.zero;
            bool foundValid = false;

            const int maxAttempts = 20; // Prevent infinite loops

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                // Random point inside square area
                float randomX = Random.Range(-spawnRadius, spawnRadius);
                float randomZ = Random.Range(-spawnRadius, spawnRadius);
                Vector3 candidatePos = transform.position + new Vector3(randomX, 0f, randomZ);

                // Check for obstacles
                bool hasObstacle = Physics.CheckSphere(candidatePos, checkRadius, obstacleMask);

                if (!hasObstacle)
                {
                    spawnPos = candidatePos;
                    foundValid = true;
                    break;
                }
            }

            if (foundValid)
            {
                Transform point = manualSpawnPoints[i];

                GameObject groupGO = Instantiate(groupPrefab, point.position, point.rotation);
                EnemyGroup group = groupGO.GetComponent<EnemyGroup>();
                group.Initialize(this, 0f); // spawnRadius unused here
                activeGroups.Add(group);
            }
            else
            {
                Debug.LogWarning($"EnemySpawner: Could not find valid spawn position for group {i}");
            }
        }
    }

    public List<EnemyGroup> GetAllGroups() => activeGroups;

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        float side = spawnRadius * 2f;
        Gizmos.DrawWireCube(transform.position, new Vector3(side, 0.1f, side));
    }
}

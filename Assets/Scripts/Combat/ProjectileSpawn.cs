using System;
using System.Collections;
using Unity.VisualScripting;
using UnityEngine;

public class ProjectileSpawner : MonoBehaviour
{
    [Header("Spawn Points (one per element)")]
    public Transform[] spawnPoints; // Fire=0, Electric=1, Water=2

    [Header("Sword Prefabs (one per element)")]
    public GameObject fireSwordPrefab;
    public GameObject electricSwordPrefab;
    public GameObject waterSwordPrefab;

    [Header("Spawn Effects")]
    public ParticleSystem spawnEffectPrefab;
    public float spawnEffectDuration = 0.5f;

    [Header("Arc Settings")]
    public float curveHeight = 3f;      // How “tall” the arc is
    public float maxDistance = 20f;     // For travelTime calculation
    public float minTravelTime = 0.1f;
    public float maxTravelTime = 1f;

    [Header("Target Settings")]
    public float targetHeightOffset = 0.5f;

    private void Start()
    {
        GameManager.Instance.onAttack += LaunchProjectile;
    }

    private void OnDestroy()
    {
        GameManager.Instance.onAttack -= LaunchProjectile;
    }

    public void LaunchProjectile(float accuracy)
    {
        var element = GameManager.Instance.selectedElement;
        int idx = (int)element - 1; // Assuming None=0, Fire=1 → idx 0, etc.

        if (idx < 0 || idx >= spawnPoints.Length)
        {
            Debug.LogWarning("Invalid element index for spawn point.");
            return;
        }

        Transform spawnPoint = spawnPoints[idx];
        Transform target = GameManager.Instance.CurrentGazeTarget;
        if (target == null)
        {
            Debug.LogWarning("No valid target to attack.");
            return;
        }

        StartCoroutine(SpawnAndShoot(element, spawnPoint, target, accuracy, idx));
    }

    private IEnumerator SpawnAndShoot(
        GameManager.ElementType element,
        Transform spawnPoint,
        Transform target,
        float accuracy,
        int spawnIndex)
    {
        // 1) Spawn VFX
        if (spawnEffectPrefab != null)
        {
            var effect = Instantiate(spawnEffectPrefab, spawnPoint.position, Quaternion.identity);
            effect.Play();
            yield return new WaitForSeconds(spawnEffectDuration);
            Destroy(effect.gameObject);
        }

        // 2) Pick prefab
        GameObject prefab = element switch
        {
            GameManager.ElementType.Fire => fireSwordPrefab,
            GameManager.ElementType.Electric => electricSwordPrefab,
            GameManager.ElementType.Water => waterSwordPrefab,
            _ => null
        };
        if (prefab == null)
        {
            Debug.LogError("No prefab found for selected element.");
            yield break;
        }

        // 3) Compute travel time
        Vector3 aimPoint = target.position + Vector3.up * targetHeightOffset;
        float distance = Vector3.Distance(spawnPoint.position, aimPoint);
        float tDist = Mathf.Clamp01(distance / maxDistance);
        float travelTime = Mathf.Lerp(minTravelTime, maxTravelTime, tDist);

        // 4) Arc direction
        float lateralAmount = spawnIndex switch
        {
            0 => -1f,
            1 => 0f,
            2 => 1f,
            _ => 0f
        };

        Vector3 lateral = spawnPoint.right * lateralAmount * curveHeight;

        Vector3 start = spawnPoint.position;
        Vector3 end = aimPoint;
        Vector3 mid = (start + end) * 0.5f;
        Vector3 control = mid + lateral + Vector3.up * (curveHeight + Mathf.Abs(lateralAmount) * curveHeight);


        // 5) Spawn and init sword
        var projGO = Instantiate(prefab, spawnPoint.position, spawnPoint.rotation);
        var proj = projGO.AddComponent<SwordProjectile>();

        proj.Initialize(
            target,
            element,
            accuracy,
            spawnPoint.position,
            lateral,
            curveHeight,
            travelTime,
            () => { }
        );
    }
}

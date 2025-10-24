using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// EnemyGroup that requests the global spawner to enqueue actual instantiation requests.
/// It keeps using RestrictedAreaController for valid spawn positions and is defensive
/// against the restricted area being destroyed at runtime.
/// 
/// NOTE: This class does NOT perform any local Instantiate fallback. The spawner MUST
/// implement EnqueueSpawn(...) and call the provided callback when the GameObject is created.
/// </summary>
public class EnemyGroup : MonoBehaviour
{
    [Header("Settings")]
    public GameObject enemyPrefab;
    public GameObject mushroomPrefab;
    public GameObject cactusPrefab;
    public bool useSeparateCounts = false;

    [Header("When not using separate counts")]
    public int minMembers = 3;
    public int maxMembers = 6;

    [Header("Per-type counts (used when useSeparateCounts = true)")]
    public int mushroomMin = 0;
    public int mushroomMax = 0;
    public int cactusMin = 0;
    public int cactusMax = 0;

    [Header("Obstacle check")]
    public float checkRadius = 0.5f;
    public LayerMask obstacleMask;
    public int maxAttempts = 20;

    [Header("Refs")]
    public RestrictedAreaController restrictedArea;

    // runtime
    private EnemySpawner spawner;
    private float spawnRadius;
    private Coroutine spawnRoutine;

    // members list (kept accurate)
    private readonly List<EnemyController> members = new List<EnemyController>();

    // exposes members and spawn completion for spawner logic
    public IReadOnlyList<EnemyController> Members => members.AsReadOnly();
    public bool SpawnCompleted { get; private set; } = false;

    public float areaRadius => (restrictedArea != null) ? restrictedArea.areaRadius : 0f;

    /// <summary>
    /// Initialize the group. If restrictedArea is missing, group will not spawn members.
    /// </summary>
    public void Initialize(EnemySpawner spawner, float spawnRadius)
    {
        this.spawner = spawner;
        this.spawnRadius = spawnRadius;

        if (restrictedArea == null)
        {
            Debug.LogWarning("EnemyGroup.Initialize: restrictedArea not assigned. Group will not spawn members.");
            // mark as completed so spawner can cleanup if needed
            SpawnCompleted = true;
            return;
        }

        // start spawn routine
        if (spawnRoutine != null) StopCoroutine(spawnRoutine);
        SpawnCompleted = false;
        spawnRoutine = StartCoroutine(SpawnMembersCoroutine());
    }

    private IEnumerator SpawnMembersCoroutine()
    {
        members.Clear();

        if (useSeparateCounts)
        {
            if (mushroomPrefab != null && mushroomMax >= mushroomMin)
            {
                int count = UnityEngine.Random.Range(mushroomMin, mushroomMax + 1);
                for (int i = 0; i < count; i++) yield return EnqueueOne(mushroomPrefab);
            }

            if (cactusPrefab != null && cactusMax >= cactusMin)
            {
                int count = UnityEngine.Random.Range(cactusMin, cactusMax + 1);
                for (int i = 0; i < count; i++) yield return EnqueueOne(cactusPrefab);
            }
        }
        else
        {
            if (enemyPrefab == null)
            {
                Debug.LogWarning("EnemyGroup.SpawnMembers: enemyPrefab is null.");
                SpawnCompleted = true;
                yield break;
            }

            int num = Mathf.Clamp(UnityEngine.Random.Range(minMembers, maxMembers + 1), 0, 9999);
            for (int i = 0; i < num; i++) yield return EnqueueOne(enemyPrefab);
        }

        // mark spawn finished; group will remain and members will be removed as they die.
        SpawnCompleted = true;
        spawnRoutine = null;

        // If there are no spawned members at all, destroy the group after notifying spawner
        if (members.Count == 0)
        {
            NotifyAndDestroy();
        }
    }

    /// <summary>
    /// Enqueue a spawn request to the spawner. The spawner is expected to instantiate the prefab
    /// and invoke the onSpawned callback with the created GameObject.
    /// If restrictedArea becomes unavailable, this method will abort and the member will not be spawned.
    /// </summary>
    private IEnumerator EnqueueOne(GameObject prefab)
    {
        // If spawner is missing, we cannot proceed with enqueuing.
        if (spawner == null)
        {
            Debug.LogWarning("EnemyGroup.EnqueueOne: spawner is null — cannot enqueue spawn. Aborting remaining spawns for this group.");
            yield break;
        }

        // Check restrictedArea — if it disappears, abort spawning further members
        if (restrictedArea == null)
        {
            Debug.LogWarning("EnemyGroup.EnqueueOne: restrictedArea is null or destroyed; aborting spawn for this group.");
            yield break;
        }

        // Choose a valid spawn position inside restricted area (defensive: re-check restrictedArea during the loop)
        Vector3 chosen = transform.position;
        bool found = false;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (restrictedArea == null) break; // became destroyed during attempts
            Vector3 offset = UnityEngine.Random.insideUnitSphere * (areaRadius * 0.5f);
            offset.y = 0f;
            Vector3 candidate = restrictedArea.transform.position + offset;
            if (!Physics.CheckSphere(candidate, checkRadius, obstacleMask))
            {
                chosen = candidate;
                found = true;
                break;
            }
            yield return null; // yield a frame between attempts to avoid hitches
        }

        if (!found)
        {
            Debug.LogWarning("EnemyGroup.EnqueueOne: could not find valid spawn position inside restricted area; aborting this member.");
            yield break;
        }

        Action<GameObject> onSpawned = (go) =>
        {
            if (go == null) return;

            var ctrl = go.GetComponent<EnemyController>();
            if (ctrl != null)
            {
                ctrl.CurrentGroup = this;
                members.Add(ctrl);

                var h = go.GetComponent<Health>();
                if (h != null)
                {
                    // local function so we can safely check `this` when the event fires
                    void OnMemberDie()
                    {
                        // UnityEngine.Object overloaded operator: this == null is true if the group has been destroyed
                        if (this == null) return;

                        // double-guard: if gameObject is missing, don't call StartCoroutine
                        if (gameObject == null) return;

                        // defensive try/catch: if somehow destroyed between the checks, ignore exception
                        try
                        {
                            StartCoroutine(RemoveMemberDelayed(ctrl));
                        }
                        catch (MissingReferenceException)
                        {
                            // group destroyed; nothing to do
                        }
                    }

                    h.OnDie += OnMemberDie;
                }
            }
            else
            {
                Debug.LogWarning("EnemyGroup: spawned prefab has no EnemyController component; destroying.");
                try { Destroy(go); } catch { }
            }
        };


        // Attempt to call the spawner's EnqueueSpawn method.
        try
        {
            spawner.EnqueueSpawn(prefab, chosen, Quaternion.identity, this, onSpawned);
        }
        catch (MissingMethodException)
        {
            Debug.LogError("EnemyGroup: spawner does not implement EnqueueSpawn(...) required by EnemyGroup. Aborting spawn.");
            yield break;
        }
        catch (Exception ex)
        {
            Debug.LogError($"EnemyGroup: exception while calling EnqueueSpawn: {ex.Message}. Aborting spawn.");
            yield break;
        }

        // yield a frame so multiple groups don't enqueue many requests in the exact same frame
        yield return null;
    }

    private IEnumerator RemoveMemberDelayed(EnemyController ctrl)
    {
        // wait a frame then remove (ensures destroy completed)
        yield return null;
        members.RemoveAll(m => m == null || m == ctrl);

        // if spawning finished and list now empty -> auto destroy
        if (SpawnCompleted && (members == null || members.Count == 0))
        {
            NotifyAndDestroy();
        }
    }

    /// <summary>
    /// Notify spawner and destroy this group.
    /// Before destroying, detach restrictedArea if it is childed to this group so enemies that reference
    /// it won't lose the reference.
    /// </summary>
    private void NotifyAndDestroy()
    {
        // detach restrictedArea if it is parented under this group so it won't be destroyed with the group
        if (restrictedArea != null)
        {
            try
            {
                if (restrictedArea.transform.IsChildOf(transform))
                {
                    restrictedArea.transform.SetParent(null, true);
                }
            }
            catch { /* defensive: ignore if object already destroyed */ }
        }

        if (spawner != null)
        {
            spawner.NotifyGroupDestroyed(this);
        }

        // destroy after a frame so any pending callbacks finish
        Destroy(gameObject);
    }

    // public cleanup that spawner can call to get the proper detach + notify behavior
    public void ForceDestroyGroup()
    {
        // destroy remaining members and notify spawner via NotifyAndDestroy
        foreach (var m in members)
        {
            if (m != null) Destroy(m.gameObject);
        }
        members.Clear();

        NotifyAndDestroy();
    }

    // optional public helper in case external code wants to force group cleanup immediately
    public void ForceDestroyGroupImmediate()
    {
        // detach restricted area then immediate destroy without waiting a frame
        if (restrictedArea != null)
        {
            try
            {
                if (restrictedArea.transform.IsChildOf(transform))
                    restrictedArea.transform.SetParent(null, true);
            }
            catch { }
        }

        if (spawner != null) spawner.NotifyGroupDestroyed(this);
        Destroy(gameObject);
    }

    void OnDrawGizmosSelected()
    {
        if (restrictedArea != null)
        {
            // guard against missing/destroyed restricted area
            try
            {
                Gizmos.color = Color.white;
                Gizmos.DrawWireSphere(restrictedArea.transform.position, areaRadius);
            }
            catch { /* ignore if restrictedArea was destroyed in editor/playmode */ }
        }
    }
}

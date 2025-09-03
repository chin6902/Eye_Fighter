using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class AttackSlotConfig
{
    public EnemyType type;
    [Min(0)] public int maxAttackers = 1;
    [Min(0f)] public float slotCooldown = 4f;
}

public class EnemyManager : MonoBehaviour
{
    public static EnemyManager Instance { get; private set; }

    [Header("Default fallback (used when a type isn't configured)")]
    [Tooltip("Default slot cooldown (seconds) used for types not configured in 'configs'")]
    public float DefaultSlotCooldown = 4f;

    [Tooltip("Default max attackers per-type used when not configured")]
    public int DefaultMaxAttackers = 1;

    [Header("Per-type attack slot configuration")]
    [Tooltip("Configure per-EnemyType maxAttackers and slotCooldown here")]
    public List<AttackSlotConfig> configs = new List<AttackSlotConfig>()
    {
        new AttackSlotConfig() { type = EnemyType.Mushroom, maxAttackers = 1, slotCooldown = 4f }
    };

    // Internal lookups created from configs for fast access
    private readonly Dictionary<EnemyType, int> _maxAttackersByType = new();
    private readonly Dictionary<EnemyType, float> _slotCooldownByType = new();

    // Active attackers per type
    private readonly Dictionary<EnemyType, List<EnemyController>> _activeAttackersByType = new();

    // Waiting queues per type
    private readonly Dictionary<EnemyType, Queue<EnemyController>> _waitingQueuesByType = new();

    // Last grant time per type
    private readonly Dictionary<EnemyType, float> _lastGrantTimeByType = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;

        // initialize dictionaries
        _maxAttackersByType.Clear();
        _slotCooldownByType.Clear();
        _activeAttackersByType.Clear();
        _waitingQueuesByType.Clear();
        _lastGrantTimeByType.Clear();

        // Populate from inspector configs
        foreach (var c in configs)
        {
            if (!_maxAttackersByType.ContainsKey(c.type))
            {
                _maxAttackersByType[c.type] = Math.Max(0, c.maxAttackers);
                _slotCooldownByType[c.type] = Mathf.Max(0f, c.slotCooldown);
                _activeAttackersByType[c.type] = new List<EnemyController>();
                _waitingQueuesByType[c.type] = new Queue<EnemyController>();
                _lastGrantTimeByType[c.type] = -Mathf.Infinity;
            }
        }
    }

    private void Update()
    {
        // For every known type (either configured or discovered), try to grant slots
        // We iterate over keys present in either _maxAttackersByType or any active/waiting collections.
        var typesToProcess = new HashSet<EnemyType>();
        foreach (var k in _maxAttackersByType.Keys) typesToProcess.Add(k);
        foreach (var kv in _activeAttackersByType) typesToProcess.Add(kv.Key);
        foreach (var kv in _waitingQueuesByType) typesToProcess.Add(kv.Key);

        foreach (var type in typesToProcess)
        {
            // Ensure structures exist for discovered types
            EnsureTypeExists(type);

            // Clean active attackers list (remove nulls / dead)
            var activeList = _activeAttackersByType[type];
            for (int i = activeList.Count - 1; i >= 0; --i)
            {
                var e = activeList[i];
                if (e == null || e.IsDead)
                {
                    activeList.RemoveAt(i);
                }
            }

            // Clean waiting queue by re-queuing only valid entries
            var q = _waitingQueuesByType[type];
            if (q.Count > 0)
            {
                var tmp = new Queue<EnemyController>();
                while (q.Count > 0)
                {
                    var e = q.Dequeue();
                    if (e == null || e.IsDead) continue;
                    // If out of chase range, skip re-adding (mimics your old filter)
                    float dist = Vector3.Distance(e.transform.position, e.Player.position);
                    if (dist <= e.ChaseRange)
                        tmp.Enqueue(e);
                }
                _waitingQueuesByType[type] = tmp;
                q = _waitingQueuesByType[type];
            }

            // Try to grant slots while there are waiting enemies and slots available and cooldown ready
            int maxForType = _maxAttackersByType[type];
            float cooldownForType = _slotCooldownByType[type];
            float lastGrant = _lastGrantTimeByType[type];
            var queueForType = _waitingQueuesByType[type];

            while (queueForType.Count > 0 && _activeAttackersByType[type].Count < maxForType)
            {
                // respect cooldown between grants for this type
                if (Time.time - lastGrant < cooldownForType)
                    break;

                var next = queueForType.Dequeue();
                if (next == null || next.IsDead) continue;

                _activeAttackersByType[type].Add(next);
                _lastGrantTimeByType[type] = Time.time;
                lastGrant = Time.time;

                next.OnAttackGranted();
            }
        }
    }

    /// <summary>
    /// Request permission to attack. Returns true if the attacker was granted a slot immediately.
    /// Otherwise the enemy will be enqueued and must wait.
    /// </summary>
    public bool TryRequestAttack(EnemyController e)
    {
        if (e == null || e.IsDead) return false;

        EnemyType t = e.Type;
        EnsureTypeExists(t);

        var activeList = _activeAttackersByType[t];

        // Already active
        if (activeList.Contains(e))
            return true;

        int maxForType = _maxAttackersByType[t];
        float cooldownForType = _slotCooldownByType[t];
        float lastGrant = _lastGrantTimeByType[t];
        var queueForType = _waitingQueuesByType[t];

        bool slotAvailable = activeList.Count < maxForType;
        bool cooldownReady = (Time.time - lastGrant) >= cooldownForType;

        // If there's an available slot and cooldown is ready and no one is waiting BEFORE this requester, grant immediately
        if (slotAvailable && cooldownReady && queueForType.Count == 0)
        {
            activeList.Add(e);
            _lastGrantTimeByType[t] = Time.time;
            e.OnAttackGranted();
            return true;
        }

        // Otherwise enqueue if not already in queue
        if (!QueueContains(queueForType, e))
            queueForType.Enqueue(e);

        return false;
    }

    /// <summary>
    /// Must be called when an enemy finishes or is interrupted — frees its active slot.
    /// </summary>
    public void OnAttackEnd(EnemyController e)
    {
        if (e == null) return;
        EnemyType t = e.Type;
        EnsureTypeExists(t);

        var activeList = _activeAttackersByType[t];
        if (activeList.Contains(e))
            activeList.Remove(e);
    }

    // ---------- Utility / runtime API ----------

    private void EnsureTypeExists(EnemyType t)
    {
        if (!_maxAttackersByType.ContainsKey(t))
        {
            _maxAttackersByType[t] = DefaultMaxAttackers;
            _slotCooldownByType[t] = DefaultSlotCooldown;
        }

        if (!_activeAttackersByType.ContainsKey(t))
            _activeAttackersByType[t] = new List<EnemyController>();

        if (!_waitingQueuesByType.ContainsKey(t))
            _waitingQueuesByType[t] = new Queue<EnemyController>();

        if (!_lastGrantTimeByType.ContainsKey(t))
            _lastGrantTimeByType[t] = -Mathf.Infinity;
    }

    private static bool QueueContains(Queue<EnemyController> q, EnemyController e)
    {
        if (q == null || e == null) return false;
        foreach (var item in q)
            if (item == e) return true;
        return false;
    }

    /// <summary>
    /// Runtime setter to change max attackers for a type.
    /// </summary>
    public void SetMaxAttackersForType(EnemyType type, int max)
    {
        if (max < 0) max = 0;
        _maxAttackersByType[type] = max;
    }

    /// <summary>
    /// Runtime setter to change slot cooldown for a type.
    /// </summary>
    public void SetSlotCooldownForType(EnemyType type, float cooldown)
    {
        if (cooldown < 0f) cooldown = 0f;
        _slotCooldownByType[type] = cooldown;
    }
}

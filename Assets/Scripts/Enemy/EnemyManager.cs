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

    [Header("Slot hold timeout")]
    [Tooltip("How long (seconds) an enemy may hold a granted attack slot before manager forcibly frees it.")]
    public float MaxAttackSlotHoldDuration = 15f;

    // Internal lookups created from configs for fast access
    private readonly Dictionary<EnemyType, int> _maxAttackersByType = new();
    private readonly Dictionary<EnemyType, float> _slotCooldownByType = new();

    // Active attackers per type
    private readonly Dictionary<EnemyType, List<EnemyController>> _activeAttackersByType = new();

    // Waiting queues per type
    private readonly Dictionary<EnemyType, Queue<EnemyController>> _waitingQueuesByType = new();

    // Last grant time per type
    private readonly Dictionary<EnemyType, float> _lastGrantTimeByType = new();

    // Grant time per enemy (when manager granted the slot)
    private readonly Dictionary<EnemyController, float> _grantTimeByEnemy = new();

    // All known types (for Update loop, avoids per-frame allocations)
    private readonly List<EnemyType> _allTypes = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;

        _maxAttackersByType.Clear();
        _slotCooldownByType.Clear();
        _activeAttackersByType.Clear();
        _waitingQueuesByType.Clear();
        _lastGrantTimeByType.Clear();
        _grantTimeByEnemy.Clear();
        _allTypes.Clear();

        // Populate from inspector configs
        foreach (AttackSlotConfig c in configs)
        {
            EnsureTypeExists(c.type);

            _maxAttackersByType[c.type] = Math.Max(0, c.maxAttackers);
            _slotCooldownByType[c.type] = Mathf.Max(0f, c.slotCooldown);

            if (!_allTypes.Contains(c.type))
            {
                _allTypes.Add(c.type);
            }
        }
    }

    private void Update()
    {
        for (int i = 0; i < _allTypes.Count; i++)
        {
            EnemyType type = _allTypes[i];
            ProcessType(type);
        }
    }

    private void ProcessType(EnemyType type)
    {
        EnsureTypeExists(type);

        List<EnemyController> activeList = _activeAttackersByType[type];

        // Clean active attackers list (remove nulls / dead) and grant-time entries
        for (int i = activeList.Count - 1; i >= 0; --i)
        {
            EnemyController e = activeList[i];
            if (e == null || e.IsDead)
            {
                activeList.RemoveAt(i);
                if (e != null)
                {
                    _grantTimeByEnemy.Remove(e);
                }
            }
        }

        // Enforce max-hold duration: forcibly free any active that exceeded MaxAttackSlotHoldDuration
        if (MaxAttackSlotHoldDuration > 0f && activeList.Count > 0)
        {
            var copy = new List<EnemyController>(activeList);
            foreach (EnemyController e in copy)
            {
                if (e == null) continue;
                if (!_grantTimeByEnemy.TryGetValue(e, out float grantTime)) continue;

                if (Time.time - grantTime >= MaxAttackSlotHoldDuration)
                {
                    activeList.Remove(e);
                    _grantTimeByEnemy.Remove(e);

                    _lastGrantTimeByType[type] = Time.time;

                    TryNotifyAttackSlotRevoked(e);
                }
            }
        }

        // Clean waiting queue by removing null or dead entries but keep others in order
        Queue<EnemyController> q = _waitingQueuesByType[type];
        if (q.Count > 0)
        {
            Queue<EnemyController> tmp = new Queue<EnemyController>();
            while (q.Count > 0)
            {
                EnemyController e = q.Dequeue();
                if (e == null || e.IsDead) continue;
                tmp.Enqueue(e);
            }
            _waitingQueuesByType[type] = tmp;
            q = _waitingQueuesByType[type];
        }

        // Try to grant slots while there are waiting enemies and slots available and cooldown ready
        int maxForType = _maxAttackersByType[type];
        float cooldownForType = _slotCooldownByType[type];
        float lastGrant = _lastGrantTimeByType[type];
        Queue<EnemyController> queueForType = _waitingQueuesByType[type];

        while (queueForType.Count > 0 && _activeAttackersByType[type].Count < maxForType)
        {
            if (Time.time - lastGrant < cooldownForType)
            {
                break;
            }

            EnemyController next = queueForType.Peek();
            if (next == null || next.IsDead)
            {
                queueForType.Dequeue();
                continue;
            }

            float dist = Vector3.Distance(next.transform.position, next.Player.position);
            if (dist > next.ChaseRange)
            {
                queueForType.Dequeue();
                queueForType.Enqueue(next);
                break;
            }

            queueForType.Dequeue();
            _activeAttackersByType[type].Add(next);
            _lastGrantTimeByType[type] = Time.time;
            _grantTimeByEnemy[next] = Time.time;
            lastGrant = Time.time;

            next.OnAttackGranted();
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

        List<EnemyController> activeList = _activeAttackersByType[t];

        // Already active
        if (activeList.Contains(e))
        {
            return true;
        }

        int maxForType = _maxAttackersByType[t];
        float cooldownForType = _slotCooldownByType[t];
        float lastGrant = _lastGrantTimeByType[t];
        Queue<EnemyController> queueForType = _waitingQueuesByType[t];

        bool slotAvailable = activeList.Count < maxForType;
        bool cooldownReady = (Time.time - lastGrant) >= cooldownForType;

        if (slotAvailable && cooldownReady && queueForType.Count == 0)
        {
            activeList.Add(e);
            _lastGrantTimeByType[t] = Time.time;
            _grantTimeByEnemy[e] = Time.time;
            e.OnAttackGranted();
            return true;
        }

        if (!QueueContains(queueForType, e))
        {
            queueForType.Enqueue(e);
        }

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

        List<EnemyController> activeList = _activeAttackersByType[t];
        if (activeList.Contains(e))
        {
            activeList.Remove(e);
        }

        if (_grantTimeByEnemy.ContainsKey(e))
        {
            _grantTimeByEnemy.Remove(e);
        }
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
        {
            _activeAttackersByType[t] = new List<EnemyController>();
        }

        if (!_waitingQueuesByType.ContainsKey(t))
        {
            _waitingQueuesByType[t] = new Queue<EnemyController>();
        }

        if (!_lastGrantTimeByType.ContainsKey(t))
        {
            _lastGrantTimeByType[t] = -Mathf.Infinity;
        }

        if (!_allTypes.Contains(t))
        {
            _allTypes.Add(t);
        }
    }

    private static bool QueueContains(Queue<EnemyController> q, EnemyController e)
    {
        if (q == null || e == null) return false;
        foreach (EnemyController item in q)
        {
            if (item == e) return true;
        }
        return false;
    }

    public void SetMaxAttackersForType(EnemyType type, int max)
    {
        if (max < 0) max = 0;
        _maxAttackersByType[type] = max;
    }

    public void SetSlotCooldownForType(EnemyType type, float cooldown)
    {
        if (cooldown < 0f) cooldown = 0f;
        _slotCooldownByType[type] = cooldown;
    }

    private void TryNotifyAttackSlotRevoked(EnemyController e)
    {
        if (e == null) return;

        try
        {
            e.OnAttackSlotRevoked();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[EnemyManager] Failed to call OnAttackSlotRevoked on enemy: " + ex);
        }
    }
}

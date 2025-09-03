// ProjectileChargeManager.cs
using System;
using UnityEngine;

/// <summary>
/// Simple singleton that tracks a single pooled charge count (used for barrier-breaking or other mechanics).
/// - Max stored charges configurable (default 3)
/// - Lightweight events for UI/feedback
/// </summary>
public class ProjectileChargeManager : MonoBehaviour
{
    public static ProjectileChargeManager Instance { get; private set; }

    [Tooltip("Maximum stored charges the player can hold.")]
    [SerializeField] private int maxCharges = 3;

    [Tooltip("Starting number of charges at scene start (useful for testing).")]
    [SerializeField] private int startCharges = 0;

    // runtime count
    private int _count = 0;

    // events
    public event Action<int> OnChargesChanged;          // passes current count
    public event Action OnChargeAdded;                 // fired when a charge is added
    public event Action OnChargeUsed;                  // fired when a charge is consumed
    public event Action OnChargeMaxReached;            // fired when add would reach max

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // sanitize config
        maxCharges = Mathf.Max(1, maxCharges);
        startCharges = Mathf.Clamp(startCharges, 0, maxCharges);

        _count = startCharges;
    }

    /// <summary>Get current stored charge count.</summary>
    public int GetCount() => _count;

    /// <summary>Get configured max charges.</summary>
    public int GetMax() => maxCharges;

    /// <summary>True if we can consume a charge.</summary>
    public bool CanUse() => _count > 0;

    /// <summary>Attempt to add a charge. Returns true if added (was below max).</summary>
    public bool AddCharge()
    {
        if (_count >= maxCharges)
        {
            OnChargeMaxReached?.Invoke();
            return false;
        }

        _count++;
        OnChargeAdded?.Invoke();
        OnChargesChanged?.Invoke(_count);

        if (_count >= maxCharges) OnChargeMaxReached?.Invoke();
        return true;
    }

    /// <summary>Consume one charge. Returns true if consumed.</summary>
    public bool UseCharge()
    {
        if (_count <= 0) return false;
        _count--;
        OnChargeUsed?.Invoke();
        OnChargesChanged?.Invoke(_count);
        return true;
    }

    /// <summary>Clear all stored charges (e.g. round start).</summary>
    public void ClearAll()
    {
        _count = 0;
        OnChargesChanged?.Invoke(_count);
    }

    /// <summary>Force-set count (editor/test helper).</summary>
    public void SetCount(int newCount)
    {
        _count = Mathf.Clamp(newCount, 0, maxCharges);
        OnChargesChanged?.Invoke(_count);
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Barrier controller v2
/// - explicit ActivateBarrier() / DeactivateBarrier()
/// - auto-assigns magic circle instances to spots (child lookup)
/// - raises events so other systems (e.g. BossController) can react
/// - recovery is explicit: call RecoverBarrierImmediate() or RecoverBarrierDelayed(delay)
/// </summary>
public class BarrierController : MonoBehaviour
{
    [Header("Boss / Barrier")]
    public GameObject Boss;
    public GameObject BarrierVisual;

    [Header("Spots (assign manually or auto-fill)")]
    public List<BarrierSpot> Spots = new List<BarrierSpot>();

    [Header("Tag")]
    [Tooltip("Tag boss will have while protected. Must exist in Project Tags.")]
    public string ProtectedTag = "EnemyProtected";

    [Header("Start")]
    public bool StartActive = true;

    // runtime
    private string _originalBossTag;
    private int _remainingSpots => CountActiveSpots();
    private Coroutine _recoverCoroutine;

    // events
    public event Action OnBarrierActivated;
    public event Action OnBarrierDeactivated;
    public event Action OnBarrierFullyBroken;

    private void Reset()
    {
        // auto-fill Spots if children have BarrierSpot
        if (Spots.Count == 0)
        {
            var found = GetComponentsInChildren<BarrierSpot>(true);
            foreach (var f in found) Spots.Add(f);
        }
    }

    private void Start()
    {
        // store original tag
        if (Boss != null) _originalBossTag = Boss.tag;

        // wire spots -> this controller
        for (int i = 0; i < Spots.Count; i++)
        {
            if (Spots[i] != null) Spots[i].SetController(this);
        }

        // auto-assign magic circle instances by child lookup (no inspector list required)
        AutoAssignMagicCircles();

        if (StartActive) ActivateBarrier();
        else DeactivateBarrierImmediate();
    }

    /// <summary>
    /// Auto assign magic circle instances -> spots using simple child-lookup:
    /// 1) find a ParticleSystem in the spot's children
    /// 2) or find a child named "MagicCircle"
    /// This keeps the inspector simpler (no MagicCircleInstances list).
    /// </summary>
    [ContextMenu("Auto Assign Magic Circles")]
    public void AutoAssignMagicCircles()
    {
        for (int i = 0; i < Spots.Count; i++)
        {
            var spot = Spots[i];
            if (spot == null) continue;

            // don't override if already assigned
            if (spot.MagicCircleInstance != null) continue;

            // 1) search for ParticleSystem in children
            var ps = spot.GetComponentInChildren<ParticleSystem>(true);
            if (ps != null)
            {
                spot.MagicCircleInstance = ps.gameObject;
                continue;
            }

            // 2) fallback: look for a child named "MagicCircle"
            var child = spot.transform.Find("MagicCircle");
            if (child != null)
            {
                spot.MagicCircleInstance = child.gameObject;
                continue;
            }

            // If neither found, leave MagicCircleInstance null — that's valid.
        }
    }

    /// <summary>
    /// Activates barrier: protects boss, shows visuals, resets spots.
    /// Use this to make barrier active whenever (spawn, after recover, etc).
    /// </summary>
    public void ActivateBarrier()
    {
        // cancel pending recovery if any
        if (_recoverCoroutine != null)
        {
            StopCoroutine(_recoverCoroutine);
            _recoverCoroutine = null;
        }

        if (Boss != null)
            Boss.tag = string.IsNullOrEmpty(ProtectedTag) ? _originalBossTag : ProtectedTag;

        if (BarrierVisual != null) BarrierVisual.SetActive(true);

        foreach (var s in Spots)
            s?.ResetSpot();

        // Force-show magic for all spots (so magic circles follow barrier visual)
        foreach (var s in Spots)
        {
            if (s == null) continue;
            s.ShowMagicImmediate(true);
        }

        // notify
        OnBarrierActivated?.Invoke();
    }

    public void DeactivateBarrier()
    {
        // restore boss tag
        if (Boss != null)
            Boss.tag = _originalBossTag;

        if (BarrierVisual != null) BarrierVisual.SetActive(false);

        // Force-hide magic for all spots before deactivating them
        foreach (var s in Spots)
        {
            if (s == null) continue;
            s.ShowMagicImmediate(false);
        }

        // hide spots while barrier is down
        foreach (var s in Spots)
            if (s != null) s.gameObject.SetActive(false);

        // refresh GetEnemy so boss becomes targetable now
        var finders = UnityEngine.Object.FindObjectsByType<GetEnemy>(FindObjectsSortMode.None);
        for (int i = 0; i < finders.Length; i++)
            finders[i]?.RefreshEnemies();

        OnBarrierDeactivated?.Invoke();
    }

    private void DeactivateBarrierImmediate()
    {
        if (Boss != null) Boss.tag = _originalBossTag;
        if (BarrierVisual != null) BarrierVisual.SetActive(false);

        // ensure magic hidden
        foreach (var s in Spots) if (s != null) s.ShowMagicImmediate(false);

        foreach (var s in Spots) if (s != null) s.gameObject.SetActive(false);
    }

    /// <summary>
    /// Called by BarrierSpot when a spot is broken.
    /// Automatic recovery has been removed — call RecoverBarrierImmediate or RecoverBarrierDelayed manually.
    /// </summary>
    internal void NotifySpotBroken(BarrierSpot spot)
    {
        if (_remainingSpots <= 0)
        {
            // barrier fully broken
            OnBarrierFullyBroken?.Invoke();

            // Deactivate barrier right away (keep same behaviour)
            DeactivateBarrier();

            // NOTE: automatic recovery removed. Call RecoverBarrierImmediate() or RecoverBarrierDelayed(...) from your BossController when you want a recovery.
        }
    }

    /// <summary>
    /// Public API: recover the barrier immediately (activate now).
    /// Call this from your BossController when you want the barrier back.
    /// </summary>
    public void RecoverBarrierImmediate()
    {
        ActivateBarrier();
    }

    /// <summary>
    /// Public API: recover the barrier after a delay.
    /// This starts a coroutine on this controller. Call this only when this GameObject/Behaviour
    /// is active (or use a global runner). Returns the started coroutine.
    /// </summary>
    public Coroutine RecoverBarrierDelayed(float delay)
    {
        if (_recoverCoroutine != null)
        {
            StopCoroutine(_recoverCoroutine);
            _recoverCoroutine = null;
        }

        _recoverCoroutine = StartCoroutine(RecoverAfterDelayInternal(delay));
        return _recoverCoroutine;
    }

    private IEnumerator RecoverAfterDelayInternal(float delay)
    {
        float timer = 0f;
        while (timer < delay)
        {
            timer += Time.deltaTime;
            yield return null;
        }

        ActivateBarrier();
        _recoverCoroutine = null;
    }

    private int CountActiveSpots()
    {
        int n = 0;
        foreach (var s in Spots) if (s != null && s.gameObject.activeInHierarchy) n++;
        return n;
    }

    // convenience for other systems
    public bool IsBarrierActive() => Boss != null && Boss.tag == ProtectedTag;
}

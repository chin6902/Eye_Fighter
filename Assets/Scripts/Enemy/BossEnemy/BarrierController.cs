using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Barrier controller v2
/// - explicit ActivateBarrier() / DeactivateBarrier()
/// - optional auto-recover (count or infinite)
/// - auto-assigns magic circle instances to spots (index or child lookup)
/// - raises events so other systems (e.g. BossController) can react
/// </summary>
public class BarrierController : MonoBehaviour
{
    [Header("Boss / Barrier")]
    public GameObject Boss;
    public GameObject BarrierVisual;

    [Header("Spots (assign manually or auto-fill)")]
    public List<BarrierSpot> Spots = new List<BarrierSpot>();

    [Header("Magic circle instances (scene objects)")]
    public List<GameObject> MagicCircleInstances = new List<GameObject>();

    [Header("Tag")]
    [Tooltip("Tag boss will have while protected. Must exist in Project Tags.")]
    public string ProtectedTag = "EnemyProtected";

    [Header("Recovery (optional)")]
    [Tooltip("If true, barrier will attempt to recover automatically after being destroyed")]
    public bool AutoRecover = false;
    [Tooltip("How many times barrier will recover. Set to <=0 for infinite recoveries when AutoRecover is true.")]
    public int RecoverCount = 0;
    [Tooltip("Delay (seconds) before barrier recovers after full break.")]
    public float RecoverDelay = 10f;

    [Header("Start")]
    public bool StartActive = true;

    // runtime
    private string _originalBossTag;
    private int _remainingSpots => CountActiveSpots();
    private int _remainingRecoveries; // internal counter for finite recover
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
            var found = GetComponentsInChildren<BarrierSpot>();
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

        AutoAssignMagicCircles();

        _remainingRecoveries = RecoverCount;

        if (StartActive) ActivateBarrier();
        else DeactivateBarrierImmediate();
    }

    /// <summary>
    /// Auto assign magic circle instances -> spots using same strategy as before.
    /// </summary>
    [ContextMenu("Auto Assign Magic Circles")]
    public void AutoAssignMagicCircles()
    {
        for (int i = 0; i < Spots.Count; i++)
        {
            var spot = Spots[i];
            if (spot == null) continue;

            if (spot.MagicCircleInstance != null) continue;

            if (i < MagicCircleInstances.Count && MagicCircleInstances[i] != null)
            {
                var candidate = MagicCircleInstances[i];
                if (candidate.scene.IsValid())
                {
                    spot.MagicCircleInstance = candidate;
                    continue;
                }
                else
                {
                    Debug.LogWarning($"BarrierController_v2: MagicCircleInstances[{i}] looks like a prefab asset. Use a scene instance.", this);
                }
            }

            var ps = spot.GetComponentInChildren<ParticleSystem>(true);
            if (ps != null)
            {
                spot.MagicCircleInstance = ps.gameObject;
                continue;
            }

            var child = spot.transform.Find("MagicCircle");
            if (child != null) spot.MagicCircleInstance = child.gameObject;
        }
    }

    /// <summary>
    /// Activates barrier: protects boss, shows visuals, resets spots.
    /// Use this to make barrier active whenever (spawn, after recover, etc).
    /// </summary>
    public void ActivateBarrier()
    {
        // cancel pending recovery
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
    /// </summary>
    internal void NotifySpotBroken(BarrierSpot spot)
    {
        if (_remainingSpots <= 0)
        {
            // barrier fully broken
            OnBarrierFullyBroken?.Invoke();
            DeactivateBarrier();

            // start recovery if enabled
            if (AutoRecover)
            {
                if (RecoverCount <= 0)
                {
                    // infinite
                    _recoverCoroutine = StartCoroutine(RecoverAfterDelay());
                }
                else if (_remainingRecoveries > 0)
                {
                    _remainingRecoveries--;
                    _recoverCoroutine = StartCoroutine(RecoverAfterDelay());
                }
            }
        }
    }

    private IEnumerator RecoverAfterDelay()
    {
        float timer = 0f;
        while (timer < RecoverDelay)
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

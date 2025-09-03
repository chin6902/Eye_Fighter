using System;
using UnityEngine;

[DisallowMultipleComponent]
public class BarrierSpot : MonoBehaviour
{
    [Tooltip("Which element this spot is associated with.")]
    public GameManager.ElementType SpotElement = GameManager.ElementType.None;

    [Tooltip("Pattern used when drawing slash/cross for this spot (designer selectable).")]
    public CurvedPathGenerator.Pattern SpotPattern = CurvedPathGenerator.Pattern.TopLeft_To_BottomRight;

    [Tooltip("Current hit points for this spot. When <= 0 the spot is considered broken.")]
    public int HP = 1;

    [Tooltip("If you want the spot to recover, set this to the starting HP. If left 0 it will be initialized from HP on Awake.")]
    public int MaxHP = 0;

    [Tooltip("Assign a pre-placed GameObject (scene instance) that contains the magic-circle particle(s).")]
    public GameObject MagicCircleInstance;

    [Tooltip("If true, the magic circle will be shown automatically whenever the barrier/spot is active (even when not targeted).")]
    public bool ShowMagicWhenBarrierActive = false;

    [Tooltip("Optional VFX prefab to spawn on break.")]
    public GameObject BreakVFX;

    private BarrierController _controller;
    private ParticleSystem _magicPS;

    public bool IsBroken => HP <= 0;
    public event Action<BarrierSpot> OnBroken;

    private void Awake()
    {
        if (MaxHP <= 0) MaxHP = Mathf.Max(1, HP);
        HP = MaxHP;
    }

    private void OnEnable()
    {
        CacheMagicParticle();
        // default hidden until highlighted or asked to show
        StopMagic();
    }

    private void OnDisable()
    {
        StopMagic();
    }

    private void OnValidate()
    {
        MaxHP = Mathf.Max(1, MaxHP);
        HP = Mathf.Clamp(HP, 0, Mathf.Max(1, MaxHP));
    }

    /// <summary>
    /// Called by BarrierController at setup so the spot can notify the controller when broken.
    /// </summary>
    public void SetController(BarrierController controller)
    {
        _controller = controller;
    }

    /// <summary>
    /// Apply damage to this spot (call from your attack system).
    /// </summary>
    public void TakeDamage(int amount = 1)
    {
        if (IsBroken) return;

        HP -= Mathf.Max(1, amount);

        if (HP <= 0) Break();
    }

    private void Break()
    {
        StopMagic();

        if (BreakVFX != null)
            Instantiate(BreakVFX, transform.position, Quaternion.identity);

        gameObject.SetActive(false);

        OnBroken?.Invoke(this);
        _controller?.NotifySpotBroken(this);
    }

    /// <summary>
    /// Called by GetEnemy when the player starts/stops targeting this spot.
    /// This toggles highlight visuals and can be used for other UX responses.
    /// </summary>
    /// <param name="aimed">true when targeted / false when un-targeted</param>
    public void NotifyAimed(bool aimed)
    {
        // We only toggle visual highlight — HP or activation state is handled elsewhere.
        SetHighlight(aimed);
    }

    #region Magic-circle visual control (no allocation)

    /// <summary>
    /// Turn visual highlight on/off. Respects ShowMagicWhenBarrierActive when turning off.
    /// </summary>
    internal void SetHighlight(bool on)
    {
        if (!gameObject.activeInHierarchy)
        {
            StopMagic();
            return;
        }

        if (MagicCircleInstance == null) return;

        if (on) StartMagic();
        else if (!ShowMagicWhenBarrierActive)
            StopMagic();
    }

    private void CacheMagicParticle()
    {
        if (MagicCircleInstance == null)
        {
            _magicPS = null;
            return;
        }

        if (!MagicCircleInstance.scene.IsValid())
        {
            Debug.LogWarning($"BarrierSpot ({name}): MagicCircleInstance appears to be a prefab asset, not a scene instance. Assign a scene object.", this);
            _magicPS = null;
            return;
        }

        _magicPS = MagicCircleInstance.GetComponentInChildren<ParticleSystem>(true);
    }

    private void StartMagic()
    {
        if (MagicCircleInstance == null) return;

        MagicCircleInstance.SetActive(true);
        if (_magicPS != null) _magicPS.Play();
    }

    private void StopMagic()
    {
        if (MagicCircleInstance == null) return;

        if (_magicPS != null)
            _magicPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        MagicCircleInstance.SetActive(false);
    }

    /// <summary>
    /// Force-show or force-hide the magic circle immediately, regardless of ShowMagicWhenBarrierActive.
    /// Safe to call from other systems (e.g. BarrierController).
    /// </summary>
    public void ShowMagicImmediate(bool on)
    {
        CacheMagicParticle();

        if (MagicCircleInstance == null) return;

        if (!gameObject.activeInHierarchy && on)
        {
            Debug.LogWarning($"BarrierSpot ({name}): showing magic while spot GameObject is inactive. Ensure spot/parents are active.", this);
        }

        if (on) StartMagic();
        else StopMagic();
    }

    #endregion

    /// <summary>
    /// Restore this spot to its initial state and hide/show visuals depending on ShowMagicWhenBarrierActive.
    /// </summary>
    public void ResetSpot()
    {
        MaxHP = Mathf.Max(1, MaxHP);
        HP = MaxHP;
        gameObject.SetActive(true);

        CacheMagicParticle();

        if (ShowMagicWhenBarrierActive && MagicCircleInstance != null)
            StartMagic();
        else
            StopMagic();
    }
}

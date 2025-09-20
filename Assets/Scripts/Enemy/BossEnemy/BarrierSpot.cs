using System;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class BarrierSpot : MonoBehaviour
{
    [Tooltip("Which element this spot is associated with.")]
    public GameManager.ElementType SpotElement = GameManager.ElementType.None;

    [Tooltip("Pattern used when drawing slash/cross for this spot (designer selectable).")]
    public CurvedPathGenerator.Pattern SpotPattern = CurvedPathGenerator.Pattern.TopLeft_To_BottomRight;

    [Tooltip("Current hit points for this spot. When <= 0 the spot is considered broken.")]
    public int HP = 100;                      // default to 100 so effective hits can break if accuracy ~= 1

    [Tooltip("If you want the spot to recover, set this to the starting HP. If left 0 it will be initialized from HP on Awake.")]
    public int MaxHP = 0;

    [Tooltip("Assign a pre-placed GameObject (scene instance) that contains the magic-circle particle(s).")]
    public GameObject MagicCircleInstance;

    [Tooltip("If true, the magic circle will be shown automatically whenever the barrier/spot is active (even when not targeted).")]
    public bool ShowMagicWhenBarrierActive = false;

    [Tooltip("Optional VFX prefab to spawn on break.")]
    public GameObject BreakVFX;

    [Header("Optional UI / Popups (assign if present)")]
    [Tooltip("Optional Image to show this spot's element icon")]
    public Image elementUI;

    // optional DamagePopUpGenerator (scene child)
    private DamagePopUpGenerator localPopUp;

    private BarrierController _controller;
    private ParticleSystem _magicPS;

    // same element pool used by enemies (to randomize)
    private static readonly GameManager.ElementType[] enemyElements = new[] {
        GameManager.ElementType.Fire,
        GameManager.ElementType.Water,
        GameManager.ElementType.Electric
    };

    public bool IsBroken => HP <= 0;
    public event Action<BarrierSpot> OnBroken;

    private void Awake()
    {
        if (MaxHP <= 0) MaxHP = Mathf.Max(1, HP);
        HP = MaxHP;

        // Randomise element each spawn (you asked for this)
        SpotElement = enemyElements[UnityEngine.Random.Range(0, enemyElements.Length)];

        // find popup generator if present in children
        localPopUp = GetComponentInChildren<DamagePopUpGenerator>(true);
    }

    private void OnEnable()
    {
        CacheMagicParticle();
        StopMagic();

        // apply element icon using the same ElementDatabase lookup as in Health
        ApplyElementIcon();
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

    public void SetController(BarrierController controller)
    {
        _controller = controller;
    }

    /// <summary>
    /// Straight damage application (keeps existing API).
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
    /// Receive elemental damage using the same rules as Health.ReceiveElementalDamage.
    /// - effective -> baseDamage=100 (scaled by accuracy)
    /// - same element -> baseDamage=50 (scaled by accuracy)
    /// - otherwise -> damage = 1
    /// Shows popup if available, spawns hit/break VFX, and calls TakeDamage/Break.
    /// </summary>
    public void ReceiveElementalDamage(GameManager.ElementType attackerElement, float accuracy)
    {
        if (IsBroken) return;

        Color popupColor = GetPopupColor(attackerElement);

        bool effective = IsEffective(attackerElement, SpotElement);
        int damage;
        int baseDamage;

        if (effective)
        {
            baseDamage = 100;
            damage = Mathf.RoundToInt(baseDamage * Mathf.Clamp01(accuracy));
        }
        else if (attackerElement == SpotElement)
        {
            damage = 1;
        }
        else
        {
            damage = 1;
        }

        string text = damage.ToString() + (effective ? "!" : string.Empty);

        if (localPopUp != null)
        {
            localPopUp.CreatePopUp(transform.position + Vector3.up * 1f, text, popupColor);
        }

        // apply damage
        TakeDamage(damage);

        if (HP > 0)
        {
            GenerateHitEffect(attackerElement);
            var data = ElementDatabase.Instance.Get(attackerElement);
        }
        else
        {
            // broken: spawn break effects and notify controller
            GenerateBreakEffect(attackerElement);
            var data = ElementDatabase.Instance.Get(attackerElement);
            /* Edit later for barrier spot SFX
            if (data != null && data.HitSFX != null)
                SoundManager.PlaySFX(data.HitSFX, 0.5f);
            */
        }
    }

    public void NotifyAimed(bool aimed)
    {
        SetHighlight(aimed);
    }

    #region Magic-circle visual control
    private void CacheMagicParticle()
    {
        if (MagicCircleInstance == null) { _magicPS = null; return; }
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

    internal void SetHighlight(bool on)
    {
        if (!gameObject.activeInHierarchy) { StopMagic(); return; }
        if (MagicCircleInstance == null) return;
        if (on) StartMagic();
        else if (!ShowMagicWhenBarrierActive) StopMagic();
    }
    #endregion

    #region Element visuals & helpers
    private Color GetPopupColor(GameManager.ElementType attackerElement)
    {
        var data = ElementDatabase.Instance.Get(attackerElement);
        return (data != null) ? data.popupColor : Color.white;
    }

    private void GenerateHitEffect(GameManager.ElementType attackerElement)
    {
        var attackerData = ElementDatabase.Instance.Get(attackerElement);
        if (attackerData != null && attackerData.hitEffect != null)
            Instantiate(attackerData.hitEffect, transform.position, Quaternion.identity);
    }

    private void GenerateBreakEffect(GameManager.ElementType attackerElement)
    {
        var attackerData = ElementDatabase.Instance.Get(attackerElement);
        if (attackerData != null && attackerData.barrierBreakEffect != null)
            Instantiate(attackerData.barrierBreakEffect, transform.position, Quaternion.identity);
    }

    private void ApplyElementIcon()
    {
        // Uses the exact same pattern as Health.ApplyElementIcon
        if (elementUI == null) return;
        var data = ElementDatabase.Instance.Get(SpotElement);
        if (data != null) elementUI.sprite = data.icon;
    }

    private bool IsEffective(GameManager.ElementType attacker, GameManager.ElementType defender)
    {
        return (attacker == GameManager.ElementType.Fire && defender == GameManager.ElementType.Electric)
            || (attacker == GameManager.ElementType.Electric && defender == GameManager.ElementType.Water)
            || (attacker == GameManager.ElementType.Water && defender == GameManager.ElementType.Fire);
    }
    #endregion

    public void ShowMagicImmediate(bool on)
    {
        CacheMagicParticle();
        if (MagicCircleInstance == null) return;
        if (!gameObject.activeInHierarchy && on)
            Debug.LogWarning($"BarrierSpot ({name}): showing magic while spot GameObject is inactive. Ensure spot/parents are active.", this);
        if (on) StartMagic(); else StopMagic();
    }

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

        // randomize element on reset/spawn and apply icon
        SpotElement = enemyElements[UnityEngine.Random.Range(0, enemyElements.Length)];
        ApplyElementIcon();
    }
}

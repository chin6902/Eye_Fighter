using System;
using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class BossHealth : MonoBehaviour
{
    [Header("HP")]
    [Tooltip("Boss max HP")]
    public int maxHP = 500;
    public int CurrentHP { get; private set; }

    [Header("Optional visual / popups")]
    [Tooltip("Optional DamagePopUpGenerator (child)")]
    public DamagePopUpGenerator localPopUp;

    [Header("Fire prime")]
    [Tooltip("If true, the next damage instance will be doubled (then reset).")]
    [SerializeField] private bool pendingDoubleDamage = false;

    [Tooltip("Duration (seconds) that a Fire charged-hit primes the boss (next hit doubled).")]
    public float FirePrimeDuration = 5f;

    // Events
    public event Action OnTakeDamage;
    public event Action<int> OnTakeDamagePopUp;
    public event Action OnDie;

    // Special effect events (BossController should subscribe to these)
    // multiplier: e.g. 0.5f for 50% speed, duration seconds
    public event Action<float, float> OnSlowed;                         // (multiplier, duration)
    public event Action<float> OnStunned;                               // (duration)
    public event Action OnPrimedFire;                                   // fired when fire-charge primes boss (next hit doubled)
    public event Action<GameManager.ElementType, float> OnChargedHit;   // (element, duration) - immediate visual hook

    private Coroutine _primeClearCoroutine;

    private void Awake()
    {
        CurrentHP = Mathf.Max(1, maxHP);

        if (localPopUp == null)
            localPopUp = GetComponentInChildren<DamagePopUpGenerator>(true);
    }

    public void DealDamage(int damage)
    {
        if (CurrentHP <= 0) return;

        int newHp = Mathf.Max(CurrentHP - Mathf.Max(0, damage), 0);

        if (newHp < CurrentHP)
        {
            CurrentHP = newHp;
            OnTakeDamage?.Invoke();
        }

        if (CurrentHP <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        OnDie?.Invoke();
    }

    /// <summary>
    /// Receive elemental damage. Boss ignores element damage multipliers for raw damage,
    /// but charged hits apply special effects.
    /// </summary>
    public void ReceiveElementalDamage(GameManager.ElementType attackerElement, float accuracy)
    {
        if (CurrentHP <= 0) return;

        bool charged = false;
        if (ProjectileChargeManager.Instance != null && ProjectileChargeManager.Instance.CanUse())
        {
            charged = ProjectileChargeManager.Instance.UseCharge();
        }

        int baseDamage = 100;
        int damage = Mathf.RoundToInt(baseDamage * Mathf.Clamp01(accuracy));

        if (pendingDoubleDamage)
        {
            damage *= 2;
            pendingDoubleDamage = false;
            // optionally notify that prime was consumed (no separate event provided here)
        }

        // charged effect durations (centralized here)
        float chargedDuration = 0f;
        if (charged)
        {
            switch (attackerElement)
            {
                case GameManager.ElementType.Water:
                    chargedDuration = 10f; // slow duration
                    OnSlowed?.Invoke(0.5f, chargedDuration); // example values
                    break;
                case GameManager.ElementType.Electric:
                    chargedDuration = 3f; // stun duration
                    OnStunned?.Invoke(chargedDuration);
                    break;
                case GameManager.ElementType.Fire:
                    chargedDuration = FirePrimeDuration;
                    pendingDoubleDamage = true;
                    OnPrimedFire?.Invoke();
                    // ensure prime clears after duration
                    if (_primeClearCoroutine != null) StopCoroutine(_primeClearCoroutine);
                    _primeClearCoroutine = StartCoroutine(PrimeClearRoutine(FirePrimeDuration));
                    break;
                default:
                    break;
            }

            // notify immediate charged-hit visual hook with element and duration
            OnChargedHit?.Invoke(attackerElement, chargedDuration);
        }

        // Show popup
        Color popupColor = Color.white;
        var data = ElementDatabase.Instance.Get(attackerElement);
        if (data != null) popupColor = data.popupColor;

        if (localPopUp != null)
        {
            string text = damage.ToString() + (charged ? "!" : string.Empty);
            localPopUp.CreatePopUp(transform.position + Vector3.up * 1f, text, popupColor);
            OnTakeDamagePopUp?.Invoke(damage);
        }

        DealDamage(damage);

        if (data != null && data.HitSFX != null)
            SoundManager.PlaySFX(data.HitSFX, 0.5f);
        if (data != null && data.hitEffect != null)
            Instantiate(data.hitEffect, transform.position, Quaternion.identity);
    }

    private IEnumerator PrimeClearRoutine(float duration)
    {
        yield return new WaitForSeconds(duration);
        pendingDoubleDamage = false;
        _primeClearCoroutine = null;
    }

    public void ClearFirePrime()
    {
        pendingDoubleDamage = false;
        if (_primeClearCoroutine != null) { StopCoroutine(_primeClearCoroutine); _primeClearCoroutine = null; }
    }

    public void SetHP(int hp)
    {
        CurrentHP = Mathf.Clamp(hp, 0, int.MaxValue);
        if (CurrentHP == 0) Die();
    }
}

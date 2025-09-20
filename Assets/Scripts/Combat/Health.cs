using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Health : MonoBehaviour
{
    [Header("Player only: pop‑up settings")]
    [Tooltip("Child DamagePopUpGenerator for player")]
    [SerializeField] private DamagePopUpGenerator playerPopUp;

    [Header("Element Settings (enemies only)")]
    [Tooltip("Which element this enemy has (ignored on player)")]
    [SerializeField] private GameManager.ElementType elementType = GameManager.ElementType.None;

    //for player invincibility frames
    private Coroutine _invincibilityRoutine;

    private static readonly GameManager.ElementType[] enemyElements = new[] {
        GameManager.ElementType.Fire,
        GameManager.ElementType.Water,
        GameManager.ElementType.Electric
    };

    [Tooltip("UI Image to show this enemy’s element icon")]
    [SerializeField] private Image elementUI;
    private DamagePopUpGenerator localPopUp;

    [Tooltip("List of visuals to disable on death")]
    [SerializeField] private List<GameObject> visualsToDisable = new List<GameObject>();

    public event Action OnTakeDamage;
    public event Action<int> OnTakeDamagePopUp;
    public event Action OnDie;

    public int CurrentHealth { get; private set; }
    public bool invincible;
    public bool isPlayer;
    public int maxHealth = 100;

    private void Awake()
    {
        CurrentHealth = maxHealth;

        //Player-specific setup
        if (isPlayer)
        {
            OnTakeDamagePopUp += SpawnPlayerPopUp;
        }

        //Enemy-specific setup
        localPopUp = GetComponentInChildren<DamagePopUpGenerator>();

        if (!isPlayer && elementUI != null)
        {
            elementType = enemyElements[UnityEngine.Random.Range(0, enemyElements.Length)];
        }
    }

    private void SpawnPlayerPopUp(int damage)
    {
        if (playerPopUp != null)
        {
            playerPopUp.CreatePopUp(
                transform.position + Vector3.up * 2f,
                PlayerDamageTakenText(damage),
                Color.white
            );
        }
    }

    private string PlayerDamageTakenText(int damage)
    {
        return "-" + damage;
    }

    private void OnEnable()
    {
        ApplyElementIcon();
    }

    private void ApplyElementIcon()
    {
        var data = ElementDatabase.Instance.Get(elementType);

        if (data != null)
        {
            elementUI.sprite = data.icon;
        }
    }

    private Color GetPopupColor(GameManager.ElementType attackerElement)
    {
        var data = ElementDatabase.Instance.Get(attackerElement);

        if (data != null)
        {
            return data.popupColor;
        }
        else
        {
            return Color.white;
        }
    }

    private void GenerateHitEffect(GameManager.ElementType attackerElement)
    {
        var attackerData = ElementDatabase.Instance.Get(attackerElement);

        if (attackerData != null && attackerData.hitEffect != null)
        {
            Instantiate(
                attackerData.hitEffect,
                transform.position,
                Quaternion.identity
            );
        }
    }

    private void GenerateDeadEffect(GameManager.ElementType attackerElement)
    {
        var attackerData = ElementDatabase.Instance.Get(attackerElement);

        if (attackerData != null && attackerData.deadEffect != null)
        {
            GameObject deadEffect = Instantiate(
                attackerData.deadEffect,
                transform 
            );
        }
    }

    public void DealDamage(int damage)
    {
        if (CurrentHealth == 0 || invincible)
        {
            return; 
        }

        int newHealth = Mathf.Max(CurrentHealth - damage, 0);

        if (newHealth < CurrentHealth)
        {
            CurrentHealth = newHealth;

            float shakeIntensity;
            if (damage > 50)
            {
                shakeIntensity = 1.2f;
            }
            else
            {
                shakeIntensity = 0.8f;
            }

            CameraShake.Instance?.ShakeCamera(shakeIntensity, 0.2f);
            
            OnTakeDamage?.Invoke();
            
            if (isPlayer)
            {
                OnTakeDamagePopUp?.Invoke(damage);
                SoundManager.PlaySound(SoundType.PlayerHit, 0.5f);
            }
        }

        if (CurrentHealth == 0)
        {
            OnDie?.Invoke();
        }
    }

    public void ReceiveElementalDamage(GameManager.ElementType attackerElement, float accuracy)
    {
        if (isPlayer || CurrentHealth == 0 || invincible)
        {
            return;
        }

        Color popupColor = GetPopupColor(attackerElement);

        bool effective = IsEffective(attackerElement, elementType);
        int damage;
        int baseDamage;

        if (effective)
        {
            baseDamage = 100;
            damage = Mathf.RoundToInt(baseDamage * Mathf.Clamp01(accuracy));
        }
        else if (attackerElement == elementType)
        {
            baseDamage = 50;
            damage = Mathf.RoundToInt(baseDamage * Mathf.Clamp01(accuracy));
        }
        else
        {
            damage = 1;
        }

        string text = damage.ToString() + (effective ? "!" : string.Empty);

        localPopUp.CreatePopUp(
            transform.position + Vector3.up * 1f,
            text,
            popupColor
        );

        DealDamage(damage);

        if (CurrentHealth > 0)
        {
            GenerateHitEffect(attackerElement);
        }
        else
        {
            foreach (var visual in visualsToDisable)
            {
                if (visual != null)
                {
                    visual.SetActive(false);
                }
            }

            GenerateDeadEffect(attackerElement);

            if (GameManager.Instance?.enemyFinder != null)
            {
                gameObject.tag = "Untagged";
                GameManager.Instance.enemyFinder.UnregisterEnemy(this.transform);
            }

            Destroy(gameObject, 1.5f);
        }
    }

    public void SetInvincibleFor(float seconds)
    {
        if (_invincibilityRoutine != null)
        {
            StopCoroutine(_invincibilityRoutine);
            _invincibilityRoutine = null;
        }

        _invincibilityRoutine = StartCoroutine(InvincibilityCoroutine(seconds));
    }

    private IEnumerator InvincibilityCoroutine(float seconds)
    {
        invincible = true;
        // OPTIONAL: visual/audio feedback here (e.g. start flash, play sound)
        yield return new WaitForSeconds(Mathf.Max(0f, seconds));
        invincible = false;
        // OPTIONAL: stop flash
        _invincibilityRoutine = null;
    }

    private bool IsEffective(GameManager.ElementType attacker, GameManager.ElementType defender)
    {
        return (attacker == GameManager.ElementType.Fire && defender == GameManager.ElementType.Electric)
            || (attacker == GameManager.ElementType.Electric && defender == GameManager.ElementType.Water)
            || (attacker == GameManager.ElementType.Water && defender == GameManager.ElementType.Fire);
    }
}

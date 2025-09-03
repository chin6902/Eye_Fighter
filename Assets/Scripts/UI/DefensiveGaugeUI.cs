using UnityEngine;
using UnityEngine.UI;

public class DefensiveGaugeUI : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("Image used for the gauge fill. Set Image.Type = Filled in the Inspector.")]
    [SerializeField] private Image fillImage = null;

    [Header("Animation")]
    [Tooltip("Speed at which the visual fill interpolates to the target (units/sec, unscaled time).")]
    [SerializeField] private float lerpSpeed = 8f;

    [Header("Flash (depleting only)")]
    [Tooltip("Color to flash toward (red).")]
    [SerializeField] private Color flashColor = Color.red;

    [Tooltip("Normal (non-flashing) fill color (green).")]
    [SerializeField] private Color normalColor = Color.green;

    [Tooltip("Flash frequency in Hz.")]
    [SerializeField] private float flashFrequency = 1.5f; // lower = gentler

    [Tooltip("How strongly to blend toward flashColor (0..1). Lower = less intense).")]
    [Range(0f, 1f)]
    [SerializeField] private float flashStrength = 0.45f;

    [Tooltip("Enable / disable flashing behavior.")]
    [SerializeField] private bool enableFlashing = true;

    private float _targetNormalized = 1f;    // 0..1 target value
    private float _currentNormalized = 1f;   // 0..1 visual
    private bool _subscribed = false;

    // track last raw gauge to detect depletion vs recovery
    private float _lastRawValue = -1f;
    private bool _isDepleting = false;

    private void OnEnable()
    {
        TrySubscribe();
    }

    private void Start()
    {
        if (fillImage != null)
        {
            fillImage.type = Image.Type.Filled;
        }

        TrySubscribe();
    }

    private void Update()
    {
        if (!_subscribed)
            TrySubscribe();

        // Smoothly move current toward target using unscaled time so it matches gauge changes during slow-motion
        if (!Mathf.Approximately(_currentNormalized, _targetNormalized))
        {
            float delta = Time.unscaledDeltaTime * lerpSpeed;
            _currentNormalized = Mathf.MoveTowards(_currentNormalized, _targetNormalized, delta);
            RefreshVisuals();
        }
        else
        {
            // still refresh flash color so it updates while depleting
            RefreshFlashColor();
        }
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void TrySubscribe()
    {
        if (_subscribed) return;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.onDefensiveGaugeChanged += OnGaugeChanged;
            _subscribed = true;

            // initialize visuals with current gauge normalized value
            _targetNormalized = Mathf.Clamp01(GameManager.Instance.DefensiveGaugeNormalized);
            _currentNormalized = _targetNormalized;
            RefreshVisuals();

            // seed lastRawValue
            _lastRawValue = GameManager.Instance.DefensiveGaugeMax * _targetNormalized;
        }
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;

        if (GameManager.Instance != null)
            GameManager.Instance.onDefensiveGaugeChanged -= OnGaugeChanged;

        _subscribed = false;
    }

    private void OnGaugeChanged(float rawValue)
    {
        if (GameManager.Instance == null)
        {
            _targetNormalized = 0f;
            return;
        }

        float max = Mathf.Max(0.0001f, GameManager.Instance.DefensiveGaugeMax);
        float normalized = Mathf.Clamp01(rawValue / max);

        // detect depleting vs recovering: depleting if rawValue < lastRawValue
        if (_lastRawValue >= 0f)
        {
            _isDepleting = rawValue < _lastRawValue;
        }
        _lastRawValue = rawValue;

        _targetNormalized = normalized;

        // if flashing is disabled or not depleting, ensure color returns to normal quickly
        if (!enableFlashing || !_isDepleting)
        {
            // stop flashing immediately (visuals will set normalColor in RefreshFlashColor)
            RefreshFlashColor();
        }
    }

    private void RefreshVisuals()
    {
        if (fillImage != null)
        {
            fillImage.fillAmount = _currentNormalized;
            RefreshFlashColor();
        }
    }

    private void RefreshFlashColor()
    {
        if (fillImage == null) return;

        // if flashing disabled or not currently depleting, use normal color
        if (!enableFlashing || !_isDepleting)
        {
            fillImage.color = normalColor;
            return;
        }

        // Simple gentle pingpong between normalColor and flashColor with reduced strength
        float t = Mathf.PingPong(Time.unscaledTime * flashFrequency, 1f); // 0..1 oscillation
        float blended = Mathf.Lerp(0f, flashStrength, t); // scale by strength so it's less intense
        Color c = Color.Lerp(normalColor, flashColor, blended);
        fillImage.color = c;
    }

    // Public helper to force-set displayed value immediately (no smoothing)
    public void ForceSetNormalized(float normalized)
    {
        _targetNormalized = Mathf.Clamp01(normalized);
        _currentNormalized = _targetNormalized;
        RefreshVisuals();
    }

    public float GetNormalized() => _currentNormalized;
}

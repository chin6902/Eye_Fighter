using UnityEngine;
using UnityEngine.UI;

public class ParryCooldownUI : MonoBehaviour
{
    [SerializeField] private Image fillImage;

    void Start()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.onParryCooldownChanged += OnParryCooldownChanged;
    }

    void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.onParryCooldownChanged -= OnParryCooldownChanged;
    }

    private void OnParryCooldownChanged(float timer, float maxCooldown)
    {
        // fill goes from 1Å®0 over the cooldown
        float t = Mathf.Clamp(timer, 0f, maxCooldown);
        fillImage.fillAmount = 1f - (t / maxCooldown);
    }
}

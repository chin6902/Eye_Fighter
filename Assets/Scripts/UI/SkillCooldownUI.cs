using UnityEngine;
using UnityEngine.UI;

public class SkillCooldownUI : MonoBehaviour
{
    [SerializeField] private Image skillImage;      // the radial fill
    [SerializeField] private Image clock;           // static clock icon
    [SerializeField] private Image unlimitedImage;  // static ÅgunlimitedÅh icon

    void Start()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.onSkillCooldownChanged += OnCooldownChanged;
    }

    void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.onSkillCooldownChanged -= OnCooldownChanged;
    }

    private void OnCooldownChanged(float timer, float maxCooldown)
    {
        var gm = GameManager.Instance;

        // Always update the fill ring
        if (gm.UnlimitedRemaining > 0f)
        {
            // Unlimited window active:
            //  - Show unlimited icon, hide clock
            clock.gameObject.SetActive(false);
            unlimitedImage.gameObject.SetActive(true);

            // Fill the ring based on unlimited remaining
            skillImage.fillAmount = gm.UnlimitedRemaining / gm.UnlimitedDuration;
        }
        else
        {
            // Normal cooldown:
            //  - Show clock icon, hide unlimited
            clock.gameObject.SetActive(true);
            unlimitedImage.gameObject.SetActive(false);

            // Fill ring from 1Å®0 over maxCooldown
            float t = Mathf.Clamp(timer, 0f, maxCooldown);
            skillImage.fillAmount = 1f - (t / maxCooldown);
        }
    }
}

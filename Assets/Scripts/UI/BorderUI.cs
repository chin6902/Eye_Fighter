using UnityEngine;
using UnityEngine.UI;

public class PhaseTimerUI : MonoBehaviour
{
    [SerializeField] private Image fillImage;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private GameManager.MiniGamePhase phase;

    private void Start()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.onPhaseTimerChanged += OnPhaseTimerChanged;
        }
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.onPhaseTimerChanged -= OnPhaseTimerChanged;
        }
    }

    private void OnPhaseTimerChanged(float timeRemaining, float maxTime, GameManager.MiniGamePhase currentPhase)
    {
        if (currentPhase == phase)
        {
            fillImage.gameObject.SetActive(true);
            fillImage.fillAmount = timeRemaining / maxTime;

            switch (GameManager.Instance.selectedElement)
            {
                case GameManager.ElementType.Fire:
                    backgroundImage.color = Color.red;
                    break;
                case GameManager.ElementType.Electric:
                    backgroundImage.color = Color.yellow;
                    break;
                case GameManager.ElementType.Water:
                    backgroundImage.color = Color.blue;
                    break;
                default:
                    backgroundImage.color = Color.white;
                    break;
            }
        }
        else
        {
            fillImage.gameObject.SetActive(false);
        }
    }
}

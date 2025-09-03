using UnityEngine;

[RequireComponent(typeof(Outline))]
public class EnemyHighlighter : MonoBehaviour
{
    private Outline outlineComponent;
    private Transform lastTarget;

    // Cached reference to current gaze target
    private Transform CurrentGazeTarget => GameManager.Instance != null ? GameManager.Instance.CurrentGazeTarget : null;

    private void Awake()
    {
        outlineComponent = GetComponent<Outline>();
        outlineComponent.enabled = false;
    }

    private void Update()
    {
        bool isCurrentlyTargeted = (CurrentGazeTarget == transform);

        if (lastTarget == transform && !isCurrentlyTargeted)
        {
            // Was targeted and now not -> disable outline
            outlineComponent.enabled = false;
            lastTarget = null;
        }
        else if (lastTarget != transform && isCurrentlyTargeted)
        {
            // Was not targeted and now is -> enable outline
            outlineComponent.enabled = true;
            lastTarget = transform;
        }
    }
}

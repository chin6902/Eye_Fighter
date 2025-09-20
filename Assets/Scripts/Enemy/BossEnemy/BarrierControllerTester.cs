using UnityEngine;

/// <summary>
/// Small helper to test BarrierController in-play:
/// - Press the configured key to toggle the barrier on/off.
/// - Calls AutoAssignMagicCircles() before ActivateBarrier() to help auto-setup.
/// Attach to any GameObject in the scene and assign the BarrierController reference.
/// </summary>
public class BarrierControllerTester : MonoBehaviour
{
    [Tooltip("Reference to the BarrierController in the scene.")]
    public BarrierController barrierController;

    [Tooltip("Key used to toggle barrier for testing.")]
    public KeyCode toggleKey = KeyCode.G;

    [Tooltip("If true, call AutoAssignMagicCircles() before activating so spots get magic circle instances.")]
    public bool autoAssignMagicCirclesBeforeActivate = true;

    private void Start()
    {
        if (barrierController == null)
            Debug.LogWarning("BarrierControllerTester: barrierController is not assigned in inspector.");
    }

    private void Update()
    {
        if (barrierController == null) return;

        if (Input.GetKeyDown(toggleKey))
        {
            // Toggle behaviour
            if (barrierController.IsBarrierActive())
            {
                Debug.Log("BarrierControllerTester: Deactivating barrier (toggle).");
                barrierController.DeactivateBarrier();
            }
            else
            {
                Debug.Log("BarrierControllerTester: Activating barrier (toggle).");
                if (autoAssignMagicCirclesBeforeActivate)
                    barrierController.AutoAssignMagicCircles();

                barrierController.ActivateBarrier();
            }
        }
    }

    // Optional editor utility to trigger activation from context menu
    [ContextMenu("Activate Barrier (Editor)")]
    private void ActivateFromContextMenu()
    {
        if (barrierController == null)
        {
            Debug.LogWarning("BarrierControllerTester: barrierController is not assigned.");
            return;
        }
        if (autoAssignMagicCirclesBeforeActivate)
            barrierController.AutoAssignMagicCircles();
        barrierController.ActivateBarrier();
    }
}

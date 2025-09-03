using Eyeware.BeamEyeTracker.Unity;
using UnityEngine;
using UnityEngine.InputSystem;

public class BeamManager : MonoBehaviour
{
    private BeamEyeTrackerInputDevice beamDevice;

    void OnEnable()
    {
        beamDevice = InputSystem.GetDevice<BeamEyeTrackerInputDevice>();
        if (beamDevice == null)
            Debug.LogError("BeamEyeTrackerInputDevice not found. Is the Beam app running?");
        else
            Debug.Log("Beam device ready: " + beamDevice);
    }

    void OnDisable()
    {
        // Nothing to clean up when using Input System device
    }

    void Update()
    {
        if (beamDevice == null) return;
        if (beamDevice.trackingStatus.ReadValue() != 1) return;

        Vector2 normGaze = beamDevice.unifiedScreenGazePosition.ReadValue();
        //Debug.Log($"Normalized gaze: {normGaze}");
    }
}
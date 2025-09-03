using UnityEngine;

public class LookAtCamera : MonoBehaviour
{
    private void LateUpdate()
    {
        if (PlayerCameraOrbit.Instance == null)
        {
            Debug.LogWarning("PlayerCameraOrbit.Instance is null!");
            return;
        }

        Vector3 dirFromCam = transform.position - PlayerCameraOrbit.Instance.transform.position;
        transform.LookAt(transform.position + dirFromCam);
    }
}

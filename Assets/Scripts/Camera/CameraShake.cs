using UnityEngine;

public class CameraShake : MonoBehaviour
{
    public static CameraShake Instance { get; private set; }

    [SerializeField] private Camera targetCamera;

    private Transform camTransform;

    private float shakeTimer;
    private float shakeTimerMax;
    private float startingIntensity;

    private Vector3 originalLocalPos;
    private Quaternion originalLocalRot;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (targetCamera == null)
        {
            Debug.LogError("CameraShake: targetCamera not set!");
            return;
        }

        camTransform = targetCamera.transform;

        originalLocalPos = camTransform.localPosition;
        originalLocalRot = camTransform.localRotation;
    }

    public void ShakeCamera(float intensity, float time)
    {
        startingIntensity = intensity;
        shakeTimerMax = time;
        shakeTimer = time;
    }

    private void LateUpdate()
    {
        if (targetCamera == null) return;

        if (shakeTimer > 0f)
        {
            shakeTimer -= Time.unscaledDeltaTime;

            float shakeAmount = Mathf.Lerp(0f, startingIntensity, shakeTimer / shakeTimerMax);

            // Random offset
            Vector3 shakeOffset = Random.insideUnitSphere * shakeAmount * 0.1f;

            // Random rotation
            Quaternion shakeRotation = Quaternion.Euler(
                Random.Range(-shakeAmount, shakeAmount),
                Random.Range(-shakeAmount, shakeAmount),
                0f);

            camTransform.localPosition = originalLocalPos + shakeOffset;
            camTransform.localRotation = originalLocalRot * shakeRotation;

            if (shakeTimer <= 0f)
            {
                ResetPosition();
            }
        }
        else
        {
            ResetPosition();
        }
    }

    private void ResetPosition()
    {
        camTransform.localPosition = originalLocalPos;
        camTransform.localRotation = originalLocalRot;
    }
}

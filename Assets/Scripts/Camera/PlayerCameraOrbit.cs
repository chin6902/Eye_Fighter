using UnityEngine;

public class PlayerCameraOrbit : MonoBehaviour
{
    public static Camera Instance { get; private set; }

    [SerializeField] private Camera orbitCamera;

    private void Awake()
    {
        Instance = orbitCamera;
    }
}

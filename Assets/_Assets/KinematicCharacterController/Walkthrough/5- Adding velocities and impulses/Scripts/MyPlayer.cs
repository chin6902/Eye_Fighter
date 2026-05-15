using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using KinematicCharacterController;
using KinematicCharacterController.Examples;

namespace KinematicCharacterController.Walkthrough.AddingImpulses
{
    public class MyPlayer : MonoBehaviour
    {
        public ExampleCharacterCamera OrbitCamera;
        public Transform CameraFollowPoint;
        public MyCharacterController Character;

        private const string MouseXInput = "Mouse X";
        private const string MouseYInput = "Mouse Y";
        private const string MouseScrollInput = "Mouse ScrollWheel";
        private const string HorizontalInput = "Horizontal";
        private const string VerticalInput = "Vertical";

        [SerializeField] private GetEnemy enemyFinder;
        [SerializeField] float CameraVirtSensitivity = 50f;
        [SerializeField] float defaultxViewportPos = 0.5f;
        [SerializeField] float defaultyViewportPos = 0.65f;
        [SerializeField] float bossxViewportPos = 0.5f;
        [SerializeField] float bossyViewportPos = 0.3f;

        Vector2 desiredViewportPos = new Vector2(0.5f, 0.65f);

        private void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;

            // Tell camera to follow transform
            OrbitCamera.SetFollowTransform(CameraFollowPoint);

            // Ignore the character's collider(s) for camera obstruction checks
            OrbitCamera.IgnoredColliders.Clear();
            OrbitCamera.IgnoredColliders.AddRange(Character.GetComponentsInChildren<Collider>());
        }

        private void Update()
        {
            if(GameManager.Instance.isPaused)
            {
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                Cursor.lockState = CursorLockMode.Locked;
            }

            if (GameManager.Instance.IsGazeModeActive())
            {
                return;
            }
            else
            {
                HandleCharacterInput();
            }
        }

        private void LateUpdate()
        {
            HandleCameraInput();
        }

        private void HandleCameraInput()
        {
            // Create the look input vector for the camera
            float mouseLookAxisUp = Input.GetAxisRaw(MouseYInput);
            float mouseLookAxisRight = Input.GetAxisRaw(MouseXInput);
            Vector3 lookInputVector = new Vector3(mouseLookAxisRight, mouseLookAxisUp, 0f);

            // Prevent moving the camera while the cursor isn't locked
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                lookInputVector = Vector3.zero;
            }
            else if (GameManager.Instance.IsGazeModeActive())
            {
                // 1. Project enemy to viewport
                Transform currentEnemy = GameManager.Instance.CurrentGazeTarget;
                
                if (currentEnemy != null)
                {
                    Vector3 vp = OrbitCamera.Camera.WorldToViewportPoint(currentEnemy.position);
                    Vector2 currentVP = new Vector2(vp.x, vp.y);

                    // 2. Compute offset to desired

                    if(GameManager.Instance.currentGamePhase == GameManager.MiniGamePhase.BossMiniGamePhase)
                    {
                        desiredViewportPos = new Vector2(bossxViewportPos, bossyViewportPos);
                    }
                    else
                    {
                        desiredViewportPos = new Vector2(defaultxViewportPos, defaultyViewportPos);
                    }

                    Vector2 delta = currentVP - desiredViewportPos;

                    // 3. Convert to virtual mouse
                    float horizVirt = delta.x * CameraVirtSensitivity;
                    float vertVirt = delta.y * CameraVirtSensitivity;

                    lookInputVector = new Vector3(horizVirt, vertVirt, 0f);
                }
                else
                {
                    lookInputVector = Vector3.zero;
                }
            }

                // Input for zooming the camera (disabled in WebGL because it can cause problems)
                float scrollInput = -Input.GetAxis(MouseScrollInput) * 0;
#if UNITY_WEBGL
        scrollInput = 0f;
#endif

            // Apply inputs to the camera
            OrbitCamera.UpdateWithInput(Time.unscaledDeltaTime, scrollInput, lookInputVector);

            /*
            // Handle toggling zoom level
            if (Input.GetMouseButtonDown(1))
            {
                OrbitCamera.TargetDistance = (OrbitCamera.TargetDistance == 0f) ? OrbitCamera.DefaultDistance : 0f;
            }
            */
        }

        private void HandleCharacterInput()
        {
            PlayerCharacterInputs characterInputs = new PlayerCharacterInputs();

            // Build the CharacterInputs struct
            characterInputs.MoveAxisForward = Input.GetAxisRaw(VerticalInput);
            characterInputs.MoveAxisRight = Input.GetAxisRaw(HorizontalInput);
            characterInputs.CameraRotation = OrbitCamera.Transform.rotation;
            characterInputs.JumpDown = Input.GetKeyDown(KeyCode.Space);

            // Apply inputs to character
            Character.SetInputs(ref characterInputs);

        }
    }
}
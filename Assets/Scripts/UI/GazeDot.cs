using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using Eyeware.BeamEyeTracker.Unity;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class GazeDot : MonoBehaviour
{
    [Header("References")]
    [SerializeField] public RectTransform dotRect;
    [SerializeField] private Canvas canvas;

    [Header("Smoothing")]
    [SerializeField] private float smoothingSpeed = 10f;

    [Header("Border Padding (pixels)")]
    [SerializeField] private Vector2 buffer = new Vector2(0f, 0f);

    private Vector2 _dotSize;
    private Vector2 _currentPos;

    #region WinAPI
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x, y; }

    [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
    #endregion

    private void Start()
    {
        if (!dotRect || !canvas)
        {
            Debug.LogError("GazeDot: Missing references.");
            enabled = false;
            return;
        }

        _dotSize = dotRect.sizeDelta;
        _currentPos = dotRect.anchoredPosition;
    }

    private void Update()
    {
        /*
        // Debug key - reliable in Editor and Build
        if (Input.GetKeyDown(KeyCode.K))
        {
            LogDebugSample();
        }
        */

        var device = InputSystem.GetDevice<BeamEyeTrackerInputDevice>();
        if (device == null || device.trackingStatus.ReadValue() != 1) { return; }

        Vector2 rawDesktop = device.unifiedScreenGazePosition.ReadValue();
        if (float.IsNaN(rawDesktop.x) || float.IsNaN(rawDesktop.y)) { return; }

        bool inside;
        Vector2 unityScreen = MapDesktopToUnityScreenWithInRange(rawDesktop, out inside);

        // Convert to local canvas space
        RectTransform canvasRect = canvas.GetComponent<RectTransform>();
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect,
                unityScreen,
                canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera,
                out Vector2 localPoint))
        {
            return;
        }

        // Clamp to visible game window area (with buffer)
        float halfW = (canvasRect.rect.width * 0.5f) - (_dotSize.x * 0.5f) - buffer.x;
        float halfH = (canvasRect.rect.height * 0.5f) - (_dotSize.y * 0.5f) - buffer.y;

        localPoint.x = Mathf.Clamp(localPoint.x, -halfW, halfW);
        localPoint.y = Mathf.Clamp(localPoint.y, -halfH, halfH);

        // If gaze inside game content -> smooth; if outside -> snap to edge (no lag)
        if (inside)
        {
            float alpha = 1f - Mathf.Exp(-smoothingSpeed * Time.unscaledDeltaTime);
            _currentPos = Vector2.Lerp(_currentPos, localPoint, alpha);
        }
        else
        {
            _currentPos = localPoint;
        }

        dotRect.anchoredPosition = _currentPos;
    }

    /// <summary>
    /// Map desktop gaze (physical pixels) -> Unity screen pixels.
    /// Returns whether desktopPoint was inside the game content area (true) or outside (false).
    /// Editor: determine content rect (Screen.width x Screen.height) centered inside GameView physical rect (gvPx).
    /// Build: use native client-area mapping.
    /// </summary>
    private Vector2 MapDesktopToUnityScreenWithInRange(Vector2 desktopPoint, out bool inside)
    {
#if UNITY_EDITOR
        // Get GameView physical rect (points->physical px via pixelsPerPoint)
        var gv = GetMainGameView();
        if (gv != null)
        {
            Rect gvPos = gv.position; // in points
            float ppp = UnityEditor.EditorGUIUtility.pixelsPerPoint; // points -> physical pixels
            Rect gvPx = new Rect(gvPos.x * ppp, gvPos.y * ppp, gvPos.width * ppp, gvPos.height * ppp);

            // Compute the actual content rectangle inside the GameView (where Unity renders)
            // For scale 1x and typical setups, this will be centered and equal to Screen.width/Screen.height.
            float contentW = Screen.width;
            float contentH = Screen.height;

            // If the content is larger than gvPx (unlikely), fall back to gvPx size.
            if (contentW > gvPx.width) contentW = gvPx.width;
            if (contentH > gvPx.height) contentH = gvPx.height;

            float contentX = gvPx.xMin + (gvPx.width - contentW) * 0.5f;
            float contentY = gvPx.yMin + (gvPx.height - contentH) * 0.5f;
            Rect contentPx = new Rect(contentX, contentY, contentW, contentH);

            // Is gaze inside the rendered content?
            inside = (desktopPoint.x >= contentPx.xMin && desktopPoint.x <= contentPx.xMax &&
                      desktopPoint.y >= contentPx.yMin && desktopPoint.y <= contentPx.yMax);

            // Clamp desktop gaze to contentPx so it will stick to the border when outside
            float clampedX = Mathf.Clamp(desktopPoint.x, contentPx.xMin, contentPx.xMax);
            float clampedY = Mathf.Clamp(desktopPoint.y, contentPx.yMin, contentPx.yMax);

            // Local px inside content (origin top-left)
            float localXpx = clampedX - contentPx.xMin;
            float localYpx = clampedY - contentPx.yMin;

            // Map to Unity screen pixels:
            // Because contentPx size equals Screen.width/height, this becomes 1:1 mapping
            float unityX = (contentPx.width > 0f) ? (localXpx * (Screen.width / contentPx.width)) : 0f;
            float unityY = (contentPx.height > 0f) ? (Screen.height - (localYpx * (Screen.height / contentPx.height))) : 0f;

            return new Vector2(unityX, unityY);
        }

        // fallback: assume fullscreen mapping
        inside = true;
        return new Vector2(desktopPoint.x, Screen.height - desktopPoint.y);
#else
        // Build: native client-area mapping (pixel-perfect)
        inside = false;
        IntPtr hwnd = GetActiveWindow();
        if (hwnd != IntPtr.Zero && GetClientRect(hwnd, out RECT clientRect))
        {
            POINT topLeft = new POINT { x = 0, y = 0 };
            if (ClientToScreen(hwnd, ref topLeft))
            {
                int width = clientRect.right - clientRect.left;
                int height = clientRect.bottom - clientRect.top;

                float xInWin = desktopPoint.x - topLeft.x;
                float yInWin = (topLeft.y + height) - desktopPoint.y;

                bool inRangeX = xInWin >= 0f && xInWin <= width;
                bool inRangeY = yInWin >= 0f && yInWin <= height;
                inside = inRangeX && inRangeY;

                xInWin = Mathf.Clamp(xInWin, 0f, width);
                yInWin = Mathf.Clamp(yInWin, 0f, height);

                return new Vector2(xInWin, yInWin);
            }
        }

        return new Vector2(desktopPoint.x, Screen.height - desktopPoint.y);
#endif
    }

    private void LogDebugSample()
    {
        var dev = InputSystem.GetDevice<BeamEyeTrackerInputDevice>();
        if (dev == null)
        {
            Debug.Log("GazeDot Debug: no device");
            return;
        }

        Vector2 raw = dev.unifiedScreenGazePosition.ReadValue();

#if UNITY_EDITOR
        var gv = GetMainGameView();
        Rect gvPos = gv != null ? gv.position : new Rect(0, 0, 0, 0);
        float ppp = UnityEditor.EditorGUIUtility.pixelsPerPoint;
        Rect gvPx = new Rect(gvPos.x * ppp, gvPos.y * ppp, gvPos.width * ppp, gvPos.height * ppp);

        // compute content rect we use
        float contentW = Screen.width;
        float contentH = Screen.height;
        if (contentW > gvPx.width) contentW = gvPx.width;
        if (contentH > gvPx.height) contentH = gvPx.height;
        Rect contentPx = new Rect(gvPx.xMin + (gvPx.width - contentW) * 0.5f,
                                   gvPx.yMin + (gvPx.height - contentH) * 0.5f,
                                   contentW, contentH);

        bool inRange = (raw.x >= contentPx.xMin && raw.x <= contentPx.xMax && raw.y >= contentPx.yMin && raw.y <= contentPx.yMax);
        Vector2 mapped = MapDesktopToUnityScreenWithInRange(raw, out bool inside);

        Debug.Log($"GazeDot Debug (Editor): raw={raw} gvPx={gvPx} contentPx={contentPx} inContent={inRange} mappedUnityScreen={mapped} Screen={Screen.width}x{Screen.height} ppp={ppp:F2}");
#else
        Vector2 mapped = MapDesktopToUnityScreenWithInRange(raw, out bool inside);
        Debug.Log($"GazeDot Debug (Build): raw={raw} mappedClient={mapped}");
#endif
    }

#if UNITY_EDITOR
    private EditorWindow GetMainGameView()
    {
        var assembly = typeof(EditorWindow).Assembly;
        var type = assembly.GetType("UnityEditor.GameView");
        return EditorWindow.GetWindow(type);
    }
#endif
}

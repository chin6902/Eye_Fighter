using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ElementSelectManager : MonoBehaviour
{
    [Header("Tracked Dot")]
    [SerializeField] private RectTransform gazeDot = null;

    [Header("Selectable Images (UI)")]
    [SerializeField] private RectTransform[] elementImages = null;

    [Header("Outline Components (1:1 with elementImages)")]
    [SerializeField] private UnityEngine.UI.Outline[] elementOutlines = null;

    [Header("Canvas Raycaster / Event System")]
    [SerializeField] private GraphicRaycaster raycaster = null;
    [SerializeField] private EventSystem eventSystem = null;

    [Header("Gaze Fill UI")]
    [Tooltip("A UI Image (radial fill) that sits on/near the gaze dot and shows progress.")]
    [SerializeField] private Image gazeFillImage = null;

    [Tooltip("How long (seconds) the player must continuously gaze an element to select it.")]
    [SerializeField] private float fillDuration = 2f;

    [Tooltip("Should the fill use unscaled time (recommended if you use slow-motion)?")]
    [SerializeField] private bool useUnscaledTime = true;

    // runtime state
    private readonly List<RaycastResult> _raycastHits = new List<RaycastResult>();
    private int _hoveredIndex = -1;
    private float _hoverTimer = 0f;

    private void Reset()
    {
        // sanity defaults so inspector is convenient
        fillDuration = 2f;
        useUnscaledTime = true;
    }

    private void OnValidate()
    {
        // Ensure arrays match length for outlines
        if (elementOutlines != null && elementImages != null && elementOutlines.Length != elementImages.Length)
        {
            Debug.LogWarning("ElementSelectManager: elementOutlines length should match elementImages length.");
        }
    }

    private void Start()
    {
        if (gazeDot == null) Debug.LogError("ElementSelectManager: gazeDot not assigned.");
        if (raycaster == null) Debug.LogError("ElementSelectManager: raycaster not assigned.");
        if (eventSystem == null) Debug.LogError("ElementSelectManager: eventSystem not assigned.");

        if (gazeFillImage != null)
        {
            gazeFillImage.type = Image.Type.Filled;
            gazeFillImage.fillMethod = Image.FillMethod.Radial360;
            gazeFillImage.fillOrigin = (int)Image.Origin360.Top;
            gazeFillImage.fillClockwise = false;
            gazeFillImage.fillAmount = 0f;
            gazeFillImage.enabled = false;
        }
    }

    private void Update()
    {
        if (GameManager.Instance == null) return;

        // Only run during ElementSelectPhase
        if (GameManager.Instance.currentGamePhase != GameManager.MiniGamePhase.ElementSelectPhase)
        {
            ResetHover();
            return;
        }

        if (gazeDot == null) return;

        // 1) Translate gazeDot position to screen coords
        Vector2 dotScreenPos = RectTransformUtility.WorldToScreenPoint(null, gazeDot.position);

        // 2) Raycast UI
        var ped = new PointerEventData(eventSystem) { position = dotScreenPos };
        _raycastHits.Clear();
        raycaster.Raycast(ped, _raycastHits);

        // 3) Default: disable all outlines
        if (elementOutlines != null)
        {
            for (int i = 0; i < elementOutlines.Length; i++)
            {
                if (elementOutlines[i] != null)
                    elementOutlines[i].enabled = false;
            }
        }

        // 4) Check hits and pick first matching element (top-most)
        int foundIndex = -1;
        foreach (var r in _raycastHits)
        {
            for (int i = 0; i < elementImages.Length; i++)
            {
                if (elementImages[i] == null) continue;
                if (r.gameObject == elementImages[i].gameObject)
                {
                    foundIndex = i;
                    break;
                }
            }
            if (foundIndex >= 0) break;
        }

        // 5) Highlight outline for hovered element
        if (foundIndex >= 0 && elementOutlines != null && foundIndex < elementOutlines.Length && elementOutlines[foundIndex] != null)
        {
            elementOutlines[foundIndex].enabled = true;
        }

        // 6) Handle hover timer and fill progress
        if (foundIndex >= 0)
        {
            // same element as previous frame?
            if (foundIndex == _hoveredIndex)
            {
                _hoverTimer += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            }
            else
            {
                // new element hovered: reset timer and start fill image
                _hoveredIndex = foundIndex;
                _hoverTimer = 0f;
            }

            // show and update fill image
            if (gazeFillImage != null)
            {
                gazeFillImage.enabled = true;
                gazeFillImage.fillAmount = Mathf.Clamp01(_hoverTimer / Mathf.Max(0.0001f, fillDuration));
            }

            // If fill complete -> confirm selection
            if (_hoverTimer >= fillDuration)
            {
                SelectElement(foundIndex);
                // Reset state so we don't double-trigger
                ResetHover();
            }
        }
        else
        {
            // not hovering any element: reset hover timer and hide fill
            ResetHover();
        }

        /*
        // 7) optional: pressing E should still work as a fallback if you want (keeps old behavior)
        if (Input.GetKeyDown(KeyCode.E))
        {
            if (_hoveredIndex >= 0)
            {
                SelectElement(_hoveredIndex);
                ResetHover();
            }
        }
        */
    }

    private void SelectElement(int index)
    {
        if (index < 0 || index >= elementImages.Length) return;

        GameManager.ElementType chosen = GameManager.ElementType.None;
        switch (index)
        {
            case 0: chosen = GameManager.ElementType.Fire; break;
            case 1: chosen = GameManager.ElementType.Electric; break;
            case 2: chosen = GameManager.ElementType.Water; break;
            default: chosen = GameManager.ElementType.None; break;
        }

        // Inform game manager and transition
        GameManager.Instance.SetSelectedElement(chosen);
        GameManager.Instance.ConfirmElementSelection();
    }

    private void ResetHover()
    {
        _hoveredIndex = -1;
        _hoverTimer = 0f;
        if (gazeFillImage != null)
        {
            gazeFillImage.fillAmount = 0f;
            gazeFillImage.enabled = false;
        }
    }

    // Optional: expose current normalized fill for UI binding
    public float GetCurrentFillNormalized()
    {
        return Mathf.Clamp01(_hoverTimer / Mathf.Max(0.0001f, fillDuration));
    }
}

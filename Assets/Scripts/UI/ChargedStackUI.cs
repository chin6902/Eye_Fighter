// ChargedStackUI.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class ChargedStackUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Drag your ProjectileChargeManager here (you said you'll assign it).")]
    [SerializeField] private ProjectileChargeManager manager;

    [Tooltip("Prefab or scene Image used as a single icon template. If this Image is already a child of this GameObject it will be used as the template and hidden.")]
    [SerializeField] private Image iconPrefab;

    [Header("Appearance")]
    [Tooltip("If true and sprites are provided, icons will swap between filled/empty sprites. Otherwise the gameobject active state will be used.")]
    [SerializeField] private bool useSpriteFill = true;

    [Tooltip("Sprite used for a filled charge slot (optional).")]
    [SerializeField] private Sprite filledSprite;

    [Tooltip("Sprite used for an empty charge slot (optional).")]
    [SerializeField] private Sprite emptySprite;

    // Internals
    private readonly List<Image> _icons = new List<Image>();
    private bool _subscribed = false;

    private void Awake()
    {
        if (iconPrefab == null)
        {
            Debug.LogError($"[{nameof(ChargedStackUI)}] iconPrefab is not assigned. Please assign an Image prefab or a child Image.", this);
            enabled = false;
            return;
        }

        // If the provided prefab is a child in the scene, keep it as the template but hide it.
        if (iconPrefab.gameObject.scene.IsValid() && iconPrefab.transform.IsChildOf(transform))
        {
            iconPrefab.gameObject.SetActive(false);
        }
    }

    private void OnEnable()
    {
        TryInitAndSubscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void TryInitAndSubscribe()
    {
        if (manager == null)
        {
            // no manager assigned in inspector — try using singleton instance as fallback
            manager = ProjectileChargeManager.Instance;
        }

        if (manager == null)
        {
            Debug.LogWarning($"[{nameof(ChargedStackUI)}] ProjectileChargeManager reference is null. Assign it in the inspector or ensure the singleton is created before this UI.", this);
            return;
        }

        // populate icon pool based on manager max
        RefreshIcons();

        // subscribe once
        if (_subscribed == false)
        {
            manager.OnChargesChanged += OnChargesChanged;
            manager.OnChargeAdded += OnChargeAdded;
            manager.OnChargeUsed += OnChargeUsed;
            manager.OnChargeMaxReached += OnChargeMaxReached;
            _subscribed = true;
        }

        // initialize visuals immediately
        OnChargesChanged(manager.GetCount());
    }

    private void Unsubscribe()
    {
        if (manager != null && _subscribed)
        {
            manager.OnChargesChanged -= OnChargesChanged;
            manager.OnChargeAdded -= OnChargeAdded;
            manager.OnChargeUsed -= OnChargeUsed;
            manager.OnChargeMaxReached -= OnChargeMaxReached;
            _subscribed = false;
        }
    }

    // Build or trim the icon pool to match manager's GetMax()
    private void RefreshIcons()
    {
        if (manager == null)
        {
            return;
        }

        int desired = manager.GetMax();

        // remove extras
        while (_icons.Count > desired)
        {
            Image last = _icons[_icons.Count - 1];
            _icons.RemoveAt(_icons.Count - 1);
            if (Application.isPlaying)
            {
                Destroy(last.gameObject);
            }
            else
            {
                DestroyImmediate(last.gameObject);
            }
        }

        // add missing
        while (_icons.Count < desired)
        {
            Image inst = Instantiate(iconPrefab, transform);
            inst.gameObject.SetActive(true);
            _icons.Add(inst);
        }

        // set initial visuals (all empty)
        UpdateVisuals(0);
    }

    // Called by manager
    private void OnChargesChanged(int count)
    {
        UpdateVisuals(count);
    }

    // Optional: react to add/use/max events for animations or sounds
    private void OnChargeAdded()
    {
        // Example: you might want to animate the newest filled icon here.
        // This method left intentionally small so you can hook in animation code.
    }

    private void OnChargeUsed()
    {
        // Example: animate or flash the icon that was consumed.
    }

    private void OnChargeMaxReached()
    {
        // Example: play a "max" pulse or sound.
    }

    private void UpdateVisuals(int count)
    {
        if (_icons.Count == 0)
        {
            return;
        }

        int clamped = Mathf.Clamp(count, 0, _icons.Count);

        if (useSpriteFill && filledSprite != null && emptySprite != null)
        {
            for (int i = 0; i < _icons.Count; i++)
            {
                Image img = _icons[i];
                if (i < clamped)
                {
                    img.sprite = filledSprite;
                    img.enabled = true;
                    img.gameObject.SetActive(true);
                }
                else
                {
                    img.sprite = emptySprite;
                    img.enabled = true;
                    img.gameObject.SetActive(true);
                }
            }
        }
        else
        {
            // fallback: toggle active for filled slots
            for (int i = 0; i < _icons.Count; i++)
            {
                Image img = _icons[i];
                img.gameObject.SetActive(i < clamped);
            }
        }
    }

    /// <summary>
    /// Force a refresh of the UI (call if manager.maxCharges changes at runtime).
    /// </summary>
    public void ForceRefresh()
    {
        RefreshIcons();
        if (manager != null)
        {
            OnChargesChanged(manager.GetCount());
        }
    }

    /// <summary>
    /// Allows assigning manager by script if needed.
    /// </summary>
    public void SetManager(ProjectileChargeManager newManager)
    {
        if (_subscribed && manager != null)
        {
            Unsubscribe();
        }

        manager = newManager;
        TryInitAndSubscribe();
    }
}

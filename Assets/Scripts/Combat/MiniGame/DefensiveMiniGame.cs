using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Defensive mini-game (persistent UI per projectile).
/// - UI segments are created once when a projectile spawns and persist until projectile destroyed or cleared.
/// - Pressing Q toggles visibility; the UI object(s) persist so re-entering Q will re-show them.
/// - Fill starts FULL and empties while gaze overlaps; when all subsegments are cleared, projectile is deflected/destroyed.
/// - Safe to call DefensiveMiniGame.RegisterProjectileStatic(proj) immediately after you Instantiate/Initialize the projectile.
/// </summary>
public class DefensiveMiniGame : MonoBehaviour
{
    public static DefensiveMiniGame Instance { get; private set; }

    // Fired when a projectile has been fully cleared (all subsegments removed and ClearByGaze() called)
    public static event Action<Projectile> OnProjectileCleared;

    // queued registrations if called before Awake
    private static readonly List<Projectile> s_pendingRegistrations = new();

    [Header("References")]
    [SerializeField] private Canvas uiCanvas = null;
    [SerializeField] private RectTransform segmentPrefab = null; // small UI element prefab (RectTransform with Image child)
    [SerializeField] private GazeDot gazeDot = null;             // your gaze dot script, must expose dotRect

    [Header("Gameplay")]
    [SerializeField] private bool canvasOnlyWhileQ = true;        // require Q to be held to clear
    [SerializeField] private bool useUnscaledTime = true;         // good for slow-motion setups

    [Header("Layout / Visuals")]
    [SerializeField] private float segmentOffset = 36f;           // distance from center to each subsegment
    [SerializeField] private float clearAnimDuration = 0.12f;
    [SerializeField] private bool fadeOnClear = true;

    [Header("Deflect VFX")]
    [SerializeField] private GameObject deflectVfxPrefab = null;
    [SerializeField] private float deflectVfxLifetime = 1.2f;

    private RectTransform _canvasRect;

    private class SubSegment
    {
        public RectTransform rect;
        public Image image;
        public CanvasGroup canvasGroup;
        public bool clearing;
    }

    private class SegmentData
    {
        public Projectile projectile;
        public RectTransform container;    // container anchored on canvas (follows projectile)
        public List<SubSegment> children;  // four subsegments
        public Vector2 prefabSize;
    }

    private readonly Dictionary<Projectile, SegmentData> _active = new();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }

        if (uiCanvas != null)
        {
            _canvasRect = uiCanvas.GetComponent<RectTransform>();
        }

        // process queued registrations (avoid duplicates)
        if (s_pendingRegistrations.Count > 0)
        {
            for (int i = 0; i < s_pendingRegistrations.Count; i++)
            {
                var p = s_pendingRegistrations[i];
                if (p != null)
                {
                    RegisterProjectileInternal(p);
                }
            }
            s_pendingRegistrations.Clear();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        ClearAll();
    }

    private void OnEnable()
    {
        // When manager gets enabled (e.g. canvas GameObject toggled), restore per-segment alpha
        bool qNow = Input.GetKey(KeyCode.Q);
        foreach (var kv in _active)
        {
            var sd = kv.Value;
            if (sd == null || sd.children == null)
            {
                continue;
            }

            foreach (var child in sd.children)
            {
                if (child == null || child.canvasGroup == null)
                {
                    continue;
                }

                // If canvasOnlyWhileQ, show only if Q is held; otherwise show always.
                SetCanvasGroupAlpha(child.canvasGroup, (!canvasOnlyWhileQ || qNow) ? 1f : 0f);

                // ensure the GameObject is active (so it can be re-enabled visually when canvas is active)
                if (child.rect != null && !child.rect.gameObject.activeInHierarchy)
                {
                    child.rect.gameObject.SetActive(true);
                }
            }

            if (sd.container != null && !sd.container.gameObject.activeInHierarchy)
            {
                sd.container.gameObject.SetActive(true);
            }
        }
    }

    private void Update()
    {
        if (uiCanvas == null || segmentPrefab == null || gazeDot == null)
        {
            return;
        }

        bool qHeld = Input.GetKey(KeyCode.Q);
        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

        // Keep canvas rendering enabled only while Q is held if requested.
        // (This doesn't destroy children; it only affects rendering.)
        if (canvasOnlyWhileQ)
        {
            uiCanvas.enabled = qHeld;
        }

        // When Q pressed we force-show (alpha) all segments so player sees them immediately.
        if (Input.GetKeyDown(KeyCode.Q))
        {
            ForceShowAllSegments();
        }

        // When Q released hide segments (alpha) if canvasOnlyWhileQ.
        if (Input.GetKeyUp(KeyCode.Q) && canvasOnlyWhileQ)
        {
            ForceHideAllSegments();
        }

        // iterate snapshot of active projectiles
        var keys = new List<Projectile>(_active.Keys);
        foreach (var proj in keys)
        {
            if (proj == null)
            {
                RemoveNullKeyEntries();
                continue;
            }

            if (!_active.TryGetValue(proj, out var sd))
            {
                continue;
            }

            if (sd == null || sd.container == null)
            {
                _active.Remove(proj);
                continue;
            }

            bool onScreen = UpdateContainerPosition(sd);

            // allow clearing only if canvasOnlyWhileQ == false OR (true & qHeld & onScreen)
            bool allowClear = !canvasOnlyWhileQ || (canvasOnlyWhileQ && qHeld && onScreen);

            // check each child: if visible & not clearing & gaze overlaps -> clear immediately
            // (we use snapshot to avoid modification during iteration)
            var childrenSnap = new List<SubSegment>(sd.children);
            foreach (var child in childrenSnap)
            {
                if (child == null || child.rect == null)
                {
                    continue;
                }

                if (child.clearing)
                {
                    continue;
                }

                float alpha = child.canvasGroup != null ? child.canvasGroup.alpha : 1f;
                if (alpha <= 0f)
                {
                    continue; // not visible
                }

                if (!allowClear)
                {
                    continue;
                }

                if (IsGazeOverlappingRect(child.rect))
                {
                    child.clearing = true;
                    StartCoroutine(PlayChildClear(child, sd));
                }
            }
        }
    }

    /// <summary>
    /// Static safe registration. Call right after Instantiate/Initialize projectile.
    /// </summary>
    public static void RegisterProjectileStatic(Projectile projectile)
    {
        if (projectile == null)
        {
            return;
        }

        if (Instance != null)
        {
            Instance.RegisterProjectileInternal(projectile);
            return;
        }

        if (!s_pendingRegistrations.Contains(projectile))
        {
            s_pendingRegistrations.Add(projectile);
        }
    }

    public void RegisterProjectile(Projectile projectile)
    {
        if (projectile == null)
        {
            return;
        }

        RegisterProjectileInternal(projectile);
    }

    private void RegisterProjectileInternal(Projectile projectile)
    {
        if (projectile == null)
        {
            return;
        }

        if (_active.ContainsKey(projectile))
        {
            return;
        }

        if (uiCanvas == null || segmentPrefab == null)
        {
            return;
        }

        // create container under canvas so it follows via Screen->Local conversion
        var containerGO = new GameObject($"DefSeg_{projectile.name}", typeof(RectTransform));
        containerGO.transform.SetParent(uiCanvas.transform, false);
        var containerRT = containerGO.GetComponent<RectTransform>();
        containerRT.localScale = Vector3.one;

        // get prefab sizeDelta (fallback to rect width/height)
        Vector2 prefabSize = segmentPrefab.sizeDelta;
        if (prefabSize == Vector2.zero)
        {
            var tmp = Instantiate(segmentPrefab, uiCanvas.transform, false);
            prefabSize = tmp.sizeDelta != Vector2.zero ? tmp.sizeDelta : new Vector2(tmp.rect.width, tmp.rect.height);
            Destroy(tmp.gameObject);
        }

        // offsets for TL, TR, BL, BR
        Vector2[] offsets = new Vector2[4]
        {
            new Vector2(-segmentOffset,  segmentOffset),
            new Vector2( segmentOffset,  segmentOffset),
            new Vector2(-segmentOffset, -segmentOffset),
            new Vector2( segmentOffset, -segmentOffset)
        };

        var children = new List<SubSegment>(4);
        for (int i = 0; i < 4; i++)
        {
            var childRT = Instantiate(segmentPrefab, containerRT, false);
            childRT.localScale = Vector3.one;
            childRT.sizeDelta = prefabSize;
            childRT.anchoredPosition = offsets[i];
            childRT.gameObject.SetActive(true);

            CanvasGroup cg = childRT.GetComponent<CanvasGroup>();
            if (cg == null)
            {
                cg = childRT.gameObject.AddComponent<CanvasGroup>();
            }

            cg.interactable = false;
            cg.blocksRaycasts = false;

            Image img = null;
            var named = childRT.Find("Fill");
            if (named != null)
            {
                img = named.GetComponent<Image>();
            }

            if (img == null)
            {
                img = childRT.GetComponentInChildren<Image>();
            }

            if (img != null)
            {
                img.fillAmount = 1f; // start full
            }

            children.Add(new SubSegment
            {
                rect = childRT,
                image = img,
                canvasGroup = cg,
                clearing = false
            });
        }

        var sd = new SegmentData()
        {
            projectile = projectile,
            container = containerRT,
            children = children,
            prefabSize = prefabSize
        };

        // initial alpha: visible now if Q held or canvasOnlyWhileQ is false
        bool qNow = Input.GetKey(KeyCode.Q);
        foreach (var c in sd.children)
        {
            SetCanvasGroupAlpha(c.canvasGroup, (!canvasOnlyWhileQ || qNow) ? 1f : 0f);
        }

        _active.Add(projectile, sd);
    }

    public void UnregisterProjectile(Projectile projectile)
    {
        if (projectile == null)
        {
            return;
        }

        if (_active.TryGetValue(projectile, out var sd))
        {
            if (sd.container != null)
            {
                Destroy(sd.container.gameObject);
            }

            _active.Remove(projectile);
        }
    }

    private void RemoveNullKeyEntries()
    {
        var toRemove = new List<Projectile>();
        foreach (var kv in _active)
        {
            if (kv.Key == null)
            {
                toRemove.Add(kv.Key);
            }
        }

        foreach (var k in toRemove)
        {
            if (_active.TryGetValue(k, out var sd))
            {
                if (sd != null && sd.container != null)
                {
                    Destroy(sd.container.gameObject);
                }
            }

            _active.Remove(k);
        }
    }

    private void ClearAll()
    {
        foreach (var kv in _active)
        {
            if (kv.Value != null && kv.Value.container != null)
            {
                Destroy(kv.Value.container.gameObject);
            }
        }

        _active.Clear();
    }

    /// <summary>
    /// Update the container position to follow the projectile (screen->local conversion). Returns true if in front of camera.
    /// </summary>
    private bool UpdateContainerPosition(SegmentData sd)
    {
        if (sd == null || sd.container == null || sd.projectile == null || uiCanvas == null)
        {
            return false;
        }

        Vector3 worldPos = sd.projectile.transform.position;

        if (uiCanvas.renderMode == RenderMode.WorldSpace)
        {
            sd.container.position = worldPos;
            sd.container.rotation = uiCanvas.transform.rotation;
            sd.container.localScale = Vector3.one;
            return true;
        }

        Camera cam = uiCanvas.worldCamera != null ? uiCanvas.worldCamera : Camera.main;
        Vector3 screenPoint = cam != null
            ? cam.WorldToScreenPoint(worldPos)
            : RectTransformUtility.WorldToScreenPoint(null, worldPos);

        // behind camera => hide alpha and skip
        if (screenPoint.z < 0f)
        {
            foreach (var c in sd.children)
            {
                SetCanvasGroupAlpha(c.canvasGroup, 0f);
            }

            return false;
        }

        Camera screenToLocalCamera = (uiCanvas.renderMode == RenderMode.ScreenSpaceOverlay) ? null : cam;
        if (_canvasRect == null)
        {
            _canvasRect = uiCanvas.GetComponent<RectTransform>();
        }

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screenPoint, screenToLocalCamera, out Vector2 localPoint))
        {
            sd.container.anchoredPosition = localPoint;
            sd.container.localScale = Vector3.one;
            foreach (var c in sd.children)
            {
                c.rect.sizeDelta = sd.prefabSize;
            }

            return true;
        }

        return false;
    }

    private bool IsGazeOverlappingRect(RectTransform rect)
    {
        if (gazeDot == null || gazeDot.dotRect == null || rect == null)
        {
            return false;
        }

        Rect gazeScreen = GetRectTransformScreenRect(gazeDot.dotRect);
        Rect segScreen = GetRectTransformScreenRect(rect);

        return gazeScreen.Overlaps(segScreen);
    }

    private Rect GetRectTransformScreenRect(RectTransform rt)
    {
        if (rt == null)
        {
            return new Rect();
        }

        Vector3[] corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        Camera cam = (uiCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
            ? null
            : (uiCanvas.worldCamera != null ? uiCanvas.worldCamera : Camera.main);

        Vector2 bl = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
        Vector2 tr = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);

        return new Rect(bl, tr - bl);
    }

    private IEnumerator PlayChildClear(SubSegment child, SegmentData sd)
    {
        if (child == null || child.rect == null)
        {
            yield break;
        }

        // animate shrink + optional fade
        float t = 0f;
        float dur = Mathf.Max(0.01f, clearAnimDuration);
        Vector3 startScale = child.rect.localScale;
        Image img = child.image;
        Color startColor = img != null ? img.color : Color.white;

        while (t < dur)
        {
            t += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float p = Mathf.Clamp01(t / dur);
            float eased = 1f - Mathf.Pow(1f - p, 3f);

            if (child.rect != null)
            {
                child.rect.localScale = Vector3.Lerp(startScale, Vector3.zero, eased);
            }

            if (fadeOnClear && img != null)
            {
                Color c = startColor;
                c.a = Mathf.Lerp(startColor.a, 0f, eased);
                img.color = c;
            }

            yield return null;
        }

        // remove child GameObject
        if (child.rect != null)
        {
            Destroy(child.rect.gameObject);
        }

        // remove entry and check completion
        if (sd != null)
        {
            sd.children.Remove(child);

            if (sd.children.Count == 0)
            {
                // Spawn deflect VFX
                if (deflectVfxPrefab != null && sd.projectile != null)
                {
                    var v = Instantiate(deflectVfxPrefab, sd.projectile.transform.position, Quaternion.identity);
                    if (deflectVfxLifetime > 0f)
                    {
                        Destroy(v, deflectVfxLifetime);
                    }

                    SoundManager.PlaySFX(SoundType.Clear, 0.6f);
                }

                if (sd.projectile != null)
                {
                    bool cleared = sd.projectile.ClearByGaze();

                    // 🔹 Notify listeners (GameFlowManager) that this projectile was cleared
                    if (cleared)
                    {
                        OnProjectileCleared?.Invoke(sd.projectile);
                    }
                }

                // destroy only the UI container (projectile removal handled by projectile.ClearByGaze)
                if (sd.container != null)
                {
                    Destroy(sd.container.gameObject);
                }

                // remove mapping (projectile may already be destroyed/pooled, but remove reference)
                if (sd.projectile != null && _active.ContainsKey(sd.projectile))
                {
                    _active.Remove(sd.projectile);
                }
            }
        }
    }

    private void SetCanvasGroupAlpha(CanvasGroup cg, float a)
    {
        if (cg == null)
        {
            return;
        }

        cg.alpha = Mathf.Clamp01(a);
    }

    private void ForceShowAllSegments()
    {
        foreach (var kv in _active)
        {
            var sd = kv.Value;
            if (sd == null || sd.children == null)
            {
                continue;
            }

            foreach (var child in sd.children)
            {
                if (child == null)
                {
                    continue;
                }

                if (child.rect != null && !child.rect.gameObject.activeInHierarchy)
                {
                    child.rect.gameObject.SetActive(true);
                }

                SetCanvasGroupAlpha(child.canvasGroup, 1f);
            }
        }
    }

    private void ForceHideAllSegments()
    {
        foreach (var kv in _active)
        {
            var sd = kv.Value;
            if (sd == null || sd.children == null)
            {
                continue;
            }

            foreach (var child in sd.children)
            {
                if (child == null)
                {
                    continue;
                }

                SetCanvasGroupAlpha(child.canvasGroup, 0f);
            }
        }
    }

    /// <summary>
    /// Hide the UI segment for a specific projectile (keeps the SegmentData entry so it can be re-shown later).
    /// Called when a projectile is disabled/returned-to-pool so its UI doesn't linger on screen.
    /// </summary>
    public void HideSegmentForProjectile(Projectile projectile)
    {
        if (projectile == null)
        {
            return;
        }

        if (!_active.TryGetValue(projectile, out var sd))
        {
            return;
        }

        if (sd == null)
        {
            return;
        }

        if (sd.container != null)
        {
            sd.container.gameObject.SetActive(false);
            return;
        }

        if (sd.children != null)
        {
            foreach (var sub in sd.children)
            {
                if (sub == null)
                {
                    continue;
                }

                if (sub.canvasGroup != null)
                {
                    sub.canvasGroup.alpha = 0f;
                }

                if (sub.image != null)
                {
                    sub.image.enabled = false;
                }
            }
        }
    }

    /// <summary>
    /// Show the UI segment for a specific projectile (reveal the pre-existing UI container).
    /// Visibility follows canvasOnlyWhileQ: if canvasOnlyWhileQ is true, it will only show when Q is currently held,
    /// otherwise it shows immediately.
    /// </summary>
    public void ShowSegmentForProjectile(Projectile projectile)
    {
        if (projectile == null)
        {
            return;
        }

        if (!_active.TryGetValue(projectile, out var sd))
        {
            return;
        }

        if (sd == null)
        {
            return;
        }

        if (sd.container != null)
        {
            sd.container.gameObject.SetActive(true);

            bool qNow = Input.GetKey(KeyCode.Q);
            float alpha = (!canvasOnlyWhileQ || qNow) ? 1f : 0f;

            var cg = sd.container.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.alpha = alpha;
            }
            else
            {
                if (sd.children != null)
                {
                    foreach (var sub in sd.children)
                    {
                        if (sub == null)
                        {
                            continue;
                        }

                        if (sub.canvasGroup != null)
                        {
                            sub.canvasGroup.alpha = alpha;
                        }

                        if (sub.image != null)
                        {
                            sub.image.enabled = (alpha > 0f);
                        }
                    }
                }
            }

            return;
        }

        if (sd.children != null)
        {
            bool qNow = Input.GetKey(KeyCode.Q);
            float alpha = (!canvasOnlyWhileQ || qNow) ? 1f : 0f;

            foreach (var sub in sd.children)
            {
                if (sub == null)
                {
                    continue;
                }

                if (sub.canvasGroup != null)
                {
                    sub.canvasGroup.alpha = alpha;
                }

                if (sub.image != null)
                {
                    sub.image.enabled = (alpha > 0f);
                }
            }
        }
    }

    /// <summary>
    /// Convenience: toggle visible or hidden for the given SegmentData.
    /// </summary>
    private void SetSegmentVisible(SegmentData sd, bool visible)
    {
        if (sd == null)
        {
            return;
        }

        if (sd.container != null)
        {
            sd.container.gameObject.SetActive(visible);
            var cg = sd.container.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.alpha = visible ? 1f : 0f;
            }

            return;
        }

        if (sd.children != null)
        {
            foreach (var c in sd.children)
            {
                if (c == null)
                {
                    continue;
                }

                if (c.canvasGroup != null)
                {
                    c.canvasGroup.alpha = visible ? 1f : 0f;
                }

                if (c.image != null)
                {
                    c.image.enabled = visible;
                }
            }
        }
    }
}

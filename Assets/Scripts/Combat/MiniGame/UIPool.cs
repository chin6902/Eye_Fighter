using System.Collections.Generic;
using UnityEngine;

public class UIPool : MonoBehaviour
{
    [Tooltip("Prefab used for pooled UI segments (must be a UI GameObject with RectTransform).")]
    public GameObject prefab;

    [Tooltip("Parent used to store pooled objects. If null, this GameObject is used.")]
    public Transform parent;

    [Tooltip("Maximum number of pooled instances. Beyond this, released objects will be destroyed.")]
    [SerializeField] private int maxPoolSize = 256;

    private readonly Queue<GameObject> _pool = new Queue<GameObject>();
    private readonly HashSet<GameObject> _inPool = new HashSet<GameObject>();

    private void Awake()
    {
        if (parent == null)
        {
            parent = this.transform;
        }
    }

    // Backward compatible: no-arg Get()
    public GameObject Get()
    {
        return Get(null);
    }

    /// <summary>
    /// Get an instance from the pool. Parent will be set to useParent (if not null) or pool.parent.
    /// </summary>
    public GameObject Get(Transform useParent)
    {
        if (prefab == null)
        {
            Debug.LogWarning($"[UIPool] No prefab assigned on {name}.");
            return null;
        }

        Transform finalParent = useParent != null ? useParent : parent;
        GameObject go = null;

        // pull first non-null object from pool
        while (_pool.Count > 0 && go == null)
        {
            var candidate = _pool.Dequeue();
            if (candidate == null)
            {
                continue;
            }

            _inPool.Remove(candidate);
            go = candidate;
        }

        if (go == null)
        {
            go = Instantiate(prefab, finalParent);
        }
        else
        {
            if (finalParent != null)
            {
                go.transform.SetParent(finalParent, false);
            }
        }

        go.SetActive(true);
        return go;
    }

    public void Release(GameObject go)
    {
        if (go == null)
        {
            return;
        }

        // already in pool → ignore
        if (_inPool.Contains(go))
        {
            return;
        }

        // if pool object is disabled/destroyed, just destroy the released object
        if (!isActiveAndEnabled || parent == null)
        {
            Destroy(go);
            return;
        }

        // avoid infinite growth
        if (_pool.Count >= maxPoolSize)
        {
            Destroy(go);
            return;
        }

        go.SetActive(false);
        go.transform.SetParent(parent, false);

        _pool.Enqueue(go);
        _inPool.Add(go);
    }

    /// <summary>
    /// Deactivate everything currently in the pool and re-parent under pool parent.
    /// Does not clear the pool contents.
    /// </summary>
    public void ReleaseAll()
    {
        foreach (var g in _pool)
        {
            if (g == null)
            {
                continue;
            }

            g.SetActive(false);

            if (parent != null)
            {
                g.transform.SetParent(parent, false);
            }
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewElementDatabase", menuName = "Elements/Database")]
public class ElementDatabase : ScriptableObject
{
    public static ElementDatabase Instance { get; private set; }

    public static void Init(ElementDatabase db)
    {
        Instance = db;
    }

    [Tooltip("Drag your three ElementData assets here: Fire, Electric, Water")]
    public List<ElementData> allElements;

    private Dictionary<GameManager.ElementType, ElementData> _map;

    private void OnEnable()
    {
        if (Instance == null)
        {
            Instance = this;
        }

        // build lookup
        _map = new Dictionary<GameManager.ElementType, ElementData>();
        foreach (var data in allElements)
        {
            if (!_map.ContainsKey(data.elementType))
            {
                _map.Add(data.elementType, data);
            }
        }
    }

    /// <summary>
    /// Returns the ElementData for any of your three elements.
    /// </summary>
    public ElementData Get(GameManager.ElementType type)
    {
        if (_map == null)
        {
            OnEnable();
        }

        _map.TryGetValue(type, out var data);
        return data;
    }
}

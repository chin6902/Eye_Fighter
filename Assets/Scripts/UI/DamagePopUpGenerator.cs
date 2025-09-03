using TMPro;
using UnityEngine;

public class DamagePopUpGenerator : MonoBehaviour
{
    [Tooltip("Prefab must have a RectTransform + TextMeshProUGUI child")]
    public GameObject damagePopUpPrefab;

    private Canvas _canvas;

    private void Awake()
    {
        _canvas = GetComponent<Canvas>();
    }

    public void CreatePopUp(Vector3 worldPosition, string text, Color color)
    {
        var popUp = Instantiate(damagePopUpPrefab, worldPosition, Quaternion.identity, transform);
        var rt = popUp.GetComponent<RectTransform>();

        if (_canvas.renderMode != RenderMode.WorldSpace)
        {
            Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPosition);
            rt.position = screenPos;
        }

        var tmp = popUp.GetComponentInChildren<TextMeshProUGUI>();
        tmp.text = text;

        color.a = 1f;
        tmp.color = color;

        Destroy(popUp, 1f);
    }
}

using TMPro;
using UnityEngine;

public class DamagePopUpAnimation : MonoBehaviour
{
    public AnimationCurve opacityCurve;
    public AnimationCurve scaleCurve;
    public AnimationCurve heightCurve;

    private TextMeshProUGUI textMesh;
    private Color baseColor;
    private float time = 0f;
    private Vector3 origin;

    private void Awake()
    {
        textMesh = transform.GetChild(0).GetComponent<TextMeshProUGUI>();
        origin = transform.position;
    }

    private void Start()
    {
        baseColor = textMesh.color;
    }

    private void Update()
    {
        float a = opacityCurve.Evaluate(time);
        textMesh.color = new Color(baseColor.r, baseColor.g, baseColor.b, a);

        transform.localScale = Vector3.one * scaleCurve.Evaluate(time);
        transform.position = origin + new Vector3(0, heightCurve.Evaluate(time), 0);

        time += Time.deltaTime * 1.5f;
    }
}

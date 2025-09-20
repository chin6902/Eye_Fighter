using UnityEngine;

[CreateAssetMenu(fileName = "NewElementData", menuName = "Elements/Element Data")]
public class ElementData : ScriptableObject
{
    public GameManager.ElementType elementType;
    public Sprite icon;
    public Color popupColor;
    public GameObject hitEffect;
    public GameObject deadEffect;
    public GameObject chargedHitEffect;
    public GameObject barrierBreakEffect;

    public AudioClip HitSFX;
}

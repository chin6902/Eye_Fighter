using UnityEngine;

[RequireComponent(typeof(SphereCollider))]
public class RestrictedAreaController : MonoBehaviour
{
    public float areaRadius = 5f;
    public float softInnerRadius => areaRadius * 0.9f;


    private SphereCollider col;

    private void Awake()
    {
        col = GetComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = areaRadius;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Wall"))
        {
            Vector3 away = transform.position - other.ClosestPoint(transform.position);
            away.y = 0f;
            away.Normalize();

            float randomAngle = Random.Range(-45f, 45f);
            away = Quaternion.Euler(0, randomAngle, 0) * away;

            transform.position += away * 0.1f;
        }
    }


    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, areaRadius);
    }
}

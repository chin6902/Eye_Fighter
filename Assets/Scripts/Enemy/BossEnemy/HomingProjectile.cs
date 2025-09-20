using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class HomingProjectile : MonoBehaviour
{
    public Transform target;
    public float speed = 8f;
    public float homingStrength = 5f;
    public Transform lookAtSource; // optional for rotation reference

    private Rigidbody rb;

    public void Initialize(Transform target, float speed, float homingStrength, Transform lookAtSource = null)
    {
        this.target = target;
        this.speed = speed;
        this.homingStrength = homingStrength;
        this.lookAtSource = lookAtSource;
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
    }

    private void Update()
    {
        if (target == null)
        {
            // fallback: move forward
            transform.position += transform.forward * speed * Time.deltaTime;
            return;
        }

        Vector3 dir = (target.position - transform.position).normalized;
        // adjust forward direction gradually
        Vector3 newDir = Vector3.RotateTowards(transform.forward, dir, homingStrength * Time.deltaTime, 0f);
        transform.rotation = Quaternion.LookRotation(newDir);
        transform.position += newDir * speed * Time.deltaTime;
    }

    private void OnTriggerEnter(Collider other)
    {
        // handle impact - deliver damage or call barrier/health receive methods here:
        // Example:
        var bs = other.GetComponent<BarrierSpot>();
        if (bs != null)
        {
            // assume the boss attack uses a specific element type - here we call ReceiveElementalDamage with some accuracy
            // You will likely call this with correct element & accuracy from the boss controller or set fields on the missile.
            bs.ReceiveElementalDamage(GameManager.Instance.selectedElement, 1f);
            Destroy(gameObject);
            return;
        }

        var h = other.GetComponent<Health>();
        if (h != null)
        {
            h.ReceiveElementalDamage(GameManager.Instance.selectedElement, 1f);
            Destroy(gameObject);
            return;
        }

        // otherwise destroy on environment collision
        Destroy(gameObject);
    }
}

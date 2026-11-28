using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// Hace que un objeto (canasta, botella, vaso) no se voltee tan fácil:
///  - baja el centro de masa cerca de la base
///  - mientras está suelto, aplica un pequeño giro que lo devuelve a estar derecho (como un muñeco porfiado)
///  - amortigua el bamboleo hacia los lados (el giro sobre el eje vertical queda libre)
/// Mientras lo sostiene la mano o un socket no hace nada.
[RequireComponent(typeof(Rigidbody))]
public class AntiVuelco : MonoBehaviour
{
    [Tooltip("Qué tan fuerte se endereza (aceleración angular). 0 = solo baja el centro de masa")]
    public float fuerzaEnderezar = 10f;
    [Tooltip("Amortiguación del bamboleo hacia los lados")]
    public float amortiguacion = 2f;
    [Tooltip("Altura del centro de masa, como fracción de la altura del objeto desde la base (0 = en la base)")]
    [Range(0f, 0.5f)] public float alturaCentroDeMasa = 0.1f;

    Rigidbody rb;
    XRGrabInteractable grab;
    Vector3 localUp = Vector3.up;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        grab = GetComponent<XRGrabInteractable>();

        // Eje local que apunta hacia arriba ahora (el objeto empieza derecho en la escena)
        localUp = NearestAxis(transform.InverseTransformDirection(Vector3.up));

        // Centro de masa abajo, a partir de los colliders sólidos del objeto
        bool any = false; Bounds b = default;
        foreach (var c in GetComponentsInChildren<Collider>())
        {
            if (c.isTrigger || c.attachedRigidbody != rb) continue;
            if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
        }
        if (any)
        {
            Vector3 low = new Vector3(b.center.x, b.min.y + b.size.y * alturaCentroDeMasa, b.center.z);
            rb.centerOfMass = Quaternion.Inverse(transform.rotation) * (low - transform.position);
        }
        rb.angularDamping = Mathf.Max(rb.angularDamping, 0.5f);
    }

    void FixedUpdate()
    {
        if (rb == null || rb.isKinematic || fuerzaEnderezar <= 0f) return;
        if (grab != null && grab.isSelected) return;

        Vector3 up = transform.TransformDirection(localUp);
        Vector3 axis = Vector3.Cross(up, Vector3.up);                       // hacia dónde girar para quedar derecho
        Vector3 av = rb.angularVelocity;
        Vector3 sideways = av - Vector3.Project(av, Vector3.up);            // bamboleo (no el giro vertical)
        rb.AddTorque(axis * fuerzaEnderezar - sideways * amortiguacion, ForceMode.Acceleration);
    }

    static Vector3 NearestAxis(Vector3 v)
    {
        Vector3 a = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        if (a.x >= a.y && a.x >= a.z) return new Vector3(Mathf.Sign(v.x), 0, 0);
        if (a.y >= a.z) return new Vector3(0, Mathf.Sign(v.y), 0);
        return new Vector3(0, 0, Mathf.Sign(v.z));
    }
}

using UnityEngine;

/// Devuelve el átomo a su punto de aparición SOLO si se cae del escenario
/// (queda más de 'fallDistance' metros por debajo de donde apareció).
/// Antes usaba una altura fija (minY = -0.5), pero el piso del laboratorio está cerca de y = -4.8:
/// todos los átomos quedaban "por debajo" y se teletransportaban cada frame (se veían flotando y,
/// al soltarlos, volvían a aparecer en el aire). Ahora la referencia es la altura de aparición.
public class AtomRespawn : MonoBehaviour
{
    [Tooltip("Solo si 'Use Absolute Min Y' está marcado: altura mínima absoluta del mundo")]
    public float minY = -0.5f;
    public bool useAbsoluteMinY = false;
    [Tooltip("Metros por debajo del punto de aparición a partir de los cuales se considera que se cayó del escenario")]
    public float fallDistance = 3f;
    [Tooltip("Velocidad máxima con la que se separan piezas que aparecen encimadas (evita que salgan disparadas)")]
    public float maxDepenetrationVelocity = 1f;

    Vector3 startPos; Quaternion startRot; Rigidbody rb; Atom atom;

    void Awake()
    {
        startPos = transform.position; startRot = transform.rotation;
        rb = GetComponent<Rigidbody>(); atom = GetComponent<Atom>();
        if (rb != null) rb.maxDepenetrationVelocity = maxDepenetrationVelocity;
    }

    void Update()
    {
        float limit = useAbsoluteMinY ? minY : startPos.y - fallDistance;
        if (transform.position.y > limit) return;
        if (atom != null && (atom.HeldBy != null || atom.BondCount > 0)) return;   // es parte de una molécula: no se separa

        if (rb != null && !rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;      // Unity 2022.3: rb.velocity
            rb.angularVelocity = Vector3.zero;
        }
        transform.SetPositionAndRotation(startPos, startRot);
    }
}

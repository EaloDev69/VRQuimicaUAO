using UnityEngine;

<<<<<<< HEAD
/// Red de seguridad: si un átomo cae al vacío vuelve a su punto de origen.
/// Si forma parte de una molécula, se devuelve la molécula entera. No actúa mientras está en la mano.
public class AtomRespawn : MonoBehaviour
{
    public float minY = -0.5f;
    Vector3 startPos;
    Atom atom;

    void Awake()
    {
        startPos = transform.position;
        atom = GetComponent<Atom>();
    }

    void Update()
    {
        if (transform.position.y > minY) return;
        if (atom != null && (atom.HeldBy != null || Molecule.IsPieceHeld(atom))) return;

        // Se mueve la molécula (o el átomo suelto) para que este átomo vuelva a su sitio
        Molecule.MovePiece(atom, startPos - transform.position);
=======
public class AtomRespawn : MonoBehaviour
{
    public float minY = -0.5f;
    Vector3 startPos; Quaternion startRot; Rigidbody rb; Atom atom;

    void Awake() { startPos = transform.position; startRot = transform.rotation;
        rb = GetComponent<Rigidbody>(); atom = GetComponent<Atom>(); }

    void Update()
    {
        if (transform.position.y > minY || atom.HeldBy != null) return;
        rb.linearVelocity = Vector3.zero;      // Unity 2022.3: rb.velocity
        rb.angularVelocity = Vector3.zero;
        transform.SetPositionAndRotation(startPos, startRot);
>>>>>>> 3f517e58c67353504dd60bb8d6a87b6eeba73bd3
    }
}

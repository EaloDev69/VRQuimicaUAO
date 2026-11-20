using UnityEngine;

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
    }
}

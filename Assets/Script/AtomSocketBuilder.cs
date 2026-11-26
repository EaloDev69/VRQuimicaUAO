using UnityEngine;

[RequireComponent(typeof(Atom))]
public class AtomSocketBuilder : MonoBehaviour
{
    [Header("Qué enlaces permite este átomo")]
    public bool createDirectSockets = true;   // plan B
    public bool createBarPorts = true;        // esferas y barras

    [Header("Enlace directo")]
    public float bondDistance = 0.1f;
    public float socketRadius = 0.04f;

    [Header("Puertos de barra")]
    public float portRadius = 0.02f;

    static readonly Vector3[] dirs =
    {
        new Vector3( 1,  1,  1), new Vector3( 1, -1, -1),
        new Vector3(-1,  1, -1), new Vector3(-1, -1,  1)
    };

    void Awake()
    {
        var atom = GetComponent<Atom>();
        int v = atom.data.valence;

        if (createDirectSockets)
        {
            atom.Sockets = new BondSocket[v];
            for (int i = 0; i < v; i++)
            {
                var go = new GameObject($"BondSocket_{i}");
                go.layer = gameObject.layer;
                go.transform.SetParent(transform, false);
                go.transform.localPosition = dirs[i].normalized * bondDistance;

                var col = go.AddComponent<SphereCollider>();
                col.isTrigger = true;
                col.radius = socketRadius;

                var socket = go.AddComponent<BondSocket>();
                socket.showInteractableHoverMeshes = false;
                atom.Sockets[i] = socket;
            }
        }

        if (createBarPorts)
        {
            atom.Ports = new BarPort[v];
            for (int i = 0; i < v; i++)
            {
                var go = new GameObject($"BarPort_{i}");
                go.layer = gameObject.layer;
                go.transform.SetParent(transform, false);
                go.transform.localPosition = dirs[i].normalized * atom.atomRadius;

                var col = go.AddComponent<SphereCollider>();
                col.isTrigger = true;
                col.radius = portRadius;

                var port = go.AddComponent<BarPort>();
                port.Owner = atom;
                atom.Ports[i] = port;
            }
        }
    }
}
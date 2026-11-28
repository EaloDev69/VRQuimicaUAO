using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class Atom : MonoBehaviour
{
    public AtomData data;
    [SerializeField] Renderer rend;

    [Header("Geometría")]
    public float atomRadius = 0.05f;      // igual al radio del Sphere Collider

    public BondSocket HeldBy { get; set; }
    public BondSocket[] Sockets { get; set; } = new BondSocket[0];
    public BarPort[] Ports { get; set; } = new BarPort[0];
    public List<Bond> Bars { get; } = new List<Bond>();   // barras completas (ambos extremos)

    public event Action<Atom> BondsChanged;

    int DirectCount => Sockets.Count(s => s != null && s.Held != null) + (HeldBy != null ? 1 : 0);
    int OccupiedPorts => Ports.Count(p => p != null && !p.IsFree);

    // Enlaces reales (para el grafo y el validador)
    public int BondCount => DirectCount + Bars.Count;
    // Valencia ya "reservada" (incluye barras con un solo extremo puesto)
    public bool HasFreeValence => DirectCount + OccupiedPorts < data.valence;

    public Atom Root
    {
        get { var a = this; while (a.HeldBy != null) a = a.HeldBy.Owner; return a; }
    }

    void Awake()
    {
        var body = GetComponent<Rigidbody>();
        if (body != null) body.solverIterations = Mathf.Max(body.solverIterations, 20);   // uniones más firmes


        if (rend == null) rend = GetComponentInChildren<Renderer>();
        if (rend != null && data != null)
        {
            var mat = rend.material;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", data.color);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color", data.color);
        }
    }

    // La molécula como una sola pieza ahora la maneja Molecule (jerarquía con raíz en la primera barra).

    public void NotifyBondsChanged()
    {
        Debug.Log($"{data.symbol} ({name}): enlaces {BondCount}/{data.valence}");
        BondsChanged?.Invoke(this);
    }
}
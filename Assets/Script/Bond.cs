<<<<<<< HEAD
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;   // XRI 2.x: borrar esta línea

/// Barra de enlace. Al acercarse a un átomo con valencia libre se engancha sola y las dos piezas pasan a ser
/// UNA molécula (un solo objeto). Ya no hay FixedJoint: la unión la hace Molecule.Rebuild().
=======
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;               // XRI 2.x: esta es la que importa
using UnityEngine.XR.Interaction.Toolkit.Interactables; // XRI 2.x: borrar esta línea

[RequireComponent(typeof(Rigidbody))]
>>>>>>> 3f517e58c67353504dd60bb8d6a87b6eeba73bd3
public class Bond : MonoBehaviour
{
    public float plugSearchRadius = 0.06f;
    public float checkInterval = 0.05f;
    [Tooltip("Si ambas piezas están sostenidas, solo se enlaza si la separación es menor a esto")]
    public float snapTolerance = 0.02f;

    [Header("Ajuste automático a la malla")]
    public bool autoFitPlugs = true;
    public float tipPadding = 0.002f;   // pequeño margen para que la esfera no se incruste

    public BarPlug PlugA { get; private set; }
    public BarPlug PlugB { get; private set; }

<<<<<<< HEAD
=======
    XRGrabInteractable grab;
    Rigidbody rb;
>>>>>>> 3f517e58c67353504dd60bb8d6a87b6eeba73bd3
    float nextCheck;

    public bool IsComplete => PlugA.Port != null && PlugB.Port != null;
    public Atom AtomA => PlugA.Port != null ? PlugA.Port.Owner : null;
    public Atom AtomB => PlugB.Port != null ? PlugB.Port.Owner : null;

<<<<<<< HEAD
=======
    // ------------------------------------------------------------------
    // Conjunto de átomos y barras conectados entre sí (se mueven como un bloque)
    // ------------------------------------------------------------------
    class Cluster
    {
        public readonly HashSet<Atom> atoms = new HashSet<Atom>();
        public readonly HashSet<Bond> bars = new HashSet<Bond>();

        public Cluster(Atom startAtom, Bond startBar)
        {
            var pendingAtoms = new Stack<Atom>();
            var pendingBars = new Stack<Bond>();
            if (startAtom != null) pendingAtoms.Push(startAtom);
            if (startBar != null) pendingBars.Push(startBar);

            while (pendingAtoms.Count > 0 || pendingBars.Count > 0)
            {
                while (pendingAtoms.Count > 0)
                {
                    var a = pendingAtoms.Pop();
                    if (!atoms.Add(a)) continue;
                    foreach (var p in a.Ports)
                        if (p != null && p.Plug != null && p.Plug.bond != null)
                            pendingBars.Push(p.Plug.bond);
                }
                while (pendingBars.Count > 0)
                {
                    var b = pendingBars.Pop();
                    if (!bars.Add(b)) continue;
                    if (b.PlugA.Port != null) pendingAtoms.Push(b.PlugA.Port.Owner);
                    if (b.PlugB.Port != null) pendingAtoms.Push(b.PlugB.Port.Owner);
                }
            }
        }

        public bool IsHeld()
        {
            foreach (var a in atoms)
            {
                var g = a.GetComponent<XRGrabInteractable>();
                if (g != null && g.isSelected) return true;
            }
            foreach (var b in bars)
                if (b.grab != null && b.grab.isSelected) return true;
            return false;
        }

        public void Move(Vector3 delta)
        {
            foreach (var a in atoms) Shift(a.transform, a.GetComponent<Rigidbody>(), delta);
            foreach (var b in bars) Shift(b.transform, b.rb, delta);
        }

        static void Shift(Transform t, Rigidbody body, Vector3 delta)
        {
            t.position += delta;
            if (body != null && !body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;   // Unity 2022.3: velocity
                body.angularVelocity = Vector3.zero;
            }
        }
    }

    // ------------------------------------------------------------------

>>>>>>> 3f517e58c67353504dd60bb8d6a87b6eeba73bd3
    void Awake()
    {
        var plugs = GetComponentsInChildren<BarPlug>();
        PlugA = plugs[0]; PlugB = plugs[1];
        PlugA.bond = this; PlugB.bond = this;

        if (autoFitPlugs) FitPlugsToMesh();

<<<<<<< HEAD
        PieceBody.Ensure(gameObject);   // guarda el Rigidbody y el agarre del prefab para poder recuperarlos al separarse
=======
        rb = GetComponent<Rigidbody>();
        grab = GetComponent<XRGrabInteractable>();
        // Agarrar la barra la desengancha de los átomos
        grab.selectEntered.AddListener(_ => DetachAll());
>>>>>>> 3f517e58c67353504dd60bb8d6a87b6eeba73bd3
    }

    // Mide la malla de la barra, detecta su eje largo y pone los plugs justo en las puntas
    void FitPlugsToMesh()
    {
        Vector3 min = Vector3.one * float.MaxValue;
        Vector3 max = Vector3.one * float.MinValue;
        bool found = false;

        foreach (var mf in GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var b = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3(
                    (i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 local = transform.InverseTransformPoint(mf.transform.TransformPoint(corner));
                min = Vector3.Min(min, local);
                max = Vector3.Max(max, local);
                found = true;
            }
        }
        if (!found) return;

        Vector3 span = max - min;
        int axis = 0;
        if (span.y > span[axis]) axis = 1;
        if (span.z > span[axis]) axis = 2;

        float half = span[axis] * 0.5f + tipPadding;
        Vector3 dir = Vector3.zero; dir[axis] = 1f;
        Vector3 center = (min + max) * 0.5f;

        PlugA.transform.localPosition = center + dir * half;
        PlugB.transform.localPosition = center - dir * half;

        var cap = GetComponent<CapsuleCollider>();
        if (cap != null) { cap.direction = axis; cap.height = span[axis]; cap.center = center; }
    }

    void Update()
    {
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + checkInterval;
        if (PlugA.IsFree || PlugB.IsFree) TryAttachFreePlugs();
    }

    public void TryAttachFreePlugs()
    {
        foreach (var plug in new[] { PlugA, PlugB })
        {
            if (!plug.IsFree) continue;

            var other = plug == PlugA ? PlugB : PlugA;
            Atom exclude = other.IsFree ? null : other.Port.Owner;

            var port = BarPort.FindFor(plug.transform.position, plugSearchRadius, exclude);

            if (port == null) { plug.needsLeave = false; continue; }  // ya se alejó
            if (plug.needsLeave) continue;                            // espera a alejarse

            if (TryAttach(plug, port))
                Debug.Log($"[Bond] {plug.name} -> {port.Owner.name}: ENLAZADO");
        }
    }

    public bool TryAttach(BarPlug plug, BarPort port)
    {
        if (!plug.IsFree || !port.IsFree) return false;

        var atom = port.Owner;
        if (!atom.HasFreeValence) return false;

        var other = plug == PlugA ? PlugB : PlugA;
        if (!other.IsFree && other.Port.Owner == atom) return false; // no unir un átomo consigo mismo

<<<<<<< HEAD
        // Una molécula bloqueada (la está procesando la transmutadora) no admite piezas nuevas
        if (Molecule.IsPieceLocked(atom) || Molecule.IsPieceLocked(this)) return false;

=======
>>>>>>> 3f517e58c67353504dd60bb8d6a87b6eeba73bd3
        // Dónde debe quedar el centro del átomo: pegado a la punta, hacia afuera de la barra
        Vector3 outward = (plug.transform.position - other.transform.position).normalized;
        Vector3 target = plug.transform.position + outward * atom.atomRadius;
        float gap = Vector3.Distance(atom.transform.position, target);

<<<<<<< HEAD
        var atomMol = Molecule.Of(atom);
        bool sameMolecule = atomMol != null && atomMol == Molecule.Of(this);   // cierre de anillo
        bool atomHeld = Molecule.IsPieceHeld(atom);
        bool barHeld = Molecule.IsPieceHeld(this);

        if (sameMolecule || (atomHeld && barHeld))
=======
        var atomCluster = new Cluster(atom, null);
        var barCluster = new Cluster(null, this);

        bool sameCluster = atomCluster.atoms.Overlaps(barCluster.atoms);   // cierre de anillo
        bool atomHeld = atomCluster.IsHeld();
        bool barHeld = barCluster.IsHeld();

        if (sameCluster || (atomHeld && barHeld))
>>>>>>> 3f517e58c67353504dd60bb8d6a87b6eeba73bd3
        {
            // No se puede mover nada: solo enlaza si ya están prácticamente en su sitio
            if (gap > snapTolerance) return false;
        }
        else if (!atomHeld)
        {
<<<<<<< HEAD
            Molecule.MovePiece(atom, target - atom.transform.position);        // el átomo (y lo que lleva) va a la barra
        }
        else
        {
            Molecule.MovePiece(this, atom.transform.position - target);        // la barra (y lo que lleva) va al átomo
=======
            atomCluster.Move(target - atom.transform.position);        // el átomo (y lo que lleva) va a la barra
        }
        else
        {
            barCluster.Move(atom.transform.position - target);         // la barra (y lo que lleva) va al átomo
>>>>>>> 3f517e58c67353504dd60bb8d6a87b6eeba73bd3
        }

        port.transform.position = plug.transform.position;
        Physics.SyncTransforms();

<<<<<<< HEAD
        plug.Port = port; port.Plug = plug;
=======
        var joint = gameObject.AddComponent<FixedJoint>();
        joint.connectedBody = atom.GetComponent<Rigidbody>();
        joint.enableCollision = false;

        plug.Port = port; plug.Joint = joint; port.Plug = plug;
        IgnoreWith(atom, true);
        IgnoreBarsAt(atom, true);
>>>>>>> 3f517e58c67353504dd60bb8d6a87b6eeba73bd3

        if (IsComplete)
        {
            AtomA.Bars.Add(this);
            AtomB.Bars.Add(this);
            AtomB.NotifyBondsChanged();
        }
        atom.NotifyBondsChanged();
<<<<<<< HEAD

        Molecule.Rebuild(new Component[] { atom, this });                    // las dos piezas pasan a ser una molécula
        return true;
    }

    /// Suelta un extremo de la barra (sin reconstruir). Lo usa DetachAll y Atom.DetachFromBars.
    public void ReleasePlug(BarPlug plug)
    {
        if (plug.IsFree) return;

        var a = AtomA; var b = AtomB; bool complete = IsComplete;
        var owner = plug.Port.Owner;

        plug.Port.Plug = null; plug.Port = null; plug.Joint = null;
        plug.needsLeave = true;                 // no reenganchar hasta alejarse

        if (complete) { a.Bars.Remove(this); b.Bars.Remove(this); }
        owner.NotifyBondsChanged();
    }

    /// Suelta los dos extremos: la barra sale de la molécula
    public void DetachAll(IXRSelectInteractor pinInteractor = null)
    {
        var atomA = AtomA; var atomB = AtomB;
        if (atomA == null && atomB == null) return;

        ReleasePlug(PlugA);
        ReleasePlug(PlugB);

        var touched = new System.Collections.Generic.List<Component> { this };
        if (atomA != null) touched.Add(atomA);
        if (atomB != null) touched.Add(atomB);
        Molecule.Rebuild(touched.ToArray(), this, pinInteractor);
    }
}
=======
        return true;
    }

    public void DetachAll()
    {
        var a = AtomA; var b = AtomB; bool complete = IsComplete;

        foreach (var p in new[] { PlugA, PlugB })
        {
            if (p.IsFree) continue;
            var atom = p.Port.Owner;
            if (p.Joint != null) Destroy(p.Joint);
            p.Port.Plug = null; p.Port = null; p.Joint = null;
            p.needsLeave = true;                 // no reenganchar hasta alejarse
            IgnoreWith(atom, false);
            IgnoreBarsAt(atom, false);
        }

        if (complete) { a.Bars.Remove(this); b.Bars.Remove(this); }
        if (a != null) a.NotifyBondsChanged();
        if (b != null) b.NotifyBondsChanged();
    }

    // Las barras que comparten átomo no deben colisionar entre sí
    void IgnoreBarsAt(Atom atom, bool ignore)
    {
        foreach (var p in atom.Ports)
        {
            if (p == null || p.Plug == null || p.Plug.bond == null || p.Plug.bond == this) continue;
            var other = p.Plug.bond;
            foreach (var c in GetComponentsInChildren<Collider>())
                foreach (var oc in other.GetComponentsInChildren<Collider>())
                    if (c != oc) Physics.IgnoreCollision(c, oc, ignore);
        }
    }

    void IgnoreWith(Atom atom, bool ignore)
    {
        foreach (var c in GetComponentsInChildren<Collider>())
            foreach (var ac in atom.GetComponentsInChildren<Collider>())
                if (c != ac) Physics.IgnoreCollision(c, ac, ignore);
    }
}
>>>>>>> 3f517e58c67353504dd60bb8d6a87b6eeba73bd3

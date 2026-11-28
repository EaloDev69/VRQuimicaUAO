using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;               // XRI 2.x: esta es la que importa
using UnityEngine.XR.Interaction.Toolkit.Interactables; // XRI 2.x: borrar esta línea

[RequireComponent(typeof(Rigidbody))]
public class Bond : MonoBehaviour
{
    // Para el monitor de pruebas: cada vez que una punta de barra se une o se separa de un átomo
    public static event System.Action<Bond, Atom> AnyAttached;
    public static event System.Action<Bond> AnyDetached;

    public float plugSearchRadius = 0.06f;
    public float checkInterval = 0.05f;
    [Tooltip("Si ambas piezas están sostenidas, solo se enlaza si la separación es menor a esto")]
    public float snapTolerance = 0.02f;
    [Tooltip("Átomo en una mano y barra en la otra: distancia a la que se enlazan")]
    public float heldSnapDistance = 0.07f;

    [Header("Ajuste automático a la malla")]
    public bool autoFitPlugs = true;
    public float tipPadding = 0.002f;   // pequeño margen para que la esfera no se incruste

    public BarPlug PlugA { get; private set; }
    public BarPlug PlugB { get; private set; }

    XRGrabInteractable grab;
    Rigidbody rb;
    float nextCheck;

    public bool IsComplete => PlugA.Port != null && PlugB.Port != null;
    public Atom AtomA => PlugA.Port != null ? PlugA.Port.Owner : null;
    public Atom AtomB => PlugB.Port != null ? PlugB.Port.Owner : null;

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
            // Solo las piezas de nivel superior: las hijas de una molécula se mueven con su raíz
            var all = new List<Transform>();
            foreach (var a in atoms) all.Add(a.transform);
            foreach (var b in bars) all.Add(b.transform);
            foreach (var t in Molecule.TopLevel(all)) Shift(t, t.GetComponent<Rigidbody>(), delta);
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

    void Awake()
    {
        var plugs = GetComponentsInChildren<BarPlug>();
        PlugA = plugs[0]; PlugB = plugs[1];
        PlugA.bond = this; PlugB.bond = this;

        if (autoFitPlugs) FitPlugsToMesh();

        rb = GetComponent<Rigidbody>();
        rb.maxDepenetrationVelocity = 1f;
        rb.solverIterations = Mathf.Max(rb.solverIterations, 20);   // uniones más firmes   // si aparece encimada con otra barra, se separa suave (no sale disparada)
        grab = GetComponent<XRGrabInteractable>();
        // Agarrar la barra la desengancha de los átomos
        // (Antes agarrar la barra la soltaba de sus átomos. Ahora la molécula es una sola pieza y se separa
        //  con el gatillo superior del control: ver Molecule.)
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

            // Solo se arma a propósito: la barra o el átomo tienen que estar en la mano.
            // (Así las piezas que están sueltas juntas en una canasta no se pegan solas.)
            if (!new Cluster(null, this).IsHeld() && !new Cluster(port.Owner, null).IsHeld()) continue;

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

        // Dónde debe quedar el centro del átomo: pegado a la punta, hacia afuera de la barra
        Vector3 outward = (plug.transform.position - other.transform.position).normalized;
        Vector3 target = plug.transform.position + outward * atom.atomRadius;
        float gap = Vector3.Distance(atom.transform.position, target);

        var atomCluster = new Cluster(atom, null);
        var barCluster = new Cluster(null, this);

        bool sameCluster = atomCluster.atoms.Overlaps(barCluster.atoms);   // cierre de anillo
        bool atomHeld = atomCluster.IsHeld();
        bool barHeld = barCluster.IsHeld();

        if (sameCluster)
        {
            // Cierre de anillo: no se puede mover nada, solo enlaza si ya están prácticamente en su sitio
            if (gap > snapTolerance) return false;
        }
        else if (atomHeld && barHeld)
        {
            // Átomo en una mano y barra en la otra: basta con acercarlas. La unión se crea ya alineada;
            // al soltar cualquiera de las dos, la física la lleva a su sitio.
            if (gap > heldSnapDistance) return false;
            barCluster.Move(atom.transform.position - target);
        }
        else if (!atomHeld)
        {
            atomCluster.Move(target - atom.transform.position);        // el átomo (y lo que lleva) va a la barra
        }
        else
        {
            barCluster.Move(atom.transform.position - target);         // la barra (y lo que lleva) va al átomo
        }

        port.transform.position = plug.transform.position;
        Physics.SyncTransforms();

        // Unión fija por jerarquía: la molécula (Molecule) hace a esta pieza hija de su raíz. No hace falta joint.
        plug.Port = port; plug.Joint = null; port.Plug = plug;
        IgnoreWith(atom, true);
        IgnoreBarsAt(atom, true);

        if (IsComplete)
        {
            AtomA.Bars.Add(this);
            AtomB.Bars.Add(this);
            AtomB.NotifyBondsChanged();
        }
        atom.NotifyBondsChanged();
        Molecule.OnLinked(this, plug, atom);
        AnyAttached?.Invoke(this, atom);
        return true;
    }

    /// Suelta UNA punta de la barra de su átomo (la usa Molecule para separar pieza por pieza)
    public void DetachPlug(BarPlug p)
    {
        if (p == null || p.IsFree) return;
        bool wasComplete = IsComplete;
        var other = p == PlugA ? PlugB : PlugA;
        var atom = p.Port.Owner;
        if (p.Joint != null) Destroy(p.Joint);
        p.Port.Plug = null; p.Port = null; p.Joint = null;
        p.needsLeave = true;                     // no reenganchar hasta alejarse
        IgnoreWith(atom, false);
        IgnoreBarsAt(atom, false);
        if (wasComplete)
        {
            atom.Bars.Remove(this);
            if (other.Port != null) { other.Port.Owner.Bars.Remove(this); other.Port.Owner.NotifyBondsChanged(); }
        }
        atom.NotifyBondsChanged();
        AnyDetached?.Invoke(this);
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
        if (a != null || b != null) AnyDetached?.Invoke(this);
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
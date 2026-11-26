using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;   // XRI 2.x: borrar esta línea

/// Barra de enlace. Al acercarse a un átomo con valencia libre se engancha sola y las dos piezas pasan a ser
/// UNA molécula (un solo objeto). Ya no hay FixedJoint: la unión la hace Molecule.Rebuild().
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

    float nextCheck;

    public bool IsComplete => PlugA.Port != null && PlugB.Port != null;
    public Atom AtomA => PlugA.Port != null ? PlugA.Port.Owner : null;
    public Atom AtomB => PlugB.Port != null ? PlugB.Port.Owner : null;

    void Awake()
    {
        var plugs = GetComponentsInChildren<BarPlug>();
        PlugA = plugs[0]; PlugB = plugs[1];
        PlugA.bond = this; PlugB.bond = this;

        if (autoFitPlugs) FitPlugsToMesh();

        PieceBody.Ensure(gameObject);   // guarda el Rigidbody y el agarre del prefab para poder recuperarlos al separarse
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

        // Una molécula bloqueada (la está procesando la transmutadora) no admite piezas nuevas
        if (Molecule.IsPieceLocked(atom) || Molecule.IsPieceLocked(this)) return false;

        // Dónde debe quedar el centro del átomo: pegado a la punta, hacia afuera de la barra
        Vector3 outward = (plug.transform.position - other.transform.position).normalized;
        Vector3 target = plug.transform.position + outward * atom.atomRadius;
        float gap = Vector3.Distance(atom.transform.position, target);

        var atomMol = Molecule.Of(atom);
        bool sameMolecule = atomMol != null && atomMol == Molecule.Of(this);   // cierre de anillo
        bool atomHeld = Molecule.IsPieceHeld(atom);
        bool barHeld = Molecule.IsPieceHeld(this);

        if (sameMolecule || (atomHeld && barHeld))
        {
            // No se puede mover nada: solo enlaza si ya están prácticamente en su sitio
            if (gap > snapTolerance) return false;
        }
        else if (!atomHeld)
        {
            Molecule.MovePiece(atom, target - atom.transform.position);        // el átomo (y lo que lleva) va a la barra
        }
        else
        {
            Molecule.MovePiece(this, atom.transform.position - target);        // la barra (y lo que lleva) va al átomo
        }

        port.transform.position = plug.transform.position;
        Physics.SyncTransforms();

        plug.Port = port; port.Plug = plug;

        if (IsComplete)
        {
            AtomA.Bars.Add(this);
            AtomB.Bars.Add(this);
            AtomB.NotifyBondsChanged();
        }
        atom.NotifyBondsChanged();

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

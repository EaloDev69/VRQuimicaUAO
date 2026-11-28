using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// Arranque automático de la Máquina Transmutadora (no hace falta botón).
///
/// Flujo:
///  1. Una molécula COMPLETA (2 o más átomos unidos con barras, sin valencias libres) entra o atraviesa la caja
///     trigger de entrada de la máquina, aunque el jugador la tenga en la mano.
///  2. La máquina la toma: la suelta de la mano y la deja quieta en el centro de la entrada.
///  3. La analiza con las recetas (MoleculeValidator) para saber qué compuesto es y si es gas o líquido.
///  4. Si no hay contenedor adecuado (botella vacía en el socket de gases para un gas, vaso vacío bajo el pico
///     para un líquido; o ninguno de los dos si la molécula no corresponde a una receta), NO arranca:
///     devuelve la molécula hacia afuera.
///  5. Si hay contenedor, arranca la reacción (TransmutatorMachine.StartReaction). Al terminar bien, el contenedor
///     queda con la receta (nombre, fórmula, color, categoría, puntos) y la molécula se gasta.
///     Si la reacción falla (molécula incorrecta, palancas mal) y la molécula sigue ahí, también la devuelve.
public class TransmutatorAutoStart : MonoBehaviour
{
    // Para el monitor de pruebas
    public static event System.Action<string> MoleculeEjected;     // motivo
    public static event System.Action<string> MoleculeAbsorbed;    // descripción de la molécula
    public static event System.Action<string, float, bool> CompleteMoleculeNear;   // fórmula, distancia a la entrada (m), en la mano

    [Tooltip("Margen (m) alrededor de la caja de entrada en el que ya se toma la molécula")]
    public float entryMargin = 0.12f;
    [Tooltip("Dibuja el contorno de la caja de entrada para saber dónde meter la molécula")]
    public bool showEntryOutline = true;

    [Tooltip("Segundos que se ignora una molécula después de devolverla (para que no la vuelva a tomar al salir)")]
    public float ignoreAfterEject = 2.5f;
    [Tooltip("Velocidad con la que sale la molécula devuelta")]
    public float ejectSpeed = 1.6f;

    TransmutatorMachine machine;
    Collider entry;

    // Molécula que la máquina tiene tomada
    readonly List<Rigidbody> held = new List<Rigidbody>();
    readonly List<bool> heldWasKinematic = new List<bool>();
    List<Atom> heldAtoms;
    bool reactionStarted;
    float decideAt;

    readonly Dictionary<int, float> ignoreUntil = new Dictionary<int, float>();

    void Awake()
    {
        machine = GetComponent<TransmutatorMachine>();
        entry = GetComponents<Collider>().FirstOrDefault(c => c.isTrigger && c.enabled) ?? GetComponent<Collider>();
        if (entry != null && !entry.isTrigger)
            Debug.LogWarning("[TransmutatorAutoStart] La caja de entrada de la máquina debería ser Trigger.", this);
        if (entry != null && showEntryOutline) BuildOutline();
    }

    // Contorno cian de la caja de entrada (12 aristas) para ver dónde va la molécula
    void BuildOutline()
    {
        var box = entry as BoxCollider;
        var go = new GameObject("Entrada (contorno)");
        go.transform.SetParent(entry.transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = false;
        lr.widthMultiplier = 0.008f;
        var sh = Shader.Find("Sprites/Default");
        if (sh != null) lr.material = new Material(sh);
        lr.startColor = lr.endColor = new Color(0.2f, 0.9f, 1f, 0.9f);
        Vector3 c = box != null ? box.center : Vector3.zero, h = box != null ? box.size * 0.5f : Vector3.one * 0.5f;
        Vector3 P(int x, int y, int z) => c + Vector3.Scale(h, new Vector3(x, y, z));
        var pts = new[] { P(-1,-1,-1), P(1,-1,-1), P(1,-1,1), P(-1,-1,1), P(-1,-1,-1), P(-1,1,-1), P(1,1,-1), P(1,-1,-1),
                          P(1,1,-1), P(1,1,1), P(1,-1,1), P(1,1,1), P(-1,1,1), P(-1,-1,1), P(-1,1,1), P(-1,1,-1) };
        lr.positionCount = pts.Length;
        lr.SetPositions(pts);
    }

    void Update()
    {
        if (machine == null || entry == null) return;

        if (heldAtoms != null) { UpdateHeld(); return; }
        if (machine.State != TransmutatorMachine.MachineState.Idle) return;

        var atom = AtomInEntry();
        if (atom == null) { ReportNearby(); return; }

        var graph = MoleculeGraph.FromAtom(atom, out var atoms);
        if (graph.AtomCount < 2 || graph.OpenValences != 0) return;            // todavía no es una molécula completa

        int key = MoleculeKey(atoms);
        if (ignoreUntil.TryGetValue(key, out float until) && Time.time < until) return;

        Absorb(atoms);
    }

    // ---------------------------------------------------------------
    // Tomar la molécula
    // ---------------------------------------------------------------
    void Absorb(List<Atom> atoms)
    {
        // Soltarla de la mano (esto también devuelve a la normalidad las piezas que "cargaba" el átomo agarrado)
        foreach (var a in atoms) ForceRelease(a.GetComponent<XRGrabInteractable>());
        foreach (var b in BarsOf(atoms)) ForceRelease(b.GetComponent<XRGrabInteractable>());

        heldAtoms = atoms;
        held.Clear(); heldWasKinematic.Clear();
        foreach (var t in Pieces(atoms))
        {
            var rb = t.GetComponent<Rigidbody>();
            if (rb == null) continue;
            heldWasKinematic.Add(rb.isKinematic);
            if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            rb.isKinematic = true;
            held.Add(rb);
        }

        // Centrarla en la entrada
        Vector3 centroid = Vector3.zero;
        foreach (var a in atoms) centroid += a.transform.position;
        centroid /= atoms.Count;
        Vector3 delta = entry.bounds.center - centroid;
        foreach (var top in Molecule.TopLevel(held.Select(r => r.transform))) top.position += delta;
        Physics.SyncTransforms();

        reactionStarted = false;
        decideAt = Time.time + 0.15f;
        MoleculeAbsorbed?.Invoke(Describe(atoms));      // un instante para que la máquina registre los átomos dentro
    }

    void UpdateHeld()
    {
        heldAtoms.RemoveAll(a => a == null);
        if (heldAtoms.Count == 0) { ClearHeld(false); return; }          // se gastó (éxito o palanca mal)

        if (!reactionStarted)
        {
            if (Time.time < decideAt) return;
            Decide();
            return;
        }

        // La reacción terminó y la molécula sigue aquí (molécula incorrecta, derrame, cancelación): se devuelve
        if (machine.State == TransmutatorMachine.MachineState.Idle)
        {
            MoleculeEjected?.Invoke("la reacción falló y la molécula seguía en la entrada");
            Eject();
        }
    }

    void Decide()
    {
        var graph = MoleculeGraph.FromAtom(heldAtoms[0], out _);
        RecipeData recipe = null;
        var state = machine.catalog != null
            ? MoleculeValidator.Validate(graph, machine.catalog.recipes, out recipe)
            : ValidationState.Unknown;

        bool bottle = BottleReady(), cup = CupReady();
        bool ok = state == ValidationState.Correct && recipe != null
            ? (recipe.category == CompoundCategory.Gas ? bottle : cup)
            : (bottle || cup);

        if (!ok)
        {
            string need = state == ValidationState.Correct && recipe != null
                ? (recipe.category == CompoundCategory.Gas ? "una botella vacía en el socket de gases" : "un vaso vacío bajo el pico")
                : "una botella o un vaso";
            Debug.Log($"[Transmutadora] Falta {need}: devuelvo la molécula.", this);
            MoleculeEjected?.Invoke($"faltaba {need}");
            machine.onNoContainer.Invoke();
            Eject();
            return;
        }

        // La máquina valida, revisa palancas y contenedor, y llena el contenedor con la receta
        machine.StartReaction();
        if (machine.State == TransmutatorMachine.MachineState.Processing) { reactionStarted = true; return; }

        // No arrancó (palancas bloqueadas u otro motivo): se devuelve
        MoleculeEjected?.Invoke("la máquina no arrancó (¿palancas de presión y temperatura las dos mal?)");
        Eject();
    }

    // ---------------------------------------------------------------
    // Devolver la molécula hacia afuera
    // ---------------------------------------------------------------
    void Eject()
    {
        var atoms = heldAtoms;
        Vector3 outDir = Vector3.ProjectOnPlane(entry.bounds.center - BodyCenter(), Vector3.up);
        if (outDir.sqrMagnitude < 1e-4f) outDir = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        outDir.Normalize();

        // Sacarla un poco por delante de la entrada para que no vuelva a tocar el trigger
        var b = entry.bounds;
        float depth = Mathf.Abs(Vector3.Dot(b.extents, new Vector3(Mathf.Abs(outDir.x), Mathf.Abs(outDir.y), Mathf.Abs(outDir.z))));
        Vector3 delta = outDir * (depth + 0.25f);
        foreach (var top in Molecule.TopLevel(held.Where(r => r != null).Select(r => r.transform))) top.position += delta;
        Physics.SyncTransforms();

        ClearHeld(true);
        foreach (var t in Pieces(atoms.Where(a => a != null).ToList()))
        {
            var rb = t.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic) rb.linearVelocity = outDir * ejectSpeed + Vector3.up * 0.8f;
        }
        if (atoms.Count > 0 && atoms[0] != null) ignoreUntil[MoleculeKey(atoms)] = Time.time + ignoreAfterEject;
    }

    void ClearHeld(bool restore)
    {
        if (restore)
            for (int i = 0; i < held.Count; i++)
                if (held[i] != null) held[i].isKinematic = heldWasKinematic[i];
        held.Clear(); heldWasKinematic.Clear();
        heldAtoms = null;
        reactionStarted = false;
    }

    // ---------------------------------------------------------------
    // Contenedores
    // ---------------------------------------------------------------
    bool BottleReady()
    {
        var socket = machine.gasSocket;
        if (socket == null || !socket.hasSelection) return false;
        foreach (var i in socket.interactablesSelected)
        {
            var s = i.transform.GetComponentInParent<SampleItem>();
            if (s != null && s.kind == ContainerKind.Bottle && s.IsEmpty) return true;
        }
        return false;
    }

    bool CupReady()
    {
        var zone = machine.cupZone;
        if (zone == null) return false;
        Collider[] hits;
        var t = zone.transform;
        if (zone is BoxCollider box)
            hits = Physics.OverlapBox(t.TransformPoint(box.center), Vector3.Scale(box.size, t.lossyScale) * 0.5f, t.rotation, ~0, QueryTriggerInteraction.Ignore);
        else
            hits = Physics.OverlapBox(zone.bounds.center, zone.bounds.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        foreach (var c in hits)
        {
            var s = c.GetComponentInParent<SampleItem>();
            if (s != null && s.kind == ContainerKind.Cup && s.IsEmpty) return true;
        }
        return false;
    }

    // ---------------------------------------------------------------
    // Utilidades
    // ---------------------------------------------------------------
    float nextNearCheck;

    // Diagnóstico: moléculas completas cerca de la máquina que todavía no entran a la caja
    void ReportNearby()
    {
        if (CompleteMoleculeNear == null || Time.time < nextNearCheck) return;
        nextNearCheck = Time.time + 0.5f;
        var c = entry.bounds.center;
        var done = new HashSet<Atom>();
        foreach (var col in Physics.OverlapSphere(c, 2.5f, ~0, QueryTriggerInteraction.Ignore))
        {
            var a = col.GetComponentInParent<Atom>();
            if (a == null || a.data == null || done.Contains(a)) continue;
            var g = MoleculeGraph.FromAtom(a, out var atoms);
            foreach (var x in atoms) done.Add(x);
            if (g.AtomCount < 2 || g.OpenValences != 0) continue;
            float d = atoms.Min(x => Vector3.Distance(entry.ClosestPoint(x.transform.position), x.transform.position));
            bool inHand = atoms.Any(x => { var gr = x.GetComponent<XRGrabInteractable>(); return gr != null && gr.isSelected; });
            CompleteMoleculeNear(Describe(atoms), d, inHand);
        }
    }

    Atom AtomInEntry()
    {
        var b = entry.bounds;
        b.Expand(entryMargin * 2f);
        foreach (var c in Physics.OverlapBox(b.center, b.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
        {
            var a = c.GetComponentInParent<Atom>();
            if (a != null && a.data != null) return a;
        }
        return null;
    }

    Vector3 BodyCenter()
    {
        bool any = false; Bounds body = default;
        foreach (var c in GetComponents<Collider>())
        {
            if (c == null || c.isTrigger || !c.enabled) continue;
            if (!any) { body = c.bounds; any = true; } else body.Encapsulate(c.bounds);
        }
        return any ? body.center : transform.position;
    }

    static void ForceRelease(XRGrabInteractable g)
    {
        if (g == null || !g.isSelected || g.interactionManager == null) return;
        g.interactionManager.CancelInteractableSelection((IXRSelectInteractable)g);
    }

    static HashSet<Bond> BarsOf(List<Atom> atoms)
    {
        var bars = new HashSet<Bond>();
        foreach (var a in atoms)
        {
            if (a == null) continue;
            foreach (var b in a.Bars) if (b != null) bars.Add(b);
            foreach (var p in a.Ports) if (p != null && p.Plug != null && p.Plug.bond != null) bars.Add(p.Plug.bond);
        }
        return bars;
    }

    static List<Transform> Pieces(List<Atom> atoms)
    {
        var list = new List<Transform>();
        foreach (var a in atoms) if (a != null) list.Add(a.transform);
        foreach (var b in BarsOf(atoms)) list.Add(b.transform);
        return list;
    }

    public static string Describe(List<Atom> atoms)
    {
        var counts = new SortedDictionary<string, int>();
        foreach (var a in atoms) if (a != null && a.data != null) counts[a.data.symbol] = counts.TryGetValue(a.data.symbol, out int n) ? n + 1 : 1;
        return string.Concat(counts.Select(kv => kv.Key + (kv.Value > 1 ? kv.Value.ToString() : "")));
    }

    static int MoleculeKey(List<Atom> atoms)
    {
        int h = 17;
        foreach (var id in atoms.Where(a => a != null).Select(a => a.GetInstanceID()).OrderBy(i => i)) h = h * 486187739 + id;
        return h;
    }
}

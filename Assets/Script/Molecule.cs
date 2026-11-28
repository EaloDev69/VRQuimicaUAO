using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// Molécula como UNA sola pieza, por jerarquía (parent):
///  - La PRIMERA BARRA que se une a un átomo es la RAÍZ (el padre) de la molécula. Cada átomo y cada barra que se
///    va conectando queda como hijo de esa barra: su Rigidbody se vuelve cinemático y su agarre se apaga.
///    Solo la barra raíz tiene física y agarre, y su agarre incluye los colliders de todas las piezas: agarrando
///    cualquier pieza se toma la molécula completa, rígida, sin tambalearse.
///  - Si tenías en la mano un átomo (u otra barra) y lo unes a la molécula, la mano pasa a sostener la barra raíz
///    en el mismo punto: no se suelta nada ni da saltos.
///  - Al juntar dos moléculas manda la que tienes en la mano (su barra raíz sigue siendo el padre).
///  - Para separar: con la molécula en la mano, presiona el GATILLO SUPERIOR del control (index trigger / activate).
///    Cada pulsación separa la última pieza que se unió, con una vibración corta y un pequeño empujón hacia
///    afuera para que no se vuelva a pegar; así se desarma pieza por pieza. Las piezas separadas caen con física
///    normal y se pueden volver a agarrar al soltar la molécula.
/// Este componente se agrega solo a la raíz (lo pone Bond al unir); no hay que ponerlo a mano.
[DisallowMultipleComponent]
public class Molecule : MonoBehaviour
{
    // Uniones en el orden en que se hicieron (para separar de la última a la primera)
    public readonly List<BarPlug> links = new List<BarPlug>();

    XRGrabInteractable grab;
    Rigidbody rb;
    List<Collider> ownColliders;                 // colliders propios de la raíz (sin las piezas hijas)
    readonly List<XRGrabInteractable> pendingEnable = new List<XRGrabInteractable>();
    bool dirty;
    bool dissolvePending;                        // deshacer la molécula en cuanto nadie la sostenga

    public XRGrabInteractable Grab => grab;
    public bool IsHeld => grab != null && grab.isSelected;

    void Awake()
    {
        grab = GetComponent<XRGrabInteractable>();
        rb = GetComponent<Rigidbody>();
        if (grab != null)
        {
            ownColliders = new List<Collider>(grab.colliders.Where(c => c != null));
            if (ownColliders.Count == 0) ownColliders.AddRange(GetComponents<Collider>());
            grab.activated.AddListener(OnActivated);
        }
    }

    void OnDestroy()
    {
        if (grab != null) grab.activated.RemoveListener(OnActivated);
    }

    void Update()
    {
        // Las piezas hijas nunca se agarran por separado (otra máquina pudo reactivar su agarre)
        foreach (var t in Children())
        {
            var g = t.GetComponent<XRGrabInteractable>();
            if (g != null && g.enabled) { g.enabled = false; dirty = true; }
            var crb = t.GetComponent<Rigidbody>();
            if (crb != null && !crb.isKinematic) crb.isKinematic = true;
        }
        if (IsHeld || grab == null) return;
        if (dissolvePending) { DissolveNow(this); return; }
        if (dirty && grab.enabled) Refresh();
    }

    // Vuelve a registrar el agarre de la raíz con los colliders de todas sus piezas.
    // Se hace solo cuando nadie la sostiene (registrar de nuevo soltaría la molécula).
    void Refresh()
    {
        dirty = false;
        grab.enabled = false;                                    // quita del registro los colliders que tenía
        grab.colliders.Clear();
        grab.colliders.AddRange(ownColliders.Where(c => c != null));
        foreach (var t in Children())
            foreach (var c in t.GetComponentsInChildren<Collider>(true))
                if (c != null && !grab.colliders.Contains(c)) grab.colliders.Add(c);
        grab.enabled = true;                                     // los registra todos para la raíz

        foreach (var g in pendingEnable) if (g != null && g.transform.parent == null) g.enabled = true;
        pendingEnable.Clear();
    }

    IEnumerable<Transform> Children()
    {
        foreach (Transform t in transform)
            if (t.GetComponent<Atom>() != null || t.GetComponent<Bond>() != null) yield return t;
    }

    // ---------------------------------------------------------------
    // Separar pieza por pieza con el gatillo superior
    // ---------------------------------------------------------------
    void OnActivated(ActivateEventArgs args)
    {
        if (args.interactorObject is XRSocketInteractor) return;
        if (DetachLast() && args.interactorObject is XRBaseInputInteractor input) input.SendHapticImpulse(0.4f, 0.06f);
    }

    /// Separa la última unión que se hizo (una pieza). Devuelve false si no había uniones.
    public bool DetachLast()
    {
        if (links.Count == 0) return false;
        var plug = links[links.Count - 1];
        links.RemoveAt(links.Count - 1);
        var before = new List<Transform>(Children());
        Vector3 center = transform.position;
        Vector3 plugPos = plug != null ? plug.transform.position : center;
        if (plug != null && plug.bond != null) plug.bond.DetachPlug(plug);
        Rebuild(this);

        // Empujoncito hacia afuera a lo que se soltó, para que se aparte limpio y no se reenganche
        var loose = before.Where(p => p != null && p.parent != transform && p != transform).ToList();
        foreach (var p in TopLevel(loose))
        {
            Vector3 dir = p.position - center;
            if (dir.sqrMagnitude < 1e-6f) dir = p.position - plugPos;
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.up;
            dir.Normalize();
            p.position += dir * SeparationOffset;
            var prb = p.GetComponent<Rigidbody>();
            if (prb != null && !prb.isKinematic)
            {
                prb.linearVelocity = dir * SeparationSpeed;
                prb.angularVelocity = Vector3.zero;
            }
        }
        Physics.SyncTransforms();
        return true;
    }

    public static float SeparationOffset = 0.025f;   // m que se aparta la pieza al separarse
    public static float SeparationSpeed = 0.5f;      // m/s del empujón

    // ---------------------------------------------------------------
    // Armado (lo llama Bond cada vez que une una punta de barra con un átomo)
    // ---------------------------------------------------------------
    public static void OnLinked(Bond bar, BarPlug plug, Atom atom)
    {
        var pieces = Connected(atom.transform);

        // Uniones ya existentes de las moléculas que se juntan, en orden
        var oldRoots = pieces.Select(p => p.GetComponent<Molecule>()).Where(m => m != null).Distinct().ToList();
        var links = new List<BarPlug>();
        foreach (var m in oldRoots) links.AddRange(m.links.Where(l => l != null && !links.Contains(l)));
        if (!links.Contains(plug)) links.Add(plug);

        // Raíz = una BARRA: la de la molécula que está en la mano; si no, la de una molécula que ya existía;
        // si no, esta barra (la primera unión).
        Molecule keepRoot = oldRoots.FirstOrDefault(m => m.GetComponent<Bond>() != null && HeldByHand(m.transform))
                            ?? oldRoots.FirstOrDefault(m => m.GetComponent<Bond>() != null);
        Transform root = keepRoot != null ? keepRoot.transform : bar.transform;

        // Sus uniones primero (la primera barra queda como la más antigua)
        if (keepRoot != null)
        {
            var ordered = new List<BarPlug>(keepRoot.links.Where(l => l != null));
            ordered.AddRange(links.Where(l => !ordered.Contains(l)));
            links = ordered;
        }

        // Manos que sostienen una pieza que dejará de ser agarrable (pasará a ser hija): luego sostendrán la raíz
        var hands = new List<IXRSelectInteractor>();
        foreach (var p in pieces)
        {
            if (p == root) continue;
            var g = p.GetComponent<XRGrabInteractable>();
            if (g == null || !g.isSelected) continue;
            foreach (var i in g.interactorsSelecting)
                if (!(i is XRSocketInteractor) && !hands.Contains(i)) hands.Add(i);
        }

        foreach (var m in oldRoots) if (m.transform != root) Dissolve(m);

        var mol = root.GetComponent<Molecule>() ?? root.gameObject.AddComponent<Molecule>();
        mol.links.Clear();
        mol.links.AddRange(links);
        Rebuild(mol, pieces);

        mol.TakeOverHands(hands);
    }

    // La mano que tenía el átomo (u otra pieza) pasa a sostener la barra raíz, en el mismo punto (agarre dinámico)
    void TakeOverHands(List<IXRSelectInteractor> hands)
    {
        if (grab == null || hands.Count == 0) return;
        if (HeldByHand(transform)) return;              // la raíz ya está en otra mano: no se la quitamos
        var manager = grab.interactionManager;
        if (manager == null) return;
        if (!grab.enabled) grab.enabled = true;
        foreach (var hand in hands)
        {
            if (hand == null || !(hand is Behaviour b) || !b.isActiveAndEnabled) continue;
            if (hand.hasSelection) continue;
            if (grab.isSelected && grab.selectMode == InteractableSelectMode.Single) break;
            if (rb != null) rb.isKinematic = false;
            manager.SelectEnter(hand, (IXRSelectInteractable)grab);
            break;                                       // una mano basta
        }
    }

    // Reorganiza la jerarquía: todo lo conectado a la raíz queda como hijo; lo que ya no está conectado se suelta
    // (y si sigue unido entre sí, forma su propia molécula).
    static void Rebuild(Molecule mol, List<Transform> knownPieces = null)
    {
        var all = new HashSet<Transform>(mol.Children()) { mol.transform };
        if (knownPieces != null) foreach (var p in knownPieces) all.Add(p);

        var keep = new HashSet<Transform>(Connected(mol.transform));
        var loose = all.Where(p => p != null && !keep.Contains(p)).ToList();

        // 1) Piezas que siguen en la molécula: hijas, cinemáticas y sin agarre propio
        foreach (var p in keep)
        {
            if (p == mol.transform) continue;
            var g = p.GetComponent<XRGrabInteractable>();
            if (g != null && g.isSelected && g.interactionManager != null) g.interactionManager.CancelInteractableSelection((IXRSelectInteractable)g);
            var m = p.GetComponent<Molecule>();
            if (m != null) Dissolve(m);
            var prb = p.GetComponent<Rigidbody>();
            if (prb != null)
            {
                if (!prb.isKinematic) { prb.linearVelocity = Vector3.zero; prb.angularVelocity = Vector3.zero; }
                prb.isKinematic = true;
            }
            if (g != null) g.enabled = false;
            if (p.parent != mol.transform) p.SetParent(mol.transform, true);
        }

        // 2) Piezas que se separaron
        foreach (var p in loose) p.SetParent(null, true);
        var groups = Groups(loose);
        foreach (var grp in groups)
        {
            if (grp.Count >= 2)
            {
                // sigue unida entre sí: nueva molécula con raíz en una barra del grupo
                var r = grp.FirstOrDefault(x => x.GetComponent<Bond>() != null) ?? grp[0];
                MakeFree(r, mol);
                var nm = r.GetComponent<Molecule>() ?? r.gameObject.AddComponent<Molecule>();
                nm.links.Clear();
                nm.links.AddRange(mol.links.Where(l => l != null && grp.Contains(l.bond.transform)));
                Rebuild(nm, grp);
            }
            else MakeFree(grp[0], mol);
        }
        mol.links.RemoveAll(l => l == null || l.bond == null || !keep.Contains(l.bond.transform));

        // La raíz con física propia
        if (mol.rb != null && !mol.IsHeld) mol.rb.isKinematic = false;
        mol.dirty = true;

        // Si la raíz quedó sola, deja de ser molécula
        if (keep.Count <= 1) Dissolve(mol);
    }

    // Pieza suelta: física normal. Su agarre se activa cuando la raíz deja de tener sus colliders registrados
    // (en el siguiente Refresh de la molécula, que se hace cuando nadie la sostiene).
    static void MakeFree(Transform p, Molecule from)
    {
        var prb = p.GetComponent<Rigidbody>();
        if (prb != null) prb.isKinematic = false;
        var g = p.GetComponent<XRGrabInteractable>();
        if (g != null && !g.enabled)
        {
            if (from != null) from.pendingEnable.Add(g);
            else g.enabled = true;
        }
        if (from != null) from.dirty = true;
    }

    static void Dissolve(Molecule m)
    {
        if (m == null) return;
        if (m.IsHeld) { m.dissolvePending = true; m.links.Clear(); return; }
        DissolveNow(m);
    }

    static void DissolveNow(Molecule m)
    {
        if (m.grab != null && m.ownColliders != null)
        {
            m.grab.enabled = false;                              // primero se quitan del registro sus colliders
            m.grab.colliders.Clear();
            m.grab.colliders.AddRange(m.ownColliders.Where(c => c != null));
            m.grab.enabled = true;
        }
        foreach (var g in m.pendingEnable) if (g != null && g.transform.parent == null) g.enabled = true;
        m.pendingEnable.Clear();
        Destroy(m);
    }

    // ---------------------------------------------------------------
    // Conectividad por barras (átomos <-> barras)
    // ---------------------------------------------------------------
    public static List<Transform> Connected(Transform start)
    {
        var atoms = new HashSet<Atom>(); var bars = new HashSet<Bond>();
        var pa = new Stack<Atom>(); var pb = new Stack<Bond>();
        var sa = start.GetComponent<Atom>(); var sb = start.GetComponent<Bond>();
        if (sa != null) pa.Push(sa);
        if (sb != null) pb.Push(sb);
        while (pa.Count > 0 || pb.Count > 0)
        {
            while (pa.Count > 0)
            {
                var a = pa.Pop();
                if (a == null || !atoms.Add(a)) continue;
                foreach (var p in a.Ports) if (p != null && p.Plug != null && p.Plug.bond != null) pb.Push(p.Plug.bond);
            }
            while (pb.Count > 0)
            {
                var b = pb.Pop();
                if (b == null || !bars.Add(b)) continue;
                if (b.PlugA != null && b.PlugA.Port != null) pa.Push(b.PlugA.Port.Owner);
                if (b.PlugB != null && b.PlugB.Port != null) pa.Push(b.PlugB.Port.Owner);
            }
        }
        var list = new List<Transform>();
        foreach (var a in atoms) list.Add(a.transform);
        foreach (var b in bars) list.Add(b.transform);
        return list;
    }

    static List<List<Transform>> Groups(List<Transform> pieces)
    {
        var left = new HashSet<Transform>(pieces);
        var result = new List<List<Transform>>();
        while (left.Count > 0)
        {
            var first = left.First();
            var grp = Connected(first).Where(left.Contains).ToList();
            if (!grp.Contains(first)) grp.Add(first);
            foreach (var g in grp) left.Remove(g);
            result.Add(grp);
        }
        return result;
    }

    static bool HeldByHand(Transform t)
    {
        var g = t.GetComponent<XRGrabInteractable>();
        if (g == null || !g.isSelected) return false;
        foreach (var i in g.interactorsSelecting) if (!(i is XRSocketInteractor)) return true;
        return false;
    }

    /// Piezas de nivel superior de una lista (las que no son hijas de otra pieza de la lista).
    /// Para mover una molécula basta con mover estas: las hijas van con su raíz.
    public static List<Transform> TopLevel(IEnumerable<Transform> pieces)
    {
        var set = new HashSet<Transform>(pieces.Where(p => p != null));
        return set.Where(p => { var q = p.parent; while (q != null) { if (set.Contains(q)) return false; q = q.parent; } return true; }).ToList();
    }
}

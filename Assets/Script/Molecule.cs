using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;   // XRI 2.x: borrar esta línea
using UnityEngine.XR.Interaction.Toolkit.Interactors;     // XRI 2.x: borrar esta línea

/// Molécula = átomos y barras unidos que se comportan como UN solo objeto.
/// - Es un objeto raíz con un solo Rigidbody y un solo XRGrabInteractable; las piezas son sus hijas.
///   Agarrar cualquier parte mueve toda la molécula, sin joints ni tirones.
/// - Se crea sola: cuando Bond engancha una barra a un átomo se llama a Rebuild(), que une las piezas.
///   Cuando se suelta un enlace, Rebuild() separa lo que quedó desconectado.
/// - Para desarmar: con una mano sosteniendo la molécula, agarrar con la otra una pieza con los dos
///   grips pulsados (OVRInput PrimaryHandTrigger + SecondaryHandTrigger) la saca de la molécula.
public class Molecule : MonoBehaviour
{
    // Ajustes globales (los fija MoleculeSettings si hay uno en la escena)
    public static bool TwoHandDetach = true;
    public static bool RequireBothGrips = true;
    public static float GripThreshold = 0.6f;
    public static float DetachIgnoreSeconds = 0.5f;

    static float gestureBlockedUntil;

    public readonly List<Atom> atoms = new List<Atom>();
    public readonly List<Bond> bars = new List<Bond>();
    public Rigidbody Body { get; private set; }
    public XRGrabInteractable Grab { get; private set; }
    public bool Locked { get; set; }
    public bool IsHeld => Grab != null && Grab.isSelected;
    public IEnumerable<Component> Pieces => atoms.Cast<Component>().Concat(bars.Cast<Component>());

    bool dissolving;

    // ---------------------------------------------------------------
    // Ayudas que usan Bond, la transmutadora y demás
    // ---------------------------------------------------------------
    /// La molécula a la que pertenece una pieza (null si está suelta)
    public static Molecule Of(Component piece) => piece != null ? piece.GetComponentInParent<Molecule>() : null;

    public static bool IsPieceHeld(Component piece)
    {
        var m = Of(piece);
        if (m != null) return m.IsHeld;
        var g = piece.GetComponent<XRGrabInteractable>();
        return g != null && g.isSelected;
    }

    public static bool IsPieceLocked(Component piece)
    {
        var m = Of(piece);
        return m != null && m.Locked;
    }

    public static void SetPieceLocked(Component piece, bool locked)
    {
        var m = Of(piece);
        if (m != null)
        {
            m.Locked = locked;
            if (m.Grab != null) m.Grab.enabled = !locked;
            return;
        }
        var g = piece.GetComponent<XRGrabInteractable>();
        if (g != null) g.enabled = !locked;
    }

    /// Mueve la molécula (o la pieza suelta) sin velocidad residual
    public static void MovePiece(Component piece, Vector3 delta)
    {
        var m = Of(piece);
        var t = m != null ? m.transform : piece.transform;
        t.position += delta;

        var rb = m != null ? m.Body : piece.GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    // ---------------------------------------------------------------
    // Rebuild: une lo conectado y separa lo desconectado
    // ---------------------------------------------------------------
    /// Se llama después de cambiar enlaces. 'touched' son las piezas cuyo enlace cambió.
    /// pinPiece / pinInteractor: al separar, esa mano se queda con esa pieza (el resto de las manos, con la parte mayor).
    public static void Rebuild(Component[] touched, Component pinPiece = null, IXRSelectInteractor pinInteractor = null)
    {
        // 1) Grupos de piezas conectadas entre sí (a partir de las piezas tocadas y de sus moléculas actuales)
        var starts = new List<Component>();
        foreach (var t in touched)
        {
            if (t == null) continue;
            starts.Add(t);
            var m = Of(t);
            if (m != null) starts.AddRange(m.Pieces.Where(p => p != null));
        }

        var seen = new HashSet<Component>();
        var comps = new List<List<Component>>();
        foreach (var s in starts)
        {
            if (s == null || seen.Contains(s)) continue;
            comps.Add(Collect(s, seen));
        }

        var all = comps.SelectMany(c => c).ToList();
        if (all.Count == 0) return;

        // 2) Qué manos sostienen algo de esto ahora mismo (para devolvérselo después)
        var oldMolecules = all.Select(Of).Where(m => m != null).Distinct().ToList();
        var holders = new List<(IXRSelectInteractor interactor, Vector3 pos)>();
        foreach (var m in oldMolecules) RecordHolders(m.Grab, holders);
        foreach (var p in all)
        {
            if (Of(p) != null) continue;
            RecordHolders(PieceBody.Ensure(p.gameObject).Grab, holders);
        }

        // 3) Se sacan las piezas de las moléculas viejas y se destruyen sus raíces
        foreach (var p in all) p.transform.SetParent(null, true);
        foreach (var m in oldMolecules) m.DestroyNow();

        // 4) Todas las piezas sueltan su cuerpo propio
        foreach (var p in all) PieceBody.Ensure(p.gameObject).Strip();

        // 5) Una pieza sola vuelve a ser un objeto suelto; varias forman una molécula nueva
        var results = new List<(List<Component> pieces, XRGrabInteractable grab)>();
        foreach (var comp in comps)
        {
            if (comp.Count == 1) results.Add((comp, PieceBody.Ensure(comp[0].gameObject).Restore()));
            else results.Add((comp, Create(comp).Grab));
        }

        // 6) Las partes recién separadas estaban solapadas: se ignoran un momento para que no salgan disparadas
        if (comps.Count > 1) IgnoreBetween(comps, DetachIgnoreSeconds);

        // 7) Cada mano vuelve a agarrar lo suyo
        gestureBlockedUntil = Time.time + 0.4f;
        foreach (var (interactor, pos) in holders)
        {
            var target = ChooseTarget(results, interactor, pos, pinPiece, pinInteractor);
            if (target == null || target.interactionManager == null) continue;
            try { target.interactionManager.SelectEnter(interactor, target); }
            catch (Exception e) { Debug.LogWarning($"[Molecule] No se pudo devolver la mano a la pieza: {e.Message}"); }
        }
    }

    // Piezas conectadas: átomo -> barras de sus puertos; barra -> átomos de sus dos extremos
    static List<Component> Collect(Component start, HashSet<Component> seen)
    {
        var list = new List<Component>();
        var stack = new Stack<Component>();
        stack.Push(start);

        while (stack.Count > 0)
        {
            var c = stack.Pop();
            if (c == null || !seen.Add(c)) continue;
            list.Add(c);

            if (c is Atom a)
            {
                foreach (var p in a.Ports)
                    if (p != null && p.Plug != null && p.Plug.bond != null) stack.Push(p.Plug.bond);
            }
            else if (c is Bond b)
            {
                if (b.PlugA.Port != null) stack.Push(b.PlugA.Port.Owner);
                if (b.PlugB.Port != null) stack.Push(b.PlugB.Port.Owner);
            }
        }
        return list;
    }

    static void RecordHolders(XRGrabInteractable grab, List<(IXRSelectInteractor interactor, Vector3 pos)> holders)
    {
        if (grab == null) return;
        foreach (var i in grab.interactorsSelecting.ToList())
            holders.Add((i, i.GetAttachTransform(grab).position));
    }

    static XRGrabInteractable ChooseTarget(List<(List<Component> pieces, XRGrabInteractable grab)> results,
                                           IXRSelectInteractor interactor, Vector3 pos,
                                           Component pinPiece, IXRSelectInteractor pinInteractor)
    {
        if (results.Count == 1) return results[0].grab;

        // La mano que sacó la pieza se queda con ella
        if (pinPiece != null && interactor == pinInteractor)
            foreach (var r in results)
                if (r.pieces.Contains(pinPiece)) return r.grab;

        // Las demás manos, con la parte más grande (y a igualdad, la más cercana)
        var candidates = results.Where(r => pinPiece == null || !r.pieces.Contains(pinPiece)).ToList();
        if (candidates.Count == 0) candidates = results;
        return candidates.OrderByDescending(r => r.pieces.Count)
                         .ThenBy(r => DistanceTo(r.pieces, pos))
                         .First().grab;
    }

    static float DistanceTo(List<Component> pieces, Vector3 pos)
    {
        float best = float.MaxValue;
        foreach (var p in pieces) best = Mathf.Min(best, (p.transform.position - pos).sqrMagnitude);
        return best;
    }

    // Crea la raíz de una molécula nueva con las piezas como hijas
    static Molecule Create(List<Component> pieces)
    {
        var first = pieces[0];
        var firstBody = PieceBody.Ensure(first.gameObject);

        var go = new GameObject("Molécula");
        go.layer = first.gameObject.layer;
        go.transform.SetPositionAndRotation(first.transform.position, first.transform.rotation);

        var rb = go.AddComponent<Rigidbody>();
        firstBody.ApplyBodySettings(rb);
        rb.mass = pieces.Sum(p => PieceBody.Ensure(p.gameObject).Mass);

        foreach (var p in pieces) p.transform.SetParent(go.transform, true);

        // Se crea con las piezas ya como hijas: así el agarre toma los colliders de todas
        var grab = go.AddComponent<XRGrabInteractable>();
        firstBody.ApplyGrabSettings(grab);
        grab.useDynamicAttach = true;                       // se agarra por donde la tomes, sin saltos
        grab.selectMode = InteractableSelectMode.Multiple;  // permite dos manos (la segunda puede sacar una pieza)

        var mol = go.AddComponent<Molecule>();
        mol.Init(rb, grab, pieces);
        return mol;
    }

    void Init(Rigidbody rb, XRGrabInteractable grab, List<Component> pieces)
    {
        Body = rb;
        Grab = grab;
        foreach (var p in pieces)
        {
            if (p is Atom a) atoms.Add(a);
            else if (p is Bond b) bars.Add(b);
        }
        grab.selectEntered.AddListener(OnSelected);
    }

    void DestroyNow()
    {
        if (Grab != null) { Grab.enabled = false; DestroyImmediate(Grab); }
        if (Body != null) DestroyImmediate(Body);
        DestroyImmediate(gameObject);
    }

    // ---------------------------------------------------------------
    // Desarmar con dos manos
    // ---------------------------------------------------------------
    void OnSelected(SelectEnterEventArgs args)
    {
        if (!TwoHandDetach || Time.time < gestureBlockedUntil) return;
        if (Grab.interactorsSelecting.Count < 2) return;                       // con una mano se mueve toda la molécula
        if (RequireBothGrips && !HandGrips.BothPressed(GripThreshold)) return;

        StartCoroutine(DetachNextFrame(args.interactorObject));
    }

    IEnumerator DetachNextFrame(IXRSelectInteractor second)
    {
        yield return null;                                                     // fuera del evento de selección
        if (Grab == null || !Grab.isSelected) yield break;

        var piece = NearestPiece(second.GetAttachTransform(Grab).position);
        if (piece == null) yield break;

        if (piece is Atom a) a.DetachFromBars(second);
        else if (piece is Bond b) b.DetachAll(second);
    }

    // La pieza más cercana a la mano (por sus colliders sólidos)
    Component NearestPiece(Vector3 pos)
    {
        Component best = null;
        float bestD = float.MaxValue;
        foreach (var p in Pieces)
        {
            if (p == null) continue;
            foreach (var c in p.GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger) continue;
                float d = (c.ClosestPoint(pos) - pos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = p; }
            }
        }
        return best;
    }

    // ---------------------------------------------------------------
    // Limpieza
    // ---------------------------------------------------------------
    void LateUpdate()
    {
        atoms.RemoveAll(a => a == null);
        bars.RemoveAll(b => b == null);

        int n = atoms.Count + bars.Count;
        if (n == 0) { Destroy(gameObject); return; }               // se gastó (ej. la transmutadora)
        if (n == 1 && !dissolving)                                 // quedó una sola pieza: vuelve a ser un objeto suelto
        {
            dissolving = true;
            Rebuild(new[] { Pieces.First(p => p != null) });
        }
    }

    // ---------------------------------------------------------------
    // Colisiones temporales entre partes recién separadas
    // ---------------------------------------------------------------
    static void IgnoreBetween(List<List<Component>> comps, float seconds)
    {
        var colliders = comps
            .Select(c => c.SelectMany(p => p.GetComponentsInChildren<Collider>()).Where(col => !col.isTrigger).ToList())
            .ToList();

        var pairs = new List<(Collider, Collider)>();
        for (int i = 0; i < colliders.Count; i++)
            for (int j = i + 1; j < colliders.Count; j++)
                foreach (var a in colliders[i])
                    foreach (var b in colliders[j])
                    {
                        Physics.IgnoreCollision(a, b, true);
                        pairs.Add((a, b));
                    }

        MoleculeRunner.Instance.StartCoroutine(Reenable(pairs, seconds));
    }

    static IEnumerator Reenable(List<(Collider, Collider)> pairs, float seconds)
    {
        yield return new WaitForSeconds(seconds);
        foreach (var (a, b) in pairs)
            if (a != null && b != null) Physics.IgnoreCollision(a, b, false);
    }
}

/// Objeto oculto que ejecuta tareas que deben sobrevivir a la destrucción de las moléculas
class MoleculeRunner : MonoBehaviour
{
    static MoleculeRunner instance;

    public static MoleculeRunner Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("MoleculeRunner");
                instance = go.AddComponent<MoleculeRunner>();
            }
            return instance;
        }
    }
}

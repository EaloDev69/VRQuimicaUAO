using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// La Máquina Transmutadora.
/// Entrada: la molécula (átomos y barras) se mete en el hueco (este objeto debe tener un Collider en modo Trigger).
/// Salida:
///   - Gas:    hace falta una botella (SampleItem tipo Bottle) pegada al socket de gases.
///   - Líquido: baja por el pico y llena el vaso (SampleItem tipo Cup) que esté bajo él.
///             El tamaño del vaso decide cuánto tarda en llenarse, así nunca se riega líquido.
/// Botones (LabButton): Iniciar -> StartReaction, Cancelar -> Cancel, Limpiar -> ClearTable.
[RequireComponent(typeof(Collider))]
public class TransmutatorMachine : MonoBehaviour
{
    public enum MachineState { Idle, Processing }

    [Header("Datos")]
    public RecipeCatalog catalog;            // catálogo único de recetas (el mismo de la evaluadora y los pedidos)

    [Header("Mesa")]
    public Collider tableZone;               // zona que limpia "Limpiar mesa"

    [Header("Salida de gases")]
    public XRSocketInteractor gasSocket;     // socket donde se pega la botella

    [Header("Salida de líquidos")]
    public Transform spout;                  // pico por donde baja el líquido
    public Collider cupZone;                 // BoxCollider o SphereCollider bajo el pico (donde va el vaso)

    [Header("Efectos")]
    public GameObject failPofPrefab;         // "pof" de humo de colores (sale por el pico)
    public float effectLifetime = 8f;

    [Header("Tiempos")]
    public float processTime = 2.5f;         // análisis de la molécula
    public float gasFillSeconds = 1.5f;      // llenado de la botella
    public float secondsPer100Ml = 1.2f;     // llenado del vaso: segundos por cada 100 ml
    public float minPourSeconds = 1f;
    public float maxPourSeconds = 8f;

    [Header("Feedback (conectar luces, sonido, animación)")]
    public UnityEvent onStarted;
    public UnityEvent onPourStarted;         // empieza a salir gas o líquido
    public UnityEvent onSuccess;
    public UnityEvent onFail;                // molécula incorrecta: "pof"
    public UnityEvent onCancelled;
    public UnityEvent onNothingInside;
    public UnityEvent onMoleculeHeld;
    public UnityEvent onNoContainer;         // falta la botella o el vaso (o ya está lleno)
    public UnityEvent onSpill;               // retiraron el contenedor mientras se llenaba

    // Eventos de C# para desacoplar de OrderManager / ScoreManager
    public event Action<RecipeData, SampleItem> Synthesized;
    public event Action<ValidationState> Failed;
    public event Action Spilled;

    public MachineState State { get; private set; } = MachineState.Idle;

    // Átomos dentro del hueco (contador por si un átomo tiene varios colliders dentro)
    readonly Dictionary<Atom, int> inside = new Dictionary<Atom, int>();

    Coroutine running;
    List<Atom> currentAtoms;
    HashSet<Bond> currentBars;
    SampleItem filling;                      // contenedor que se está llenando
    GameObject activeFx;

    void Reset() { GetComponent<Collider>().isTrigger = true; }

    // ---------------------------------------------------------------
    // Hueco de entrada
    // ---------------------------------------------------------------
    void OnTriggerEnter(Collider other)
    {
        var atom = other.GetComponentInParent<Atom>();
        if (atom == null) return;
        inside[atom] = inside.TryGetValue(atom, out int n) ? n + 1 : 1;
    }

    void OnTriggerExit(Collider other)
    {
        var atom = other.GetComponentInParent<Atom>();
        if (atom == null || !inside.TryGetValue(atom, out int n)) return;
        if (n <= 1) inside.Remove(atom); else inside[atom] = n - 1;
    }

    Atom FirstAtomInside()
    {
        foreach (var k in inside.Keys.ToList()) if (k == null) inside.Remove(k);
        return inside.Keys.FirstOrDefault();
    }

    public bool HasMoleculeInside => FirstAtomInside() != null;

    // ---------------------------------------------------------------
    // Botones
    // ---------------------------------------------------------------
    public void StartReaction()
    {
        if (State != MachineState.Idle) return;
        if (catalog == null) { Debug.LogError("[TransmutatorMachine] Falta asignar el RecipeCatalog.", this); return; }

        var start = FirstAtomInside();
        if (start == null) { onNothingInside.Invoke(); return; }

        var graph = MoleculeGraph.FromAtom(start, out var atoms);
        var bars = CollectBars(atoms);

        if (IsHeld(atoms, bars)) { onMoleculeHeld.Invoke(); return; }   // hay que soltarla primero

        var state = MoleculeValidator.Validate(graph, catalog.recipes, out var recipe);

        // Si es correcta, el contenedor tiene que estar listo ANTES de gastar la molécula
        SampleItem container = null;
        if (state == ValidationState.Correct && recipe != null)
        {
            container = FindContainerFor(recipe);
            if (container == null) { onNoContainer.Invoke(); return; }
        }

        running = StartCoroutine(Process(state, recipe, atoms, bars, container));
    }

    public void Cancel()
    {
        if (State != MachineState.Processing) return;
        if (running != null) StopCoroutine(running);
        running = null;

        StopFx();
        if (filling != null) filling.ResetContents();
        filling = null;

        Release();
        onCancelled.Invoke();
    }

    /// Destruye todo lo suelto en la mesa (no toca lo que está en la mano).
    public void ClearTable()
    {
        if (State == MachineState.Processing || tableZone == null) return;

        var b = tableZone.bounds;
        var hits = Physics.OverlapBox(b.center, b.extents, tableZone.transform.rotation,
                                      ~0, QueryTriggerInteraction.Ignore);
        var atoms = new HashSet<Atom>();
        foreach (var h in hits)
        {
            var a = h.GetComponentInParent<Atom>();
            if (a != null) atoms.Add(a);
        }
        var list = atoms.ToList();
        var bars = CollectBars(list);
        foreach (var h in hits)
        {
            var bar = h.GetComponentInParent<Bond>();
            if (bar != null) bars.Add(bar);
        }
        if (IsHeld(list, bars)) { onMoleculeHeld.Invoke(); return; }
        Consume(list, bars);
    }

    // ---------------------------------------------------------------
    // Proceso
    // ---------------------------------------------------------------
    IEnumerator Process(ValidationState state, RecipeData recipe,
                        List<Atom> atoms, HashSet<Bond> bars, SampleItem container)
    {
        State = MachineState.Processing;
        currentAtoms = atoms; currentBars = bars;
        SetLocked(atoms, bars, true);          // la molécula no se mueve mientras trabaja
        onStarted.Invoke();

        yield return new WaitForSeconds(processTime);

        // ----- Molécula incorrecta: "pof" -----
        if (state != ValidationState.Correct || recipe == null)
        {
            SpawnFx(failPofPrefab, spout != null ? spout : transform);
            Release();
            onFail.Invoke();
            Failed?.Invoke(state);
            yield break;
        }

        // ----- Molécula correcta: el contenedor debe seguir ahí -----
        if (!IsStillThere(container))
        {
            Release();
            onNoContainer.Invoke();
            yield break;
        }

        bool gas = recipe.category == CompoundCategory.Gas;
        float duration = gas
            ? gasFillSeconds
            : Mathf.Clamp(container.capacityMl / 100f * secondsPer100Ml, minPourSeconds, maxPourSeconds);

        filling = container;
        onPourStarted.Invoke();
        activeFx = SpawnFx(recipe.effectPrefab, gas ? gasSocket.transform : spout);

        float t = 0f;
        bool spilled = false;
        while (t < duration)
        {
            if (!IsStillThere(container)) { spilled = true; break; }
            t += Time.deltaTime;
            container.SetFillLevel(t / duration, recipe.sampleColor);
            yield return null;
        }

        StopFx();

        if (spilled)
        {
            if (container != null) container.ResetContents();
            filling = null;
            Release();
            onSpill.Invoke();
            Spilled?.Invoke();
            yield break;
        }

        // ----- Éxito: se gasta la molécula y el contenedor pasa a ser la muestra -----
        Consume(atoms, bars);
        container.CompleteFill(recipe);
        filling = null;

        running = null;
        currentAtoms = null; currentBars = null;
        State = MachineState.Idle;

        onSuccess.Invoke();
        Synthesized?.Invoke(recipe, container);
    }

    // Vuelve a Idle y desbloquea la molécula (fallo, derrame o cancelación)
    void Release()
    {
        SetLocked(currentAtoms, currentBars, false);
        currentAtoms = null; currentBars = null;
        running = null;
        State = MachineState.Idle;
    }

    // ---------------------------------------------------------------
    // Contenedores
    // ---------------------------------------------------------------
    SampleItem FindContainerFor(RecipeData recipe)
    {
        return recipe.category == CompoundCategory.Gas ? FindBottle() : FindCup();
    }

    SampleItem FindBottle()
    {
        if (gasSocket == null || !gasSocket.hasSelection) return null;
        foreach (var i in gasSocket.interactablesSelected)
        {
            var s = i.transform.GetComponentInParent<SampleItem>();
            if (s != null && s.kind == ContainerKind.Bottle && s.IsEmpty) return s;
        }
        return null;
    }

    SampleItem FindCup()
    {
        Vector3 from = spout != null ? spout.position : transform.position;
        return CupsInZone().Where(c => c.IsEmpty)
                           .OrderBy(c => (c.transform.position - from).sqrMagnitude)
                           .FirstOrDefault();
    }

    List<SampleItem> CupsInZone()
    {
        var set = new HashSet<SampleItem>();
        foreach (var c in OverlapCupZone())
        {
            var s = c.GetComponentInParent<SampleItem>();
            if (s != null && s.kind == ContainerKind.Cup) set.Add(s);
        }
        return set.ToList();
    }

    // ¿Sigue el contenedor en su sitio (en el socket o bajo el pico)?
    bool IsStillThere(SampleItem c)
    {
        if (c == null) return false;

        if (c.kind == ContainerKind.Bottle)
        {
            if (gasSocket == null || !gasSocket.hasSelection) return false;
            foreach (var i in gasSocket.interactablesSelected)
                if (i.transform.GetComponentInParent<SampleItem>() == c) return true;
            return false;
        }
        return CupsInZone().Contains(c);
    }

    Collider[] OverlapCupZone()
    {
        if (cupZone == null) return new Collider[0];
        var t = cupZone.transform;

        if (cupZone is SphereCollider sphere)
        {
            float r = sphere.radius * Mathf.Max(t.lossyScale.x, t.lossyScale.y, t.lossyScale.z);
            return Physics.OverlapSphere(t.TransformPoint(sphere.center), r, ~0, QueryTriggerInteraction.Ignore);
        }
        if (cupZone is BoxCollider box)
        {
            var half = Vector3.Scale(box.size, t.lossyScale) * 0.5f;
            return Physics.OverlapBox(t.TransformPoint(box.center), half, t.rotation, ~0, QueryTriggerInteraction.Ignore);
        }

        Debug.LogWarning("[TransmutatorMachine] cupZone debe ser un BoxCollider o un SphereCollider.", this);
        return new Collider[0];
    }

    // ---------------------------------------------------------------
    // Utilidades
    // ---------------------------------------------------------------
    GameObject SpawnFx(GameObject prefab, Transform anchor)
    {
        if (prefab == null || anchor == null) return null;
        var fx = Instantiate(prefab, anchor.position, anchor.rotation);
        Destroy(fx, effectLifetime);
        return fx;
    }

    // Deja de emitir y limpia el efecto sin cortarlo de golpe
    void StopFx()
    {
        if (activeFx == null) return;
        foreach (var ps in activeFx.GetComponentsInChildren<ParticleSystem>())
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        Destroy(activeFx, 3f);
        activeFx = null;
    }

    static HashSet<Bond> CollectBars(List<Atom> atoms)
    {
        var bars = new HashSet<Bond>();
        foreach (var a in atoms)
        {
            foreach (var b in a.Bars) bars.Add(b);
            foreach (var p in a.Ports)                       // incluye barras con un solo extremo puesto
                if (p != null && p.Plug != null && p.Plug.bond != null) bars.Add(p.Plug.bond);
        }
        return bars;
    }

    static bool IsHeld(List<Atom> atoms, HashSet<Bond> bars)
    {
        foreach (var a in atoms)
        {
            var g = a.GetComponent<XRGrabInteractable>();
            if (g != null && g.isSelected) return true;
        }
        foreach (var b in bars)
        {
            var g = b.GetComponent<XRGrabInteractable>();
            if (g != null && g.isSelected) return true;
        }
        return false;
    }

    static void SetLocked(List<Atom> atoms, HashSet<Bond> bars, bool locked)
    {
        if (atoms == null) return;
        foreach (var a in atoms)
        {
            if (a == null) continue;
            var g = a.GetComponent<XRGrabInteractable>();
            if (g != null) g.enabled = !locked;
        }
        if (bars == null) return;
        foreach (var b in bars)
        {
            if (b == null) continue;
            var g = b.GetComponent<XRGrabInteractable>();
            if (g != null) g.enabled = !locked;
        }
    }

    void Consume(List<Atom> atoms, HashSet<Bond> bars)
    {
        foreach (var b in bars) if (b != null) Destroy(b.gameObject);
        foreach (var a in atoms)
        {
            if (a == null) continue;
            inside.Remove(a);
            Destroy(a.gameObject);
        }
    }
}
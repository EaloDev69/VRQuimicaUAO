using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public enum LeverFail { Pressure, Temperature }

/// La Máquina Transmutadora.
/// Entrada: la molécula (átomos y barras) se mete en el hueco (este objeto debe tener un Collider en modo Trigger).
/// Salida:
///   - Gas:    hace falta una botella (SampleItem tipo Bottle) pegada al socket de gases.
///   - Líquido: baja por el pico y llena el vaso (SampleItem tipo Cup) que esté bajo él.
///             El tamaño del vaso decide cuánto tarda en llenarse, así nunca se riega líquido.
/// Botones (LabButton): Iniciar -> StartReaction, Cancelar -> Cancel.
[RequireComponent(typeof(Collider))]
public class TransmutatorMachine : MonoBehaviour
{
    public enum MachineState { Idle, Processing }

    [Header("Datos")]
    public RecipeCatalog catalog;            // catálogo único de recetas (el mismo de la evaluadora y los pedidos)

    [Header("Salida de gases")]
    public XRSocketInteractor gasSocket;     // socket donde se pega la botella

    [Header("Salida de líquidos")]
    public Transform spout;                  // pico por donde baja el líquido
    public Collider cupZone;                 // BoxCollider o SphereCollider bajo el pico (donde va el vaso)

    [Header("Palancas de presión y temperatura")]
    public LabLever pressureLever;
    public LabLever temperatureLever;
    [Tooltip("Dónde aparece el cubo de residuo si una palanca está mal. Vacío = el pico")]
    public Transform junkSpawnPoint;
    public GameObject pressureFailCube;      // cubo negro (presión incorrecta)
    public GameObject temperatureFailCube;   // cubo gris pequeño (temperatura incorrecta)
    [Tooltip("Segundos hasta que el cubo desaparece. 0 = no desaparece")]
    public float junkLifetime = 30f;
    [Tooltip("Si una palanca está mal, la molécula se convierte en el cubo y se gasta")]
    public bool consumeMoleculeOnLeverFail = true;

    [Header("Chorro de líquido (sin animación)")]
    [Tooltip("LineRenderer de 2 puntos: se estira solo del pico al vaso mientras se llena")]
    public LineRenderer liquidStream;

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
    public UnityEvent onFail;                // molécula o palanca incorrecta: sonido de fallo
    public UnityEvent onBlocked;             // presión y temperatura mal: la máquina no se activa
    public UnityEvent onCancelled;
    public UnityEvent onNothingInside;
    public UnityEvent onMoleculeHeld;
    public UnityEvent onNoContainer;         // falta la botella o el vaso (o ya está lleno)
    public UnityEvent onSpill;               // retiraron el contenedor mientras se llenaba

    // Eventos de C# para desacoplar de OrderManager / ScoreManager
    public event Action<RecipeData, SampleItem> Synthesized;
    public event Action<ValidationState> Failed;
    public event Action Spilled;
    public event Action<LeverFail> LeverFailed;

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

        // Palancas: solo se comparan si la molécula es correcta (ya se conoce la receta)
        if (state == ValidationState.Correct && recipe != null)
        {
            bool pressureOk = pressureLever == null || pressureLever.Level == recipe.requiredPressure;
            bool temperatureOk = temperatureLever == null || temperatureLever.Level == recipe.requiredTemperature;

            if (!pressureOk && !temperatureOk) { onBlocked.Invoke(); return; }   // ambas mal: no se deja activar

            if (!pressureOk || !temperatureOk)
            {
                var kind = pressureOk ? LeverFail.Temperature : LeverFail.Pressure;
                running = StartCoroutine(ProcessLeverFail(kind, atoms, bars));
                return;
            }
        }

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

        if (!gas && liquidStream != null)
        {
            liquidStream.positionCount = 2;
            liquidStream.startColor = recipe.sampleColor;
            liquidStream.endColor = recipe.sampleColor;
            liquidStream.enabled = true;
        }

        float t = 0f;
        bool spilled = false;
        while (t < duration)
        {
            if (!IsStillThere(container)) { spilled = true; break; }
            t += Time.deltaTime;
            container.SetFillLevel(t / duration, recipe.sampleColor);
            if (!gas && liquidStream != null)
            {
                liquidStream.SetPosition(0, spout.position);
                liquidStream.SetPosition(1, container.StreamPoint);
            }
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

    // Una palanca mal puesta: la máquina trabaja, suelta un cubo de residuo y suena el fallo
    IEnumerator ProcessLeverFail(LeverFail kind, List<Atom> atoms, HashSet<Bond> bars)
    {
        State = MachineState.Processing;
        currentAtoms = atoms; currentBars = bars;
        SetLocked(atoms, bars, true);
        onStarted.Invoke();

        yield return new WaitForSeconds(processTime);

        if (consumeMoleculeOnLeverFail)
        {
            Consume(atoms, bars);
            currentAtoms = null; currentBars = null;
        }

        var prefab = kind == LeverFail.Pressure ? pressureFailCube : temperatureFailCube;
        var point = junkSpawnPoint != null ? junkSpawnPoint : (spout != null ? spout : transform);
        if (prefab != null)
        {
            var cube = Instantiate(prefab, point.position, point.rotation);
            if (junkLifetime > 0f) Destroy(cube, junkLifetime);
        }

        Release();                      // desbloquea la molécula si no se gastó
        onFail.Invoke();
        LeverFailed?.Invoke(kind);
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
        if (liquidStream != null) liquidStream.enabled = false;
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
            if (a == null || IsMoleculeChild(a.transform)) continue;   // las piezas hijas no tienen agarre propio
            var g = a.GetComponent<XRGrabInteractable>();
            if (g != null) g.enabled = !locked;
        }
        if (bars == null) return;
        foreach (var b in bars)
        {
            if (b == null || IsMoleculeChild(b.transform)) continue;
            var g = b.GetComponent<XRGrabInteractable>();
            if (g != null) g.enabled = !locked;
        }
    }

    static bool IsMoleculeChild(Transform t) => t.parent != null && t.parent.GetComponent<Molecule>() != null;

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

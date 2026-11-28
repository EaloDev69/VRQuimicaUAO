using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// Animaciones y efectos visuales del juego. Se crea solo al dar Play (no hay que ponerlo en la escena) y solo
/// ESCUCHA los eventos que ya emiten los scripts: no cambia la lógica de nada.
///
///  Armado de moléculas
///    - Unir barra y átomo: chispas del color del átomo + anillo en el punto de unión + vibración corta.
///    - Separar una pieza: chispas grises.
///  Spawners de átomos y barras
///    - La pieza nueva aparece creciendo (pop) con un destello en la boquilla.
///    - Sin cesta / cesta llena: humo rojo y aviso flotante. Borrar: humo gris.
///  Botones y palancas
///    - Botones sin "visual" asignado: se aplastan al presionarlos. Palancas: chispa del color del nivel.
///  Transmutadora
///    - El contorno de la entrada respira; brilla más cuando acercas una molécula completa en la mano.
///    - Molécula tomada: destello verde y remolino. Devuelta: destello rojo y aviso con el motivo.
///    - Procesando: luz ámbar que pulsa y partículas girando en la entrada.
///    - Éxito: chispas en la salida y brillo del color de la muestra alrededor del contenedor.
///    - Fallo: humo rojo (receta) o humo oscuro (palanca), con aviso flotante.
///  Encargos y entrega
///    - Aceptar encargo: el panel late y brilla. Poco tiempo: el panel tiembla. Encargo perdido: tiembla y aviso rojo.
///    - Entrega aceptada: confeti y "+puntos" flotando. Rechazada: humo rojo y el motivo.
///    - Nivel completado: confeti grande.
///  Menú de la muñeca
///    - El menú y sus páginas aparecen con una animación corta (crecen y se desvanecen hacia dentro).
///
/// Para apagar todo: FeedbackFX.Enabled = false antes de dar Play.
public class FeedbackFX : MonoBehaviour
{
    public static bool Enabled = false;   // APAGADO: vista previa pendiente de aprobación
    public static FeedbackFX Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
        Create();
    }

    static void OnLoaded(Scene s, LoadSceneMode m) => Create();

    static void Create()
    {
        if (!Enabled || Instance != null) return;
        new GameObject("FeedbackFX (auto)").AddComponent<FeedbackFX>();
    }

    // ---------------------------------------------------------------
    // Colores
    // ---------------------------------------------------------------
    static readonly Color Cyan = new Color(0.25f, 0.9f, 1f);
    static readonly Color Green = new Color(0.3f, 1f, 0.45f);
    static readonly Color Red = new Color(1f, 0.25f, 0.2f);
    static readonly Color Amber = new Color(1f, 0.7f, 0.2f);
    static readonly Color Gold = new Color(1f, 0.85f, 0.25f);
    static readonly Color Smoke = new Color(0.25f, 0.25f, 0.28f);
    static readonly Color Grey = new Color(0.75f, 0.78f, 0.82f);

    // ---------------------------------------------------------------
    // Partículas (un sistema por tipo; se reubica y se emite: sin crear objetos en cada efecto)
    // ---------------------------------------------------------------
    enum Fx { Sparks, Puff, Glitter, Confetti, Ring }
    readonly Dictionary<Fx, ParticleSystem> systems = new Dictionary<Fx, ParticleSystem>();
    Material particleMat;

    // Referencias de la escena
    TransmutatorMachine machine;
    TransmutatorAutoStart autoStart;
    Collider machineEntry;
    LineRenderer entryOutline;
    Color outlineBase;
    float outlineBaseWidth;
    Light machineLight;
    DeliveryEvaluator evaluator;
    OrderManager orders;
    GestorEncargosVR gestor;
    RectTransform gestorPanel;
    Vector3 panelBaseScale, panelBasePos;
    Coroutine panelAnim;

    float outlineFlashUntil; Color outlineFlashColor;
    float nearUntil;
    float swirlTimer;
    bool wasProcessing;
    float lightFlashUntil; Color lightFlashColor;

    readonly List<System.Action> unsubscribe = new List<System.Action>();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildParticles();
    }

    void Start() => Hook();

    void OnDestroy()
    {
        foreach (var u in unsubscribe) { try { u(); } catch { } }
        unsubscribe.Clear();
        if (Instance == this) Instance = null;
    }

    // ===============================================================
    // Conexión con los eventos del juego
    // ===============================================================
    void Hook()
    {
        // Armado
        Bond.AnyAttached += OnAttached; unsubscribe.Add(() => Bond.AnyAttached -= OnAttached);
        Bond.AnyDetached += OnDetached; unsubscribe.Add(() => Bond.AnyDetached -= OnDetached);

        // Botones y palancas
        LabButton.AnyPressed += OnButton; unsubscribe.Add(() => LabButton.AnyPressed -= OnButton);
        foreach (var lever in FindObjectsByType<LabLever>(FindObjectsSortMode.None))
        {
            var l = lever;
            System.Action<LeverLevel> h = lvl => OnLever(l, lvl);
            l.LevelChanged += h; unsubscribe.Add(() => { if (l != null) l.LevelChanged -= h; });
        }

        // Spawners
        foreach (var sp in FindObjectsByType<AtomSpawner>(FindObjectsSortMode.None)) HookAtomSpawner(sp);
        foreach (var sp in FindObjectsByType<BarSpawner>(FindObjectsSortMode.None)) HookBarSpawner(sp);

        // Transmutadora
        machine = FindFirstObjectByType<TransmutatorMachine>();
        if (machine != null)
        {
            autoStart = machine.GetComponent<TransmutatorAutoStart>();
            machineEntry = machine.GetComponents<Collider>().FirstOrDefault(c => c.isTrigger && c.enabled) ?? machine.GetComponent<Collider>();
            System.Action<RecipeData, SampleItem> syn = OnSynthesized;
            System.Action<ValidationState> fail = s => OnRecipeFailed();
            System.Action<LeverFail> lf = OnLeverFail;
            machine.Synthesized += syn; machine.Failed += fail; machine.LeverFailed += lf;
            unsubscribe.Add(() => { if (machine != null) { machine.Synthesized -= syn; machine.Failed -= fail; machine.LeverFailed -= lf; } });
            AddListener(machine.onBlocked, () => Warn(EntryCenter(), "Presión y temperatura incorrectas", Red));
            AddListener(machine.onPourStarted, OnPourStarted);
            AddListener(machine.onSpill, () => { Burst(Fx.Puff, PourPoint(), Red, 10); Warn(PourPoint(), "¡Se regó! Retiraste el contenedor", Red); });
            BuildMachineLight();
        }
        TransmutatorAutoStart.MoleculeAbsorbed += OnAbsorbed; unsubscribe.Add(() => TransmutatorAutoStart.MoleculeAbsorbed -= OnAbsorbed);
        TransmutatorAutoStart.MoleculeEjected += OnEjected; unsubscribe.Add(() => TransmutatorAutoStart.MoleculeEjected -= OnEjected);
        TransmutatorAutoStart.CompleteMoleculeNear += OnNear; unsubscribe.Add(() => TransmutatorAutoStart.CompleteMoleculeNear -= OnNear);

        // Entrega
        evaluator = FindFirstObjectByType<DeliveryEvaluator>();
        if (evaluator != null)
        {
            System.Action<DeliveryResult> d = OnDelivered;
            System.Action<SampleItem, RejectReason> r = OnRejected;
            evaluator.Delivered += d; evaluator.Rejected += r;
            unsubscribe.Add(() => { if (evaluator != null) { evaluator.Delivered -= d; evaluator.Rejected -= r; } });
        }

        // Encargos
        orders = evaluator != null && evaluator.orders != null ? evaluator.orders : FindFirstObjectByType<OrderManager>();
        gestor = FindFirstObjectByType<GestorEncargosVR>();
        if (gestor != null)
        {
            gestorPanel = gestor.transform as RectTransform;
            if (gestorPanel != null) { panelBaseScale = gestorPanel.localScale; panelBasePos = gestorPanel.localPosition; }
        }
        if (orders != null)
        {
            System.Action<Order> acc = o => OnOrderAccepted();
            System.Action<Order> lost = o => OnOrderLost();
            System.Action<int> lvl = OnLevelCompleted;
            orders.OrderAccepted += acc; orders.OrderLost += lost; orders.LevelCompleted += lvl;
            unsubscribe.Add(() => { if (orders != null) { orders.OrderAccepted -= acc; orders.OrderLost -= lost; orders.LevelCompleted -= lvl; } });
            AddListener(orders.onLowTime, OnLowTime);
        }

        // Menú de la muñeca: animación al aparecer
        foreach (var menu in FindObjectsByType<WristMenu>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            AddPop(menu.gameObject, 0.85f);
            AddPop(menu.mainPage, 0.94f);
            AddPop(menu.tutorialPage, 0.94f);
            AddPop(menu.glossaryPage, 0.94f);
        }
    }

    void AddListener(UnityEngine.Events.UnityEvent ev, UnityEngine.Events.UnityAction a)
    {
        if (ev == null) return;
        ev.AddListener(a);
        unsubscribe.Add(() => ev.RemoveListener(a));
    }

    static void AddPop(GameObject go, float from)
    {
        if (go == null || go.GetComponent<UIPopIn>() != null) return;
        go.AddComponent<UIPopIn>().fromScale = from;
    }

    // ===============================================================
    // Armado
    // ===============================================================
    void OnAttached(Bond bar, Atom atom)
    {
        if (bar == null || atom == null) return;
        Vector3 p = atom.transform.position;
        foreach (var plug in new[] { bar.PlugA, bar.PlugB })
            if (plug != null && plug.Port != null && plug.Port.Owner == atom) { p = plug.transform.position; break; }

        Color c = atom.data != null ? Color.Lerp(atom.data.color, Color.white, 0.25f) : Cyan;
        Burst(Fx.Sparks, p, c, 14);
        var dir = (atom.transform.position - bar.transform.position);
        Ring(p, dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.up, Cyan, 16);

        // Vibración corta en la mano que sostiene la molécula
        var mol = atom.GetComponentInParent<Molecule>() ?? bar.GetComponent<Molecule>();
        var grab = mol != null ? mol.Grab : bar.GetComponent<XRGrabInteractable>();
        if (grab != null && grab.isSelected)
            foreach (var i in grab.interactorsSelecting)
                if (i is XRBaseInputInteractor input) input.SendHapticImpulse(0.25f, 0.05f);
    }

    void OnDetached(Bond bar)
    {
        if (bar == null) return;
        Burst(Fx.Sparks, bar.transform.position, Grey, 8);
    }

    // ===============================================================
    // Spawners
    // ===============================================================
    void HookAtomSpawner(AtomSpawner sp)
    {
        AddListener(sp.onSpawned, () =>
        {
            if (sp == null || sp.nozzle == null) return;
            StartCoroutine(NextFrame(() =>
            {
                if (sp == null || sp.nozzle == null) return;
                var atom = FindNewPiece<Atom>(sp.nozzle.position);
                Color c = atom != null && atom.data != null ? atom.data.color : Cyan;
                Burst(Fx.Glitter, sp.nozzle.position, Color.Lerp(c, Color.white, 0.3f), 12);
                if (atom != null) StartCoroutine(PopIn(atom.transform));
            }));
        });
        AddListener(sp.onNoBasket, () => Warn(NozzleOf(sp), "Pon una cesta bajo la boquilla", Red));
        AddListener(sp.onFull, () => Warn(NozzleOf(sp), "La cesta está llena", Amber));
        AddListener(sp.onDeleted, () => Burst(Fx.Puff, BasketCenter(sp.basketZone, NozzleOf(sp)), Grey, 8));
    }

    void HookBarSpawner(BarSpawner sp)
    {
        AddListener(sp.onSpawned, () =>
        {
            if (sp == null || sp.nozzle == null) return;
            StartCoroutine(NextFrame(() =>
            {
                if (sp == null || sp.nozzle == null) return;
                var bar = FindNewPiece<Bond>(sp.nozzle.position);
                Burst(Fx.Glitter, sp.nozzle.position, Color.Lerp(Cyan, Color.white, 0.3f), 12);
                if (bar != null) StartCoroutine(PopIn(bar.transform));
            }));
        });
        AddListener(sp.onNoBasket, () => Warn(sp.nozzle != null ? sp.nozzle.position : sp.transform.position, "Pon una cesta bajo la boquilla", Red));
        AddListener(sp.onFull, () => Warn(sp.nozzle != null ? sp.nozzle.position : sp.transform.position, "La cesta está llena", Amber));
        AddListener(sp.onDeleted, () => Burst(Fx.Puff, BasketCenter(sp.basketZone, sp.transform.position), Grey, 8));
    }

    static IEnumerator NextFrame(System.Action a) { yield return null; a(); }

    static Vector3 NozzleOf(AtomSpawner sp) => sp.nozzle != null ? sp.nozzle.position : sp.transform.position;
    static Vector3 BasketCenter(Collider zone, Vector3 fallback) => zone != null ? zone.bounds.center : fallback;

    // La pieza recién creada: la más cercana a la boquilla, suelta (sin padre)
    static T FindNewPiece<T>(Vector3 at) where T : Component
    {
        T best = null; float bestD = 0.2f * 0.2f;
        foreach (var c in Physics.OverlapSphere(at, 0.2f, ~0, QueryTriggerInteraction.Ignore))
        {
            var p = c.GetComponentInParent<T>();
            if (p == null || p.transform.parent != null) continue;
            float d = (p.transform.position - at).sqrMagnitude;
            if (d < bestD) { bestD = d; best = p; }
        }
        return best;
    }

    // Aparece creciendo con un pequeño rebote. Si la agarran o la unen antes de terminar, queda en su tamaño.
    IEnumerator PopIn(Transform t)
    {
        if (t == null) yield break;
        Vector3 full = t.localScale;
        var grab = t.GetComponent<XRGrabInteractable>();
        const float dur = 0.22f;
        for (float e = 0f; e < dur; e += Time.deltaTime)
        {
            if (t == null) yield break;
            if (t.parent != null || (grab != null && grab.isSelected)) break;
            t.localScale = full * EaseOutBack(Mathf.Lerp(0.15f, 1f, e / dur));
            yield return null;
        }
        if (t == null) yield break;
        if (t.parent == null) t.localScale = full;
        else
        {
            // ya es hija de una molécula: tamaño mundial original
            var ps = t.parent.lossyScale;
            t.localScale = new Vector3(Div(full.x, ps.x), Div(full.y, ps.y), Div(full.z, ps.z));
        }
    }

    static float Div(float a, float b) => Mathf.Abs(b) < 1e-6f ? a : a / b;

    // ===============================================================
    // Botones y palancas
    // ===============================================================
    readonly HashSet<LabButton> squashing = new HashSet<LabButton>();

    void OnButton(LabButton b)
    {
        if (b == null) return;
        Burst(Fx.Sparks, ButtonTop(b), Color.Lerp(Cyan, Color.white, 0.4f), 6);
        if (b.visual == null && !squashing.Contains(b)) StartCoroutine(Squash(b));
    }

    static Vector3 ButtonTop(LabButton b)
    {
        var r = b.GetComponent<Renderer>();
        return r != null ? r.bounds.center + Vector3.up * r.bounds.extents.y : b.transform.position;
    }

    // Botón sin tapa asignada: se aplasta un poco y vuelve
    IEnumerator Squash(LabButton b)
    {
        squashing.Add(b);
        var t = b.transform;
        Vector3 s = t.localScale;
        const float down = 0.06f, up = 0.14f;
        for (float e = 0f; e < down + up; e += Time.deltaTime)
        {
            if (t == null) break;
            float k = e < down ? e / down : 1f - (e - down) / up;
            t.localScale = new Vector3(s.x, s.y * (1f - 0.3f * k), s.z);
            yield return null;
        }
        if (t != null) t.localScale = s;
        squashing.Remove(b);
    }

    void OnLever(LabLever lever, LeverLevel lvl)
    {
        if (lever == null) return;
        Color c = lvl == LeverLevel.Low ? new Color(0.3f, 0.6f, 1f) : lvl == LeverLevel.Medium ? Green : Red;
        var r = lever.GetComponentInChildren<Renderer>();
        Burst(Fx.Sparks, r != null ? r.bounds.center : lever.transform.position, c, 6);
    }

    // ===============================================================
    // Transmutadora
    // ===============================================================
    Vector3 EntryCenter() => machineEntry != null ? machineEntry.bounds.center : (machine != null ? machine.transform.position : transform.position);

    Vector3 PourPoint()
    {
        if (machine == null) return transform.position;
        if (machine.spout != null) return machine.spout.position;
        return machine.transform.position;
    }

    void BuildMachineLight()
    {
        var go = new GameObject("FeedbackFX luz transmutadora");
        go.transform.SetParent(machine.transform, false);
        go.transform.position = EntryCenter();
        machineLight = go.AddComponent<Light>();
        machineLight.type = LightType.Point;
        machineLight.range = 1.4f;
        machineLight.intensity = 0f;
        machineLight.shadows = LightShadows.None;
        machineLight.enabled = false;
    }

    void FindOutline()
    {
        if (entryOutline != null || machineEntry == null) return;
        var t = machineEntry.transform.Find("Entrada (contorno)");
        if (t == null) return;
        entryOutline = t.GetComponent<LineRenderer>();
        if (entryOutline != null) { outlineBase = entryOutline.startColor; outlineBaseWidth = entryOutline.widthMultiplier; }
    }

    void OnAbsorbed(string desc)
    {
        FlashOutline(Green, 0.6f);
        FlashLight(Green, 0.4f);
        Ring(EntryCenter(), Vector3.up, Green, 24);
        Burst(Fx.Glitter, EntryCenter(), Green, 16);
    }

    void OnEjected(string reason)
    {
        FlashOutline(Red, 0.9f);
        FlashLight(Red, 0.4f);
        Burst(Fx.Puff, EntryCenter(), Red, 12);
        string msg = string.IsNullOrEmpty(reason) ? "Molécula devuelta"
                   : reason.StartsWith("faltaba") ? "Falta " + reason.Substring("faltaba".Length).Trim()
                   : reason.Contains("palancas") ? "Revisa las palancas"
                   : "La reacción falló";
        Warn(EntryCenter(), msg, Red);
    }

    void OnNear(string formula, float distance, bool inHand)
    {
        if (inHand && distance < 0.6f) nearUntil = Time.time + 0.25f;
    }

    void OnPourStarted()
    {
        Burst(Fx.Sparks, PourPoint(), Gold, 18);
        FlashLight(Gold, 0.5f);
    }

    void OnSynthesized(RecipeData recipe, SampleItem container)
    {
        Color c = recipe != null ? recipe.sampleColor : Green;
        if (c.a < 0.1f) c = Green;
        c.a = 1f;
        Vector3 at = container != null ? CenterOf(container.transform) : PourPoint();
        Burst(Fx.Glitter, at, Color.Lerp(c, Color.white, 0.2f), 30);
        Ring(at, Vector3.up, c, 20);
        FlashLight(Green, 0.6f);
        FlashOutline(Green, 0.8f);
        string name = recipe != null ? (string.IsNullOrEmpty(recipe.displayName) ? recipe.name : recipe.displayName) : "Muestra";
        FloatText(at + Vector3.up * 0.18f, $"¡{name} lista!", Green, 1f);
    }

    void OnRecipeFailed()
    {
        Burst(Fx.Puff, PourPoint(), Red, 16);
        Burst(Fx.Puff, PourPoint(), new Color(0.9f, 0.4f, 0.9f), 8);
        FlashLight(Red, 0.5f);
        Warn(PourPoint(), "La molécula no coincide con ninguna receta", Red);
    }

    void OnLeverFail(LeverFail kind)
    {
        Burst(Fx.Puff, PourPoint(), Smoke, 18);
        FlashLight(Red, 0.5f);
        Warn(PourPoint(), kind == LeverFail.Pressure ? "Presión incorrecta" : "Temperatura incorrecta", Amber);
    }

    void FlashOutline(Color c, float seconds) { outlineFlashColor = c; outlineFlashUntil = Time.time + seconds; }
    void FlashLight(Color c, float seconds) { lightFlashColor = c; lightFlashUntil = Time.time + seconds; }

    void UpdateMachine()
    {
        if (machine == null) return;
        FindOutline();
        bool processing = machine.State == TransmutatorMachine.MachineState.Processing;
        float t = Time.time;

        // Contorno de la entrada
        if (entryOutline != null)
        {
            Color c; float w = outlineBaseWidth;
            if (t < outlineFlashUntil)
            {
                float k = Mathf.PingPong(t * 8f, 1f);
                c = Color.Lerp(outlineFlashColor, Color.white, k * 0.35f);
                w = outlineBaseWidth * 1.6f;
            }
            else if (processing)
            {
                c = Color.Lerp(Amber, Color.white, Mathf.PingPong(t * 2.5f, 1f) * 0.4f);
                w = outlineBaseWidth * 1.3f;
            }
            else if (t < nearUntil)
            {
                c = Color.Lerp(outlineBase, Color.white, 0.35f + 0.35f * Mathf.PingPong(t * 4f, 1f));
                c.a = 1f;
                w = outlineBaseWidth * 1.7f;
            }
            else
            {
                c = outlineBase;
                c.a = outlineBase.a * (0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(t * 2f)));
            }
            entryOutline.startColor = entryOutline.endColor = c;
            entryOutline.widthMultiplier = w;
        }

        // Luz
        if (machineLight != null)
        {
            if (t < lightFlashUntil)
            {
                machineLight.enabled = true;
                machineLight.color = lightFlashColor;
                machineLight.intensity = 3.5f * Mathf.Clamp01((lightFlashUntil - t) / 0.4f);
            }
            else if (processing)
            {
                machineLight.enabled = true;
                machineLight.color = Amber;
                machineLight.intensity = 0.8f + 1.4f * (0.5f + 0.5f * Mathf.Sin(t * 6f));
            }
            else machineLight.enabled = false;
            machineLight.transform.position = EntryCenter();
        }

        // Remolino de partículas mientras procesa
        if (processing)
        {
            if (!wasProcessing) Ring(EntryCenter(), Vector3.up, Amber, 20);
            swirlTimer -= Time.deltaTime;
            if (swirlTimer <= 0f)
            {
                swirlTimer = 0.07f;
                float a = t * 5f;
                float r = machineEntry != null ? Mathf.Min(machineEntry.bounds.extents.x, machineEntry.bounds.extents.z) * 0.8f : 0.15f;
                Vector3 c0 = EntryCenter();
                Burst(Fx.Glitter, c0 + new Vector3(Mathf.Cos(a), -0.1f, Mathf.Sin(a)) * r, Amber, 2);
                Burst(Fx.Glitter, c0 + new Vector3(Mathf.Cos(a + Mathf.PI), -0.1f, Mathf.Sin(a + Mathf.PI)) * r, Gold, 2);
            }
        }
        wasProcessing = processing;
    }

    // ===============================================================
    // Entrega y encargos
    // ===============================================================
    Vector3 DeliveryPoint() => evaluator != null && evaluator.inputZone != null ? evaluator.inputZone.bounds.center
                             : (evaluator != null ? evaluator.transform.position : transform.position);

    void OnDelivered(DeliveryResult r)
    {
        Vector3 at = DeliveryPoint();
        Confetti(at, 60);
        Ring(at, Vector3.up, Gold, 28);
        FloatText(at + Vector3.up * 0.35f, $"+{r.totalPoints} pts", Gold, 1.6f);
        if (r.bonusPoints > 0) FloatText(at + Vector3.up * 0.22f, $"bonus por tiempo +{r.bonusPoints}", Green, 0.8f, 0.25f);
        PanelPunch();
    }

    void OnRejected(SampleItem item, RejectReason reason)
    {
        Vector3 at = item != null ? CenterOf(item.transform) : DeliveryPoint();
        Burst(Fx.Puff, at, Red, 14);
        string msg;
        switch (reason)
        {
            case RejectReason.Empty: msg = "El contenedor está vacío"; break;
            case RejectReason.NotClassified: msg = "Falta clasificar la muestra"; break;
            case RejectReason.NoMatchingOrder: msg = "No coincide con el encargo"; break;
            case RejectReason.NoActiveOrder: msg = "No hay encargo aceptado"; break;
            case RejectReason.WrongRecipe: msg = "Receta equivocada"; break;
            case RejectReason.Expired: msg = "Se acabó el tiempo del encargo"; break;
            default: msg = "Entrega rechazada"; break;
        }
        Warn(at, msg, Red);
    }

    Vector3 PanelPoint()
    {
        if (gestorPanel == null) return transform.position;
        Vector3 p = gestorPanel.position;
        var cam = Camera.main;
        if (cam != null) p += (cam.transform.position - p).normalized * 0.12f;
        return p;
    }

    void OnOrderAccepted()
    {
        PanelPunch();
        if (gestorPanel != null) { Burst(Fx.Glitter, PanelPoint(), Green, 20); FloatText(PanelPoint() + Vector3.up * 0.25f, "¡Encargo aceptado!", Green, 1f); }
    }

    void OnLowTime()
    {
        PanelShake(0.6f, 6f);
        if (gestorPanel != null) FloatText(PanelPoint() + Vector3.up * 0.25f, "¡Queda poco tiempo!", Amber, 1f);
    }

    void OnOrderLost()
    {
        PanelShake(0.8f, 12f);
        if (gestorPanel != null) { Burst(Fx.Puff, PanelPoint(), Red, 10); FloatText(PanelPoint() + Vector3.up * 0.25f, "Encargo perdido", Red, 1.1f); }
    }

    void OnLevelCompleted(int level)
    {
        Vector3 at = gestorPanel != null ? PanelPoint() : DeliveryPoint();
        Confetti(at, 90);
        Confetti(DeliveryPoint(), 60);
        FloatText(at + Vector3.up * 0.35f, "¡Nivel completado!", Gold, 1.8f);
    }

    void PanelPunch()
    {
        if (gestorPanel == null) return;
        if (panelAnim != null) StopCoroutine(panelAnim);
        panelAnim = StartCoroutine(PanelPunchCo());
    }

    IEnumerator PanelPunchCo()
    {
        const float dur = 0.3f;
        for (float e = 0f; e < dur; e += Time.deltaTime)
        {
            float k = Mathf.Sin(e / dur * Mathf.PI);
            gestorPanel.localScale = panelBaseScale * (1f + 0.06f * k);
            gestorPanel.localPosition = panelBasePos;
            yield return null;
        }
        gestorPanel.localScale = panelBaseScale;
        panelAnim = null;
    }

    void PanelShake(float seconds, float amplitude)
    {
        if (gestorPanel == null) return;
        if (panelAnim != null) StopCoroutine(panelAnim);
        panelAnim = StartCoroutine(PanelShakeCo(seconds, amplitude));
    }

    IEnumerator PanelShakeCo(float seconds, float amplitude)
    {
        gestorPanel.localScale = panelBaseScale;
        for (float e = 0f; e < seconds; e += Time.deltaTime)
        {
            float fall = 1f - e / seconds;
            gestorPanel.localPosition = panelBasePos + new Vector3(Mathf.Sin(e * 55f), Mathf.Sin(e * 37f) * 0.4f, 0f) * amplitude * fall;
            yield return null;
        }
        gestorPanel.localPosition = panelBasePos;
        panelAnim = null;
    }

    // ===============================================================
    // Bucle
    // ===============================================================
    void Update()
    {
        UpdateMachine();
    }

    // ===============================================================
    // Utilidades de efectos
    // ===============================================================
    static Vector3 CenterOf(Transform t)
    {
        var r = t.GetComponentInChildren<Renderer>();
        if (r != null) return r.bounds.center;
        var c = t.GetComponentInChildren<Collider>();
        return c != null ? c.bounds.center : t.position;
    }

    void Burst(Fx kind, Vector3 at, Color color, int count)
    {
        if (!systems.TryGetValue(kind, out var ps) || ps == null) return;
        ps.transform.SetPositionAndRotation(at, Quaternion.identity);
        var ep = new ParticleSystem.EmitParams { startColor = color, applyShapeToPosition = true };
        ps.Emit(ep, count);
    }

    void Ring(Vector3 at, Vector3 normal, Color color, int count)
    {
        if (!systems.TryGetValue(Fx.Ring, out var ps) || ps == null) return;
        ps.transform.SetPositionAndRotation(at, Quaternion.FromToRotation(Vector3.forward, normal));
        var ep = new ParticleSystem.EmitParams { startColor = color, applyShapeToPosition = true };
        ps.Emit(ep, count);
    }

    void Confetti(Vector3 at, int count)
    {
        var colors = new[] { Gold, Green, Cyan, new Color(1f, 0.4f, 0.7f), new Color(0.6f, 0.5f, 1f) };
        int per = Mathf.Max(1, count / colors.Length);
        foreach (var c in colors) Burst(Fx.Confetti, at, c, per);
    }

    void Warn(Vector3 at, string text, Color color)
    {
        FloatText(at + Vector3.up * 0.2f, text, color, 0.75f);
    }

    // Texto que sube, mira al jugador y se desvanece
    void FloatText(Vector3 at, string text, Color color, float size, float delay = 0f)
    {
        StartCoroutine(FloatTextCo(at, text, color, size, delay));
    }

    IEnumerator FloatTextCo(Vector3 at, string text, Color color, float size, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        var go = new GameObject("FeedbackFX texto");
        go.transform.position = at;
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.fontStyle = FontStyles.Bold;
        tmp.outlineWidth = 0.2f;
        tmp.outlineColor = new Color32(0, 0, 0, 200);
        tmp.rectTransform.sizeDelta = new Vector2(3f, 0.5f);
        tmp.color = color;

        const float dur = 1.8f;
        Vector3 start = at;
        for (float e = 0f; e < dur; e += Time.deltaTime)
        {
            if (go == null) yield break;
            float k = e / dur;
            go.transform.position = start + Vector3.up * (0.25f * EaseOutCubic(k));
            var cam = Camera.main;
            if (cam != null) go.transform.rotation = Quaternion.LookRotation(go.transform.position - cam.transform.position, Vector3.up);
            float pop = e < 0.15f ? EaseOutBack(e / 0.15f) : 1f;
            go.transform.localScale = Vector3.one * Mathf.Max(0.01f, pop);
            var c = color; c.a = k < 0.7f ? 1f : 1f - (k - 0.7f) / 0.3f;
            tmp.color = c;
            yield return null;
        }
        if (go != null) Destroy(go);
    }

    static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }

    static float EaseOutCubic(float x) => 1f - Mathf.Pow(1f - x, 3f);

    // ---------------------------------------------------------------
    // Construcción de los sistemas de partículas (todo por código, ligeros para Quest)
    // ---------------------------------------------------------------
    void BuildParticles()
    {
        var shader = Shader.Find("Sprites/Default");     // incluido siempre en la build (Graphics > Always Included)
        particleMat = new Material(shader != null ? shader : Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        particleMat.mainTexture = SoftDot(32);

        // Chispas: rápidas, pequeñas, caen
        systems[Fx.Sparks] = Make("Chispas", 300, life: (0.25f, 0.55f), speed: (0.5f, 1.3f), size: (0.006f, 0.016f),
            gravity: 0.6f, shape: ParticleSystemShapeType.Sphere, radius: 0.01f, sizeEnd: 0.2f);
        // Humo: lento, crece y sube
        systems[Fx.Puff] = Make("Humo", 200, life: (0.8f, 1.4f), speed: (0.05f, 0.25f), size: (0.05f, 0.11f),
            gravity: -0.04f, shape: ParticleSystemShapeType.Sphere, radius: 0.05f, sizeEnd: 1.8f, alphaStart: 0.75f);
        // Brillo: flota
        systems[Fx.Glitter] = Make("Brillo", 300, life: (0.7f, 1.4f), speed: (0.02f, 0.12f), size: (0.008f, 0.022f),
            gravity: -0.02f, shape: ParticleSystemShapeType.Sphere, radius: 0.1f, sizeEnd: 0.1f);
        // Confeti: sale hacia arriba y cae
        systems[Fx.Confetti] = Make("Confeti", 400, life: (1.4f, 2.2f), speed: (1.2f, 2.4f), size: (0.015f, 0.03f),
            gravity: 0.55f, shape: ParticleSystemShapeType.Cone, radius: 0.05f, sizeEnd: 0.8f, coneAngle: 35f);
        // Anillo: se abre en círculo
        systems[Fx.Ring] = Make("Anillo", 200, life: (0.25f, 0.35f), speed: (0.35f, 0.45f), size: (0.008f, 0.012f),
            gravity: 0f, shape: ParticleSystemShapeType.Circle, radius: 0.01f, sizeEnd: 0.3f, edgeOnly: true);
    }

    ParticleSystem Make(string name, int max, (float, float) life, (float, float) speed, (float, float) size,
                        float gravity, ParticleSystemShapeType shape, float radius, float sizeEnd,
                        float alphaStart = 1f, float coneAngle = 25f, bool edgeOnly = false)
    {
        var go = new GameObject("FeedbackFX " + name);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.maxParticles = max;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life.Item1, life.Item2);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed.Item1, speed.Item2);
        main.startSize = new ParticleSystem.MinMaxCurve(size.Item1, size.Item2);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = gravity;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        var em = ps.emission; em.enabled = false;

        var sh = ps.shape;
        sh.enabled = true;
        sh.shapeType = shape;
        sh.radius = radius;
        sh.angle = coneAngle;
        if (shape == ParticleSystemShapeType.Cone) sh.rotation = new Vector3(-90f, 0f, 0f);   // hacia arriba
        if (edgeOnly) sh.radiusThickness = 0f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(alphaStart, 0f), new GradientAlphaKey(alphaStart, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, sizeEnd));

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sharedMaterial = particleMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    static Texture2D SoftDot(int n)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "FeedbackFX punto" };
        float h = (n - 1) * 0.5f;
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Sqrt((x - h) * (x - h) + (y - h) * (y - h)) / h;
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }
}

/// Animación corta al aparecer un panel de UI (crece desde fromScale con rebote y se desvanece hacia dentro).
/// La agrega FeedbackFX al menú de la muñeca y sus páginas.
public class UIPopIn : MonoBehaviour
{
    public float fromScale = 0.9f;
    public float duration = 0.18f;

    Vector3 baseScale;
    bool hasBase;
    CanvasGroup group;
    Coroutine running;

    void Awake() => CaptureBase();

    void CaptureBase()
    {
        if (hasBase) return;
        baseScale = transform.localScale;
        hasBase = true;
        group = GetComponent<CanvasGroup>();
        if (group == null && GetComponent<RectTransform>() != null) group = gameObject.AddComponent<CanvasGroup>();
    }

    void OnEnable()
    {
        CaptureBase();
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(Play());
    }

    void OnDisable()
    {
        if (running != null) StopCoroutine(running);
        running = null;
        if (hasBase) transform.localScale = baseScale;
        if (group != null) group.alpha = 1f;
    }

    IEnumerator Play()
    {
        for (float e = 0f; e < duration; e += Time.unscaledDeltaTime)
        {
            float k = e / duration;
            float s = Mathf.LerpUnclamped(fromScale, 1f, Back(k));
            transform.localScale = baseScale * s;
            if (group != null) group.alpha = Mathf.Clamp01(k * 1.5f);
            yield return null;
        }
        transform.localScale = baseScale;
        if (group != null) group.alpha = 1f;
        running = null;
    }

    static float Back(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }
}

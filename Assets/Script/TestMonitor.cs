using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// Monitor de pruebas: se crea solo al dar Play (no hay que ponerlo en la escena).
///  - Al empezar revisa la configuración (máquinas, recetas, encargos, contenedores, puertas, poke...).
///  - Durante la prueba anota lo que pasa: átomos y barras creados, uniones, moléculas completas,
///    reacciones del transmutador, encargos aceptados / entregados / perdidos, entregas y rechazos, errores.
///  - Al terminar la prueba (salir de Play) escribe en la consola UN informe general ordenado,
///    con una lista de qué partes del ciclo se completaron y cuáles no.
/// También se puede pedir el informe en cualquier momento: clic derecho en el componente > "Informe ahora".
public class TestMonitor : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindFirstObjectByType<TestMonitor>() != null) return;
        var go = new GameObject("TestMonitor (auto)");
        DontDestroyOnLoad(go);
        go.AddComponent<TestMonitor>();
    }

    // ---------------------------------------------------------------
    readonly List<string> setupOk = new List<string>();
    readonly List<string> setupWarn = new List<string>();
    readonly List<string> setupError = new List<string>();
    readonly List<string> timeline = new List<string>();
    readonly Dictionary<string, int> counters = new Dictionary<string, int>();
    readonly Dictionary<string, int> molecules = new Dictionary<string, int>();
    readonly List<string> syntheses = new List<string>();
    readonly List<string> deliveries = new List<string>();
    readonly List<string> rejections = new List<string>();
    readonly List<string> ejections = new List<string>();
    readonly Dictionary<string, int> errors = new Dictionary<string, int>();
    readonly Dictionary<string, int> warnings = new Dictionary<string, int>();

    float startTime;
    float nearestMolecule = float.MaxValue; string nearestInfo = "";
    bool reported;
    ScoreManager score;

    void Start()
    {
        startTime = Time.time;
        Application.logMessageReceived += OnLog;
        CheckSetup();
        Subscribe();
        Debug.Log("[TestMonitor] Prueba iniciada. Al salir de Play verás el informe general en la consola.");
    }

    void OnDestroy()
    {
        Application.logMessageReceived -= OnLog;
        Unsubscribe();
    }

    void OnApplicationQuit() => Report();

    [ContextMenu("Informe ahora")]
    public void ReportNow() { reported = false; Report(); }

    // ---------------------------------------------------------------
    // 1. Revisión de la configuración
    // ---------------------------------------------------------------
    void CheckSetup()
    {
        var machines = FindObjectsByType<TransmutatorMachine>(FindObjectsSortMode.None);
        var evaluators = FindObjectsByType<DeliveryEvaluator>(FindObjectsSortMode.None);
        var orderManagers = FindObjectsByType<OrderManager>(FindObjectsSortMode.None);
        var gestores = FindObjectsByType<GestorEncargosVR>(FindObjectsSortMode.None);

        // Transmutadora
        if (machines.Length == 0) setupError.Add("No hay Máquina Transmutadora en la escena.");
        foreach (var m in machines)
        {
            if (m.catalog == null) setupError.Add("Transmutadora: falta el RecipeCatalog."); else CheckRecipes(m.catalog);
            var entry = m.GetComponents<Collider>().FirstOrDefault(c => c.isTrigger && c.enabled);
            if (entry == null) setupError.Add("Transmutadora: no tiene caja Trigger de entrada en su objeto.");
            else setupOk.Add("Transmutadora: caja de entrada (trigger) encontrada.");
            if (m.gasSocket == null) setupWarn.Add("Transmutadora: sin socket de gases (los gases no tendrán botella).");
            else
            {
                var sc = m.gasSocket.GetComponent<Collider>();
                if (sc == null || !sc.isTrigger) setupError.Add("Transmutadora: el collider del socket de gases debe ser Trigger.");
                else setupOk.Add("Transmutadora: socket de gases listo (trigger).");
            }
            if (m.cupZone == null) setupWarn.Add("Transmutadora: sin zona del vaso (los líquidos no tendrán vaso).");
            else if (!m.cupZone.isTrigger) setupWarn.Add("Transmutadora: la zona del vaso no es Trigger (el vaso queda encima y puede no detectarse).");
            else setupOk.Add("Transmutadora: zona del vaso lista (trigger).");
            if (m.spout == null) setupWarn.Add("Transmutadora: falta el pico (spout).");
            if (m.pressureLever == null || m.temperatureLever == null) setupWarn.Add("Transmutadora: falta alguna palanca (no se revisará presión/temperatura).");
            if (m.GetComponent<TransmutatorAutoStart>() == null && !HasStartButton()) setupWarn.Add("Transmutadora: sin arranque automático ni botón Iniciar.");
            else setupOk.Add("Transmutadora: arranque automático activo.");
        }

        // Pedidos y evaluadora
        if (evaluators.Length == 0) setupError.Add("No hay DeliveryEvaluator (máquina de envío).");
        foreach (var e in evaluators)
        {
            if (e.orders == null) setupError.Add("DeliveryEvaluator: falta el OrderManager.");
            if (e.inputZone == null) setupError.Add("DeliveryEvaluator: falta la zona de entrada (inputZone).");
            else setupOk.Add("DeliveryEvaluator: zona de entrada asignada.");
        }
        foreach (var om in orderManagers)
        {
            if (om.catalog == null) setupWarn.Add("OrderManager: falta el RecipeCatalog.");
            setupOk.Add($"OrderManager: modo {(om.manualOrders ? "manual (GestorEncargosVR)" : "por niveles")}.");
        }
        if (gestores.Length == 0) setupWarn.Add("No hay GestorEncargosVR (pantalla de encargos).");
        foreach (var g in gestores)
        {
            if (g.orders == null) setupError.Add("GestorEncargosVR: falta el OrderManager.");
            else if (evaluators.Length > 0 && evaluators[0].orders != null && g.orders != evaluators[0].orders)
                setupError.Add("GestorEncargosVR y DeliveryEvaluator usan OrderManager distintos: las entregas no coincidirán con los encargos.");
            else setupOk.Add("GestorEncargosVR y DeliveryEvaluator comparten el mismo OrderManager.");
            int noRecipe = g.encargos.Count(x => x.recipe == null), noSprite = g.encargos.Count(x => x.sprite == null);
            if (noRecipe > 0) setupError.Add($"GestorEncargosVR: {noRecipe} encargo(s) sin receta.");
            if (noSprite > 0) setupWarn.Add($"GestorEncargosVR: {noSprite} encargo(s) sin imagen.");
            var tiempos = g.encargos.Where(x => x.recipe != null)
                                    .Select(x => $"{x.recipe.formula} {(x.tiempoLimite > 0 ? x.tiempoLimite : x.recipe.timeLimit):0}s");
            setupOk.Add($"GestorEncargosVR: {g.encargos.Count} encargos ({string.Join(", ", tiempos)}).");
        }
        score = FindFirstObjectByType<ScoreManager>();
        if (score == null) setupWarn.Add("No hay ScoreManager (no se sumarán puntos).");
        else setupOk.Add("ScoreManager activo.");

        // Spawners y contenedores
        foreach (var s in FindObjectsByType<AtomSpawner>(FindObjectsSortMode.None))
        {
            int n = s.elements?.Count(x => x != null && x.data != null && x.spawnButton != null) ?? 0;
            if (s.atomPrefab == null) setupError.Add("AtomSpawner: falta el prefab del átomo.");
            else setupOk.Add($"AtomSpawner: {n} botones de elementos conectados.");
        }
        foreach (var s in FindObjectsByType<BarSpawner>(FindObjectsSortMode.None))
            if (s.barPrefab == null) setupWarn.Add($"BarSpawner ({s.name}): falta el prefab de la barra.");
        var samples = FindObjectsByType<SampleItem>(FindObjectsSortMode.None);
        int bottles = samples.Count(x => x.kind == ContainerKind.Bottle), cups = samples.Length - bottles;
        if (bottles == 0) setupWarn.Add("No hay botellas en la escena (los gases no se podrán servir).");
        if (cups == 0) setupWarn.Add("No hay vasos en la escena (los líquidos no se podrán servir).");
        setupOk.Add($"Contenedores: {bottles} botella(s), {cups} vaso(s).");

        // Interacción
        int pokes = FindObjectsByType<XRPokeInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        if (pokes == 0) setupWarn.Add("No hay Poke Interactors (no se podrá tocar el menú con el dedo).");
        else setupOk.Add($"Poke: {pokes} interactor(es).");
        int doors = FindObjectsByType<PuertaVR>(FindObjectsSortMode.None).Length;
        setupOk.Add(doors > 0 ? $"Puertas automáticas activas: {doors} hoja(s)." : "No hay puertas PuertaVR activas en la escena.");
    }

    void CheckRecipes(RecipeCatalog cat)
    {
        if (cat.recipes == null || cat.recipes.Length == 0) { setupError.Add("RecipeCatalog vacío."); return; }
        int good = 0;
        foreach (var r in cat.recipes)
        {
            if (r == null) { setupError.Add("RecipeCatalog: hay una receta vacía en la lista."); continue; }
            string problem = RecipeProblem(r);
            if (problem != null) setupError.Add($"Receta {r.formula}: {problem}");
            else good++;
        }
        setupOk.Add($"Recetas bien definidas: {good}/{cat.recipes.Length}.");
    }

    static string RecipeProblem(RecipeData r)
    {
        if (r.atoms == null || r.atoms.Length == 0) return "no tiene átomos.";
        var used = new int[r.atoms.Length];
        foreach (var b in r.bonds ?? new RecipeData.BondDef[0])
        {
            if (b.a < 0 || b.b < 0 || b.a >= r.atoms.Length || b.b >= r.atoms.Length) return $"el enlace {b.a}-{b.b} apunta a un átomo que no existe.";
            used[b.a]++; used[b.b]++;
        }
        for (int i = 0; i < r.atoms.Length; i++)
        {
            if (r.atoms[i] == null) return $"el átomo {i} está vacío.";
            if (used[i] != r.atoms[i].valence) return $"el átomo {i} ({r.atoms[i].symbol}) tiene {used[i]} enlaces y su valencia es {r.atoms[i].valence}.";
        }
        return null;
    }

    static bool HasStartButton()
    {
        foreach (var b in FindObjectsByType<LabButton>(FindObjectsSortMode.None))
            for (int i = 0; i < b.onPressed.GetPersistentEventCount(); i++)
                if (b.onPressed.GetPersistentMethodName(i) == "StartReaction" && b.onPressed.GetPersistentTarget(i) != null) return true;
        return false;
    }

    // ---------------------------------------------------------------
    // 2. Seguimiento durante la prueba
    // ---------------------------------------------------------------
    readonly List<System.Action> unsubscribe = new List<System.Action>();

    void Subscribe()
    {
        foreach (var s in FindObjectsByType<AtomSpawner>(FindObjectsSortMode.None))
        {
            var sp = s;
            UnityEngine.Events.UnityAction a = () => Count("Átomos creados"), b = () => { Count("Spawner de átomos sin canasta"); Note("Spawner de átomos: no detecta canasta bajo la boquilla."); }, c = () => Count("Spawner de átomos lleno");
            sp.onSpawned.AddListener(a); sp.onNoBasket.AddListener(b); sp.onFull.AddListener(c);
            unsubscribe.Add(() => { if (sp) { sp.onSpawned.RemoveListener(a); sp.onNoBasket.RemoveListener(b); sp.onFull.RemoveListener(c); } });
        }
        foreach (var s in FindObjectsByType<BarSpawner>(FindObjectsSortMode.None))
        {
            var sp = s;
            UnityEngine.Events.UnityAction a = () => Count("Barras creadas"), b = () => { Count("Spawner de barras sin canasta"); Note("Spawner de barras: no detecta canasta bajo la boquilla."); }, c = () => Count("Spawner de barras lleno (límite de barras)");
            sp.onSpawned.AddListener(a); sp.onNoBasket.AddListener(b); sp.onFull.AddListener(c);
            unsubscribe.Add(() => { if (sp) { sp.onSpawned.RemoveListener(a); sp.onNoBasket.RemoveListener(b); sp.onFull.RemoveListener(c); } });
        }

        Bond.AnyAttached += OnBondAttached;
        Bond.AnyDetached += OnBondDetached;
        TransmutatorAutoStart.MoleculeAbsorbed += OnAbsorbed;
        TransmutatorAutoStart.MoleculeEjected += OnEjected;
        TransmutatorAutoStart.CompleteMoleculeNear += OnNear;

        foreach (var m in FindObjectsByType<TransmutatorMachine>(FindObjectsSortMode.None))
        {
            var mm = m;
            mm.Synthesized += OnSynthesized; mm.Failed += OnFailed; mm.LeverFailed += OnLeverFailed; mm.Spilled += OnSpilled;
            UnityEngine.Events.UnityAction blocked = () => { Count("Transmutadora bloqueada (las 2 palancas mal)"); Note("Transmutadora: las dos palancas estaban mal, no arrancó."); };
            mm.onBlocked.AddListener(blocked);
            unsubscribe.Add(() => { if (mm) { mm.Synthesized -= OnSynthesized; mm.Failed -= OnFailed; mm.LeverFailed -= OnLeverFailed; mm.Spilled -= OnSpilled; mm.onBlocked.RemoveListener(blocked); } });
        }
        foreach (var o in FindObjectsByType<OrderManager>(FindObjectsSortMode.None))
        {
            var om = o;
            om.OrderAccepted += OnOrderAccepted; om.OrderDelivered += OnOrderDelivered; om.OrderLost += OnOrderLost;
            unsubscribe.Add(() => { if (om) { om.OrderAccepted -= OnOrderAccepted; om.OrderDelivered -= OnOrderDelivered; om.OrderLost -= OnOrderLost; } });
        }
        foreach (var e in FindObjectsByType<DeliveryEvaluator>(FindObjectsSortMode.None))
        {
            var ev = e;
            ev.Delivered += OnDelivered; ev.Rejected += OnRejected;
            unsubscribe.Add(() => { if (ev) { ev.Delivered -= OnDelivered; ev.Rejected -= OnRejected; } });
        }
    }

    void Unsubscribe()
    {
        Bond.AnyAttached -= OnBondAttached;
        Bond.AnyDetached -= OnBondDetached;
        TransmutatorAutoStart.MoleculeAbsorbed -= OnAbsorbed;
        TransmutatorAutoStart.MoleculeEjected -= OnEjected;
        TransmutatorAutoStart.CompleteMoleculeNear -= OnNear;
        foreach (var u in unsubscribe) u();
        unsubscribe.Clear();
    }

    void OnBondAttached(Bond bar, Atom atom)
    {
        Count("Uniones barra-átomo");
        if (atom == null) return;
        var graph = MoleculeGraph.FromAtom(atom, out var atoms);
        if (graph.AtomCount < 2 || graph.OpenValences != 0) return;
        var cat = FindFirstObjectByType<TransmutatorMachine>()?.catalog;
        string name = TransmutatorAutoStart.Describe(atoms);
        if (cat != null && MoleculeValidator.Validate(graph, cat.recipes, out var r) == ValidationState.Correct && r != null) name = $"{r.displayName} ({r.formula})";
        else name += " (no es una receta)";
        molecules[name] = molecules.TryGetValue(name, out int n) ? n + 1 : 1;
        Note($"Molécula completa armada: {name}");
    }

    void OnBondDetached(Bond bar) => Count("Barras separadas");
    void OnNear(string formula, float dist, bool inHand)
    {
        if (dist >= nearestMolecule) return;
        nearestMolecule = dist;
        nearestInfo = $"{formula} a {dist:0.00} m de la caja de entrada ({(inHand ? "en la mano" : "suelta")}, t={Time.time - startTime:0}s)";
    }
    void OnAbsorbed(string formula) { Count("Moléculas tomadas por la transmutadora"); Note($"Transmutadora tomó la molécula {formula}"); }
    void OnEjected(string why) { ejections.Add($"{T()} {why}"); Note($"Transmutadora devolvió la molécula: {why}"); }

    void OnSynthesized(RecipeData r, SampleItem s)
    {
        string what = $"{r.displayName} ({r.formula}) en {(s != null ? s.name : "?")} — receta guardada: {(s != null && s.recipe == r ? "sí" : "NO")}";
        syntheses.Add($"{T()} {what}");
        Note($"Síntesis correcta: {what}");
    }
    void OnFailed(ValidationState st) { Count($"Reacciones fallidas ({st})"); Note($"Transmutadora: molécula {st} (pof)."); }
    void OnLeverFailed(LeverFail k) { Count($"Fallos por palanca ({k})"); Note($"Transmutadora: palanca de {(k == LeverFail.Pressure ? "presión" : "temperatura")} mal."); }
    void OnSpilled() { Count("Derrames (quitaron el contenedor)"); Note("Transmutadora: se retiró el contenedor mientras se llenaba."); }

    void OnOrderAccepted(Order o) { Count("Encargos aceptados"); Note($"Encargo aceptado: {o.recipe.displayName}, tiempo límite {o.timeLimit:0} s (empieza a contar)."); }
    void OnOrderDelivered(Order o) { Count("Encargos entregados"); Note($"Encargo entregado: {o.recipe.displayName} en {o.TimeUsed:0.0} s de {o.timeLimit:0} s."); }
    void OnOrderLost(Order o) { Count("Encargos perdidos"); Note($"Encargo perdido: {o.recipe.displayName} ({(o.timeRemaining <= 0f ? "se acabó el tiempo" : "entrega equivocada")})."); }

    void OnDelivered(DeliveryResult r)
    {
        deliveries.Add($"{T()} {r.recipe.displayName}: base {r.basePoints} + bonus tiempo {r.bonusPoints} + tapa {r.sealedBonus} = {r.totalPoints} pts (tardó {r.timeUsed:0.0} s)");
        Note($"Entrega aceptada: {r.recipe.displayName}, {r.totalPoints} pts.");
    }
    void OnRejected(SampleItem s, RejectReason reason)
    {
        string why = reason switch
        {
            RejectReason.Empty => "contenedor vacío",
            RejectReason.NotClassified => "sin clasificar",
            RejectReason.NoActiveOrder => "no había encargo en curso",
            RejectReason.NoMatchingOrder => "no coincide con el encargo",
            RejectReason.WrongRecipe => "compuesto equivocado: encargo perdido, 0 puntos",
            RejectReason.Expired => "fuera de tiempo: no se recibe",
            _ => reason.ToString()
        };
        rejections.Add($"{T()} {(s != null ? s.name : "?")}{(s != null && s.recipe != null ? $" ({s.recipe.formula})" : "")}: {why}");
    }

    void OnLog(string msg, string stack, LogType type)
    {
        if (msg.StartsWith("[TestMonitor]")) return;
        string key = msg.Length > 160 ? msg.Substring(0, 160) + "…" : msg;
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors[key] = errors.TryGetValue(key, out int n) ? n + 1 : 1;
        else if (type == LogType.Warning)
            warnings[key] = warnings.TryGetValue(key, out int n) ? n + 1 : 1;
    }

    void Count(string k) => counters[k] = counters.TryGetValue(k, out int n) ? n + 1 : 1;
    int Get(string k) => counters.TryGetValue(k, out int n) ? n : 0;
    string T() => $"[{Time.time - startTime,6:0.0}s]";
    void Note(string s) { if (timeline.Count < 80) timeline.Add($"{T()} {s}"); else if (timeline.Count == 80) timeline.Add("… (más eventos omitidos)"); }

    // ---------------------------------------------------------------
    // 3. Informe final
    // ---------------------------------------------------------------
    void Report()
    {
        if (reported) return;
        reported = true;
        if (score == null) score = FindFirstObjectByType<ScoreManager>();

        var sb = new StringBuilder();
        sb.AppendLine("==================== INFORME DE LA PRUEBA — MOLÉCULA CAFÉ ====================");
        sb.AppendLine($"Duración: {Time.time - startTime:0} s");
        sb.AppendLine();

        sb.AppendLine("1) CICLO DEL JUEGO");
        Step(sb, "Crear átomos con el spawner", Get("Átomos creados") > 0, $"{Get("Átomos creados")} átomos");
        Step(sb, "Crear barras con el spawner", Get("Barras creadas") > 0, $"{Get("Barras creadas")} barras");
        Step(sb, "Unir átomos con barras", Get("Uniones barra-átomo") > 0, $"{Get("Uniones barra-átomo")} uniones, {Get("Barras separadas")} separaciones");
        Step(sb, "Armar una molécula completa", molecules.Count > 0, string.Join(", ", molecules.Select(kv => $"{kv.Key} x{kv.Value}")));
        Step(sb, "La transmutadora toma la molécula", Get("Moléculas tomadas por la transmutadora") > 0, $"{Get("Moléculas tomadas por la transmutadora")} veces, {ejections.Count} devoluciones");
        if (Get("Moléculas tomadas por la transmutadora") == 0 && molecules.Count > 0)
            sb.AppendLine(nearestMolecule < float.MaxValue
                ? $"       → La molécula más cercana estuvo {nearestInfo}. Hay que meterla dentro del contorno cian."
                : "       → Ninguna molécula completa llegó a menos de 2,5 m de la transmutadora.");
        Step(sb, "Síntesis y llenado del contenedor", syntheses.Count > 0, $"{syntheses.Count} síntesis");
        Step(sb, "Aceptar un encargo (empieza el tiempo)", Get("Encargos aceptados") > 0, $"{Get("Encargos aceptados")} aceptados");
        Step(sb, "Entregar la muestra correcta a tiempo", deliveries.Count > 0, $"{deliveries.Count} entregas aceptadas, {Get("Encargos perdidos")} encargos perdidos");
        sb.AppendLine($"   Puntaje final: {(score != null ? $"{score.Score} pts — {score.StarTitle} ({score.Stars} estrella(s)), fallos {score.Failures}, derrames {score.Spills}" : "sin ScoreManager")}");
        sb.AppendLine();

        sb.AppendLine("2) CONFIGURACIÓN AL INICIAR");
        foreach (var s in setupError) sb.AppendLine("   [ERROR] " + s);
        foreach (var s in setupWarn) sb.AppendLine("   [AVISO] " + s);
        foreach (var s in setupOk) sb.AppendLine("   [OK]    " + s);
        sb.AppendLine();

        sb.AppendLine("3) TRANSMUTADORA");
        List(sb, "Síntesis", syntheses);
        List(sb, "Moléculas devueltas", ejections);
        foreach (var kv in counters.Where(k => k.Key.StartsWith("Reacciones") || k.Key.StartsWith("Fallos") || k.Key.StartsWith("Derrames") || k.Key.StartsWith("Transmutadora")))
            sb.AppendLine($"   {kv.Key}: {kv.Value}");
        sb.AppendLine();

        sb.AppendLine("4) ENCARGOS Y ENTREGAS");
        sb.AppendLine($"   Aceptados: {Get("Encargos aceptados")} · Entregados: {Get("Encargos entregados")} · Perdidos: {Get("Encargos perdidos")}");
        List(sb, "Entregas aceptadas", deliveries);
        List(sb, "Rechazos de la máquina de envío", rejections);
        sb.AppendLine();

        sb.AppendLine("5) SPAWNERS");
        foreach (var kv in counters.Where(k => k.Key.StartsWith("Spawner") || k.Key.StartsWith("Átomos") || k.Key.StartsWith("Barras")))
            sb.AppendLine($"   {kv.Key}: {kv.Value}");
        sb.AppendLine();

        sb.AppendLine($"6) ERRORES Y AVISOS EN CONSOLA ({errors.Values.Sum()} errores, {warnings.Values.Sum()} avisos)");
        foreach (var kv in errors.OrderByDescending(k => k.Value).Take(12)) sb.AppendLine($"   [ERROR x{kv.Value}] {kv.Key}");
        foreach (var kv in warnings.OrderByDescending(k => k.Value).Take(8)) sb.AppendLine($"   [AVISO x{kv.Value}] {kv.Key}");
        if (errors.Count == 0 && warnings.Count == 0) sb.AppendLine("   Ninguno.");
        sb.AppendLine();

        sb.AppendLine("7) LÍNEA DE TIEMPO");
        if (timeline.Count == 0) sb.AppendLine("   (sin eventos)");
        foreach (var t in timeline) sb.AppendLine("   " + t);
        sb.AppendLine("===============================================================================");

        var text = sb.ToString();
        if (setupError.Count > 0 || errors.Count > 0) Debug.LogWarning("[TestMonitor]\n" + text);
        else Debug.Log("[TestMonitor]\n" + text);
    }

    static void Step(StringBuilder sb, string name, bool done, string detail)
        => sb.AppendLine($"   {(done ? "[✔]" : "[ ]")} {name}{(string.IsNullOrEmpty(detail) ? "" : " — " + detail)}");

    static void List(StringBuilder sb, string title, List<string> items)
    {
        sb.AppendLine($"   {title}: {(items.Count == 0 ? "ninguna" : items.Count.ToString())}");
        foreach (var i in items.Take(20)) sb.AppendLine("     " + i);
    }
}

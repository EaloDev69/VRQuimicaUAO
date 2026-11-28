using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// Prueba de TODAS las recetas del catálogo, sin visor VR.
/// Uso: en Play, menú  Molecula > Prueba de todas las recetas (en Play).
/// Para cada receta hace lo mismo que el jugador y anota el resultado:
///   1. Revisa los datos de la receta (valencias cerradas, molécula conectada, hash para el validador).
///   2. Acepta un encargo de esa receta (el tiempo corre).
///   3. Pone las palancas en lo que pide la receta.
///   4. Pone el contenedor correcto: botella en el socket (gas) o vaso bajo el pico (líquido).
///   5. Arma la molécula con átomos y barras reales dentro de la entrada de la transmutadora.
///   6. Espera la síntesis y revisa que el contenedor guardó ESA receta.
///   7. Lleva el contenedor a la máquina de envío y revisa que la entrega se acepte y sume puntos.
/// Al final prueba un caso malo: entregar una receta distinta a la del encargo (debe rechazarse).
/// Escribe una tabla con el resultado de cada receta en la consola.
public class PruebaRecetas : MonoBehaviour
{
#if UNITY_EDITOR
    [UnityEditor.MenuItem("Molecula/Prueba de todas las recetas (en Play)")]
    static void Run()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[Recetas] Dale Play primero."); return; }
        if (FindFirstObjectByType<PruebaRecetas>() != null) { Debug.LogWarning("[Recetas] Ya hay una prueba corriendo."); return; }
        new GameObject("PruebaRecetas").AddComponent<PruebaRecetas>();
    }
#endif

    class Row { public string name, formula, kind, data = "-", build = "-", synth = "-", fill = "-", deliver = "-", note = ""; public bool ok; }
    readonly List<Row> rows = new List<Row>();

    TransmutatorMachine machine;
    DeliveryEvaluator evaluator;
    OrderManager orders;
    ScoreManager score;
    AtomSpawner atomSpawner;
    BarSpawner barSpawner;
    Collider entry;

    IEnumerator Start()
    {
        machine = FindFirstObjectByType<TransmutatorMachine>();
        evaluator = FindFirstObjectByType<DeliveryEvaluator>();
        orders = evaluator != null ? evaluator.orders : FindFirstObjectByType<OrderManager>();
        score = FindFirstObjectByType<ScoreManager>();
        atomSpawner = FindFirstObjectByType<AtomSpawner>();
        barSpawner = FindFirstObjectByType<BarSpawner>();
        entry = machine != null ? machine.GetComponents<Collider>().FirstOrDefault(c => c.isTrigger && c.enabled) : null;

        if (machine == null || evaluator == null || orders == null || atomSpawner == null || barSpawner == null || entry == null || machine.catalog == null)
        {
            var missing = new List<string>();
            if (machine == null) missing.Add("transmutadora (TransmutatorMachine)");
            else { if (entry == null) missing.Add("caja de entrada (Collider trigger activo en la transmutadora)"); if (machine.catalog == null) missing.Add("catálogo de recetas en la transmutadora"); }
            if (evaluator == null) missing.Add("máquina de envío (DeliveryEvaluator)");
            if (orders == null) missing.Add("OrderManager");
            if (atomSpawner == null) missing.Add("spawner de átomos (AtomSpawner activo)");
            if (barSpawner == null) missing.Add("spawner de barras (BarSpawner activo)");
            Debug.LogError("[Recetas] Falta en la escena: " + string.Join(", ", missing));
            Destroy(gameObject); yield break;
        }

        var recipes = machine.catalog.recipes.Where(r => r != null).ToList();
        Debug.Log($"[Recetas] Probando {recipes.Count} recetas del catálogo…");

        // Si quedó un encargo en curso de antes, se espera a que termine (no se puede aceptar otro)
        if (orders.HasActiveOrder) Debug.LogWarning("[Recetas] Hay un encargo en curso; la prueba lo reemplaza al aceptarse cada receta si el OrderManager lo permite.");

        foreach (var r in recipes)
        {
            var row = new Row { name = Clean(r.displayName), formula = r.formula, kind = r.category.ToString() };
            rows.Add(row);
            yield return TestRecipe(r, row);
            yield return new WaitForSeconds(1.5f);
        }

        // Caso malo: entregar una receta que no es la del encargo
        if (recipes.Count >= 2) yield return TestWrongDelivery(recipes[0], recipes[1]);

        Report();
        Destroy(gameObject, 1f);
    }

    // =================================================================
    IEnumerator TestRecipe(RecipeData r, Row row)
    {
        // 1. Datos
        string dataProblem = CheckData(r);
        row.data = dataProblem == null ? "ok" : "MAL";
        if (dataProblem != null) { row.note = dataProblem; yield break; }

        // 2. Encargo
        ForceFreeOrder();
        if (!orders.AcceptOrder(r, r.timeLimit)) { row.note = "No se pudo aceptar el encargo (¿hay otro en curso?)"; yield break; }
        int before = score != null ? score.Score : 0;

        // 3. Palancas
        if (machine.pressureLever != null) machine.pressureLever.SetLevel(r.requiredPressure);
        if (machine.temperatureLever != null) machine.temperatureLever.SetLevel(r.requiredTemperature);

        // 4. Contenedor
        bool gas = r.category == CompoundCategory.Gas;
        ClearOutput();
        yield return null;
        var container = EmptyContainer(gas);
        if (container == null) { row.note = $"No hay {(gas ? "botella" : "vaso")} vacío disponible"; yield break; }
        if (gas)
        {
            Place(container, machine.gasSocket.transform.position);
            yield return new WaitForSeconds(1f);
            if (!machine.gasSocket.hasSelection) { row.note = "La botella no se pegó al socket de gases"; yield break; }
        }
        else
        {
            var z = machine.cupZone.bounds;
            Place(container, new Vector3(z.center.x, z.min.y + 0.02f, z.center.z), bottomAt: true);
            yield return new WaitForSeconds(1f);
        }

        // 5. Molécula
        RecipeData synthesized = null; SampleItem filled = null; ValidationState? failed = null; LeverFail? leverFail = null; string ejected = null;
        System.Action<RecipeData, SampleItem> onSyn = (rr, s) => { synthesized = rr; filled = s; };
        System.Action<ValidationState> onFail = s => failed = s;
        System.Action<LeverFail> onLever = k => leverFail = k;
        System.Action<string> onEject = why => ejected = why;
        machine.Synthesized += onSyn; machine.Failed += onFail; machine.LeverFailed += onLever;
        TransmutatorAutoStart.MoleculeEjected += onEject;

        var existing = new HashSet<Molecule>(FindObjectsByType<Molecule>(FindObjectsSortMode.None));
        var builder = new GameObject("Molécula " + r.formula).AddComponent<MoleculaDePrueba>();
        builder.transform.position = entry.bounds.center;
        builder.recipe = r; builder.atomPrefab = atomSpawner.atomPrefab; builder.barPrefab = barSpawner.barPrefab;
        yield return null; yield return null;

        var mol = FindObjectsByType<Molecule>(FindObjectsSortMode.None).Where(m => !existing.Contains(m))
                  .OrderBy(m => (m.transform.position - entry.bounds.center).sqrMagnitude).FirstOrDefault();
        int pieces = mol != null ? 1 + mol.transform.Cast<Transform>().Count(t => t.GetComponent<Atom>() != null || t.GetComponent<Bond>() != null) : 0;
        int expected = r.atoms.Length + (r.bonds?.Length ?? 0);
        row.build = mol != null && pieces == expected ? $"ok ({r.atoms.Length}a+{r.bonds.Length}b)" : $"MAL ({pieces}/{expected} piezas)";

        // 6. Síntesis
        float t0 = Time.time;
        while (synthesized == null && failed == null && leverFail == null && Time.time - t0 < 20f) yield return null;
        machine.Synthesized -= onSyn; machine.Failed -= onFail; machine.LeverFailed -= onLever;
        TransmutatorAutoStart.MoleculeEjected -= onEject;

        if (synthesized == null)
        {
            row.synth = "MAL";
            row.note = failed != null ? $"El validador dijo {failed} (no reconoce la molécula)"
                     : leverFail != null ? $"Falló la palanca de {leverFail}"
                     : ejected != null ? $"La máquina devolvió la molécula: {ejected}"
                     : "No sintetizó en 20 s";
            CleanupLoosePieces();
            yield break;
        }
        row.synth = synthesized == r ? $"ok ({Time.time - t0:0.0}s)" : $"MAL (salió {synthesized.formula})";
        row.fill = filled != null && filled.recipe == r ? "ok" : $"MAL ({(filled != null && filled.recipe != null ? filled.recipe.formula : "vacío")})";
        if (synthesized != r || filled == null) { row.note = "La transmutadora identificó otra receta"; yield break; }

        // 7. Entrega
        DeliveryResult? res = null; RejectReason? rej = null;
        System.Action<DeliveryResult> onDel = x => res = x;
        System.Action<SampleItem, RejectReason> onRej = (s, x) => rej = x;
        evaluator.Delivered += onDel; evaluator.Rejected += onRej;
        yield return TakeTo(filled, evaluator.inputZone.bounds.center);
        t0 = Time.time;
        while (res == null && rej == null && Time.time - t0 < 6f) yield return null;
        evaluator.Delivered -= onDel; evaluator.Rejected -= onRej;

        if (res != null)
        {
            int after = score != null ? score.Score : 0;
            row.deliver = $"ok +{res.Value.totalPoints}";
            row.ok = score == null || after > before;
            if (!row.ok) row.note = "Se aceptó pero el puntaje no subió";
        }
        else
        {
            row.deliver = rej != null ? $"RECHAZO {rej}" : "MAL (no evaluó)";
            row.note = rej != null ? "La máquina de envío rechazó una muestra correcta" : "La muestra no entró a la zona de envío";
        }
    }

    // Entregar la receta B con un encargo de la receta A: tiene que rechazarse
    IEnumerator TestWrongDelivery(RecipeData orderRecipe, RecipeData madeRecipe)
    {
        var row = new Row { name = "Caso malo", formula = $"{madeRecipe.formula}≠{orderRecipe.formula}", kind = "-" };
        rows.Add(row);
        ForceFreeOrder();
        if (!orders.AcceptOrder(orderRecipe, orderRecipe.timeLimit)) { row.note = "No se pudo aceptar el encargo"; yield break; }
        if (machine.pressureLever != null) machine.pressureLever.SetLevel(madeRecipe.requiredPressure);
        if (machine.temperatureLever != null) machine.temperatureLever.SetLevel(madeRecipe.requiredTemperature);
        bool gas = madeRecipe.category == CompoundCategory.Gas;
        ClearOutput(); yield return null;
        var container = EmptyContainer(gas);
        if (container == null) { row.note = "No hay contenedor vacío"; yield break; }
        if (gas) Place(container, machine.gasSocket.transform.position);
        else { var z = machine.cupZone.bounds; Place(container, new Vector3(z.center.x, z.min.y + 0.02f, z.center.z), bottomAt: true); }
        yield return new WaitForSeconds(1f);

        SampleItem filled = null;
        System.Action<RecipeData, SampleItem> onSyn = (rr, s) => filled = s;
        machine.Synthesized += onSyn;
        var builder = new GameObject("Molécula caso malo").AddComponent<MoleculaDePrueba>();
        builder.transform.position = entry.bounds.center;
        builder.recipe = madeRecipe; builder.atomPrefab = atomSpawner.atomPrefab; builder.barPrefab = barSpawner.barPrefab;
        float t0 = Time.time;
        while (filled == null && Time.time - t0 < 20f) yield return null;
        machine.Synthesized -= onSyn;
        row.synth = filled != null ? "ok" : "MAL";
        if (filled == null) { row.note = "No sintetizó"; CleanupLoosePieces(); yield break; }

        DeliveryResult? res = null; RejectReason? rej = null;
        System.Action<DeliveryResult> onDel = x => res = x;
        System.Action<SampleItem, RejectReason> onRej = (s, x) => rej = x;
        evaluator.Delivered += onDel; evaluator.Rejected += onRej;
        yield return TakeTo(filled, evaluator.inputZone.bounds.center);
        t0 = Time.time;
        while (res == null && rej == null && Time.time - t0 < 6f) yield return null;
        evaluator.Delivered -= onDel; evaluator.Rejected -= onRej;
        row.deliver = res != null ? "ACEPTÓ (mal)" : rej != null ? $"rechazo {rej}" : "no evaluó";
        row.ok = rej != null && res == null;
        row.note = row.ok ? "Correcto: rechaza la receta equivocada" : "Debería rechazar la receta equivocada";
        yield return new WaitForSeconds(2f);
        // Recoge la muestra rechazada para no dejarla en la zona de envío
        if (filled != null && filled.recipe != null) filled.ResetContents();
        ForceFreeOrder();
    }

    // =================================================================
    static string CheckData(RecipeData r)
    {
        if (r.atoms == null || r.atoms.Length == 0) return "La receta no tiene átomos";
        if (r.atoms.Any(a => a == null)) return "Hay un átomo vacío en la lista";
        var deg = new int[r.atoms.Length];
        foreach (var b in r.bonds ?? new RecipeData.BondDef[0])
        {
            if (b.a < 0 || b.b < 0 || b.a >= deg.Length || b.b >= deg.Length) return $"Enlace fuera de rango ({b.a}-{b.b})";
            if (b.a == b.b) return $"Enlace de un átomo consigo mismo ({b.a})";
            deg[b.a]++; deg[b.b]++;
        }
        for (int i = 0; i < deg.Length; i++)
            if (deg[i] != r.atoms[i].valence) return $"{r.atoms[i].symbol}[{i}] tiene {deg[i]} enlaces y su valencia es {r.atoms[i].valence}";
        return null;
    }

    // Deja libre el OrderManager para aceptar otro encargo (la prueba lo hace por código)
    void ForceFreeOrder()
    {
        if (!orders.HasActiveOrder) return;
        var p = typeof(OrderManager).GetProperty("Current");
        var setter = p != null ? p.GetSetMethod(true) : null;
        if (setter != null) setter.Invoke(orders, new object[] { null });
    }

    // Quita de la salida cualquier contenedor que haya quedado (socket y zona del vaso)
    void ClearOutput()
    {
        foreach (var sock in MachineSockets())
        {
            if (!sock.hasSelection) continue;
            var sel = sock.firstInteractableSelected as XRGrabInteractable;
            if (sel == null) continue;
            StartCoroutine(SocketPause(sock));
            sel.interactionManager.CancelInteractableSelection((IXRSelectInteractable)sel);
            sel.transform.position += Vector3.up * 3f + Vector3.right * 2f;
        }
        if (machine.cupZone != null)
            foreach (var s in FindObjectsByType<SampleItem>(FindObjectsSortMode.None))
                if (machine.cupZone.bounds.Intersects(BoundsOf(s))) s.transform.position += Vector3.up * 3f + Vector3.right * 2f;
        Physics.SyncTransforms();
    }

    IEnumerator SocketPause(UnityEngine.XR.Interaction.Toolkit.Interactors.XRSocketInteractor sock)
    {
        sock.socketActive = false;
        yield return new WaitForSeconds(0.6f);
        if (sock != null) sock.socketActive = true;
    }

    SampleItem EmptyContainer(bool gas)
    {
        var zone = evaluator.inputZone != null ? evaluator.inputZone.bounds : new Bounds();
        return FindObjectsByType<SampleItem>(FindObjectsSortMode.None)
            .Where(s => s.kind == (gas ? ContainerKind.Bottle : ContainerKind.Cup) && s.IsEmpty && !zone.Intersects(BoundsOf(s)))
            .FirstOrDefault();
    }

    // Saca el contenedor de la salida (como la mano del jugador) y lo pone en un punto.
    // El socket se apaga un momento para que no lo vuelva a agarrar mientras se mueve.
    IEnumerator TakeTo(SampleItem s, Vector3 point)
    {
        var socks = MachineSockets();
        foreach (var k in socks) k.socketActive = false;
        var g = s.GetComponent<XRGrabInteractable>();
        if (g != null && g.isSelected && g.interactionManager != null)
            g.interactionManager.CancelInteractableSelection((IXRSelectInteractable)g);
        Place(s, point);
        yield return new WaitForSeconds(0.6f);
        foreach (var k in socks) if (k != null) k.socketActive = true;
    }

    // Sockets de la salida: el de gases y el del vaso (CupZoneSocket)
    List<UnityEngine.XR.Interaction.Toolkit.Interactors.XRSocketInteractor> MachineSockets()
    {
        var list = new List<UnityEngine.XR.Interaction.Toolkit.Interactors.XRSocketInteractor>();
        if (machine.gasSocket != null) list.Add(machine.gasSocket);
        var cup = CupZoneSocket.Find(machine);
        if (cup != null) list.Add(cup);
        return list;
    }

    static void CleanupLoosePieces()
    {
        // Átomos y barras de prueba que quedaron sueltos tras un fallo (no tocar los de las cestas)
        foreach (var a in FindObjectsByType<Atom>(FindObjectsSortMode.None))
            if (a != null && a.name.EndsWith("(prueba)")) Destroy(a.gameObject);
        foreach (var b in FindObjectsByType<Bond>(FindObjectsSortMode.None))
            if (b != null && b.transform.parent == null && (b.AtomA == null || b.AtomA.name.EndsWith("(prueba)"))) { if (b.IsComplete || b.AtomA != null) Destroy(b.gameObject); }
    }

    static Bounds BoundsOf(SampleItem s)
    {
        var r = s.GetComponent<Renderer>();
        return r != null ? r.bounds : new Bounds(s.transform.position, Vector3.one * 0.1f);
    }

    static void Place(SampleItem s, Vector3 point, bool bottomAt = false)
    {
        var rb = s.GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        var b = BoundsOf(s);
        Vector3 refPoint = bottomAt ? new Vector3(b.center.x, b.min.y, b.center.z) : b.center;
        s.transform.position += point - refPoint;
        Physics.SyncTransforms();
    }

    static string Clean(string s) => string.IsNullOrEmpty(s) ? "?" : s.Trim();

    void Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine("==================== PRUEBA DE TODAS LAS RECETAS ====================");
        sb.AppendLine(" Receta                  | Fórmula   | Tipo   | Datos | Armado       | Síntesis     | Contenedor | Entrega");
        foreach (var r in rows)
        {
            sb.AppendLine($" {(r.ok ? "✔" : "✘")} {r.name,-22}| {r.formula,-10}| {r.kind,-7}| {r.data,-6}| {r.build,-13}| {r.synth,-13}| {r.fill,-11}| {r.deliver}");
            if (!string.IsNullOrEmpty(r.note)) sb.AppendLine($"     → {r.note}");
        }
        int good = rows.Count(r => r.ok);
        sb.AppendLine($" Resultado: {good} de {rows.Count} bien.");
        sb.AppendLine("=====================================================================");
        if (good == rows.Count) Debug.Log("[Recetas]\n" + sb); else Debug.LogWarning("[Recetas]\n" + sb);
    }
}

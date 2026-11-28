using System.Collections;
using System.Linq;
using System.Text;
using UnityEngine;

/// Prueba automática del ciclo completo, sin visor VR.
/// Uso: en Play, menú  Molecula > Prueba automática del ciclo (en Play).
/// Hace, paso a paso, lo mismo que haría el jugador y escribe en la consola qué pasó en cada paso:
///   1. Acepta el primer encargo de la pantalla (el gestor de encargos), así empieza a correr el tiempo.
///   2. Pone las palancas en lo que pide la receta.
///   3. Pone un vaso vacío (o una botella en el socket, si es gas) en la salida de la transmutadora.
///   4. Arma la molécula con átomos y barras reales dentro de la entrada de la transmutadora.
///   5. Espera la síntesis y revisa que el contenedor guardó la receta.
///   6. Lleva el contenedor a la máquina de envío y revisa la entrega y los puntos.
///   7. Prueba la molécula emparentada: arma otra y la separa pieza por pieza.
public class PruebaAutomatica : MonoBehaviour
{
#if UNITY_EDITOR
    [UnityEditor.MenuItem("Molecula/Prueba automática del ciclo (en Play)")]
    static void Run()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[Prueba] Dale Play primero."); return; }
        var go = new GameObject("PruebaAutomatica");
        go.AddComponent<PruebaAutomatica>();
    }
#endif

    readonly StringBuilder log = new StringBuilder();
    int ok, fail;

    void Step(bool good, string text)
    {
        if (good) ok++; else fail++;
        log.AppendLine($"   {(good ? "[✔]" : "[✘]")} {text}");
        Debug.Log($"[Prueba] {(good ? "OK" : "FALLA")}: {text}");
    }

    IEnumerator Start()
    {
        log.AppendLine("==================== PRUEBA AUTOMÁTICA DEL CICLO ====================");
        var machine = FindFirstObjectByType<TransmutatorMachine>();
        var gestor = FindFirstObjectByType<GestorEncargosVR>();
        var evaluator = FindFirstObjectByType<DeliveryEvaluator>();
        var score = FindFirstObjectByType<ScoreManager>();
        var atomSpawner = FindFirstObjectByType<AtomSpawner>();
        var barSpawner = FindFirstObjectByType<BarSpawner>();
        Step(machine != null, "Hay transmutadora");
        Step(gestor != null, "Hay gestor de encargos");
        Step(evaluator != null, "Hay máquina de envío (DeliveryEvaluator)");
        if (machine == null || gestor == null || evaluator == null || atomSpawner == null || barSpawner == null) { Finish(); yield break; }

        // 1. Encargo
        var encargo = gestor.encargos.FirstOrDefault(e => e.recipe != null && e.estado == EstadoEncargo.Pendiente);
        Step(encargo != null, encargo != null ? $"Encargo pendiente: {encargo.recipe.displayName}" : "No hay encargos pendientes");
        if (encargo == null) { Finish(); yield break; }
        while (gestor.encargos.IndexOf(encargo) > 0 && GestorIndex(gestor) != gestor.encargos.IndexOf(encargo)) { gestor.SiguienteEncargo(); yield return null; }
        int before = score != null ? score.Score : 0;
        gestor.AceptarEncargo();
        var orders = evaluator.orders;
        Step(orders != null && orders.HasActiveOrder && orders.Current.recipe == encargo.recipe,
             $"Aceptar encargo: tiempo corriendo ({(orders != null && orders.Current != null ? orders.Current.timeLimit : 0):0} s)");
        var recipe = encargo.recipe;

        // 2. Palancas
        if (machine.pressureLever != null) machine.pressureLever.SetLevel(recipe.requiredPressure);
        if (machine.temperatureLever != null) machine.temperatureLever.SetLevel(recipe.requiredTemperature);
        yield return null;
        Step((machine.pressureLever == null || machine.pressureLever.Level == recipe.requiredPressure) &&
             (machine.temperatureLever == null || machine.temperatureLever.Level == recipe.requiredTemperature),
             $"Palancas en presión {recipe.requiredPressure.Label()} / temperatura {recipe.requiredTemperature.Label()}");

        // 3. Contenedor
        bool gas = recipe.category == CompoundCategory.Gas;
        SampleItem container = FindObjectsByType<SampleItem>(FindObjectsSortMode.None)
            .Where(s => s.kind == (gas ? ContainerKind.Bottle : ContainerKind.Cup) && s.IsEmpty).FirstOrDefault();
        Step(container != null, container != null ? $"Contenedor vacío encontrado: {container.name}" : "No hay contenedor vacío del tipo correcto");
        if (container == null) { Finish(); yield break; }
        if (gas)
        {
            Place(container, machine.gasSocket.transform.position);
            yield return new WaitForSeconds(1.0f);
            Step(machine.gasSocket.hasSelection, "La botella quedó puesta en el socket de gases");
        }
        else
        {
            var z = machine.cupZone.bounds;
            Place(container, new Vector3(z.center.x, z.min.y + 0.02f, z.center.z), bottomAt: true);
            yield return new WaitForSeconds(1.0f);
            var cs = CupZoneSocket.Find(machine);
            bool snapped = cs != null && cs.hasSelection;
            Step(machine.cupZone.bounds.Intersects(Bounds(container)) && (cs == null || snapped),
                 snapped ? "El vaso se encajó en su socket bajo el pico" : "El vaso quedó en la zona bajo el pico");
        }

        // 4. Molécula dentro de la entrada
        var entry = machine.GetComponents<Collider>().FirstOrDefault(c => c.isTrigger && c.enabled);
        Step(entry != null, "La transmutadora tiene caja de entrada");
        if (entry == null) { Finish(); yield break; }
        RecipeData synthesized = null; SampleItem filled = null;
        System.Action<RecipeData, SampleItem> onSyn = (r, s) => { synthesized = r; filled = s; };
        machine.Synthesized += onSyn;
        var builder = new GameObject("Molécula de prueba").AddComponent<MoleculaDePrueba>();
        builder.transform.position = entry.bounds.center;
        builder.recipe = recipe; builder.atomPrefab = atomSpawner.atomPrefab; builder.barPrefab = barSpawner.barPrefab;
        yield return null; yield return null;
        var mol = FindObjectsByType<Molecule>(FindObjectsSortMode.None).OrderByDescending(m => m.GetInstanceID()).FirstOrDefault();
        Step(mol != null && mol.transform.childCount >= recipe.atoms.Length - 1,
             mol != null ? $"Molécula armada como una sola pieza: raíz {mol.name} con {mol.transform.childCount} piezas hijas" : "La molécula no quedó emparentada");

        // 5. Síntesis
        float t0 = Time.time;
        while (synthesized == null && Time.time - t0 < 15f) yield return null;
        machine.Synthesized -= onSyn;
        Step(synthesized == recipe, synthesized != null ? $"Síntesis: {synthesized.displayName} en {Time.time - t0:0.0} s" : "La transmutadora no sintetizó en 15 s (¿tomó la molécula? ver consola)");
        if (filled == null) { Finish(); yield break; }
        Step(filled.recipe == recipe, $"El contenedor guardó la receta ({(filled.recipe != null ? filled.recipe.formula : "nada")})");

        // 6. Entrega
        DeliveryResult? res = null; RejectReason? rej = null;
        System.Action<DeliveryResult> onDel = r => res = r;
        System.Action<SampleItem, RejectReason> onRej = (s, r) => rej = r;
        evaluator.Delivered += onDel; evaluator.Rejected += onRej;
        // Se saca de la salida como lo haría la mano (socket de gases o socket del vaso), con los sockets en pausa
        var socks = new System.Collections.Generic.List<UnityEngine.XR.Interaction.Toolkit.Interactors.XRSocketInteractor>();
        if (machine.gasSocket != null) socks.Add(machine.gasSocket);
        var cupSock = CupZoneSocket.Find(machine); if (cupSock != null) socks.Add(cupSock);
        foreach (var k in socks) k.socketActive = false;
        { var g = filled.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>(); if (g != null && g.isSelected && g.interactionManager != null) g.interactionManager.CancelInteractableSelection((UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable)g); }
        Place(filled, evaluator.inputZone.bounds.center);
        StartCoroutine(Resume(socks));
        t0 = Time.time;
        while (res == null && rej == null && Time.time - t0 < 5f) yield return null;
        evaluator.Delivered -= onDel; evaluator.Rejected -= onRej;
        if (res != null) Step(true, $"Entrega aceptada: base {res.Value.basePoints} + bonus {res.Value.bonusPoints} + tapa {res.Value.sealedBonus} = {res.Value.totalPoints} pts");
        else Step(false, rej != null ? $"Entrega rechazada: {rej}" : "La máquina de envío no evaluó el contenedor en 5 s (¿entró a la zona?)");
        if (score != null) Step(score.Score > before, $"Puntaje: {before} → {score.Score}");

        // 7. Separar pieza por pieza
        var builder2 = new GameObject("Molécula para separar").AddComponent<MoleculaDePrueba>();
        builder2.transform.position = evaluator.transform.position + Vector3.up * 1.5f + Vector3.right * 1.5f;
        builder2.recipe = recipe; builder2.atomPrefab = atomSpawner.atomPrefab; builder2.barPrefab = barSpawner.barPrefab;
        yield return null; yield return null;
        var mol2 = FindObjectsByType<Molecule>(FindObjectsSortMode.None).OrderByDescending(m => m.GetInstanceID()).FirstOrDefault();
        if (mol2 != null)
        {
            int pieces = mol2.transform.childCount + 1, links = mol2.links.Count;
            for (int i = 0; i < links && mol2 != null; i++) { mol2.DetachLast(); yield return null; }
            Step(mol2 == null || mol2.transform.childCount == 0, $"Separar pieza por pieza: {links} uniones deshechas, {pieces} piezas sueltas");
        }
        else Step(false, "No se armó la segunda molécula para probar la separación");

        Finish();
    }

    static IEnumerator Resume(System.Collections.Generic.List<UnityEngine.XR.Interaction.Toolkit.Interactors.XRSocketInteractor> socks)
    {
        yield return new WaitForSeconds(0.6f);
        foreach (var k in socks) if (k != null) k.socketActive = true;
    }

    static int GestorIndex(GestorEncargosVR g)
    {
        var f = typeof(GestorEncargosVR).GetField("indiceActual", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return f != null ? (int)f.GetValue(g) : 0;
    }

    static Bounds Bounds(SampleItem s)
    {
        var r = s.GetComponent<Renderer>();
        return r != null ? r.bounds : new Bounds(s.transform.position, Vector3.one * 0.1f);
    }

    // Coloca un contenedor derecho con su centro (o su base) en un punto
    static void Place(SampleItem s, Vector3 point, bool bottomAt = false)
    {
        var rb = s.GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        var b = Bounds(s);
        Vector3 refPoint = bottomAt ? new Vector3(b.center.x, b.min.y, b.center.z) : b.center;
        s.transform.position += point - refPoint;
        Physics.SyncTransforms();
    }

    void Finish()
    {
        log.AppendLine($"   Resultado: {ok} pasos bien, {fail} con falla.");
        log.AppendLine("=====================================================================");
        if (fail > 0) Debug.LogWarning("[Prueba]\n" + log); else Debug.Log("[Prueba]\n" + log);
        Destroy(gameObject, 1f);
    }
}

using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// Arreglos del core loop que se aplican solos al dar Play (no hay que ponerlo en la escena):
///  1. Botones del spawner de átomos que NO estén en su lista "elements" ni tengan acción en el inspector:
///     el botón con letra (H, O, C, N, Cl, Si...) suelta ese átomo y el botón sin letra borra.
///     (Los de la lista "elements" ya los conecta el AtomSpawner; no se duplican.)
///  2. Máquina Transmutadora sin botón Iniciar: arranca sola cuando hay una molécula completa en la entrada,
///     suelta (nadie la sostiene) durante un momento.
///  3. Puntaje: si la escena no tiene ScoreManager se crea uno, con un letrero "Puntos" sobre la máquina de envío.
public static class CoreLoopFixer
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
        Fix();
    }

    static void OnLoaded(Scene s, LoadSceneMode m) => Fix();

    static void Fix()
    {
        var atomTypes = KnownAtomData();
        foreach (var sp in Object.FindObjectsByType<AtomSpawner>(FindObjectsSortMode.None)) WireAtomSpawner(sp, atomTypes);

        foreach (var m in Object.FindObjectsByType<TransmutatorMachine>(FindObjectsSortMode.None))
            if (m.GetComponent<TransmutatorAutoStart>() == null && !HasStartButton()) m.gameObject.AddComponent<TransmutatorAutoStart>();

        EnsureScoreManager();
    }

    // ---------------------------------------------------------------
    // 1. Spawner de átomos
    // ---------------------------------------------------------------
    static Dictionary<string, AtomData> KnownAtomData()
    {
        var d = new Dictionary<string, AtomData>(System.StringComparer.OrdinalIgnoreCase);
        void Add(AtomData a) { if (a != null && !string.IsNullOrEmpty(a.symbol) && !d.ContainsKey(a.symbol.Trim())) d[a.symbol.Trim()] = a; }

        foreach (var a in Resources.FindObjectsOfTypeAll<AtomData>()) Add(a);               // los que ya están cargados
        foreach (var m in Object.FindObjectsByType<TransmutatorMachine>(FindObjectsSortMode.None))
            if (m.catalog != null) foreach (var r in m.catalog.recipes) if (r != null && r.atoms != null) foreach (var a in r.atoms) Add(a);
        foreach (var sp in Object.FindObjectsByType<AtomSpawner>(FindObjectsSortMode.None))
            if (sp.atomPrefab != null) Add(sp.atomPrefab.data);
        return d;
    }

    static bool HasValidAction(UnityEvent e)
    {
        for (int i = 0; i < e.GetPersistentEventCount(); i++)
            if (e.GetPersistentTarget(i) != null && !string.IsNullOrEmpty(e.GetPersistentMethodName(i))) return true;
        return false;
    }

    static readonly HashSet<int> fixedSpawners = new HashSet<int>();

    static void WireAtomSpawner(AtomSpawner sp, Dictionary<string, AtomData> atoms)
    {
        if (!fixedSpawners.Add(sp.GetInstanceID())) return;          // nunca conectar dos veces el mismo spawner

        // Los botones que ya están en la lista "elements" del spawner los conecta el propio AtomSpawner
        var own = new HashSet<LabButton>();
        if (sp.elements != null)
            foreach (var e in sp.elements)
                if (e != null) { if (e.spawnButton != null) own.Add(e.spawnButton); if (e.deleteButton != null) own.Add(e.deleteButton); }

        int wired = 0;
        foreach (var b in sp.GetComponentsInChildren<LabButton>(true))
        {
            if (own.Contains(b) || HasValidAction(b.onPressed)) continue;
            string label = LabelOf(b);
            if (!string.IsNullOrEmpty(label) && atoms.TryGetValue(label, out var data))
            {
                b.onPressed.AddListener(() => sp.Spawn(data));
                wired++;
            }
            else if (string.IsNullOrEmpty(label) || label.ToLowerInvariant().StartsWith("borr") || label == "X")
            {
                b.onPressed.AddListener(sp.DeleteAtoms);
                wired++;
            }
            else Debug.LogWarning($"[CoreLoopFixer] Botón \"{label}\" del spawner: no hay AtomData con ese símbolo.", b);
        }
        if (wired > 0) Debug.Log($"[CoreLoopFixer] Spawner de átomos: {wired} botones conectados.", sp);
    }

    static string LabelOf(LabButton b)
    {
        var t = b.GetComponentInChildren<TMP_Text>(true);
        if (t == null && b.transform.parent != null)
        {
            // texto hermano más cercano
            float best = 0.25f;
            foreach (var c in b.transform.parent.GetComponentsInChildren<TMP_Text>(true))
            {
                float d = Vector3.Distance(c.transform.position, b.transform.position);
                if (d < best) { best = d; t = c; }
            }
        }
        return t != null ? t.text.Trim() : null;
    }

    static bool HasStartButton()
    {
        foreach (var b in Object.FindObjectsByType<LabButton>(FindObjectsSortMode.None))
            for (int i = 0; i < b.onPressed.GetPersistentEventCount(); i++)
                if (b.onPressed.GetPersistentMethodName(i) == "StartReaction" && b.onPressed.GetPersistentTarget(i) != null) return true;
        return false;
    }

    // ---------------------------------------------------------------
    // 3. Puntaje
    // ---------------------------------------------------------------
    static void EnsureScoreManager()
    {
        if (Object.FindFirstObjectByType<ScoreManager>() != null) return;
        var evaluator = Object.FindFirstObjectByType<DeliveryEvaluator>();
        if (evaluator == null) return;

        var go = new GameObject("ScoreManager (auto)");
        go.SetActive(false);                                   // se configura antes de que se suscriba a los eventos
        var sm = go.AddComponent<ScoreManager>();
        sm.evaluator = evaluator;
        sm.orders = evaluator.orders != null ? evaluator.orders : Object.FindFirstObjectByType<OrderManager>();
        sm.machine = Object.FindFirstObjectByType<TransmutatorMachine>();

        // Letrero de puntos sobre la máquina de envío, mirando al jugador
        var label = new GameObject("PuntosTexto");
        Transform anchor = evaluator.inputZone != null ? evaluator.inputZone.transform : evaluator.transform;
        label.transform.position = anchor.position + Vector3.up * 0.45f;
        var tmp = label.AddComponent<TextMeshPro>();
        tmp.text = "Puntos: 0";
        tmp.fontSize = 2.2f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(1f, 0.92f, 0.4f);
        tmp.rectTransform.sizeDelta = new Vector2(2f, 0.5f);
        label.AddComponent<FaceCamera>();
        sm.scoreText = tmp;

        go.SetActive(true);
        Debug.Log("[CoreLoopFixer] ScoreManager creado: los puntos de cada entrega se suman y se ven sobre la máquina de envío.");
    }
}


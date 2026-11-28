using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// Conecta los eventos del juego con los sonidos 360, sin tocar la escena ni arrastrar nada en los inspectores.
/// Cada medio segundo busca objetos nuevos (átomos y barras que salen del spawner, botones, palancas...) y les
/// engancha su sonido una sola vez. Cada sonido sale del punto donde ocurre.
public class Audio360Binder : MonoBehaviour
{
    public float scanInterval = 0.5f;

    readonly HashSet<int> hooked = new HashSet<int>();
    float nextScan;
    AudioSource humLoop, pourLoop;
    TransmutatorMachine machine;
    Transform orderScreen;
    readonly Dictionary<WristMenu, bool> wristState = new Dictionary<WristMenu, bool>();

    void OnEnable() { LabButton.AnyPressed += OnLabButton; }
    void OnDisable() { LabButton.AnyPressed -= OnLabButton; }

    void Update()
    {
        if (Time.time >= nextScan)
        {
            nextScan = Time.time + scanInterval;
            Scan();
        }

        // Menú de la muñeca: sonido al abrir y al cerrar
        foreach (var wm in FindObjectsByType<WristMenu>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            bool open = wm.gameObject.activeInHierarchy;
            if (wristState.TryGetValue(wm, out var was) && was != open)
                Audio360Manager.Play(open ? SoundId360.WristOpen : SoundId360.WristClose, wm.transform);
            wristState[wm] = open;
        }
    }

    bool First(Object o) => o != null && hooked.Add(o.GetInstanceID());

    void Scan()
    {
        // ---- Agarrar cualquier objeto (átomos, barras, botellas, vasos, canastas) ----
        foreach (var g in FindObjectsByType<XRGrabInteractable>(FindObjectsSortMode.None))
            if (First(g)) g.selectEntered.AddListener(a => Audio360Manager.Play(SoundId360.Grab, a.interactableObject.transform));

        // ---- Enlaces: clic de puerto con variante por elemento y tono según la valencia ----
        foreach (var atom in FindObjectsByType<Atom>(FindObjectsSortMode.None))
            if (First(atom))
            {
                int last = atom.BondCount;
                atom.BondsChanged += a =>
                {
                    if (a == null || a.data == null) return;
                    int now = a.BondCount;
                    if (now > last)
                        Audio360Manager.Play(SoundId360.PortClick, a.transform, a.data.symbol, 1.25f - 0.08f * a.data.valence);
                    last = now;
                };
            }

        // ---- Palancas ----
        foreach (var lever in FindObjectsByType<LabLever>(FindObjectsSortMode.None))
            if (First(lever))
            {
                var l = lever;
                lever.LevelChanged += _ => { if (l.clickSound == null) Audio360Manager.Play(SoundId360.LeverClick, l.pivot != null ? l.pivot : l.transform); };
            }

        // ---- Botones de interfaz (pantalla de pedidos, muñeca) ----
        foreach (var b in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (First(b))
            {
                var t = b.transform;
                b.onClick.AddListener(() => Audio360Manager.Play(SoundId360.UiTap, t));
            }

        // ---- Spawners ----
        foreach (var s in FindObjectsByType<AtomSpawner>(FindObjectsSortMode.None))
            if (First(s))
            {
                var at = s.nozzle != null ? s.nozzle : s.transform;
                s.onSpawned.AddListener(() => Audio360Manager.Play(SoundId360.SpawnerDispense, at));
                s.onDeleted.AddListener(() => Audio360Manager.Play(SoundId360.SpawnerDelete, at));
                s.onFull.AddListener(() => Audio360Manager.Play(SoundId360.SpawnerDenied, at));
                s.onNoBasket.AddListener(() => Audio360Manager.Play(SoundId360.SpawnerDenied, at));
                s.onNothingToDelete.AddListener(() => Audio360Manager.Play(SoundId360.SpawnerDenied, at));
            }
        foreach (var s in FindObjectsByType<BarSpawner>(FindObjectsSortMode.None))
            if (First(s))
            {
                var at = s.nozzle != null ? s.nozzle : s.transform;
                s.onSpawned.AddListener(() => Audio360Manager.Play(SoundId360.SpawnerDispense, at));
                s.onDeleted.AddListener(() => Audio360Manager.Play(SoundId360.SpawnerDelete, at));
                s.onFull.AddListener(() => Audio360Manager.Play(SoundId360.SpawnerDenied, at));
                s.onNoBasket.AddListener(() => Audio360Manager.Play(SoundId360.SpawnerDenied, at));
                s.onNothingToDelete.AddListener(() => Audio360Manager.Play(SoundId360.SpawnerDenied, at));
            }

        // ---- Máquina Transmutadora ----
        foreach (var m in FindObjectsByType<TransmutatorMachine>(FindObjectsSortMode.None))
            if (First(m)) HookMachine(m);

        // ---- Pedidos ----
        if (orderScreen == null)
        {
            var screen = FindFirstObjectByType<GestorEncargosVR>();
            if (screen != null) orderScreen = screen.transform;
        }
        foreach (var o in FindObjectsByType<OrderManager>(FindObjectsSortMode.None))
            if (First(o))
            {
                var om = o;
                o.OrderPublished += _ => Audio360Manager.Play(SoundId360.OrderArrive, ScreenOr(om.transform));
                o.OrderAccepted += _ => Audio360Manager.Play(SoundId360.OrderAccept, ScreenOr(om.transform));
                o.OrderLost += _ => Audio360Manager.Play(SoundId360.OrderLost, ScreenOr(om.transform));
                o.onLowTime.AddListener(() => Audio360Manager.Play(SoundId360.LowTime, ScreenOr(om.transform)));
                o.LevelCompleted += _ => Audio360Manager.Play(SoundId360.LevelUp, Camera.main != null ? Camera.main.transform : om.transform);
            }

        // ---- Máquina Evaluadora ----
        foreach (var ev in FindObjectsByType<DeliveryEvaluator>(FindObjectsSortMode.None))
            if (First(ev))
            {
                var at = ev.inputZone != null ? ev.inputZone.transform : ev.transform;
                int tick = 0;
                ev.Delivered += _ => { tick = 0; Audio360Manager.Play(SoundId360.DeliveryOk, at); };
                ev.onCountTick.AddListener(() => Audio360Manager.Play(SoundId360.ScoreCount, at, null, 1f + 0.03f * Mathf.Min(tick++, 15)));
                ev.Rejected += (s, r) => Audio360Manager.Play(SoundId360.DeliveryRejected, s != null ? s.transform : at);
            }

        // ---- Tutorial de la muñeca ----
        foreach (var tut in FindObjectsByType<WristTutorial>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (First(tut))
            {
                var at = tut.transform;
                tut.onGrab.AddListener(() => Audio360Manager.Play(SoundId360.UiTap, at));
                tut.onSnap.AddListener(() => Audio360Manager.Play(SoundId360.PortClick, at));
                tut.onWrong.AddListener(() => Audio360Manager.Play(SoundId360.SpawnerDenied, at));
                tut.onStepDone.AddListener(() => Audio360Manager.Play(SoundId360.TutorialStep, at));
                tut.onMachineWorking.AddListener(() => Audio360Manager.Play(SoundId360.MachineStart, at, null, 1.2f));
                tut.onJarReady.AddListener(() => Audio360Manager.Play(SoundId360.MachineSuccess, at));
                tut.onDelivered.AddListener(() => Audio360Manager.Play(SoundId360.DeliveryOk, at));
                tut.Finished += () => Audio360Manager.Play(SoundId360.TutorialComplete, at);
            }
    }

    Transform ScreenOr(Transform fallback) => orderScreen != null ? orderScreen : fallback;

    void OnLabButton(LabButton b)
    {
        if (b != null && b.clickSound == null) Audio360Manager.Play(SoundId360.ButtonClick, b.transform);
    }

    void HookMachine(TransmutatorMachine m)
    {
        machine = m;
        Transform outlet = m.spout != null ? m.spout : m.transform;
        m.onStarted.AddListener(() =>
        {
            Audio360Manager.Play(SoundId360.MachineStart, m.transform);
            if (Audio360Manager.Instance != null && humLoop == null) humLoop = Audio360Manager.Instance.StartLoop(SoundId360.MachineHum, m.transform);
        });
        m.onPourStarted.AddListener(() =>
        {
            if (Audio360Manager.Instance != null && pourLoop == null) pourLoop = Audio360Manager.Instance.StartLoop(SoundId360.MachinePour, outlet);
        });
        m.Synthesized += (recipe, sample) =>
        {
            StopLoops();
            Audio360Manager.Play(SoundId360.MachineSuccess, outlet);
            // sonido propio de la receta (agua, llama, chispas...) por el nombre del asset: H2O, HCL, CH4...
            if (recipe != null) Audio360Manager.Play(SoundId360.RecipeEffect, sample != null ? sample.transform : outlet, recipe.name);
        };
        m.Failed += _ => { StopLoops(); Audio360Manager.Play(SoundId360.MachineFailPof, outlet); };
        m.LeverFailed += _ => { StopLoops(); Audio360Manager.Play(SoundId360.MachineDenied, m.transform); };
        m.onBlocked.AddListener(() => { StopLoops(); Audio360Manager.Play(SoundId360.MachineDenied, m.transform); });
        m.onCancelled.AddListener(StopLoops);
        m.onSpill.AddListener(() => { StopLoops(); Audio360Manager.Play(SoundId360.MachineSpill, outlet); });
        m.onNothingInside.AddListener(() => Audio360Manager.Play(SoundId360.MachineDenied, m.transform));
        m.onMoleculeHeld.AddListener(() => Audio360Manager.Play(SoundId360.MachineDenied, m.transform));
        m.onNoContainer.AddListener(() => Audio360Manager.Play(SoundId360.MachineDenied, outlet));
    }

    void StopLoops()
    {
        if (Audio360Manager.Instance == null) return;
        Audio360Manager.Instance.StopLoop(humLoop, 0.6f); humLoop = null;
        Audio360Manager.Instance.StopLoop(pourLoop, 0.4f); pourLoop = null;
    }
}

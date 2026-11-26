using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// Tutorial de la muñeca: 4 pasos tipo minijuego en la zona de acción (arriba) con el texto corto abajo.
/// Todo son imágenes 2D que se arrastran (TutorialDraggable). Cada paso avanza solo cuando se cumple su condición:
///   1. Spawners y canastas: pulsar cada spawner (sale 1 átomo y 1 barra) y llevarlos a su canasta.
///   2. Enlace con barras: encajar las 2 barras en el átomo central y pegar los 2 átomos en los extremos.
///   3. Transmutadora: meter la molécula armada en la entrada; la máquina trabaja y suelta un tarrito.
///   4. Evaluadora: llevar el tarrito a la ranura; se entrega, suena la confirmación y aparece el botón Glosario.
/// Las piezas se crean por código en cada paso: no hace falta preparar prefabs ni imágenes (usa formas de colores).
/// Lo crea WristMenu > Construir UI y lo controla WristMenu (Begin al abrir el tutorial, Stop al salir).
public class WristTutorial : MonoBehaviour
{
    [Header("Referencias (las crea WristMenu > Construir UI)")]
    public RectTransform actionZone;                     // zona de arriba donde pasan los minijuegos
    public TMP_Text instructionText;                     // texto corto de abajo
    public TMP_Text counterText;                         // "1 / 4"
    public Button btnGlossary;                           // aparece al terminar el último paso
    [HideInInspector] public float fontScale = 1f;

    [Header("Textos de abajo (uno por paso)")]
    [TextArea(1, 3)]
    public string[] stepTexts =
    {
        "Pulsa cada spawner y arrastra el átomo y la barra a su canasta.",
        "Acerca las barras al átomo central y pega los otros átomos en los extremos.",
        "Arrastra la molécula dentro de la Máquina Transmutadora.",
        "Arrastra el tarrito hacia el enviador para entregarlo."
    };
    [TextArea(1, 3)] public string finishText = "¡Tutorial completo! Consulta el Glosario para ver cómo armar cada receta.";

    [Header("Feedback (conectar sonido, luces)")]
    public UnityEvent onGrab;                            // agarrar una pieza
    public UnityEvent onSnap;                            // pieza colocada en su sitio
    public UnityEvent onWrong;                           // soltada sobre un lugar que no era
    public UnityEvent onStepDone;                        // paso completado
    public UnityEvent onMachineWorking;                  // la Transmutadora empieza su animación
    public UnityEvent onJarReady;                        // sale el tarrito
    public UnityEvent onDelivered;                       // confirmación de la entrega

    /// Se lanza al terminar el último paso (WristMenu la usa para onTutorialFinished)
    public event Action Finished;

    public int StepIndex { get; private set; }

    const int StepCount = 4;

    class Slot
    {
        public string accepts;
        public Vector2 pos, size;
        public bool active = true, filled;
        public Action<TutorialDraggable> onFill;
    }

    readonly List<Slot> slots = new List<Slot>();
    Coroutine routine;
    float zw, zh, u;                                     // tamaño de la zona y unidad base de tamaño
    float rO, rH, barL, barH;                            // medidas de átomo central, átomo lateral y barra
    int filled;
    Vector2 jarSpawnPos;

    static readonly Color AtomBlue = new Color(0.35f, 0.70f, 1.00f);
    static readonly Color OxygenRed = new Color(0.90f, 0.25f, 0.25f);
    static readonly Color HydrogenWhite = new Color(0.95f, 0.95f, 0.95f);
    static readonly Color DarkText = new Color(0.1f, 0.1f, 0.12f);
    static readonly Color BarGray = new Color(0.70f, 0.72f, 0.78f);
    static readonly Color BasketBrown = new Color(0.55f, 0.38f, 0.22f);
    static readonly Color MachineGray = new Color(0.30f, 0.34f, 0.42f);
    static readonly Color SlotDark = new Color(0.12f, 0.14f, 0.18f);
    static readonly Color EvaluatorGreen = new Color(0.25f, 0.38f, 0.32f);
    static readonly Color JarBody = new Color(0.55f, 0.95f, 0.95f, 0.9f);
    static readonly Color JarLid = new Color(0.25f, 0.30f, 0.35f);
    static readonly Color Ghost = new Color(1f, 1f, 1f, 0.22f);

    // ---------------------------------------------------------------
    // Control (lo llama WristMenu)
    // ---------------------------------------------------------------
    public void Begin()
    {
        Stop();
        StartStep(0);
    }

    public void Stop()
    {
        if (routine != null) { StopCoroutine(routine); routine = null; }
        Clear();
        if (btnGlossary != null) btnGlossary.gameObject.SetActive(false);
    }

    void Clear()
    {
        slots.Clear();
        if (actionZone == null) return;
        for (int i = actionZone.childCount - 1; i >= 0; i--)
        {
            var c = actionZone.GetChild(i).gameObject;
            c.SetActive(false);                          // desaparece ya; Destroy lo borra al final del frame
            Destroy(c);
        }
    }

    void StartStep(int i)
    {
        StepIndex = i;
        filled = 0;
        Clear();

        var r = actionZone.rect;
        zw = r.width; zh = r.height;
        u = Mathf.Min(zw, zh) * 0.2f;
        rO = 0.65f * u; rH = 0.5f * u; barL = 1.1f * u; barH = 0.32f * u;

        if (instructionText != null) instructionText.text = i < stepTexts.Length ? stepTexts[i] : "";
        if (counterText != null) counterText.text = $"{i + 1} / {StepCount}";

        switch (i)
        {
            case 0: BuildSpawners(); break;
            case 1: BuildBonding(); break;
            case 2: BuildTransmutator(); break;
            default: BuildEvaluator(); break;
        }
    }

    void CompleteStep()
    {
        onStepDone?.Invoke();
        Delay(0.7f, NextStep);
    }

    void NextStep()
    {
        if (StepIndex + 1 < StepCount) StartStep(StepIndex + 1);
        else Finish();
    }

    void Finish()
    {
        if (instructionText != null) instructionText.text = finishText;
        if (counterText != null) counterText.text = "Completado";
        if (btnGlossary != null) btnGlossary.gameObject.SetActive(true);
        Finished?.Invoke();
    }

    void Delay(float seconds, Action then)
    {
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(DelayRoutine(seconds, then));
    }

    IEnumerator DelayRoutine(float seconds, Action then)
    {
        yield return new WaitForSeconds(seconds);
        routine = null;
        then();
    }

    // ---------------------------------------------------------------
    // Paso 1: spawners y canastas
    // ---------------------------------------------------------------
    void BuildSpawners()
    {
        var atomBasket = Box("CanastaAtomos", new Vector2(2.6f * u, 1.9f * u), P(0.28f, 0.25f), BasketBrown,
                             label: "Canasta de átomos", fontSize: 13f, align: TextAlignmentOptions.Top);
        var barBasket = Box("CanastaBarras", new Vector2(2.6f * u, 1.9f * u), P(0.28f, -0.25f), BasketBrown,
                            label: "Canasta de barras", fontSize: 13f, align: TextAlignmentOptions.Top);

        AddSlot("atom", atomBasket.anchoredPosition, atomBasket.sizeDelta, OnBasketFilled);
        AddSlot("bar", barBasket.anchoredPosition, barBasket.sizeDelta, OnBasketFilled);

        MakeSpawner("Spawner\nátomos", P(-0.30f, 0.25f), P(-0.04f, 0.25f), "atom");
        MakeSpawner("Spawner\nbarras", P(-0.30f, -0.25f), P(-0.04f, -0.25f), "bar");
    }

    void MakeSpawner(string label, Vector2 buttonPos, Vector2 spawnPos, string id)
    {
        var btn = WristUIKit.MakeButton(actionZone, "Spawner_" + id, label, WristUIKit.ButtonColor, 15f, fontScale);
        WristUIKit.Center((RectTransform)btn.transform, new Vector2(2.2f * u, 1.1f * u), buttonPos);

        btn.onClick.AddListener(() =>
        {
            btn.interactable = false;                    // sale una sola pieza por spawner
            RectTransform piece = id == "atom"
                ? Box("Atomo", new Vector2(1.1f * u, 1.1f * u), spawnPos, AtomBlue, circle: true, raycast: true)
                : MakeBar(spawnPos);
            MakeDraggable(piece, id);
            StartCoroutine(ScaleRoutine(piece, 0f, 1f, 0.25f, true));
        });
    }

    void OnBasketFilled(TutorialDraggable d)
    {
        filled++;
        if (filled >= 2) CompleteStep();                 // átomo y barra ya están en su canasta
    }

    // ---------------------------------------------------------------
    // Paso 2: enlace con barras
    // ---------------------------------------------------------------
    void BuildBonding()
    {
        float oy = 0.12f * zh;                           // altura del átomo central
        float barX = rO + barL / 2f;                     // centro de cada barra respecto al átomo central
        float hX = rO + barL + rH;                       // centro de cada átomo lateral

        Box("AtomoCentral", new Vector2(2f * rO, 2f * rO), new Vector2(0f, oy), OxygenRed,
            circle: true, label: "O", fontSize: 22f);

        for (int side = -1; side <= 1; side += 2)
        {
            var barGhost = Box("PuertoBarra", new Vector2(barL, barH), new Vector2(side * barX, oy), Ghost);
            var atomGhost = Box("PuertoAtomo", new Vector2(2f * rH, 2f * rH), new Vector2(side * hX, oy), Ghost, circle: true);
            atomGhost.gameObject.SetActive(false);       // el lugar del átomo aparece cuando su barra ya está puesta

            var atomSlot = AddSlot("atomH", atomGhost.anchoredPosition, atomGhost.sizeDelta, OnBondPiece, false);
            AddSlot("bar", barGhost.anchoredPosition, barGhost.sizeDelta, d =>
            {
                atomGhost.gameObject.SetActive(true);
                atomSlot.active = true;
                OnBondPiece(d);
            });
        }

        // Piezas sueltas abajo: 2 barras y 2 hidrógenos
        float ly = -0.30f * zh;
        MakeDraggable(MakeBar(new Vector2(-0.33f * zw, ly)), "bar");
        MakeDraggable(MakeBar(new Vector2(-0.13f * zw, ly)), "bar");
        MakeDraggable(Box("Hidrogeno", new Vector2(2f * rH, 2f * rH), new Vector2(0.13f * zw, ly), HydrogenWhite,
                          circle: true, label: "H", fontSize: 18f, labelColor: DarkText, raycast: true), "atomH");
        MakeDraggable(Box("Hidrogeno", new Vector2(2f * rH, 2f * rH), new Vector2(0.33f * zw, ly), HydrogenWhite,
                          circle: true, label: "H", fontSize: 18f, labelColor: DarkText, raycast: true), "atomH");
    }

    void OnBondPiece(TutorialDraggable d)
    {
        filled++;
        if (filled >= 4) CompleteStep();                 // 2 barras + 2 átomos = molécula armada
    }

    // ---------------------------------------------------------------
    // Paso 3: Máquina Transmutadora
    // ---------------------------------------------------------------
    void BuildTransmutator()
    {
        var mPos = P(0.28f, 0.10f);
        var machine = Box("Transmutadora", new Vector2(2.7f * u, 2.9f * u), mPos, MachineGray,
                          label: "Transmutadora", fontSize: 12f, align: TextAlignmentOptions.Top);
        var entrance = Box("Entrada", new Vector2(1.6f * u, 1.6f * u), mPos + new Vector2(0f, -0.2f * u), SlotDark,
                           label: "Entrada", fontSize: 12f);
        jarSpawnPos = new Vector2(mPos.x, mPos.y - 1.45f * u - 0.7f * u);   // el tarrito sale por debajo

        AddSlot("molecule", entrance.anchoredPosition, entrance.sizeDelta,
                d => routine = StartCoroutine(MachineRoutine((RectTransform)d.transform, machine, entrance)));

        var molecule = BuildMolecule(P(-0.22f, 0f), 0.5f);
        MakeDraggable(molecule, "molecule");
    }

    IEnumerator MachineRoutine(RectTransform molecule, RectTransform machine, RectTransform entrance)
    {
        yield return ScaleRoutine(molecule, 0.5f, 0f, 0.3f, false);      // la molécula entra a la máquina
        onMachineWorking?.Invoke();

        // Animación corta: la máquina vibra y parpadea, y se va calmando
        Vector2 machineBase = machine.anchoredPosition, entranceBase = entrance.anchoredPosition;
        var img = machine.GetComponent<Image>();
        Color baseColor = img.color, flash = new Color(1f, 0.85f, 0.3f);
        const float duration = 1.2f;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float calm = 1f - t / duration;
            var shake = new Vector2(Mathf.Sin(t * 60f), Mathf.Cos(t * 47f)) * (0.06f * u * calm);
            machine.anchoredPosition = machineBase + shake;
            entrance.anchoredPosition = entranceBase + shake;
            img.color = Color.Lerp(baseColor, flash, 0.5f * (0.5f + 0.5f * Mathf.Sin(t * 14f)) * calm);
            yield return null;
        }
        machine.anchoredPosition = machineBase;
        entrance.anchoredPosition = entranceBase;
        img.color = baseColor;

        // Suelta el tarrito (la muestra)
        var jar = BuildJar(jarSpawnPos);
        onJarReady?.Invoke();
        yield return ScaleRoutine(jar, 0f, 1f, 0.3f, true);

        onStepDone?.Invoke();
        yield return new WaitForSeconds(0.8f);
        routine = null;
        NextStep();
    }

    // ---------------------------------------------------------------
    // Paso 4: Máquina Evaluadora (enviador)
    // ---------------------------------------------------------------
    void BuildEvaluator()
    {
        var ePos = P(0.28f, 0.08f);
        Box("Evaluadora", new Vector2(2.7f * u, 2.6f * u), ePos, EvaluatorGreen,
            label: "Evaluadora", fontSize: 12f, align: TextAlignmentOptions.Top);

        var slotPos = ePos + new Vector2(0f, 0.5f * u);
        var slot = Box("Ranura", new Vector2(1.6f * u, 0.45f * u), slotPos, SlotDark, label: "Ranura", fontSize: 11f);
        AddSlot("jar", slot.anchoredPosition, slot.sizeDelta,
                d => routine = StartCoroutine(DeliverRoutine((RectTransform)d.transform, ePos)));

        var jar = BuildJar(P(-0.22f, 0f));
        MakeDraggable(jar, "jar");
        StartCoroutine(ScaleRoutine(jar, 0f, 1f, 0.25f, true));
    }

    IEnumerator DeliverRoutine(RectTransform jar, Vector2 evaluatorPos)
    {
        yield return ScaleRoutine(jar, 1f, 0f, 0.3f, false);             // el tarrito se consume
        onDelivered?.Invoke();

        var ok = Box("Confirmacion", new Vector2(1.1f * u, 1.1f * u), evaluatorPos + new Vector2(0f, -0.5f * u),
                     new Color(0.2f, 0.85f, 0.35f), circle: true, label: "OK", fontSize: 18f, labelColor: DarkText);
        yield return ScaleRoutine(ok, 0f, 1f, 0.3f, true);

        yield return new WaitForSeconds(0.7f);
        routine = null;
        Finish();
    }

    // ---------------------------------------------------------------
    // Arrastrar y soltar
    // ---------------------------------------------------------------
    TutorialDraggable MakeDraggable(RectTransform rt, string id)
    {
        var d = rt.gameObject.AddComponent<TutorialDraggable>();
        d.id = id;
        d.DropHandler = HandleDrop;
        d.Grabbed += _ => onGrab?.Invoke();
        return d;
    }

    bool HandleDrop(TutorialDraggable d)
    {
        var slot = FindSlot(d, out bool overlapsWrong);
        if (slot == null)
        {
            if (overlapsWrong) onWrong?.Invoke();
            return false;
        }
        slot.filled = true;
        d.SnapTo(slot.pos);
        onSnap?.Invoke();
        slot.onFill?.Invoke(d);
        return true;
    }

    // Lugar libre y activo que acepta esta pieza y está bajo ella (el más cercano)
    Slot FindSlot(TutorialDraggable d, out bool overlapsWrong)
    {
        overlapsWrong = false;
        Slot best = null;
        float bestDist = float.MaxValue;
        Vector2 p = ((RectTransform)d.transform).anchoredPosition;
        float margin = 0.5f * u;                         // tolerancia: no hay que acertar al píxel

        foreach (var s in slots)
        {
            if (!s.active || s.filled) continue;
            Vector2 diff = p - s.pos;
            if (Mathf.Abs(diff.x) > s.size.x * 0.5f + margin || Mathf.Abs(diff.y) > s.size.y * 0.5f + margin) continue;

            if (s.accepts != d.id) { overlapsWrong = true; continue; }
            if (diff.sqrMagnitude < bestDist) { bestDist = diff.sqrMagnitude; best = s; }
        }
        return best;
    }

    Slot AddSlot(string accepts, Vector2 pos, Vector2 size, Action<TutorialDraggable> onFill = null, bool active = true)
    {
        var s = new Slot { accepts = accepts, pos = pos, size = size, onFill = onFill, active = active };
        slots.Add(s);
        return s;
    }

    // ---------------------------------------------------------------
    // Piezas (formas simples de colores, sin imágenes)
    // ---------------------------------------------------------------
    // Posición a partir de fracciones del ancho y alto de la zona (0 = centro)
    Vector2 P(float fx, float fy) { return new Vector2(fx * zw, fy * zh); }

    RectTransform Box(string name, Vector2 size, Vector2 pos, Color color, bool circle = false,
                      string label = null, float fontSize = 16f,
                      TextAlignmentOptions align = TextAlignmentOptions.Center,
                      Color? labelColor = null, bool raycast = false, Transform parent = null)
    {
        var rt = WristUIKit.NewRect(name, parent != null ? parent : actionZone);
        WristUIKit.Center(rt, size, pos);

        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = raycast;
        if (circle) img.sprite = CircleSprite();

        if (!string.IsNullOrEmpty(label))
        {
            var t = WristUIKit.MakeText(rt, "Texto", label, fontSize, align, fontScale);
            WristUIKit.Anchor(t.rectTransform, 0, 0, 1, 1);
            t.rectTransform.offsetMin = new Vector2(4f, 4f);
            t.rectTransform.offsetMax = new Vector2(-4f, -4f);
            t.color = labelColor ?? Color.white;
        }
        return rt;
    }

    // Barra: el contenedor transparente es más grande que la barra para que sea fácil de agarrar
    RectTransform MakeBar(Vector2 pos)
    {
        var root = WristUIKit.NewRect("Barra", actionZone);
        WristUIKit.Center(root, new Vector2(barL + 0.2f * u, 0.8f * u), pos);
        var hit = root.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        hit.raycastTarget = true;
        Box("Visual", new Vector2(barL, barH), Vector2.zero, BarGray, parent: root);
        return root;
    }

    // Molécula armada (O con dos H); se agarra por todo el conjunto
    RectTransform BuildMolecule(Vector2 pos, float scale)
    {
        float barX = rO + barL / 2f, hX = rO + barL + rH;
        var root = WristUIKit.NewRect("Molecula", actionZone);
        WristUIKit.Center(root, new Vector2(2f * (hX + rH), 2f * rO), pos);
        var hit = root.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        hit.raycastTarget = true;

        Box("BarraIzq", new Vector2(barL, barH), new Vector2(-barX, 0f), BarGray, parent: root);
        Box("BarraDer", new Vector2(barL, barH), new Vector2(barX, 0f), BarGray, parent: root);
        Box("O", new Vector2(2f * rO, 2f * rO), Vector2.zero, OxygenRed, circle: true, label: "O", fontSize: 22f, parent: root);
        Box("HIzq", new Vector2(2f * rH, 2f * rH), new Vector2(-hX, 0f), HydrogenWhite,
            circle: true, label: "H", fontSize: 18f, labelColor: DarkText, parent: root);
        Box("HDer", new Vector2(2f * rH, 2f * rH), new Vector2(hX, 0f), HydrogenWhite,
            circle: true, label: "H", fontSize: 18f, labelColor: DarkText, parent: root);

        root.localScale = Vector3.one * scale;
        return root;
    }

    // El tarrito (la muestra): cuerpo y tapa
    RectTransform BuildJar(Vector2 pos)
    {
        var root = WristUIKit.NewRect("Tarrito", actionZone);
        WristUIKit.Center(root, new Vector2(0.9f * u, 1.2f * u), pos);
        var hit = root.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        hit.raycastTarget = true;

        Box("Cuerpo", new Vector2(0.9f * u, 0.95f * u), new Vector2(0f, -0.12f * u), JarBody, parent: root);
        Box("Tapa", new Vector2(0.6f * u, 0.25f * u), new Vector2(0f, 0.47f * u), JarLid, parent: root);
        return root;
    }

    // ---------------------------------------------------------------
    // Animación
    // ---------------------------------------------------------------
    IEnumerator ScaleRoutine(RectTransform r, float from, float to, float duration, bool pop)
    {
        if (r != null) r.localScale = Vector3.one * from;
        float t = 0f;
        while (t < duration)
        {
            if (r == null) yield break;
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            float e = pop ? EaseOutBack(k) : k;
            r.localScale = Vector3.one * Mathf.LerpUnclamped(from, to, e);
            yield return null;
        }
        if (r != null) r.localScale = Vector3.one * to;
    }

    static float EaseOutBack(float k)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(k - 1f, 3f) + c1 * Mathf.Pow(k - 1f, 2f);
    }

    // Círculo generado por código (así no hace falta ninguna imagen)
    static Sprite circleSprite;
    static Sprite CircleSprite()
    {
        if (circleSprite != null) return circleSprite;
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        var px = new Color32[n * n];
        float c = (n - 1) / 2f, radius = n / 2f - 1f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                byte a = (byte)(Mathf.Clamp01(radius - d + 0.5f) * 255f);
                px[y * n + x] = new Color32(255, 255, 255, a);
            }
        tex.SetPixels32(px);
        tex.Apply();
        circleSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        return circleSprite;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// Canvas de la muñeca: menú principal (Tutorial básico / Glosario), lista de las recetas del catálogo,
/// ventana emergente con la información de cada receta y tutorial por pasos.
///
/// USO: pon este script en el objeto Canvas de la muñeca, asigna el RecipeCatalog, y con clic derecho en
/// el componente elige "Construir UI". Crea toda la interfaz como hijos del Canvas (hijo "WristUI").
/// Después puedes mover, recolorear o cambiar lo que quieras: el script solo usa las referencias.
/// Requisitos de XR UI: un EventSystem con XR UI Input Module y, en la mano libre, un Poke Interactor con la
/// interacción con UI activada.
[RequireComponent(typeof(Canvas))]
public class WristMenu : MonoBehaviour
{
    [Serializable]
    public class TutorialStep
    {
        public string title;
        [TextArea(2, 5)] public string body;
        public Sprite image;                     // opcional: imagen del mando, por ejemplo
    }

    [Header("Datos")]
    public RecipeCatalog catalog;
    [Tooltip("Recetas de niveles mayores a este aparecen como ??? y no se pueden abrir (el GameFlowManager lo sube con SetUnlockedLevel)")]
    public int unlockedLevel = 99;
    [Tooltip("Pasos del tutorial. Si se deja vacío se usa un tutorial básico por defecto")]
    public TutorialStep[] tutorialSteps;

    [Header("Construcción")]
    [Tooltip("Desmárcalo si tu Canvas ya está configurado (tamaño, escala, raycaster, gaze). Entonces 'Construir UI' solo crea el contenido")]
    public bool configureCanvas = true;
    [HideInInspector] public float fontScale = 1f;   // la fija 'Construir UI' según el tamaño del Canvas

    [Header("Eventos")]
    public UnityEvent onTutorialFinished;        // se llama al terminar el último paso (ej. para el ProgressManager)

    [Header("Referencias (las crea 'Construir UI')")]
    public GameObject mainPage;
    public GameObject glossaryPage;
    public GameObject tutorialPage;
    public GameObject detailPopup;
    public Button btnTutorial;
    public Button btnGlossary;
    public Button btnGlossaryBack;
    public RectTransform recipeListRoot;
    public Button btnDetailClose;
    public TMP_Text detailTitle;
    public TMP_Text detailBody;
    public TMP_Text tutorialTitle;
    public TMP_Text tutorialBody;
    public TMP_Text tutorialCounter;
    public Image tutorialImage;
    public Button btnTutorialPrev;
    public Button btnTutorialNext;
    public Button btnTutorialBack;

    int tutorialIndex;

    // ---------------------------------------------------------------
    // Funcionamiento
    // ---------------------------------------------------------------
    void Awake()
    {
        if (mainPage == null || glossaryPage == null || tutorialPage == null || detailPopup == null)
        {
            Debug.LogWarning("[WristMenu] Falta la interfaz. Clic derecho en el componente > Construir UI.", this);
            return;
        }

        Wire(btnTutorial, OpenTutorial);
        Wire(btnGlossary, OpenGlossary);
        Wire(btnGlossaryBack, OpenMain);
        Wire(btnDetailClose, CloseDetail);
        Wire(btnTutorialBack, OpenMain);
        Wire(btnTutorialPrev, () => ShowTutorialStep(tutorialIndex - 1));
        Wire(btnTutorialNext, NextTutorialStep);

        RefreshRecipeList();
        OpenMain();
    }

    static void Wire(Button b, UnityAction action) { if (b != null) b.onClick.AddListener(action); }

    public void OpenMain() { ShowPage(mainPage); }
    public void OpenGlossary() { ShowPage(glossaryPage); }
    public void OpenTutorial() { ShowPage(tutorialPage); ShowTutorialStep(0); }
    public void CloseDetail() { if (detailPopup != null) detailPopup.SetActive(false); }

    void ShowPage(GameObject page)
    {
        mainPage.SetActive(page == mainPage);
        glossaryPage.SetActive(page == glossaryPage);
        tutorialPage.SetActive(page == tutorialPage);
        detailPopup.SetActive(false);
    }

    /// El GameFlowManager lo llama al avanzar de nivel
    public void SetUnlockedLevel(int level)
    {
        unlockedLevel = level;
        RefreshRecipeList();
    }

    // ---------------------------------------------------------------
    // Glosario
    // ---------------------------------------------------------------
    // La lista se arma desde el catálogo: si cambias o añades recetas, se actualiza sola
    public void RefreshRecipeList()
    {
        if (recipeListRoot == null || catalog == null) return;

        for (int i = recipeListRoot.childCount - 1; i >= 0; i--)
            Destroy(recipeListRoot.GetChild(i).gameObject);

        foreach (var r in catalog.recipes)
        {
            if (r == null) continue;
            bool locked = r.level > unlockedLevel;

            string label = locked ? "???" : $"{r.displayName}\n<size=70%>{Sub(r.formula)}</size>";
            var btn = MakeButton(recipeListRoot, "Receta_" + r.formula, label, RecipeButtonColor, 20);
            btn.interactable = !locked;

            var recipe = r;
            btn.onClick.AddListener(() => ShowDetail(recipe));
        }
    }

    void ShowDetail(RecipeData r)
    {
        detailTitle.text = $"{r.displayName}\n<size=75%>{Sub(r.formula)}</size>";
        detailBody.text = BuildDetail(r);
        detailPopup.SetActive(true);
    }

    string BuildDetail(RecipeData r)
    {
        string state = r.category == CompoundCategory.Gas ? "Gas"
                     : r.category == CompoundCategory.Liquid ? "Líquido" : "Sólido";

        string atoms = string.Join(", ", r.AtomCounts.OrderBy(k => k.Key).Select(k => $"{k.Key} ×{k.Value}"));
        int bars = r.Graph.edges.Count;

        var sb = new StringBuilder();
        sb.AppendLine($"<b>Nivel:</b> {r.level}     <b>Estado:</b> {state}");
        sb.AppendLine();
        sb.AppendLine("<b>Piezas</b>");
        sb.AppendLine($"Átomos: {atoms}");
        sb.AppendLine($"Barras: {bars}");
        sb.AppendLine();
        sb.AppendLine("<b>Máquina</b>");
        sb.AppendLine($"Presión: {r.requiredPressure.Label()}");
        sb.AppendLine($"Temperatura: {r.requiredTemperature.Label()}");
        if (!string.IsNullOrEmpty(r.educationalFact))
        {
            sb.AppendLine();
            sb.Append($"<i>{r.educationalFact}</i>");
        }
        return sb.ToString();
    }

    // El texto por defecto de TMP no trae los subíndices Unicode (₂ ₃ ₄): se cambian por la etiqueta <sub>
    static string Sub(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            if (c >= '\u2080' && c <= '\u2089') sb.Append("<sub>").Append((char)('0' + (c - '\u2080'))).Append("</sub>");
            else sb.Append(c);
        }
        return sb.ToString();
    }

    // ---------------------------------------------------------------
    // Tutorial
    // ---------------------------------------------------------------
    TutorialStep[] Steps => tutorialSteps != null && tutorialSteps.Length > 0 ? tutorialSteps : DefaultSteps();

    void ShowTutorialStep(int index)
    {
        var steps = Steps;
        tutorialIndex = Mathf.Clamp(index, 0, steps.Length - 1);
        var step = steps[tutorialIndex];

        if (tutorialTitle != null) tutorialTitle.text = step.title;
        if (tutorialBody != null) tutorialBody.text = step.body;
        if (tutorialCounter != null) tutorialCounter.text = $"{tutorialIndex + 1} / {steps.Length}";
        if (tutorialImage != null)
        {
            tutorialImage.sprite = step.image;
            tutorialImage.gameObject.SetActive(step.image != null);
        }
        if (btnTutorialPrev != null) btnTutorialPrev.interactable = tutorialIndex > 0;

        bool last = tutorialIndex == steps.Length - 1;
        if (btnTutorialNext != null)
        {
            var label = btnTutorialNext.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = last ? "Fin" : ">";
        }
    }

    void NextTutorialStep()
    {
        if (tutorialIndex >= Steps.Length - 1)
        {
            onTutorialFinished.Invoke();
            OpenMain();
            return;
        }
        ShowTutorialStep(tutorialIndex + 1);
    }

    static TutorialStep[] DefaultSteps() => new[]
    {
        new TutorialStep { title = "Moverte", body = "Camina en tu espacio y usa el joystick para desplazarte. El otro joystick gira la vista por saltos." },
        new TutorialStep { title = "Agarrar", body = "Acerca la mano a una pieza y mantén el botón de agarre. Suéltalo para dejarla." },
        new TutorialStep { title = "Bodega", body = "Pon una cesta bajo la boquilla de un spawner y pulsa sus botones. Saca la cantidad exacta de átomos y barras que pide la receta." },
        new TutorialStep { title = "Armar", body = "Acerca las barras a los átomos para encajarlas. Cada átomo acepta tantas barras como su valencia." },
        new TutorialStep { title = "Transmutadora", body = "Mete la molécula en el hueco y pon las palancas de presión y temperatura que pide la receta. Coloca un vaso (líquidos) o una botella (gases) y pulsa Iniciar." },
        new TutorialStep { title = "Entregar", body = "Acepta el pedido en la pantalla grande antes de empezar. Lleva la muestra a la máquina de entrega: cuanto más rápido, más puntos. La tapa en la botella da puntos extra." },
    };

    // ---------------------------------------------------------------
    // Construcción de la interfaz (clic derecho en el componente > Construir UI)
    // ---------------------------------------------------------------
    static readonly Color PanelColor = new Color(0.08f, 0.10f, 0.14f, 0.94f);
    static readonly Color ButtonColor = new Color(0.20f, 0.45f, 0.75f, 1f);
    static readonly Color RecipeButtonColor = new Color(0.18f, 0.40f, 0.62f, 1f);
    static readonly Color BackButtonColor = new Color(0.32f, 0.35f, 0.42f, 1f);

    [ContextMenu("Construir UI")]
    public void BuildUI()
    {
        var rect = (RectTransform)transform;
        if (configureCanvas)
        {
            GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            rect.sizeDelta = new Vector2(400f, 520f);
            if (transform.localScale == Vector3.one) transform.localScale = Vector3.one * 0.0005f;   // unos 20 x 26 cm
            EnsureRaycaster();
        }

        // Todo se dimensiona según el tamaño real del Canvas (el diseño base es de 400 x 520)
        float w = Mathf.Max(1f, rect.rect.width);
        float h = Mathf.Max(1f, rect.rect.height);
        fontScale = Mathf.Min(w / 400f, h / 520f);

        var old = transform.Find("WristUI");
        if (old != null) DestroyImmediate(old.gameObject);

        var root = NewRect("WristUI", transform);
        Anchor(root, 0, 0, 1, 1);

        // ----- Menú principal -----
        var main = MakePage("PaginaMenu", root);
        Label(main.transform, "Titulo", "MENÚ", 36, TextAlignmentOptions.Center, 0, 0.80f, 1, 0.98f);
        btnTutorial = Place(MakeButton(main.transform, "BtnTutorial", "Tutorial básico", ButtonColor, 28), 0.1f, 0.50f, 0.9f, 0.72f);
        btnGlossary = Place(MakeButton(main.transform, "BtnGlosario", "Glosario", ButtonColor, 28), 0.1f, 0.22f, 0.9f, 0.44f);

        // ----- Glosario: lista de recetas -----
        var gloss = MakePage("PaginaGlosario", root);
        Label(gloss.transform, "Titulo", "GLOSARIO", 30, TextAlignmentOptions.Center, 0, 0.90f, 1, 1);
        recipeListRoot = NewRect("ListaRecetas", gloss.transform);
        Anchor(recipeListRoot, 0.04f, 0.14f, 0.96f, 0.88f);
        var grid = recipeListRoot.gameObject.AddComponent<GridLayoutGroup>();
        float gap = 8f * fontScale;
        float listW = w * 0.92f, listH = h * 0.74f;          // el área de la lista ocupa el 92 % x 74 % del Canvas
        grid.cellSize = new Vector2((listW - gap) / 2f, (listH - gap * 3f) / 4f);   // 2 columnas x 4 filas = 8 recetas
        grid.spacing = new Vector2(gap, gap);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;
        grid.childAlignment = TextAnchor.UpperCenter;
        btnGlossaryBack = Place(MakeButton(gloss.transform, "BtnVolver", "Volver", BackButtonColor, 24), 0.3f, 0.02f, 0.7f, 0.11f);

        // ----- Tutorial -----
        var tut = MakePage("PaginaTutorial", root);
        tutorialTitle = Label(tut.transform, "Titulo", "", 30, TextAlignmentOptions.Center, 0, 0.88f, 1, 0.98f);
        var imgGo = new GameObject("Imagen", typeof(RectTransform), typeof(Image));
        imgGo.transform.SetParent(tut.transform, false);
        Anchor((RectTransform)imgGo.transform, 0.3f, 0.64f, 0.7f, 0.86f);
        tutorialImage = imgGo.GetComponent<Image>();
        tutorialImage.preserveAspect = true;
        tutorialImage.raycastTarget = false;
        imgGo.SetActive(false);
        tutorialBody = Label(tut.transform, "Texto", "", 22, TextAlignmentOptions.Top, 0.07f, 0.20f, 0.93f, 0.62f);
        tutorialBody.enableAutoSizing = true; tutorialBody.fontSizeMin = 14f * fontScale; tutorialBody.fontSizeMax = 22f * fontScale;
        tutorialCounter = Label(tut.transform, "Contador", "", 18, TextAlignmentOptions.Center, 0.35f, 0.14f, 0.65f, 0.20f);
        btnTutorialPrev = Place(MakeButton(tut.transform, "BtnAnterior", "<", ButtonColor, 28), 0.05f, 0.02f, 0.30f, 0.12f);
        btnTutorialBack = Place(MakeButton(tut.transform, "BtnMenu", "Menú", BackButtonColor, 22), 0.35f, 0.02f, 0.65f, 0.12f);
        btnTutorialNext = Place(MakeButton(tut.transform, "BtnSiguiente", ">", ButtonColor, 28), 0.70f, 0.02f, 0.95f, 0.12f);

        // ----- Ventana emergente de la receta (encima de todo) -----
        var popup = NewRect("VentanaReceta", root);
        Anchor(popup, 0, 0, 1, 1);
        var dim = popup.gameObject.AddComponent<Image>();
        dim.color = new Color(0, 0, 0, 0.65f);               // oscurece y bloquea los clics de lo que hay detrás

        var panel = NewRect("Panel", popup);
        Anchor(panel, 0.06f, 0.08f, 0.94f, 0.92f);
        panel.gameObject.AddComponent<Image>().color = new Color(0.12f, 0.15f, 0.21f, 1f);

        detailTitle = Label(panel, "Titulo", "", 28, TextAlignmentOptions.Center, 0, 0.84f, 1, 0.99f);
        detailBody = Label(panel, "Texto", "", 22, TextAlignmentOptions.TopLeft, 0.06f, 0.14f, 0.94f, 0.84f);
        detailBody.enableAutoSizing = true; detailBody.fontSizeMin = 12f * fontScale; detailBody.fontSizeMax = 22f * fontScale;
        btnDetailClose = Place(MakeButton(panel, "BtnCerrar", "Cerrar", BackButtonColor, 24), 0.3f, 0.02f, 0.7f, 0.12f);

        mainPage = main; glossaryPage = gloss; tutorialPage = tut; detailPopup = popup.gameObject;
        gloss.SetActive(false); tut.SetActive(false); popup.gameObject.SetActive(false);

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
        if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
        Debug.Log("[WristMenu] Interfaz construida. Las páginas Glosario y Tutorial empiezan ocultas; el script las muestra al dar Play.", this);
    }

    // Para que la UI responda a la mano en XR se necesita el raycaster de XRI (se busca por nombre para no depender de la versión)
    void EnsureRaycaster()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType("UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster");
            if (t == null) continue;
            if (GetComponent(t) == null) gameObject.AddComponent(t);
            return;
        }
        if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
        Debug.LogWarning("[WristMenu] No encontré TrackedDeviceGraphicRaycaster (XR Interaction Toolkit). Añádelo a mano al Canvas para que responda a la mano.", this);
    }

    // ----- Ayudas de construcción -----
    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static void Anchor(RectTransform rt, float x0, float y0, float x1, float y1)
    {
        rt.anchorMin = new Vector2(x0, y0);
        rt.anchorMax = new Vector2(x1, y1);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static Button Place(Button b, float x0, float y0, float x1, float y1)
    {
        Anchor((RectTransform)b.transform, x0, y0, x1, y1);
        return b;
    }

    static GameObject MakePage(string name, Transform parent)
    {
        var rt = NewRect(name, parent);
        Anchor(rt, 0, 0, 1, 1);
        rt.gameObject.AddComponent<Image>().color = PanelColor;
        return rt.gameObject;
    }

    TMP_Text Label(Transform parent, string name, string text, float size, TextAlignmentOptions align,
                          float x0, float y0, float x1, float y1)
    {
        var t = MakeText(parent, name, text, size, align);
        Anchor(t.rectTransform, x0, y0, x1, y1);
        return t;
    }

    TMP_Text MakeText(Transform parent, string name, string text, float size, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size * fontScale;
        t.alignment = align;
        t.color = Color.white;
        t.raycastTarget = false;
        return t;
    }

    Button MakeButton(Transform parent, string name, string label, Color color, float fontSize)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        var img = go.GetComponent<Image>();
        img.color = color;
        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;

        // Con gaze el resaltado indica qué estás mirando: normal un poco apagado, resaltado a pleno color
        var cb = btn.colors;
        cb.normalColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        cb.highlightedColor = Color.white;
        cb.pressedColor = new Color(0.6f, 0.6f, 0.6f, 1f);
        cb.selectedColor = cb.normalColor;                    // tras pulsar no se queda resaltado
        cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.7f);
        cb.colorMultiplier = 1f;
        btn.colors = cb;

        var t = MakeText(go.transform, "Texto", label, fontSize, TextAlignmentOptions.Center);
        Anchor(t.rectTransform, 0, 0, 1, 1);
        t.rectTransform.offsetMin = new Vector2(6, 4);
        t.rectTransform.offsetMax = new Vector2(-6, -4);
        return btn;
    }
}

using System;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// Canvas de la muñeca. Tres páginas:
///   - Menú inicial: dos botones, Tutorial y Glosario.
///   - Tutorial: minijuego 2D por pasos (lo controla WristTutorial).
///   - Glosario: todas las recetas del catálogo en una lista que se desliza como en un celular, con su
///     formación (átomos y barras), presión y temperatura. Se arma sola desde el RecipeCatalog.
///
/// USO: pon este script en el objeto Canvas de la muñeca, asigna el RecipeCatalog, y con clic derecho en
/// el componente elige "Construir UI". Crea toda la interfaz como hijos del Canvas (hijo "WristUI").
/// Después puedes mover, recolorear o cambiar lo que quieras: el script solo usa las referencias.
/// Requisitos de XR UI: un EventSystem con XR UI Input Module y, en la mano libre, un Poke Interactor con la
/// interacción con UI activada.
[RequireComponent(typeof(Canvas))]
public class WristMenu : MonoBehaviour
{
    [Header("Datos")]
    public RecipeCatalog catalog;                // de aquí sale la lista del glosario
    [Tooltip("Recetas de niveles mayores a este aparecen como ??? en el glosario. 99 = se ven todas (el GameFlowManager lo sube con SetUnlockedLevel)")]
    public int unlockedLevel = 99;

    [Header("Construcción")]
    [Tooltip("Desmárcalo si tu Canvas ya está configurado (tamaño, escala, raycaster, gaze). Entonces 'Construir UI' solo crea el contenido")]
    public bool configureCanvas = true;
    [HideInInspector] public float fontScale = 1f;   // la fija 'Construir UI' según el tamaño del Canvas

    [Header("Eventos")]
    public UnityEvent onMenuOpened;
    public UnityEvent onTutorialOpened;
    public UnityEvent onGlossaryOpened;
    public UnityEvent onTutorialFinished;        // se llama al completar el último paso del tutorial
    public UnityEvent onMenuClosed;              // botón X: se cierra la interfaz

    [Header("Cerrar")]
    [Tooltip("Lo que se oculta con el botón X. Vacío = este Canvas. Se vuelve a abrir girando la muñeca hacia la cara")]
    public GameObject closeTarget;
    public Button btnClose;                      // si está vacío se crea solo (esquina superior derecha)

    [Header("Referencias (las crea 'Construir UI')")]
    public GameObject mainPage;
    public GameObject tutorialPage;
    public GameObject glossaryPage;
    public WristTutorial tutorial;
    public Button btnTutorial;
    public Button btnGlossary;
    public Button btnTutorialBack;
    public Button btnGlossaryBack;
    public RectTransform recipeListRoot;         // contenido de la lista deslizable del glosario
    public ScrollRect glossaryScroll;

    // ---------------------------------------------------------------
    // Funcionamiento
    // ---------------------------------------------------------------
    void Awake()
    {
        if (mainPage == null || tutorialPage == null || glossaryPage == null)
        {
            Debug.LogWarning("[WristMenu] Falta la interfaz. Clic derecho en el componente > Construir UI.", this);
            return;
        }

        EnsureCloseButton();
        Wire(btnClose, CloseMenu);
        Wire(btnTutorial, OpenTutorial);
        Wire(btnGlossary, OpenGlossary);
        Wire(btnTutorialBack, OpenMain);
        Wire(btnGlossaryBack, OpenMain);

        if (tutorial != null)
        {
            tutorial.Finished += () => onTutorialFinished.Invoke();
            Wire(tutorial.btnGlossary, OpenGlossary);    // el botón Glosario que aparece al terminar el tutorial
        }

        OpenMain();
    }

    static void Wire(Button b, UnityAction action) { if (b != null) b.onClick.AddListener(action); }

    /// Botón X: oculta la interfaz de la muñeca (vuelve a aparecer con el gesto de mirar la muñeca)
    public void CloseMenu()
    {
        if (tutorial != null) tutorial.Stop();
        onMenuClosed.Invoke();
        (closeTarget != null ? closeTarget : gameObject).SetActive(false);
    }

    // Botón X arriba a la derecha, por encima de todas las páginas (se crea si el Canvas ya estaba construido sin él)
    void EnsureCloseButton()
    {
        if (btnClose != null) return;
        var root = transform.Find("WristUI");
        if (root == null) return;
        var existing = root.Find("BtnCerrar");
        if (existing != null) { btnClose = existing.GetComponent<Button>(); return; }

        var rect = (RectTransform)transform;
        float fs = fontScale > 0f ? fontScale : Mathf.Min(rect.rect.width / 400f, rect.rect.height / 520f);
        btnClose = WristUIKit.MakeButton(root, "BtnCerrar", "X", new Color(0.75f, 0.22f, 0.22f, 1f), 26, fs);
        WristUIKit.Place(btnClose, 0.86f, 0.925f, 0.985f, 0.995f);
        btnClose.transform.SetAsLastSibling();           // encima de las páginas
    }

    public void OpenMain()
    {
        if (tutorial != null) tutorial.Stop();
        ShowPage(mainPage);
        onMenuOpened.Invoke();
    }

    public void OpenTutorial()
    {
        ShowPage(tutorialPage);
        if (tutorial != null) tutorial.Begin();          // siempre empieza desde el paso 1
        onTutorialOpened.Invoke();
    }

    public void OpenGlossary()
    {
        if (tutorial != null) tutorial.Stop();
        ShowPage(glossaryPage);
        RefreshRecipeList();                             // se arma desde el catálogo y empieza arriba
        onGlossaryOpened.Invoke();
    }

    void ShowPage(GameObject page)
    {
        mainPage.SetActive(page == mainPage);
        tutorialPage.SetActive(page == tutorialPage);
        glossaryPage.SetActive(page == glossaryPage);
    }

    /// El GameFlowManager lo llama al avanzar de nivel
    public void SetUnlockedLevel(int level)
    {
        unlockedLevel = level;
    }

    // ---------------------------------------------------------------
    // Glosario: todas las recetas en una lista que se desliza como en un celular
    // ---------------------------------------------------------------
    static readonly Color CardColor = new Color(0.14f, 0.18f, 0.25f, 1f);
    static readonly Color LockedCardColor = new Color(0.14f, 0.15f, 0.18f, 1f);

    // La lista se arma desde el catálogo: si cambias o añades recetas, se actualiza sola
    public void RefreshRecipeList()
    {
        if (recipeListRoot == null || catalog == null) return;

        for (int i = recipeListRoot.childCount - 1; i >= 0; i--)
        {
            var old = recipeListRoot.GetChild(i).gameObject;
            old.SetActive(false);                        // sale del diseño ya; Destroy lo borra al final del frame
            Destroy(old);
        }

        foreach (var r in catalog.recipes.Where(x => x != null).OrderBy(x => x.level))
            MakeRecipeCard(r, r.level > unlockedLevel);

        LayoutRebuilder.ForceRebuildLayoutImmediate(recipeListRoot);
        if (glossaryScroll != null)
        {
            glossaryScroll.StopMovement();
            glossaryScroll.verticalNormalizedPosition = 1f;   // arriba del todo
        }
    }

    void MakeRecipeCard(RecipeData r, bool locked)
    {
        var card = WristUIKit.NewRect("Receta_" + r.formula, recipeListRoot);
        // El fondo recibe el puntero: arrastrar sobre una tarjeta también desliza la lista
        card.gameObject.AddComponent<Image>().color = locked ? LockedCardColor : CardColor;

        var layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
        int pad = Mathf.RoundToInt(12f * fontScale);
        layout.padding = new RectOffset(pad, pad, pad, pad);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        string body = locked
            ? "<size=125%><b>???</b></size>\n<color=#8A929E>Se desbloquea al avanzar de nivel</color>"
            : BuildCardText(r);
        WristUIKit.MakeText(card, "Texto", body, 20f, TextAlignmentOptions.TopLeft, fontScale);
    }

    string BuildCardText(RecipeData r)
    {
        string state, color;
        switch (r.category)
        {
            case CompoundCategory.Gas: state = "Gas"; color = "#8FD3FF"; break;
            case CompoundCategory.Liquid: state = "Líquido"; color = "#7FE0A8"; break;
            default: state = "Sólido"; color = "#FFD27F"; break;
        }

        string atoms = string.Join(", ", r.AtomCounts.OrderBy(k => k.Key).Select(k => $"{k.Key} ×{k.Value}"));
        int bars = r.Graph.edges.Count;

        var sb = new StringBuilder();
        sb.AppendLine($"<size=125%><b>{(r.displayName ?? "").Trim()}</b></size>   {Sub(r.formula)}");
        sb.AppendLine($"<color={color}>{state}</color>   Nivel {r.level}");
        sb.AppendLine();
        sb.AppendLine("<b>Formación</b>");
        sb.AppendLine($"Átomos: {atoms}");
        sb.AppendLine($"Barras: {bars}");
        sb.AppendLine();
        sb.AppendLine("<b>Máquina</b>");
        sb.AppendLine($"Presión: {r.requiredPressure.Label()}");
        sb.Append($"Temperatura: {r.requiredTemperature.Label()}");
        if (!string.IsNullOrEmpty(r.educationalFact))
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.Append($"<i>{r.educationalFact}</i>");
        }
        return sb.ToString();
    }

    // La fórmula se guarda como "H2O": los números pasan a subíndice con la etiqueta <sub> de TMP
    static string Sub(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            if (c >= '₀' && c <= '₉') sb.Append("<sub>").Append((char)('0' + (c - '₀'))).Append("</sub>");
            else if (c >= '0' && c <= '9') sb.Append("<sub>").Append(c).Append("</sub>");
            else sb.Append(c);
        }
        return sb.ToString();
    }

    // ---------------------------------------------------------------
    // Construcción de la interfaz (clic derecho en el componente > Construir UI)
    // ---------------------------------------------------------------
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

        var root = WristUIKit.NewRect("WristUI", transform);
        WristUIKit.Anchor(root, 0, 0, 1, 1);

        // ----- Menú inicial -----
        var main = WristUIKit.MakePage("PaginaMenu", root);
        Label(main.transform, "Titulo", "MENÚ", 36, TextAlignmentOptions.Center, 0, 0.80f, 1, 0.98f);
        btnTutorial = WristUIKit.Place(MakeBtn(main.transform, "BtnTutorial", "Tutorial", WristUIKit.ButtonColor, 30), 0.1f, 0.48f, 0.9f, 0.70f);
        btnGlossary = WristUIKit.Place(MakeBtn(main.transform, "BtnGlosario", "Glosario", WristUIKit.ButtonColor, 30), 0.1f, 0.20f, 0.9f, 0.42f);

        // ----- Tutorial: zona de acción arriba, texto corto abajo -----
        var tut = WristUIKit.MakePage("PaginaTutorial", root);
        Label(tut.transform, "Titulo", "TUTORIAL", 26, TextAlignmentOptions.Center, 0, 0.92f, 1, 0.99f);

        var zone = WristUIKit.NewRect("ZonaAccion", tut.transform);
        WristUIKit.Anchor(zone, 0.04f, 0.38f, 0.96f, 0.91f);
        var zoneBg = zone.gameObject.AddComponent<Image>();
        zoneBg.color = new Color(0.04f, 0.05f, 0.08f, 0.9f);
        zoneBg.raycastTarget = false;
        zone.gameObject.AddComponent<RectMask2D>();        // las piezas no se salen de la zona

        var instruction = Label(tut.transform, "Instruccion", "", 20, TextAlignmentOptions.Center, 0.06f, 0.15f, 0.94f, 0.37f);
        instruction.enableAutoSizing = true;
        instruction.fontSizeMin = 12f * fontScale;
        instruction.fontSizeMax = 20f * fontScale;
        var counter = Label(tut.transform, "Contador", "", 16, TextAlignmentOptions.Center, 0.36f, 0.03f, 0.64f, 0.13f);

        btnTutorialBack = WristUIKit.Place(MakeBtn(tut.transform, "BtnMenu", "Menú", WristUIKit.BackButtonColor, 22), 0.04f, 0.03f, 0.34f, 0.13f);
        var btnTutGlossary = WristUIKit.Place(MakeBtn(tut.transform, "BtnGlosario", "Glosario", WristUIKit.ButtonColor, 22), 0.66f, 0.03f, 0.96f, 0.13f);
        btnTutGlossary.gameObject.SetActive(false);        // aparece al terminar el último paso

        tutorial = tut.AddComponent<WristTutorial>();
        tutorial.actionZone = zone;
        tutorial.instructionText = instruction;
        tutorial.counterText = counter;
        tutorial.btnGlossary = btnTutGlossary;
        tutorial.fontScale = fontScale;

        // ----- Glosario: lista deslizable (las tarjetas las crea RefreshRecipeList al abrir la página) -----
        var gloss = WristUIKit.MakePage("PaginaGlosario", root);
        Label(gloss.transform, "Titulo", "GLOSARIO", 30, TextAlignmentOptions.Center, 0, 0.92f, 1, 0.99f);
        Label(gloss.transform, "Ayuda", "Desliza para ver todas las recetas", 14, TextAlignmentOptions.Center, 0, 0.885f, 1, 0.925f);

        var listRt = WristUIKit.NewRect("Lista", gloss.transform);
        WristUIKit.Anchor(listRt, 0.04f, 0.15f, 0.96f, 0.88f);
        var listHit = listRt.gameObject.AddComponent<Image>();   // transparente: recibe el arrastre aunque no haya tarjeta debajo
        listHit.color = new Color(0f, 0f, 0f, 0f);
        listHit.raycastTarget = true;

        var viewport = WristUIKit.NewRect("Viewport", listRt);
        WristUIKit.Anchor(viewport, 0, 0, 1, 1);
        viewport.gameObject.AddComponent<RectMask2D>();          // recorta lo que sale de la lista

        recipeListRoot = WristUIKit.NewRect("Contenido", viewport);
        recipeListRoot.anchorMin = new Vector2(0f, 1f);
        recipeListRoot.anchorMax = new Vector2(1f, 1f);
        recipeListRoot.pivot = new Vector2(0.5f, 1f);
        recipeListRoot.sizeDelta = Vector2.zero;
        recipeListRoot.anchoredPosition = Vector2.zero;

        var list = recipeListRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        list.spacing = 10f * fontScale;
        list.padding = new RectOffset(0, 0, 0, Mathf.RoundToInt(10f * fontScale));
        list.childControlWidth = true;
        list.childControlHeight = true;
        list.childForceExpandWidth = true;
        list.childForceExpandHeight = false;
        recipeListRoot.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Se desliza como un celular: arrastrar, inercia y rebote al llegar al borde
        glossaryScroll = listRt.gameObject.AddComponent<ScrollRect>();
        glossaryScroll.viewport = viewport;
        glossaryScroll.content = recipeListRoot;
        glossaryScroll.horizontal = false;
        glossaryScroll.vertical = true;
        glossaryScroll.movementType = ScrollRect.MovementType.Elastic;
        glossaryScroll.elasticity = 0.1f;
        glossaryScroll.inertia = true;
        glossaryScroll.decelerationRate = 0.135f;
        glossaryScroll.scrollSensitivity = 20f;
        btnGlossaryBack = WristUIKit.Place(MakeBtn(gloss.transform, "BtnVolver", "Menú", WristUIKit.BackButtonColor, 24), 0.3f, 0.02f, 0.7f, 0.12f);

        mainPage = main; tutorialPage = tut; glossaryPage = gloss;
        tut.SetActive(false); gloss.SetActive(false);

        // ----- Botón X para cerrar la interfaz (encima de todas las páginas) -----
        btnClose = WristUIKit.Place(MakeBtn(root, "BtnCerrar", "X", new Color(0.75f, 0.22f, 0.22f, 1f), 26), 0.86f, 0.925f, 0.985f, 0.995f);
        btnClose.transform.SetAsLastSibling();

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
        if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
        Debug.Log("[WristMenu] Interfaz construida. Tutorial y Glosario empiezan ocultos; el script los muestra al dar Play.", this);
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

    // Atajos que usan el tamaño de fuente de este Canvas
    TMP_Text Label(Transform parent, string name, string text, float size, TextAlignmentOptions align,
                   float x0, float y0, float x1, float y1)
    {
        return WristUIKit.Label(parent, name, text, size, align, fontScale, x0, y0, x1, y1);
    }

    Button MakeBtn(Transform parent, string name, string label, Color color, float fontSize)
    {
        return WristUIKit.MakeButton(parent, name, label, color, fontSize, fontScale);
    }
}

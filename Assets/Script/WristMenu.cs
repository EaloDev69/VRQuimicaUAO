using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// Canvas de la muñeca. Tres páginas:
///   - Menú inicial: dos botones, Tutorial y Glosario.
///   - Tutorial: minijuego 2D por pasos (lo controla WristTutorial).
///   - Glosario: lista con scroll tipo celular con presión, temperatura y formación (FASE 3, por ahora vacía).
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
    public RecipeCatalog catalog;                // lo usará el glosario (fase 3)
    [Tooltip("Recetas de niveles mayores a este aparecerán bloqueadas en el glosario (el GameFlowManager lo sube con SetUnlockedLevel)")]
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

    [Header("Referencias (las crea 'Construir UI')")]
    public GameObject mainPage;
    public GameObject tutorialPage;
    public GameObject glossaryPage;
    public WristTutorial tutorial;
    public Button btnTutorial;
    public Button btnGlossary;
    public Button btnTutorialBack;
    public Button btnGlossaryBack;

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

        // ----- Glosario (fase 3: por ahora vacío) -----
        var gloss = WristUIKit.MakePage("PaginaGlosario", root);
        Label(gloss.transform, "Titulo", "GLOSARIO", 30, TextAlignmentOptions.Center, 0, 0.88f, 1, 0.98f);
        Label(gloss.transform, "Pendiente", "(en construcción)", 22, TextAlignmentOptions.Center, 0.07f, 0.40f, 0.93f, 0.60f);
        btnGlossaryBack = WristUIKit.Place(MakeBtn(gloss.transform, "BtnVolver", "Menú", WristUIKit.BackButtonColor, 24), 0.3f, 0.02f, 0.7f, 0.12f);

        mainPage = main; tutorialPage = tut; glossaryPage = gloss;
        tut.SetActive(false); gloss.SetActive(false);

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

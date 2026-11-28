using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public enum ContainerKind { Bottle, Cup }

/// Contenedor de la salida de la Máquina Transmutadora: botella (gases) o vaso (líquidos).
/// Una vez lleno es la "muestra": guarda la receta para la clasificación y la Máquina Evaluadora.
/// Montaje: Rigidbody + XRGrabInteractable + collider + este script.
///
/// El contenido (contentVisual) se llena de dos maneras, según el material:
///  - Material con shader "Molecula/LiquidFill": copia de la forma del recipiente; se recorta a una altura.
///    Se adapta a cualquier forma y la superficie queda horizontal aunque se incline.
///  - Material con shader "Molecula/GasFill": nube translúcida que se vuelve más densa al llenarse (botellas).
///  - Cualquier otro material: el contenido se escala en Y (pivote en la base). Sirve para formas rectas.
public class SampleItem : MonoBehaviour
{
    public ContainerKind kind = ContainerKind.Cup;

    [Tooltip("Tamaño del vaso en ml. Define cuánto tarda en llenarse (no se usa en botellas)")]
    public float capacityMl = 250f;

    [Header("Contenido visual")]
    public Transform contentVisual;          // objeto del líquido o gas dentro del contenedor
    public Renderer contentRenderer;         // renderer del contenido (se busca solo si se deja vacío)
    [Tooltip("Punto dentro del vaso adonde llega el chorro (opcional). Vacío = el origen del vaso")]
    public Transform streamTarget;

    [Header("Seguridad")]
    [Tooltip("Si el contenedor se cae del escenario (queda más de 'Fall Distance' m por debajo de donde empezó), vuelve a su sitio")]
    public bool respawnIfFallen = true;
    public float fallDistance = 2f;

    [Header("Estado (solo lectura)")]
    public RecipeData recipe;                // null mientras está vacío
    public float fillLevel01;
    public bool classified;                  // la estación de clasificación lo marca en true
    public float filledAt;

    Vector3 baseScale = Vector3.one;
    Vector3 startPos; Quaternion startRot; Rigidbody body;
    Material contentMat;
    bool shaderMode;
    bool gasMode;

    // Hacia dónde cae el chorro: el streamTarget si está asignado; si no, el centro de la boca del recipiente
    public Vector3 StreamPoint
    {
        get
        {
            if (streamTarget != null) return streamTarget.position;
            var r = GetComponent<Renderer>();
            if (r == null) return transform.position + Vector3.up * 0.05f;
            var b = r.bounds;
            return new Vector3(b.center.x, b.max.y, b.center.z);
        }
    }

    public bool IsFilled => recipe != null;
    public bool IsEmpty => recipe == null && fillLevel01 <= 0.001f;
    public CompoundCategory? Category => recipe != null ? recipe.category : (CompoundCategory?)null;

    void Awake()
    {
        startPos = transform.position; startRot = transform.rotation;
        body = GetComponent<Rigidbody>();
        if (body != null) body.maxDepenetrationVelocity = 1f;      // si empieza encimado con la mesa, sube suave
        UsePrimitiveCollider();
        SetupGrab();
        if (contentVisual != null)
        {
            baseScale = contentVisual.localScale;
            // includeInactive: el contenido empieza apagado y, si no, no se encontraría el renderer
            if (contentRenderer == null) contentRenderer = contentVisual.GetComponentInChildren<Renderer>(true);
        }
        if (contentRenderer != null)
        {
            contentMat = contentRenderer.material;               // instancia propia por contenedor
            shaderMode = contentMat.HasProperty("_FillY");
            gasMode = contentMat.HasProperty("_Density");
        }
        Apply(0f, Color.white);
    }

    // El collider de malla convexo de estos modelos falla: la malla es diminuta y el objeto está escalado ×13826,
    // y PhysX la "cocina" mal (por eso botellas y vasos atravesaban la mesa). Se cambia por una caja del tamaño
    // de la malla, que es exacta a cualquier escala.
    void UsePrimitiveCollider()
    {
        var mc = GetComponent<MeshCollider>();
        var mf = GetComponent<MeshFilter>();
        if (mc == null || !mc.enabled || mf == null || mf.sharedMesh == null) return;
        var box = gameObject.AddComponent<BoxCollider>();
        var b = mf.sharedMesh.bounds;
        box.center = b.center;
        box.size = b.size;
        box.sharedMaterial = mc.sharedMaterial;
        mc.enabled = false;

        var grab = GetComponent<XRGrabInteractable>();
        if (grab == null) return;
        bool wasEnabled = grab.enabled;
        if (wasEnabled) grab.enabled = false;
        grab.colliders.Remove(mc);
        if (!grab.colliders.Contains(box)) grab.colliders.Add(box);
        if (wasEnabled) grab.enabled = true;
    }

    // Agarre: la mano lo toma desde donde lo toca (lado del vaso o la botella), no desde la base.
    // Botellas: punto de encaje en el centro de la botella y derecho, para que el socket de gases la ponga parada.
    // (Los sockets siempre usan este punto fijo; la mano usa el punto dinámico.)
    void SetupGrab()
    {
        var grab = GetComponent<XRGrabInteractable>();
        if (grab == null) return;
        grab.useDynamicAttach = true;               // con la mano se agarra de donde se toca
        if (grab.attachTransform != null) return;

        // Punto fijo para los sockets (los sockets siempre usan este punto, no el dinámico):
        //  - botella: su centro (socket de gases)
        //  - vaso:    el centro de su base (socket bajo el pico), así queda apoyado y centrado
        var b = WorldBounds();
        Vector3 point = kind == ContainerKind.Cup ? new Vector3(b.center.x, b.min.y, b.center.z) : b.center;
        Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(transform.up, Vector3.up);
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;

        var attach = new GameObject("Attach (socket)").transform;
        attach.SetParent(transform, false);
        attach.SetPositionAndRotation(point, Quaternion.LookRotation(fwd.normalized, Vector3.up));   // empieza parado
        grab.attachTransform = attach;
    }

    // Caja del contenedor en el mundo (la del modelo; el collider recién creado en Awake aún no tiene caja fiable)
    Bounds WorldBounds()
    {
        var r = GetComponent<Renderer>();
        if (r != null) return r.bounds;
        var c = GetComponent<Collider>();
        return c != null ? c.bounds : new Bounds(transform.position, Vector3.one * 0.1f);
    }

    // Sin superficie debajo (ni mesa ni piso con collider): se queda quieto donde lo pusieron hasta que lo agarren,
    // en vez de caer y reaparecer una y otra vez.
    bool parked;
    bool warnedNoSurface;

    void Start()
    {
        if (respawnIfFallen && body != null && !body.isKinematic && !HasSurfaceBelow(startPos)) Park("no hay mesa ni piso con collider debajo");
        var grab = GetComponent<XRGrabInteractable>();
        if (grab != null) grab.selectExited.AddListener(_ => { if (parked) StartCoroutine(Unpark()); });
    }

    System.Collections.IEnumerator Unpark()
    {
        yield return null;                                   // después de que el agarre restaure su estado
        parked = false;
        var g = GetComponent<XRGrabInteractable>();
        if (body != null && (g == null || !g.isSelected)) body.isKinematic = false;
    }

    void Park(string why)
    {
        parked = true;
        if (body != null)
        {
            if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            body.isKinematic = true;
        }
        transform.SetPositionAndRotation(startPos, startRot);
        if (!warnedNoSurface)
        {
            warnedNoSurface = true;
            Debug.LogWarning($"[SampleItem] {name}: {why} en {startPos:F2}. Se queda quieto ahí hasta que lo agarres. Ponlo sobre la mesa (o dale collider a esa superficie).", this);
        }
    }

    bool HasSurfaceBelow(Vector3 from)
    {
        foreach (var h in Physics.RaycastAll(from + Vector3.up * 0.05f, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore))
            if (h.collider != null && !h.collider.transform.IsChildOf(transform) && h.collider.attachedRigidbody != body) return true;
        return false;
    }

    void FixedUpdate()
    {
        if (!respawnIfFallen || parked || transform.position.y > startPos.y - fallDistance) return;
        if (body != null && !body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
        transform.SetPositionAndRotation(startPos, startRot);
        if (!HasSurfaceBelow(startPos)) Park("se cayó y no hay superficie con collider debajo de su sitio");
        else Debug.LogWarning($"[SampleItem] {name} se cayó del escenario y volvió a su sitio.", this);
    }

    // En modo shader la altura del recorte sigue al recipiente si se mueve o se inclina
    void LateUpdate()
    {
        if (shaderMode && fillLevel01 > 0.001f) UpdateFillPlane();
    }

    /// Llenado progresivo (la máquina lo llama cada frame mientras sirve)
    public void SetFillLevel(float t, Color color)
    {
        fillLevel01 = Mathf.Clamp01(t);
        Apply(fillLevel01, color);
    }

    /// Termina de llenar: el contenedor pasa a ser la muestra de esa receta
    public void CompleteFill(RecipeData r)
    {
        recipe = r;
        filledAt = Time.time;
        SetFillLevel(1f, r.sampleColor);
        ShowLabel(true);
    }

    // ---------------------------------------------------------------
    // Etiqueta con las características del compuesto (nombre y fórmula), sobre el contenedor lleno
    // ---------------------------------------------------------------
    [Header("Etiqueta")]
    [Tooltip("Muestra el nombre y la fórmula del compuesto encima del contenedor cuando está lleno")]
    public bool showLabel = true;
    TextMeshPro label;

    void ShowLabel(bool on)
    {
        if (!on || !showLabel || recipe == null) { if (label != null) label.gameObject.SetActive(false); return; }
        if (label == null)
        {
            var go = new GameObject("Etiqueta");
            label = go.AddComponent<TextMeshPro>();
            label.fontSize = 0.6f;
            label.alignment = TextAlignmentOptions.Center;
            label.rectTransform.sizeDelta = new Vector2(0.4f, 0.12f);
            go.AddComponent<FaceCamera>();
        }
        label.text = $"{recipe.displayName}\n<size=80%>{recipe.formula}</size>";
        label.color = Color.Lerp(recipe.sampleColor, Color.white, 0.4f);
        label.gameObject.SetActive(true);
    }

    void Update()
    {
        if (label == null || !label.gameObject.activeSelf) return;
        // Sigue al contenedor sin heredar su escala (las botellas y vasos tienen escalas muy grandes)
        var r = GetComponent<Renderer>();
        Vector3 top = r != null ? new Vector3(r.bounds.center.x, r.bounds.max.y, r.bounds.center.z) : transform.position;
        label.transform.position = top + Vector3.up * 0.06f;
    }

    void OnDestroy() { if (label != null) Destroy(label.gameObject); }

    /// Lo vacía (derrame o cancelación)
    public void ResetContents()
    {
        recipe = null;
        classified = false;
        SetFillLevel(0f, Color.white);
        ShowLabel(false);
    }

    void Apply(float level, Color color)
    {
        if (contentVisual == null) return;

        bool visible = level > 0.001f;
        contentVisual.gameObject.SetActive(visible);

        if (!shaderMode && !gasMode)
        {
            var s = baseScale; s.y = baseScale.y * level;
            contentVisual.localScale = s;
        }

        if (contentMat != null)
        {
            if (contentMat.HasProperty("_BaseColor")) contentMat.SetColor("_BaseColor", color);
            if (contentMat.HasProperty("_Color")) contentMat.SetColor("_Color", color);
        }

        if (gasMode && contentMat != null) contentMat.SetFloat("_Density", level);
        if (shaderMode && visible) UpdateFillPlane();
    }

    // Altura de llenado = del punto más bajo al más alto del contenido, según el nivel
    void UpdateFillPlane()
    {
        var b = contentRenderer.bounds;
        contentMat.SetFloat("_FillY", Mathf.Lerp(b.min.y, b.max.y, fillLevel01));
    }
}
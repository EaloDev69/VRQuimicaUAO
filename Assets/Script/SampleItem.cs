using UnityEngine;

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

    [Header("Estado (solo lectura)")]
    public RecipeData recipe;                // null mientras está vacío
    public float fillLevel01;
    public bool classified;                  // la estación de clasificación lo marca en true
    public float filledAt;

    Vector3 baseScale = Vector3.one;
    Material contentMat;
    bool shaderMode;
    bool gasMode;

    public Vector3 StreamPoint => streamTarget != null ? streamTarget.position : transform.position + Vector3.up * 0.05f;

    public bool IsFilled => recipe != null;
    public bool IsEmpty => recipe == null && fillLevel01 <= 0.001f;
    public CompoundCategory? Category => recipe != null ? recipe.category : (CompoundCategory?)null;

    void Awake()
    {
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
    }

    /// Lo vacía (derrame o cancelación)
    public void ResetContents()
    {
        recipe = null;
        classified = false;
        SetFillLevel(0f, Color.white);
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
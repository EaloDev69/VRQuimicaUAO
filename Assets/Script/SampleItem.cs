using UnityEngine;

public enum ContainerKind { Bottle, Cup }

/// Contenedor de la salida de la Máquina Transmutadora: botella (gases) o vaso (líquidos).
/// Una vez lleno es la "muestra": guarda la receta para la clasificación y la Máquina Evaluadora.
/// Montaje: Rigidbody + XRGrabInteractable + collider + este script.
/// contentVisual: un objeto hijo con el pivote en la BASE (ej. un cilindro dentro del vaso);
/// se escala en Y de 0 a 1 según el nivel de llenado.
public class SampleItem : MonoBehaviour
{
    public ContainerKind kind = ContainerKind.Cup;

    [Tooltip("Tamaño del vaso en ml. Define cuánto tarda en llenarse (no se usa en botellas)")]
    public float capacityMl = 250f;

    [Header("Contenido visual")]
    public Transform contentVisual;          // líquido o gas dentro del contenedor
    public Renderer contentRenderer;         // renderer del contenido (para teñirlo)

    [Header("Estado (solo lectura)")]
    public RecipeData recipe;                // null mientras está vacío
    public float fillLevel01;
    public bool classified;                  // la estación de clasificación lo marca en true
    public float filledAt;

    Vector3 baseScale = Vector3.one;

    public bool IsFilled => recipe != null;
    public bool IsEmpty => recipe == null && fillLevel01 <= 0.001f;
    public CompoundCategory? Category => recipe != null ? recipe.category : (CompoundCategory?)null;

    void Awake()
    {
        if (contentVisual != null)
        {
            baseScale = contentVisual.localScale;
            if (contentRenderer == null) contentRenderer = contentVisual.GetComponentInChildren<Renderer>();
        }
        Apply(0f, Color.white);
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
        contentVisual.gameObject.SetActive(level > 0.001f);
        var s = baseScale; s.y = baseScale.y * level;
        contentVisual.localScale = s;

        if (contentRenderer == null) return;
        var mat = contentRenderer.material;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
    }
}
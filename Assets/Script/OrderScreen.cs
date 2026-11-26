using TMPro;
using UnityEngine;

/// Pantalla grande del laboratorio (GDD 4.7): tarjeta del pedido, temporizador grande y barra de color.
/// Solo muestra; el botón Aceptar es un LabButton aparte con On Pressed -> OrderManager.AcceptCurrent.
public class OrderScreen : MonoBehaviour
{
    public OrderManager orders;

    [Header("Textos")]
    public TMP_Text titleText;       // "Agua"
    public TMP_Text formulaText;     // "H₂O"
    public TMP_Text timerText;       // "01:45"
    public TMP_Text stateText;       // "Pulsa Aceptar", "En curso"...
<<<<<<< HEAD
    public TMP_Text pressureText;    // "Presión: Alta" (opcional)
    public TMP_Text temperatureText; // "Temperatura: Media" (opcional)
=======
>>>>>>> 3f517e58c67353504dd60bb8d6a87b6eeba73bd3

    [Header("Barra del temporizador (opcional)")]
    public Transform barFill;        // objeto con pivote en el extremo izquierdo; se escala en X
    public Renderer barRenderer;
    public Color okColor = new Color(0.3f, 0.75f, 0.35f);
    public Color warnColor = new Color(0.95f, 0.8f, 0.2f);
    public Color dangerColor = new Color(0.9f, 0.25f, 0.2f);

    Vector3 barBaseScale = Vector3.one;

    void Awake() { if (barFill != null) barBaseScale = barFill.localScale; }

    void Update()
    {
        var o = orders != null ? orders.Current : null;
        if (o == null)
        {
            Set(titleText, "Sin pedidos"); Set(formulaText, ""); Set(timerText, "--:--"); Set(stateText, "");
<<<<<<< HEAD
            Set(pressureText, ""); Set(temperatureText, "");
=======
>>>>>>> 3f517e58c67353504dd60bb8d6a87b6eeba73bd3
            SetBar(0f);
            return;
        }

        Set(titleText, o.recipe.displayName);
        Set(formulaText, o.recipe.formula);
<<<<<<< HEAD
        Set(pressureText, $"Presión: {o.recipe.requiredPressure.Label()}");
        Set(temperatureText, $"Temperatura: {o.recipe.requiredTemperature.Label()}");
=======
>>>>>>> 3f517e58c67353504dd60bb8d6a87b6eeba73bd3

        float shown = o.state == OrderState.Waiting ? o.timeLimit : o.timeRemaining;
        int secs = Mathf.CeilToInt(shown);
        Set(timerText, $"{secs / 60:00}:{secs % 60:00}");

        switch (o.state)
        {
            case OrderState.Waiting:   Set(stateText, "Pulsa Aceptar"); break;
            case OrderState.Active:    Set(stateText, "En curso"); break;
            case OrderState.Delivered: Set(stateText, "¡Entregado!"); break;
            case OrderState.Lost:      Set(stateText, "Perdido"); break;
        }

        SetBar(o.timeLimit > 0f ? shown / o.timeLimit : 0f);
    }

    void SetBar(float fraction)
    {
        fraction = Mathf.Clamp01(fraction);
        if (barFill != null)
        {
            var s = barBaseScale; s.x = barBaseScale.x * fraction;
            barFill.localScale = s;
        }
        if (barRenderer != null)
        {
            var c = fraction > 0.5f ? okColor : fraction > 0.2f ? warnColor : dangerColor;   // además del número, no solo color
            var mat = barRenderer.material;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
        }
    }

    static void Set(TMP_Text t, string value) { if (t != null) t.text = value; }
}

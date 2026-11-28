using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum EstadoEncargo { Pendiente, EnCurso, Entregado, Fallido }

[Serializable]
public class Encargo
{
    public string nombre;
    public RecipeData recipe;                 // lo que hay que entregar
    public Sprite sprite;                     // imagen del encargo en la pantalla
    [Tooltip("Tiempo límite en segundos. 0 = usa el tiempo de la receta")]
    public float tiempoLimite = 0f;
    [HideInInspector] public EstadoEncargo estado = EstadoEncargo.Pendiente;
}

/// Pantalla de encargos. Las flechas cambian entre encargos; Aceptar lo pasa al OrderManager
/// y el tiempo límite empieza a contar en ese momento. Solo se puede tener un encargo en curso.
/// El cuadro indicador muestra el estado del encargo que se está viendo:
/// gris = pendiente, amarillo = en curso, verde = entregado, rojo = fuera de tiempo o entrega equivocada.
public class GestorEncargosVR : MonoBehaviour
{
    [Header("Datos")]
    public OrderManager orders;
    public List<Encargo> encargos = new List<Encargo>();

    [Header("Indicador de estado (opcional)")]
    public Image indicadorEstado;
    public Color colorPendiente = Color.gray;
    public Color colorEnCurso = new Color(1f, 0.85f, 0.2f);
    public Color colorEntregado = new Color(0.2f, 0.85f, 0.3f);
    public Color colorFallido = new Color(0.9f, 0.2f, 0.2f);

    [Header("Opcional")]
    public TMP_Text textoTiempo;              // tiempo límite del encargo que se está viendo

    Image imagenEncargo;
    Button botonAceptar, botonCancelar, botonDerecha, botonIzquierda;
    int indiceActual = 0;
    Encargo encargoEnCurso;

    void Start()
    {
        imagenEncargo = transform.Find("Image").GetComponent<Image>();
        botonAceptar = transform.Find("BotonAceptar").GetComponent<Button>();
        botonCancelar = transform.Find("BotonCancelar").GetComponent<Button>();
        botonDerecha = transform.Find("BotonDerecha").GetComponent<Button>();
        botonIzquierda = transform.Find("BotonIzquierda").GetComponent<Button>();

        botonAceptar.onClick.AddListener(AceptarEncargo);
        botonCancelar.onClick.AddListener(CancelarEncargo);
        botonDerecha.onClick.AddListener(SiguienteEncargo);
        botonIzquierda.onClick.AddListener(AnteriorEncargo);

        if (orders != null)
        {
            orders.OrderDelivered += OnOrderDelivered;
            orders.OrderLost += OnOrderLost;
        }
        ActualizarInterfaz();
    }

    // Mientras se ve el encargo en curso, el texto de tiempo muestra la cuenta regresiva
    void Update()
    {
        if (textoTiempo == null || encargoEnCurso == null || orders == null || orders.Current == null) return;
        if (encargos.Count == 0 || encargos[indiceActual] != encargoEnCurso) return;
        textoTiempo.text = Mathf.CeilToInt(Mathf.Max(0f, orders.Current.timeRemaining)) + " s";
    }

    void OnDestroy()
    {
        if (orders == null) return;
        orders.OrderDelivered -= OnOrderDelivered;
        orders.OrderLost -= OnOrderLost;
    }

    void OnOrderDelivered(Order o) { CerrarEncargo(EstadoEncargo.Entregado); }
    void OnOrderLost(Order o) { CerrarEncargo(EstadoEncargo.Fallido); }

    void CerrarEncargo(EstadoEncargo estado)
    {
        if (encargoEnCurso != null) encargoEnCurso.estado = estado;
        encargoEnCurso = null;
        ActualizarInterfaz();
    }

    public void SiguienteEncargo()
    {
        if (indiceActual < encargos.Count - 1) { indiceActual++; ActualizarInterfaz(); }
    }

    public void AnteriorEncargo()
    {
        if (indiceActual > 0) { indiceActual--; ActualizarInterfaz(); }
    }

    public void AceptarEncargo()
    {
        if (encargos.Count == 0 || orders == null) return;
        var e = encargos[indiceActual];
        if (e.estado != EstadoEncargo.Pendiente) return;
        if (!orders.AcceptOrder(e.recipe, e.tiempoLimite)) return;   // ya hay uno en curso o falta la receta

        e.estado = EstadoEncargo.EnCurso;
        encargoEnCurso = e;
        ActualizarInterfaz();
    }

    /// Descarta un encargo que aún no se ha aceptado
    public void CancelarEncargo()
    {
        if (encargos.Count == 0) return;
        if (encargos[indiceActual].estado != EstadoEncargo.Pendiente) return;

        encargos.RemoveAt(indiceActual);
        if (indiceActual >= encargos.Count && indiceActual > 0) indiceActual--;
        ActualizarInterfaz();
    }

    void ActualizarInterfaz()
    {
        bool hay = encargos.Count > 0;
        imagenEncargo.gameObject.SetActive(hay);
        if (indicadorEstado != null) indicadorEstado.gameObject.SetActive(hay);

        botonIzquierda.interactable = hay && indiceActual > 0;
        botonDerecha.interactable = hay && indiceActual < encargos.Count - 1;

        if (!hay)
        {
            botonAceptar.interactable = false;
            botonCancelar.interactable = false;
            if (textoTiempo != null) textoTiempo.text = "";
            return;
        }

        var e = encargos[indiceActual];
        bool pendiente = e.estado == EstadoEncargo.Pendiente;
        botonAceptar.interactable = pendiente && orders != null && !orders.HasActiveOrder;
        botonCancelar.interactable = pendiente;

        imagenEncargo.sprite = e.sprite;
        if (indicadorEstado != null) indicadorEstado.color = ColorDe(e.estado);
        if (textoTiempo != null)
        {
            float t = e.tiempoLimite > 0f ? e.tiempoLimite : (e.recipe != null ? e.recipe.timeLimit : 0f);
            textoTiempo.text = Mathf.RoundToInt(t) + " s";
        }
    }

    Color ColorDe(EstadoEncargo s)
    {
        switch (s)
        {
            case EstadoEncargo.EnCurso: return colorEnCurso;
            case EstadoEncargo.Entregado: return colorEntregado;
            case EstadoEncargo.Fallido: return colorFallido;
            default: return colorPendiente;
        }
    }
}

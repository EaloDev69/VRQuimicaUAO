using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class GestorEncargosVR : MonoBehaviour
{
    [Header("Lista de Encargos (Arrastra tus Sprites aquí)")]
    public List<Sprite> listaEncargos;

    // Referencias internas basadas en los nombres de tu Hierarchy
    private Image imagenEncargo;
    private Button botonAceptar;
    private Button botonCancelar;
    private Button botonDerecha;
    private Button botonIzquierda;

    private int indiceActual = 0;

    void Start()
    {
        // Busca automáticamente los hijos dentro de "Panel" por sus nombres exactos
        imagenEncargo = transform.Find("Image").GetComponent<Image>();
        botonAceptar = transform.Find("BotonAceptar").GetComponent<Button>();
        botonCancelar = transform.Find("BotonCancelar").GetComponent<Button>();
        botonDerecha = transform.Find("BotonDerecha").GetComponent<Button>();
        botonIzquierda = transform.Find("BotonIzquierda").GetComponent<Button>();

        // Conecta los clics de los botones automáticamente por código
        botonAceptar.onClick.AddListener(AceptarEncargo);
        botonCancelar.onClick.AddListener(CancelarEncargo);
        botonDerecha.onClick.AddListener(SiguienteEncargo);
        botonIzquierda.onClick.AddListener(AnteriorEncargo);

        ActualizarInterfaz();
    }

    public void SiguienteEncargo()
    {
        if (indiceActual < listaEncargos.Count - 1)
        {
            indiceActual++;
            ActualizarInterfaz();
        }
    }

    public void AnteriorEncargo()
    {
        if (indiceActual > 0)
        {
            indiceActual--;
            ActualizarInterfaz();
        }
    }

    public void AceptarEncargo()
    {
        if (listaEncargos.Count == 0) return;

        Debug.Log("Encargo ACEPTADO: " + listaEncargos[indiceActual].name);
        EliminarEncargoActual();
    }

    public void CancelarEncargo()
    {
        if (listaEncargos.Count == 0) return;

        Debug.Log("Encargo CANCELADO: " + listaEncargos[indiceActual].name);
        EliminarEncargoActual();
    }

    private void EliminarEncargoActual()
    {
        listaEncargos.RemoveAt(indiceActual);

        if (indiceActual >= listaEncargos.Count && indiceActual > 0)
        {
            indiceActual--;
        }

        ActualizarInterfaz();
    }

    private void ActualizarInterfaz()
    {
        if (listaEncargos.Count > 0)
        {
            imagenEncargo.gameObject.SetActive(true);
            imagenEncargo.sprite = listaEncargos[indiceActual];

            // Desactiva el botón si ya no hay más encargos hacia ese lado
            botonIzquierda.interactable = (indiceActual > 0);
            botonDerecha.interactable = (indiceActual < listaEncargos.Count - 1);
            botonAceptar.interactable = true;
            botonCancelar.interactable = true;
        }
        else
        {
            // Si la lista queda vacía, oculta la imagen y desactiva los 4 botones
            imagenEncargo.gameObject.SetActive(false);
            botonIzquierda.interactable = false;
            botonDerecha.interactable = false;
            botonAceptar.interactable = false;
            botonCancelar.interactable = false;
            Debug.Log("No quedan encargos pendientes.");
        }
    }
}

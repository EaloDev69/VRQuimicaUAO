using UnityEngine;

public class PuertaAnimada : MonoBehaviour
{
    private Animator animador;
    private bool estaAbierta = false;

    void Start()
    {
        animador = GetComponent<Animator>();
    }

    // Opción 1: Abre y cierra al presionar la tecla E
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.E))
        {
            AlternarPuerta();
        }
    }

    // Opción 2: Abre y cierra al hacer clic izquierdo sobre la puerta en el modo Play
    private void OnMouseDown()
    {
        AlternarPuerta();
    }

    // Función pública lista para que tu compañero la conecte en VR
    public void AlternarPuerta()
    {
        estaAbierta = !estaAbierta;
        animador.SetBool("Abierta", estaAbierta);
    }
}

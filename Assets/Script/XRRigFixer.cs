using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// Ajustes del XR Origin que se aplican solos al dar Play (no hay que ponerlo en la escena).
///
/// Poke con el dedo: el punto de poke de cada mano estaba en el centro del control, a varios centímetros del dedo
/// índice del modelo de mano. Al acercar el dedo al menú de la muñeca el punto de poke no llegaba al panel y el
/// toque no se registraba (con el mouse sí funcionaba). Aquí el punto de poke se pega a la punta del índice
/// del modelo de mano (hueso "..._index_ignore" o el último hueso "index" que encuentre).
/// El menú va en la muñeca izquierda: se toca con el índice de la mano DERECHA.
/// Además agranda la zona de poke para que sea más cómodo y crea el filtro que evita que el cuerpo del
/// jugador choque con los objetos agarrables, y pone AntiVuelco en canastas, botellas y vasos.
public static class XRRigFixer
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
        Fix();
    }

    static void OnLoaded(Scene s, LoadSceneMode m) => Fix();

    static void Fix()
    {
        if (Object.FindFirstObjectByType<GrabbableCollisionFilter>() == null)
            new GameObject("GrabbableCollisionFilter (auto)").AddComponent<GrabbableCollisionFilter>();

        // Que canastas, botellas y vasos no se volteen tan fácil
        foreach (var s in Object.FindObjectsByType<SampleItem>(FindObjectsSortMode.None))
            if (s.GetComponent<Rigidbody>() != null && s.GetComponent<AntiVuelco>() == null) s.gameObject.AddComponent<AntiVuelco>();
        foreach (var rb in Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
        {
            string n = rb.name.ToLowerInvariant();
            bool basket = n.Contains("canasta") || n.Contains("cesta") || n.Contains("basket");
            if (basket && rb.GetComponent<AntiVuelco>() == null) rb.gameObject.AddComponent<AntiVuelco>();
        }

        foreach (var poke in Object.FindObjectsByType<XRPokeInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            AttachToFingertip(poke);
            Enlarge(poke);
        }
    }

    static void AttachToFingertip(XRPokeInteractor poke)
    {
        var point = poke.attachTransform;
        if (point == null || point.name.EndsWith("(dedo)")) return;     // ya ajustado

        // El modelo de mano es hermano del Poke Interactor, bajo el mismo control (Left/Right Controller)
        var controller = poke.transform.parent;
        if (controller == null) return;

        Transform tip = null; int bestDepth = -1;
        foreach (var t in controller.GetComponentsInChildren<Transform>(true))
        {
            if (t.IsChildOf(poke.transform)) continue;
            string n = t.name.ToLowerInvariant();
            if (!n.Contains("index")) continue;
            int depth = Depth(t, controller);
            bool isTip = n.Contains("ignore") || n.Contains("tip") || n.Contains("null");
            if (isTip) depth += 100;                    // la punta tiene prioridad
            if (depth > bestDepth) { bestDepth = depth; tip = t; }
        }
        if (tip == null)
        {
            Debug.LogWarning($"[XRRigFixer] No encontré el dedo índice del modelo de mano para {poke.name}; el poke queda en el control.", poke);
            return;
        }

        point.SetParent(tip, false);
        point.localPosition = Vector3.zero;
        point.localRotation = Quaternion.identity;
        point.name += " (dedo)";
        Debug.Log($"[XRRigFixer] Poke de {controller.name} pegado a la punta del índice ({tip.name}).", poke);
    }

    // Zona de poke más grande para que tocar el menú sea cómodo (valores originales: hover 0.015, select 0.015, ancho 0.0075)
    public static float PokeHoverRadius = 0.035f;
    public static float PokeSelectWidth = 0.03f;
    public static float PokeWidth = 0.015f;

    static void Enlarge(XRPokeInteractor poke)
    {
        poke.pokeHoverRadius = Mathf.Max(poke.pokeHoverRadius, PokeHoverRadius);
        poke.pokeSelectWidth = Mathf.Max(poke.pokeSelectWidth, PokeSelectWidth);
        poke.pokeWidth = Mathf.Max(poke.pokeWidth, PokeWidth);
    }

    static int Depth(Transform t, Transform root)
    {
        int d = 0;
        while (t != null && t != root) { d++; t = t.parent; }
        return d;
    }
}

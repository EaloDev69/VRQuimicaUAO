using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables; // XRI 2.x: borrar esta línea
using UnityEngine.XR.Interaction.Toolkit.Interactors;   // XRI 2.x: borrar esta línea

public enum SocketFilter
{
    BottleOnly,    // solo acepta botellas (SampleItem con Kind = Bottle): socket de gases de la máquina
    Tag            // solo acepta objetos con el tag indicado: socket de la tapa en la boca de la botella
}

/// Socket único para todo el proyecto: es un XRSocketInteractor que solo acepta lo que toca.
///  - En la máquina (GasSocket):      Filter = BottleOnly
///  - En la botella (CapSocket):      Filter = Tag, Required Tag = BottleCap (el tag de la tapa)
/// Sirve de socket de la tapa y de la botella sin usar capas de interacción.
public class FilteredSocket : XRSocketInteractor
{
    [Header("Filtro")]
    public SocketFilter filter = SocketFilter.BottleOnly;
    [Tooltip("Solo con Filter = Tag: el tag que debe tener el objeto (créalo en Tags and Layers)")]
    public string requiredTag = "BottleCap";

    bool tagErrorShown;

    public override bool CanHover(IXRHoverInteractable interactable)
        => base.CanHover(interactable) && Matches(interactable.transform);

    public override bool CanSelect(IXRSelectInteractable interactable)
        => base.CanSelect(interactable) && Matches(interactable.transform);

    bool Matches(Transform t)
    {
        switch (filter)
        {
            case SocketFilter.BottleOnly:
                var s = t.GetComponentInParent<SampleItem>();
                return s != null && s.kind == ContainerKind.Bottle;

            case SocketFilter.Tag:
                try { return t.gameObject.CompareTag(requiredTag); }
                catch (UnityException)
                {
                    if (!tagErrorShown)
                    {
                        tagErrorShown = true;
                        Debug.LogError($"[FilteredSocket] El tag \"{requiredTag}\" no existe. Créalo en Edit > Project Settings > Tags and Layers.", this);
                    }
                    return false;
                }
        }
        return false;
    }
}

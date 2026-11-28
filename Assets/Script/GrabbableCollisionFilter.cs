using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// El cuerpo del jugador (CharacterController del XR Origin) no choca con los objetos que se agarran
/// (átomos, barras, canastas, botellas, vasos, tapas).
/// Sin esto, al caminar con una canasta o una molécula en la mano el objeto "frena" al jugador o sale disparado,
/// y al acercarse a la mesa el cuerpo empuja las piezas.
/// Lo crea XRRigFixer al dar Play; revisa cada medio segundo los objetos nuevos (los que sueltan los spawners).
public class GrabbableCollisionFilter : MonoBehaviour
{
    readonly HashSet<int> done = new HashSet<int>();
    readonly List<Collider> player = new List<Collider>();
    float next;

    void Update()
    {
        if (Time.time < next) return;
        next = Time.time + 0.5f;

        player.Clear();
        foreach (var cc in FindObjectsByType<CharacterController>(FindObjectsSortMode.None))
            if (cc != null) player.Add(cc);
        if (player.Count == 0) return;

        foreach (var g in FindObjectsByType<XRGrabInteractable>(FindObjectsSortMode.None))
        {
            if (g == null || !done.Add(g.GetInstanceID())) continue;
            foreach (var c in g.GetComponentsInChildren<Collider>(true))
            {
                if (c == null || c.isTrigger) continue;
                foreach (var p in player) Physics.IgnoreCollision(c, p, true);
            }
        }
    }
}

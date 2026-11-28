using UnityEngine;
using UnityEngine.SceneManagement;

/// Socket del vaso en la transmutadora. Se crea solo al dar Play (no hay que ponerlo en la escena).
/// Antes el vaso podía quedar en cualquier punto de la CupZone: si quedaba en una orilla, el chorro salía
/// inclinado y no caía dentro del vaso. Ahora, al acercar o soltar un vaso en la CupZone, se encaja solo:
/// queda parado, con el centro de su base justo debajo del pico y apoyado sobre la superficie.
///  - Solo acepta vasos (FilteredSocket con filtro CupOnly); botellas, átomos y demás no se pegan.
///  - Con la mano se saca normal (agarrándolo).
///  - La transmutadora lo sigue encontrando igual: el vaso queda dentro de la CupZone.
public static class CupZoneSocket
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
        SetupAll();
    }

    static void OnLoaded(Scene s, LoadSceneMode m) => SetupAll();

    static void SetupAll()
    {
        foreach (var m in Object.FindObjectsByType<TransmutatorMachine>(FindObjectsSortMode.None))
            Setup(m);
    }

    /// El socket del vaso de una máquina (null si no tiene CupZone)
    public static FilteredSocket Find(TransmutatorMachine m)
    {
        if (m == null || m.cupZone == null) return null;
        foreach (var s in m.cupZone.GetComponentsInChildren<FilteredSocket>(true))
            if (s.filter == SocketFilter.CupOnly) return s;
        return null;
    }

    static void Setup(TransmutatorMachine m)
    {
        if (m == null || m.cupZone == null || Find(m) != null) return;
        var zone = m.cupZone;

        // Objeto apagado mientras se configura (así el socket arranca ya con su filtro y su punto)
        var go = new GameObject("CupSocket (auto)");
        go.SetActive(false);
        go.transform.SetParent(zone.transform, false);
        go.layer = zone.gameObject.layer;

        // Zona de detección: la misma forma de la CupZone
        if (zone is BoxCollider box)
        {
            var c = go.AddComponent<BoxCollider>();
            c.center = box.center; c.size = box.size; c.isTrigger = true;
        }
        else if (zone is SphereCollider sphere)
        {
            var c = go.AddComponent<SphereCollider>();
            c.center = sphere.center; c.radius = sphere.radius; c.isTrigger = true;
        }
        else
        {
            var c = go.AddComponent<SphereCollider>();
            c.radius = 0.15f; c.isTrigger = true;
        }

        // Punto donde queda la base del vaso: debajo del pico, sobre la superficie
        var attach = new GameObject("Punto del vaso").transform;
        attach.SetParent(go.transform, false);
        attach.SetPositionAndRotation(BasePoint(m, zone), FlatRotation(m.transform));

        var socket = go.AddComponent<FilteredSocket>();
        socket.filter = SocketFilter.CupOnly;
        socket.attachTransform = attach;
        socket.showInteractableHoverMeshes = false;      // sin la vista previa fantasma del vaso
        socket.recycleDelayTime = 0.6f;                  // al sacarlo con la mano no lo vuelve a tomar al instante

        go.SetActive(true);
        Debug.Log($"[CupZoneSocket] Socket del vaso listo en {m.name}: la base del vaso queda en {attach.position:F2}.", go);
    }

    // Centro de la base del vaso: en X/Z el pico (si cae dentro de la zona) y en Y la superficie de apoyo
    static Vector3 BasePoint(TransmutatorMachine m, Collider zone)
    {
        var zb = zone.bounds;
        Vector3 top = zb.center;
        if (m.spout != null)
        {
            var sp = m.spout.position;
            bool inside = sp.x >= zb.min.x && sp.x <= zb.max.x && sp.z >= zb.min.z && sp.z <= zb.max.z;
            if (inside) top = new Vector3(sp.x, Mathf.Max(sp.y, zb.max.y), sp.z);
            else top = new Vector3(zb.center.x, zb.max.y, zb.center.z);
        }

        // Superficie: lo primero sólido y fijo debajo (mesa o base de la máquina), sin contar vasos ni piezas sueltas
        float floorY = zb.min.y;
        float best = float.MaxValue;
        foreach (var h in Physics.RaycastAll(top + Vector3.up * 0.01f, Vector3.down, (top.y - zb.min.y) + 0.6f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.collider.GetComponentInParent<SampleItem>() != null) continue;
            if (h.rigidbody != null && !h.rigidbody.isKinematic) continue;
            if (h.point.y > zb.max.y || h.point.y < zb.min.y - 0.3f) continue;   // fuera de la zona del vaso (p. ej. el propio pico)
            if (h.distance < best) { best = h.distance; floorY = h.point.y; }
        }
        return new Vector3(top.x, floorY + 0.002f, top.z);
    }

    static Quaternion FlatRotation(Transform t)
    {
        var f = Vector3.ProjectOnPlane(t.forward, Vector3.up);
        return f.sqrMagnitude < 1e-4f ? Quaternion.identity : Quaternion.LookRotation(f.normalized, Vector3.up);
    }
}

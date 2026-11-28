using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

/// Diagnóstico de la escena (menú Molecula). Solo escribe un informe en la consola; no cambia nada.
///  - "Colisionadores en la puerta": todo collider (activo, apagado, trigger o sólido, incluidos los que se crean
///    al dar Play) que toca el marco de la puerta automática, con su ruta completa y si se ve (renderer) o no.
///    Un collider sólido SIN renderer es una "pared invisible": no se puede seleccionar con clic en la escena.
///  - "Objetos flotando": cuerpos con física que están dormidos o congelados sin nada debajo.
public static class DiagnosticoEscena
{
#if UNITY_EDITOR
    [UnityEditor.MenuItem("Molecula/Diagnóstico: colisionadores en la puerta")]
    static void MenuPuerta() => Puerta();

    [UnityEditor.MenuItem("Molecula/Diagnóstico: objetos flotando")]
    static void MenuFlotando() => Flotando();

    /// La entrada de la transmutadora (su PRIMER BoxCollider) tiene que ser Trigger: así la máquina detecta la
    /// molécula que entra. Si se desmarcó (p. ej. al revertir cambios del prefab), la máquina deja de tomar moléculas.
    [UnityEditor.MenuItem("Molecula/Corregir: entrada de la transmutadora como trigger")]
    static void MenuCorregirEntrada()
    {
        if (Application.isPlaying) { Debug.LogWarning("[Corrección] Sal de Play primero: los cambios en Play no se guardan."); return; }
        int n = 0;
        foreach (var m in Object.FindObjectsByType<TransmutatorMachine>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var box = m.GetComponent<BoxCollider>();                 // el primero en el Inspector
            if (box == null) { Debug.LogWarning($"[Corrección] {m.name} no tiene BoxCollider de entrada.", m); continue; }
            if (box.isTrigger && box.enabled) { Debug.Log($"[Corrección] La entrada de {m.name} ya era trigger.", m); continue; }
            UnityEditor.Undo.RecordObject(box, "Entrada de la transmutadora como trigger");
            box.isTrigger = true;
            box.enabled = true;
            UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(box);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(m.gameObject.scene);
            n++;
            Debug.Log($"[Corrección] Entrada de {m.name} marcada como trigger (tamaño {box.size}). Guarda la escena (Ctrl+S).", m);
        }
        if (n == 0) Debug.Log("[Corrección] No había nada que corregir en la entrada de la transmutadora.");
    }

    /// Apaga los BoxCollider sólidos que encierran un volumen grande (más de 2,5 m en los tres ejes).
    /// Un BoxCollider puesto sobre el modelo completo del cuarto (p. ej. "floor_and_walls") toma el tamaño de
    /// toda la habitación: por dentro empuja hacia arriba lo que sube (objetos flotando) y su cara coincide con la
    /// puerta (pared invisible). Los pisos, paredes delgadas y triggers no se tocan. Se puede deshacer con Ctrl+Z.
    [UnityEditor.MenuItem("Molecula/Corregir: volúmenes invisibles que bloquean")]
    static void MenuCorregirVolumenes()
    {
        if (Application.isPlaying) { Debug.LogWarning("[Corrección] Sal de Play primero: los cambios en Play no se guardan."); return; }
        var sb = new StringBuilder("[Corrección] VOLÚMENES INVISIBLES\n");
        int n = 0;
        foreach (var box in Object.FindObjectsByType<BoxCollider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (box == null || !box.enabled || box.isTrigger || box.attachedRigidbody != null) continue;
            var s = box.bounds.size;
            if (s.x < 2.5f || s.y < 2.5f || s.z < 2.5f) continue;
            UnityEditor.Undo.RecordObject(box, "Apagar volumen invisible");
            box.enabled = false;
            UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(box);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(box.gameObject.scene);
            n++;
            sb.AppendLine($"  Apagado: {Path(box.transform)} (tamaño {s:F2})");
        }
        sb.AppendLine(n == 0 ? "  No había ninguno." : $"  Total: {n}. Guarda la escena (Ctrl+S) para conservarlo.");
        Debug.Log(sb.ToString());
    }
#endif

    public static void Puerta()
    {
        var leaves = Object.FindObjectsByType<PuertaVR>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (leaves.Length == 0) { Debug.LogWarning("[Diagnóstico] No hay puertas (PuertaVR) en la escena."); return; }

        var sb = new StringBuilder("[Diagnóstico] COLISIONADORES EN LA PUERTA\n");
        foreach (var grp in leaves.GroupBy(l => l.transform.parent))
        {
            Bounds door = default; bool any = false;
            foreach (var l in grp)
            {
                var r = l.GetComponent<Renderer>();
                var b = r != null ? r.bounds : new Bounds(l.transform.position, Vector3.one);
                if (!any) { door = b; any = true; } else door.Encapsulate(b);
                sb.AppendLine($"  Hoja {Path(l.transform)}  activa={l.gameObject.activeInHierarchy}  abierta={l.IsOpen}  pos={l.transform.position:F2}");
            }
            // Zona de paso: el marco + 1 m a cada lado de la puerta
            var zone = door;
            zone.Expand(new Vector3(door.size.x < door.size.z ? 2f : 0.6f, 0.2f, door.size.x < door.size.z ? 0.6f : 2f));
            sb.AppendLine($"  Zona revisada: centro {zone.center:F2} tamaño {zone.size:F2}");

            var found = new List<Collider>();
            foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (c != null && c.bounds.Intersects(zone)) found.Add(c);
            foreach (var c in found.OrderBy(c => c.isTrigger).ThenBy(c => Path(c.transform)))
            {
                bool live = c.enabled && c.gameObject.activeInHierarchy;
                var rend = c.GetComponent<Renderer>();
                bool visible = rend != null && rend.enabled && rend.gameObject.activeInHierarchy;
                string tag = !live ? "apagado" : c.isTrigger ? "TRIGGER" : visible ? "sólido" : "SÓLIDO INVISIBLE";
                string rb = c.attachedRigidbody != null ? $" rb={(c.attachedRigidbody.isKinematic ? "cinemático" : "dinámico")}" : "";
                sb.AppendLine($"   - [{tag}] {Path(c.transform)} ({c.GetType().Name}){rb} capa={LayerMask.LayerToName(c.gameObject.layer)} centro={c.bounds.center:F2} tamaño={c.bounds.size:F2}");
            }
        }
        Debug.Log(sb.ToString());
    }

    public static void Flotando()
    {
        var sb = new StringBuilder("[Diagnóstico] OBJETOS FLOTANDO\n");
        int n = 0;
        foreach (var rb in Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
        {
            if (rb == null) continue;
            bool sleeping = Application.isPlaying && rb.IsSleeping();
            if (!sleeping && !rb.isKinematic) continue;
            var col = rb.GetComponentInChildren<Collider>();
            if (col == null) continue;
            var b = col.bounds;
            bool support = false;
            foreach (var h in Physics.RaycastAll(b.center, Vector3.down, b.extents.y + 0.05f, ~0, QueryTriggerInteraction.Ignore))
                if (h.rigidbody != rb) { support = true; break; }
            if (support) continue;
            n++;
            string why = rb.isKinematic ? "cinemático" : "dormido";
            var parent = rb.transform.parent != null ? $" hijo de {Path(rb.transform.parent)}" : "";
            sb.AppendLine($"  - {Path(rb.transform)} [{why}]{parent} y={rb.position.y:F2}");
        }
        sb.AppendLine(n == 0 ? "  Ninguno." : $"  Total: {n}");
        Debug.Log(sb.ToString());
    }

    static string Path(Transform t)
    {
        var s = t.name;
        while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }
}

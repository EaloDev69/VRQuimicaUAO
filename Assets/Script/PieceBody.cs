using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;   // XRI 2.x: borrar esta línea

/// Guarda la configuración física y de agarre de una pieza (átomo o barra) tal como viene en tu prefab.
/// Una pieza suelta tiene su propio Rigidbody + XRGrabInteractable (como siempre).
/// Al unirse a otra, pierde ambos (Strip) y pasa a formar parte del cuerpo único de la molécula;
/// al separarse, los recupera con los mismos valores (Restore). Se añade solo desde Atom y Bond.
public class PieceBody : MonoBehaviour
{
    public Rigidbody Body { get; private set; }
    public XRGrabInteractable Grab { get; private set; }
    public float Mass { get; private set; } = 0.05f;

    Action<Rigidbody> applyBody;
    Action<XRGrabInteractable> applyGrab;
    bool captured;

    public static PieceBody Ensure(GameObject go)
    {
        var pb = go.GetComponent<PieceBody>();
        return pb != null ? pb : go.AddComponent<PieceBody>();
    }

    void Awake() { Capture(); }

    // Copia los valores del prefab en funciones que luego se pueden volver a aplicar
    void Capture()
    {
        if (captured) return;
        captured = true;

        Body = GetComponent<Rigidbody>();
        Grab = GetComponent<XRGrabInteractable>();

        if (Body != null)
        {
            var mass = Body.mass;
            var linearDamping = Body.linearDamping;
            var angularDamping = Body.angularDamping;
            var gravity = Body.useGravity;
            var interpolation = Body.interpolation;
            var detection = Body.collisionDetectionMode;
            Mass = mass;
            applyBody = r =>
            {
                r.mass = mass;
                r.linearDamping = linearDamping;
                r.angularDamping = angularDamping;
                r.useGravity = gravity;
                r.interpolation = interpolation;
                r.collisionDetectionMode = detection;
            };
        }

        if (Grab != null)
        {
            var movement = Grab.movementType;
            var trackPosition = Grab.trackPosition;
            var trackRotation = Grab.trackRotation;
            var throwOnDetach = Grab.throwOnDetach;
            var dynamicAttach = Grab.useDynamicAttach;
            var layers = Grab.interactionLayers;
            var mode = Grab.selectMode;
            applyGrab = g =>
            {
                g.movementType = movement;
                g.trackPosition = trackPosition;
                g.trackRotation = trackRotation;
                g.throwOnDetach = throwOnDetach;
                g.useDynamicAttach = dynamicAttach;
                g.interactionLayers = layers;
                g.selectMode = mode;
            };
        }
    }

    public void ApplyBodySettings(Rigidbody r) { if (applyBody != null) applyBody(r); }
    public void ApplyGrabSettings(XRGrabInteractable g) { if (applyGrab != null) applyGrab(g); }

    /// La pieza deja de tener cuerpo propio (pasa a ser parte de una molécula)
    public void Strip()
    {
        Capture();
        if (Grab != null) { Grab.enabled = false; DestroyImmediate(Grab); }   // primero el agarre: depende del Rigidbody
        if (Body != null) DestroyImmediate(Body);
        Grab = null;
        Body = null;
    }

    /// La pieza vuelve a ser un objeto suelto, con su Rigidbody y su agarre originales
    public XRGrabInteractable Restore()
    {
        Capture();
        if (Body == null)
        {
            Body = gameObject.AddComponent<Rigidbody>();
            ApplyBodySettings(Body);
        }
        if (Grab == null)
        {
            Grab = gameObject.AddComponent<XRGrabInteractable>();
            ApplyGrabSettings(Grab);
        }
        return Grab;
    }
}

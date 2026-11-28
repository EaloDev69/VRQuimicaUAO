using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// Botón físico de laboratorio (Iniciar, Cancelar, Limpiar, Aceptar, spawners...).
/// Es un XRSimpleInteractable: funciona con Poke Interactor o con rayo/mano.
/// Conecta la acción en el inspector: onPressed -> TransmutatorMachine.StartReaction, etc.
public class LabButton : XRSimpleInteractable
{
    [Header("Botón")]
    public UnityEvent onPressed;
    public float cooldown = 0.4f;
    public bool interactableByDefault = true;

    [Header("Feedback")]
    public Transform visual;                 // la tapa del botón que se hunde
    public float pressDepth = 0.008f;
    public AudioSource clickSound;
    [Range(0, 1)] public float hapticAmplitude = 0.4f;
    public float hapticDuration = 0.08f;

    [Header("Poke (pulsar con la punta del dedo)")]
    [Tooltip("El botón se pulsa al tocarlo con el Poke Interactor de cualquier mano, sin necesitar XR Poke Filter")]
    public bool pressByPoke = true;
    [Tooltip("Distancia (m) entre la punta del dedo y el collider para contar como toque")]
    public float pokeDistance = 0.012f;

    /// Cualquier botón se pulsó (lo usa el audio 360)
    public static event System.Action<LabButton> AnyPressed;

    float nextAllowed;
    Vector3 visualRest;
    bool pokeInside;
    static XRPokeInteractor[] pokers = new XRPokeInteractor[0];
    static float nextPokerSearch;

    protected override void Awake()
    {
        // Entradas vacías en la lista Colliders impiden que XRI busque los colliders solos: se limpian
        colliders.RemoveAll(c => c == null);
        base.Awake();
        if (visual != null) visualRest = visual.localPosition;
        enabled = interactableByDefault;
    }

    protected override void OnSelectEntered(SelectEnterEventArgs args)
    {
        base.OnSelectEntered(args);
        var input = args.interactorObject.transform.GetComponentInParent<XRBaseInputInteractor>();
        DoPress(input);
    }

    void DoPress(XRBaseInputInteractor hapticTarget)
    {
        if (!enabled || Time.time < nextAllowed) return;
        nextAllowed = Time.time + cooldown;

        if (hapticTarget != null) hapticTarget.SendHapticImpulse(hapticAmplitude, hapticDuration);
        if (clickSound != null) clickSound.Play();
        if (visual != null) StartCoroutine(Press());

        onPressed.Invoke();
        AnyPressed?.Invoke(this);
    }

    /// Para conectar desde otros scripts o UnityEvents
    public void PressFromCode() => DoPress(null);

    // Poke sin filtro: se mira si la punta de algún Poke Interactor toca el collider del botón
    void Update()
    {
        if (!pressByPoke) return;
        if (Time.time >= nextPokerSearch)
        {
            nextPokerSearch = Time.time + 2f;
            pokers = FindObjectsByType<XRPokeInteractor>(FindObjectsSortMode.None);
        }

        bool touching = false;
        XRPokeInteractor who = null;
        foreach (var p in pokers)
        {
            if (p == null || !p.isActiveAndEnabled) continue;
            Vector3 tip = p.attachTransform != null ? p.attachTransform.position : p.transform.position;
            foreach (var c in colliders)
            {
                if (c == null || !c.enabled) continue;
                if ((c.ClosestPoint(tip) - tip).sqrMagnitude <= pokeDistance * pokeDistance) { touching = true; who = p; break; }
            }
            if (touching) break;
        }

        if (touching && !pokeInside)
        {
            var haptic = who != null ? who.GetComponentInParent<XRBaseInputInteractor>() : null;
            if (haptic == null && who != null)
            {
                // el Poke no es un input interactor: busca el de la misma mano para vibrar
                var hand = who.transform.parent;
                if (hand != null) haptic = hand.GetComponentInChildren<XRBaseInputInteractor>();
            }
            DoPress(haptic);
        }
        pokeInside = touching;
    }

    IEnumerator Press()
    {
        visual.localPosition = visualRest + Vector3.down * pressDepth;
        yield return new WaitForSeconds(0.12f);
        visual.localPosition = visualRest;
    }

    /// Para bloquear un botón (ej. Iniciar mientras la máquina trabaja)
    public void SetUsable(bool usable) => enabled = usable;
}
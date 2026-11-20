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

    float nextAllowed;
    Vector3 visualRest;

    protected override void Awake()
    {
        base.Awake();
        if (visual != null) visualRest = visual.localPosition;
        enabled = interactableByDefault;
    }

    protected override void OnSelectEntered(SelectEnterEventArgs args)
    {
        base.OnSelectEntered(args);
        if (Time.time < nextAllowed) return;
        nextAllowed = Time.time + cooldown;

        var input = args.interactorObject.transform.GetComponentInParent<XRBaseInputInteractor>();
        if (input != null) input.SendHapticImpulse(hapticAmplitude, hapticDuration);
        if (clickSound != null) clickSound.Play();
        if (visual != null) StartCoroutine(Press());

        onPressed.Invoke();
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
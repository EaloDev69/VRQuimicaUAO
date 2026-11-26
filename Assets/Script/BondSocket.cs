using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables; // XRI 2.x: borrar esta línea
using UnityEngine.XR.Interaction.Toolkit.Interactors;   // XRI 2.x: borrar esta línea

public class BondSocket : XRSocketInteractor
{
    public Atom Owner { get; private set; }
    public Atom Held { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        Owner = GetComponentInParent<Atom>();
    }

    bool IsValidAtom(Transform t, out Atom atom)
    {
        atom = t.GetComponent<Atom>();
        if (atom == null || Owner == null) return false;
        if (atom.HeldBy == this) return true;             // ya lo tengo
        if (atom == Owner) return false;
        if (atom.HeldBy != null) return false;            // ya está enlazado como "hijo"
        if (atom.Root == Owner.Root) return false;        // evita ciclos
        return Owner.HasFreeValence && atom.HasFreeValence;
    }

    public override bool CanHover(IXRHoverInteractable interactable)
        => base.CanHover(interactable) && IsValidAtom(interactable.transform, out _);

    public override bool CanSelect(IXRSelectInteractable interactable)
        => base.CanSelect(interactable) && IsValidAtom(interactable.transform, out _);

    protected override void OnSelectEntered(SelectEnterEventArgs args)
    {
        base.OnSelectEntered(args);
        Held = args.interactableObject.transform.GetComponent<Atom>();
        if (Held != null) { Held.HeldBy = this; Held.NotifyBondsChanged(); }
        Owner.NotifyBondsChanged();
    }

    protected override void OnSelectExited(SelectExitEventArgs args)
    {
        base.OnSelectExited(args);
        var old = Held;
        Held = null;
        if (old != null && old.HeldBy == this) { old.HeldBy = null; old.NotifyBondsChanged(); }
        Owner.NotifyBondsChanged();
    }
}

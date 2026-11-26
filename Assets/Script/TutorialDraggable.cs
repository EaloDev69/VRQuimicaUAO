using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// Pieza de UI que se arrastra con el puntero (mouse, gaze o el dedo/poke de XR).
/// Al soltarla pregunta a DropHandler si cayó en un lugar válido: si devuelve true se queda donde la dejó
/// el controlador (SnapTo); si no, vuelve a donde estaba.
/// Funciona en cualquier Canvas (Overlay, Camera o World Space).
public class TutorialDraggable : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public string id;                                        // tipo de pieza: "atom", "bar", "jar"...
    public bool locked;                                      // ya colocada: no se puede volver a mover
    public Func<TutorialDraggable, bool> DropHandler;
    public event Action<TutorialDraggable> Grabbed;

    RectTransform rt, parentRt;
    Vector2 startPos;
    Vector3 grabOffset;
    bool dragging;
    Coroutine back;

    void Awake() { rt = (RectTransform)transform; }

    public void OnBeginDrag(PointerEventData e)
    {
        if (locked) return;
        if (rt == null) rt = (RectTransform)transform;
        parentRt = rt.parent as RectTransform;
        if (parentRt == null) return;

        if (back != null) { StopCoroutine(back); back = null; }
        dragging = true;
        startPos = rt.anchoredPosition;
        rt.SetAsLastSibling();                               // se dibuja encima de todo mientras se arrastra

        // Distancia entre el puntero y el centro de la pieza, para que no "salte" al agarrarla
        grabOffset = Vector3.zero;
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(parentRt, e.position, e.pressEventCamera, out var wp))
            grabOffset = rt.position - wp;

        Grabbed?.Invoke(this);
    }

    public void OnDrag(PointerEventData e)
    {
        if (!dragging) return;
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(parentRt, e.position, e.pressEventCamera, out var wp))
            rt.position = wp + grabOffset;
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (!dragging) return;
        dragging = false;
        bool accepted = DropHandler != null && DropHandler(this);
        if (!accepted) back = StartCoroutine(ReturnRoutine());
    }

    /// El controlador la coloca en su sitio y la bloquea
    public void SnapTo(Vector2 anchoredPos)
    {
        if (back != null) { StopCoroutine(back); back = null; }
        if (rt == null) rt = (RectTransform)transform;
        rt.anchoredPosition = anchoredPos;
        locked = true;
    }

    IEnumerator ReturnRoutine()
    {
        const float duration = 0.15f;
        Vector2 from = rt.anchoredPosition;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            rt.anchoredPosition = Vector2.Lerp(from, startPos, Mathf.Clamp01(t / duration));
            yield return null;
        }
        rt.anchoredPosition = startPos;
        back = null;
    }
}

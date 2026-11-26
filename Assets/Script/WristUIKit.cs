using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// Ayudas compartidas para construir la interfaz de la muñeca (menú, tutorial y glosario).
public static class WristUIKit
{
    public static readonly Color PanelColor = new Color(0.08f, 0.10f, 0.14f, 0.94f);
    public static readonly Color ButtonColor = new Color(0.20f, 0.45f, 0.75f, 1f);
    public static readonly Color BackButtonColor = new Color(0.32f, 0.35f, 0.42f, 1f);

    public static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    /// Estira el rectángulo entre dos esquinas (fracciones 0-1 del padre)
    public static void Anchor(RectTransform rt, float x0, float y0, float x1, float y1)
    {
        rt.anchorMin = new Vector2(x0, y0);
        rt.anchorMax = new Vector2(x1, y1);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// Rectángulo con tamaño fijo, centrado en el padre y movido a una posición (en unidades del Canvas)
    public static void Center(RectTransform rt, Vector2 size, Vector2 pos)
    {
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }

    public static Button Place(Button b, float x0, float y0, float x1, float y1)
    {
        Anchor((RectTransform)b.transform, x0, y0, x1, y1);
        return b;
    }

    public static GameObject MakePage(string name, Transform parent)
    {
        var rt = NewRect(name, parent);
        Anchor(rt, 0, 0, 1, 1);
        rt.gameObject.AddComponent<Image>().color = PanelColor;
        return rt.gameObject;
    }

    public static TMP_Text MakeText(Transform parent, string name, string text, float size,
                                    TextAlignmentOptions align, float fontScale)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size * fontScale;
        t.alignment = align;
        t.color = Color.white;
        t.raycastTarget = false;
        return t;
    }

    public static TMP_Text Label(Transform parent, string name, string text, float size, TextAlignmentOptions align,
                                 float fontScale, float x0, float y0, float x1, float y1)
    {
        var t = MakeText(parent, name, text, size, align, fontScale);
        Anchor(t.rectTransform, x0, y0, x1, y1);
        return t;
    }

    public static Button MakeButton(Transform parent, string name, string label, Color color, float fontSize, float fontScale)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        var img = go.GetComponent<Image>();
        img.color = color;
        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;

        // Con gaze el resaltado indica qué estás mirando: normal un poco apagado, resaltado a pleno color
        var cb = btn.colors;
        cb.normalColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        cb.highlightedColor = Color.white;
        cb.pressedColor = new Color(0.6f, 0.6f, 0.6f, 1f);
        cb.selectedColor = cb.normalColor;                    // tras pulsar no se queda resaltado
        cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.7f);
        cb.colorMultiplier = 1f;
        btn.colors = cb;

        var t = MakeText(go.transform, "Texto", label, fontSize, TextAlignmentOptions.Center, fontScale);
        Anchor(t.rectTransform, 0, 0, 1, 1);
        t.rectTransform.offsetMin = new Vector2(6, 4);
        t.rectTransform.offsetMax = new Vector2(-6, -4);
        return btn;
    }
}

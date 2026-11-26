using System.Collections;
using UnityEngine;

/// Cubo de residuo: aparece con un "pop" (crece con un pequeño rebote) y, pasado su tiempo,
/// se encoge y desaparece. Ponlo en los prefabs de los cubos de fallo.
/// Si usas este script, pon "Junk Lifetime" = 0 en la máquina: el tiempo lo controla el propio cubo.
public class JunkCube : MonoBehaviour
{
    [Header("Aparición")]
    public float popDuration = 0.3f;
    public float overshoot = 1.7f;           // cuánto se pasa de tamaño antes de asentarse

    [Header("Desaparición")]
    [Tooltip("Segundos que dura el cubo. 0 = no desaparece")]
    public float lifetime = 30f;
    public float shrinkDuration = 1f;

    const float MinScale = 0.01f;            // nunca escala 0: evita problemas con el collider
    Vector3 fullScale;

    void Awake()
    {
        fullScale = transform.localScale;
        transform.localScale = fullScale * MinScale;     // empieza pequeño para que no se vea un destello
    }

    IEnumerator Start()
    {
        // Pop
        for (float t = 0f; t < popDuration; t += Time.deltaTime)
        {
            transform.localScale = fullScale * Mathf.Max(MinScale, EaseOutBack(t / popDuration));
            yield return null;
        }
        transform.localScale = fullScale;

        if (lifetime <= 0f) yield break;

        // Espera y se encoge
        float wait = Mathf.Max(0f, lifetime - popDuration - shrinkDuration);
        yield return new WaitForSeconds(wait);

        for (float t = 0f; t < shrinkDuration; t += Time.deltaTime)
        {
            transform.localScale = fullScale * Mathf.Max(MinScale, 1f - t / shrinkDuration);
            yield return null;
        }

#if UNITY_EDITOR
        // Si está seleccionado en el editor, se deselecciona antes de destruirlo (evita errores del Inspector)
        if (UnityEditor.Selection.activeGameObject == gameObject) UnityEditor.Selection.activeGameObject = null;
#endif
        Destroy(gameObject);
    }

    float EaseOutBack(float x)
    {
        x = Mathf.Clamp01(x);
        float c1 = overshoot, c3 = c1 + 1f;
        float p = x - 1f;
        return 1f + c3 * p * p * p + c1 * p * p;
    }
}
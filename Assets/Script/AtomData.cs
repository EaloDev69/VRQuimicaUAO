using UnityEngine;

[CreateAssetMenu(menuName = "Molecula/AtomData")]
public class AtomData : ScriptableObject
{
    public string elementName;   // "Hidrógeno" (para el glosario)
    public string symbol;
    [Range(1, 4)] public int valence = 1;
    public Color color = Color.white;
}
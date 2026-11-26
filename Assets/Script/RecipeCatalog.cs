using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// Catálogo único de recetas del juego. Lo comparten la Transmutadora, el OrderManager,
/// la Evaluadora y (luego) el glosario de la muñeca. Se llena con el menú
/// Molecula > Crear recetas y catálogo (GDD), o a mano.
[CreateAssetMenu(menuName = "Molecula/RecipeCatalog")]
public class RecipeCatalog : ScriptableObject
{
    public RecipeData[] recipes;

    public IEnumerable<RecipeData> ForLevel(int level)
        => recipes.Where(r => r != null && r.level == level);

    public RecipeData Find(string formula)
        => recipes.FirstOrDefault(r => r != null && r.formula == formula);

    public int MaxLevel
        => recipes.Where(r => r != null).Select(r => r.level).DefaultIfEmpty(1).Max();
}

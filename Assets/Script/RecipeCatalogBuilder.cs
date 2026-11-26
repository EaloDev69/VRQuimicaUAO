using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// Menú: Molecula > Crear recetas y catálogo (GDD)
/// Crea (o actualiza) los 10 AtomData, las 8 recetas del GDD y el RecipeCatalog.
/// Es seguro repetirlo: si un asset ya existe lo reutiliza. No toca tu Effect Prefab ni los AtomData que ya tengas.
/// Poner este archivo en una carpeta llamada "Editor".
public static class RecipeCatalogBuilder
{
    const string DataDir = "Assets/Data";
    const string AtomsDir = "Assets/Data/Atoms";
    const string RecipesDir = "Assets/Data/Recipes";
    const string CatalogPath = "Assets/Data/RecipeCatalog.asset";

    struct AtomDef { public string symbol; public int valence; public string hex; }

    // GDD 4.1: 10 elementos, valencia y color (CPK simplificado)
    static readonly AtomDef[] AtomDefs =
    {
        new AtomDef { symbol = "H",  valence = 1, hex = "#FFFFFF" },
        new AtomDef { symbol = "O",  valence = 2, hex = "#E53935" },
        new AtomDef { symbol = "C",  valence = 4, hex = "#4A4A4A" },
        new AtomDef { symbol = "N",  valence = 3, hex = "#2F5BD3" },
        new AtomDef { symbol = "Cl", valence = 1, hex = "#1B7F3B" },
        new AtomDef { symbol = "Si", valence = 4, hex = "#D9B55F" },
        new AtomDef { symbol = "F",  valence = 1, hex = "#A8E64A" },
        new AtomDef { symbol = "Br", valence = 1, hex = "#8B3A2E" },
        new AtomDef { symbol = "S",  valence = 2, hex = "#F2D027" },
        new AtomDef { symbol = "P",  valence = 3, hex = "#F28C28" },
    };

    struct RecipeDef
    {
        public string file, name, formula;
        public int level;
        public CompoundCategory category;
        public string[] atoms;      // un símbolo por átomo; el orden define los índices
        public int[] bonds;         // pares a,b,a,b... (índices de "atoms")
        public int points;
        public float time;
        public string hex, fact;
    }

    // GDD 5.1 (versión del 17/11/2026: 8 recetas, solo enlaces simples, 2 niveles)
    static readonly RecipeDef[] RecipeDefs =
    {
        new RecipeDef { file="HCl", name="Cloruro de hidrógeno", formula="HCl", level=1, category=CompoundCategory.Gas,
            atoms=new[]{"Cl","H"}, bonds=new[]{0,1}, points=100, time=120, hex="#C8E6C9",
            fact="El cloro y el hidrógeno tienen una sola conexión cada uno, por eso se unen con una barra." },
        new RecipeDef { file="H2O", name="Agua", formula="H₂O", level=1, category=CompoundCategory.Liquid,
            atoms=new[]{"O","H","H"}, bonds=new[]{0,1, 0,2}, points=100, time=120, hex="#64B5F6",
            fact="El oxígeno tiene dos conexiones, por eso une exactamente dos hidrógenos." },
        new RecipeDef { file="NH3", name="Amoníaco", formula="NH₃", level=1, category=CompoundCategory.Gas,
            atoms=new[]{"N","H","H","H"}, bonds=new[]{0,1, 0,2, 0,3}, points=100, time=120, hex="#BA68C8",
            fact="El nitrógeno forma tres enlaces, uno con cada hidrógeno." },
        new RecipeDef { file="CH4", name="Metano", formula="CH₄", level=1, category=CompoundCategory.Gas,
            atoms=new[]{"C","H","H","H","H"}, bonds=new[]{0,1, 0,2, 0,3, 0,4}, points=100, time=120, hex="#FFB74D",
            fact="El carbono forma cuatro enlaces, el máximo que pueden tener los átomos del juego." },

        new RecipeDef { file="SiH4", name="Silano", formula="SiH₄", level=2, category=CompoundCategory.Gas,
            atoms=new[]{"Si","H","H","H","H"}, bonds=new[]{0,1, 0,2, 0,3, 0,4}, points=150, time=150, hex="#FFF176",
            fact="El silicio, igual que el carbono, forma cuatro enlaces." },
        new RecipeDef { file="H2O2", name="Peróxido de hidrógeno", formula="H₂O₂", level=2, category=CompoundCategory.Liquid,
            atoms=new[]{"O","O","H","H"}, bonds=new[]{0,1, 0,2, 1,3}, points=150, time=150, hex="#B2EBF2",
            fact="Dos oxígenos pueden unirse entre sí; cada uno completa su segunda conexión con un hidrógeno." },
        new RecipeDef { file="CH3Cl", name="Clorometano", formula="CH₃Cl", level=2, category=CompoundCategory.Gas,
            atoms=new[]{"C","Cl","H","H","H"}, bonds=new[]{0,1, 0,2, 0,3, 0,4}, points=150, time=150, hex="#80DEEA",
            fact="Un mismo carbono puede unir átomos distintos: tres hidrógenos y un cloro." },
        new RecipeDef { file="C2H6O", name="Etanol", formula="C₂H₆O", level=2, category=CompoundCategory.Liquid,
            atoms=new[]{"C","C","O","H","H","H","H","H","H"}, bonds=new[]{0,1, 1,2, 2,3, 0,4, 0,5, 0,6, 1,7, 1,8},
            points=150, time=210, hex="#4FC3F7",
            fact="El etanol es una cadena de dos carbonos con un oxígeno que lleva su propio hidrógeno." },
    };

<<<<<<< HEAD
    // Presión y temperatura de cada receta (propuesta de balance; se ajusta en las pruebas)
    static readonly Dictionary<string, (LeverLevel pressure, LeverLevel temperature)> Levers =
        new Dictionary<string, (LeverLevel pressure, LeverLevel temperature)>
    {
        { "HCl",   (LeverLevel.Low,    LeverLevel.Low)    },
        { "H2O",   (LeverLevel.Medium, LeverLevel.Medium) },
        { "NH3",   (LeverLevel.High,   LeverLevel.Medium) },
        { "CH4",   (LeverLevel.Medium, LeverLevel.High)   },
        { "SiH4",  (LeverLevel.Low,    LeverLevel.High)   },
        { "H2O2",  (LeverLevel.Low,    LeverLevel.Medium) },
        { "CH3Cl", (LeverLevel.High,   LeverLevel.Low)    },
        { "C2H6O", (LeverLevel.Medium, LeverLevel.Low)    },
    };

=======
>>>>>>> 3f517e58c67353504dd60bb8d6a87b6eeba73bd3
    [MenuItem("Molecula/Crear recetas y catálogo (GDD)")]
    public static void Build()
    {
        EnsureFolder("Assets", "Data");
        EnsureFolder(DataDir, "Atoms");
        EnsureFolder(DataDir, "Recipes");

        // 1) AtomData: reutiliza los existentes (por símbolo) y crea los que falten
        var atoms = new Dictionary<string, AtomData>();
        foreach (var guid in AssetDatabase.FindAssets("t:AtomData"))
        {
            var a = AssetDatabase.LoadAssetAtPath<AtomData>(AssetDatabase.GUIDToAssetPath(guid));
            if (a != null && !string.IsNullOrEmpty(a.symbol) && !atoms.ContainsKey(a.symbol)) atoms[a.symbol] = a;
        }
        foreach (var d in AtomDefs)
        {
            if (atoms.ContainsKey(d.symbol)) continue;
            var a = ScriptableObject.CreateInstance<AtomData>();
            a.symbol = d.symbol; a.valence = d.valence; a.color = Hex(d.hex);
            AssetDatabase.CreateAsset(a, $"{AtomsDir}/AtomData_{d.symbol}.asset");
            atoms[d.symbol] = a;
        }

        // 2) RecipeData: reutiliza por fórmula; crea las que falten; actualiza los datos del GDD
        var existing = new Dictionary<string, RecipeData>();
        foreach (var guid in AssetDatabase.FindAssets("t:RecipeData"))
        {
            var r = AssetDatabase.LoadAssetAtPath<RecipeData>(AssetDatabase.GUIDToAssetPath(guid));
            if (r != null && !string.IsNullOrEmpty(r.formula) && !existing.ContainsKey(r.formula)) existing[r.formula] = r;
        }

        var list = new List<RecipeData>();
        foreach (var def in RecipeDefs)
        {
            if (!existing.TryGetValue(def.formula, out var r))
            {
                r = ScriptableObject.CreateInstance<RecipeData>();
                AssetDatabase.CreateAsset(r, $"{RecipesDir}/Recipe_{def.file}.asset");
            }

            r.displayName = def.name;
            r.formula = def.formula;
            r.level = def.level;
            r.category = def.category;
            r.atoms = def.atoms.Select(s => atoms[s]).ToArray();
            r.bonds = Enumerable.Range(0, def.bonds.Length / 2)
                                .Select(i => new RecipeData.BondDef { a = def.bonds[i * 2], b = def.bonds[i * 2 + 1] })
                                .ToArray();
            r.basePoints = def.points;
            r.timeLimit = def.time;
<<<<<<< HEAD
            if (Levers.TryGetValue(def.file, out var lv)) { r.requiredPressure = lv.pressure; r.requiredTemperature = lv.temperature; }
=======
>>>>>>> 3f517e58c67353504dd60bb8d6a87b6eeba73bd3
            r.sampleColor = Hex(def.hex);
            r.educationalFact = def.fact;       // el Effect Prefab no se toca
            r.InvalidateCache();
            EditorUtility.SetDirty(r);
            list.Add(r);
        }

        // 3) Catálogo
        var catalog = AssetDatabase.LoadAssetAtPath<RecipeCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<RecipeCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }
        catalog.recipes = list.ToArray();
        EditorUtility.SetDirty(catalog);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 4) Comprobación: valencias cerradas y molécula conectada
        int bad = 0;
        foreach (var r in list)
        {
            var g = r.Graph;
            if (g.OpenValences != 0 || !g.IsConnected())
            {
                bad++;
                Debug.LogError($"[Catálogo] {r.formula}: valencias abiertas = {g.OpenValences}, conectada = {g.IsConnected()}", r);
            }
        }
        Debug.Log(bad == 0
            ? $"[Catálogo] Listo: {list.Count} recetas válidas en {CatalogPath}"
            : $"[Catálogo] Hecho, pero {bad} receta(s) con errores (ver consola)", catalog);

        EditorGUIUtility.PingObject(catalog);
        Selection.activeObject = catalog;
    }

    static Color Hex(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

    static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder($"{parent}/{name}")) AssetDatabase.CreateFolder(parent, name);
    }
}

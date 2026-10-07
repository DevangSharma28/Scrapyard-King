using System.Text;
using ScrapYardKing.EditorTools.Art;
using ScrapYardKing.Harvest;
using ScrapYardKing.Items;
using UnityEditor;
using UnityEngine;

// Revamp 4 data: what the Heavy Scrap Yard's wrecks are and what they give.
//  - The Excavator becomes its own scrap type (it was a look of the Tractor): the yard's toughest regular wreck.
//  - Every heavy type drops one of the four metals as its rare drop, in the colours the player already knows:
//    tractor → iron, truck → copper, garbage truck / mixer → steel, excavator → aluminum.
// The yard's layout (bays, lanes, quick targets) is Env_Build's; its crane and belt are Crane_Build's.
// Entry point: Assets. Run before Env_Build.All (the bays use the Excavator). Idempotent.
public static class R4_Build
{
    const string DataDir = "Assets/_Project/Data";
    const string PrefabDir = "Assets/_Project/Prefabs";

    public static string Assets()
    {
        var log = new StringBuilder();
        string path = DataDir + "/Scrap/Scrap_Excavator.asset";
        if (AssetDatabase.LoadAssetAtPath<ScrapDefinition>(path) == null) AssetDatabase.CopyAsset(DataDir + "/Scrap/Scrap_GarbageTruck.asset", path);
        var excavator = AssetDatabase.LoadAssetAtPath<ScrapDefinition>(path);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Scrap/Scrap_Tractor_tractor-shovel.prefab");
        var tractor = Def("Tractor");
        var tractorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Scrap/Scrap_Tractor_tractor.prefab");

        // Excavator: needs the best cutter but one, takes the longest of the regular wrecks and bursts into the most pieces.
        ArtAssets.Set(excavator, ("id", "excavator"), ("displayName", "Excavator"), ("tier", 4), ("maxHealth", 2000f), ("minCutPower", 45f), ("xpReward", 70), ("respawnDelay", 22f));
        Range(excavator, "dropAmount", 80, 95);
        if (prefab != null)
        {
            var scrap = prefab.GetComponent<ScrapObject>();
            ArtAssets.Set(scrap, ("definition", excavator));
            EditorUtility.SetDirty(prefab);
            ArtAssets.Set(excavator, ("prefab", scrap));
            ArtAssets.SetArray(excavator, "prefabVariants", new Object[] { scrap });
        }
        else log.AppendLine("!! excavator prefab missing");

        if (tractorPrefab != null)
        {
            var scrap = tractorPrefab.GetComponent<ScrapObject>();
            ArtAssets.Set(tractor, ("prefab", scrap));
            ArtAssets.SetArray(tractor, "prefabVariants", new Object[] { scrap });
        }

        Rare(tractor, "Iron", 0.6f, 3, 5);
        Rare(Def("Truck"), "Copper", 0.6f, 3, 6);
        Rare(Def("GarbageTruck"), "Steel", 0.6f, 3, 6);
        Rare(excavator, "Aluminum", 1f, 6, 10);
        AssetDatabase.SaveAssets();
        log.AppendLine("excavator is its own scrap type; rare drops: tractor iron, truck copper, garbage truck steel, excavator aluminum");
        return log.ToString();
    }

    static ScrapDefinition Def(string name) => AssetDatabase.LoadAssetAtPath<ScrapDefinition>($"{DataDir}/Scrap/Scrap_{name}.asset");

    static void Rare(ScrapDefinition def, string item, float chance, int min, int max)
    {
        ArtAssets.Set(def, ("rareDropItem", AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{DataDir}/Items/Item_{item}.asset")), ("rareDropChance", chance));
        Range(def, "rareDropAmount", min, max);
    }

    static void Range(ScrapDefinition def, string field, int min, int max)
    {
        var so = new SerializedObject(def);
        so.FindProperty(field).vector2IntValue = new Vector2Int(min, max);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(def);
    }
}

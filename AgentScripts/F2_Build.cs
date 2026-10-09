using System.Linq;
using System.Text;
using ScrapYardKing.Factory;
using ScrapYardKing.Harvest;
using ScrapYardKing.Workers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Fun pass F2 (owner's brief, 2026-10-09):
///  - the Claw Crane lifts whole light scrap (tyre stacks, drums, cars, karts, stoves, washers, fridges) and feeds it into
///    the Crusher piece by piece; it no longer picks loose fragments (ScrapDefinition.craneLiftable, ClawCrane.liftWholeScrap);
///  - the Scrap Porter carries a chainsaw: with nothing loose to carry it cuts the nearest scrap in its zone, at about a
///    third of the player's pace, then collects and delivers the pieces (WorkerDefinition cut fields).
/// The Heavy Yard crane keeps lifting loose pieces onto its belt until F5 reworks the Heavy Yard.
/// Entry points: Assets, Scene, All. Idempotent. Run after F1_Build.
/// </summary>
public static class F2_Build
{
    const string Root = "Assets/_Project";
    const string ScenePath = Root + "/Scenes/Area1_OldScrapYard.unity";
    static readonly StringBuilder Log = new();

    /// <summary>Light scrap a crane can swing: everything a level-1 chainsaw can also cut, plus the fridge.</summary>
    static readonly string[] Liftable = { "barrel", "tire_stack", "car_wreck", "kart", "stove", "washer", "fridge" };

    public static string All() => Assets() + Scene();

    public static string Assets()
    {
        Log.Clear();
        int n = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:ScrapDefinition", new[] { Root + "/Data" }))
        {
            var def = AssetDatabase.LoadAssetAtPath<ScrapDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            bool lift = Liftable.Contains(def.Id);
            Set(def, so => so.FindProperty("craneLiftable").boolValue = lift);
            if (lift) n++;
        }

        // player: 10 damage x 7 cuts/s = 70/s. The porter: 10 x 2.5 = 25/s, about a third.
        Set(AssetDatabase.LoadAssetAtPath<WorkerDefinition>(Root + "/Data/Workers/Worker_Porter.asset"), so =>
        {
            so.FindProperty("cutPower").floatValue = 10f;
            so.FindProperty("cutDamage").floatValue = 10f;
            so.FindProperty("cutsPerSecond").floatValue = 2.5f;
            so.FindProperty("cutReach").floatValue = 1.4f;
            so.FindProperty("tagline").stringValue = "CUTS AND HAULS SCRAP";
        });
        AssetDatabase.SaveAssets();
        Log.AppendLine($"assets: {n} scrap types crane-liftable, Scrap Porter cuts at 25/s");
        return Log.ToString();
    }

    public static string Scene()
    {
        Log.Clear();
        if (EditorApplication.isPlaying) return "!! stop Play mode first";
        var active = SceneManager.GetActiveScene();
        if (active.path == ScenePath && active.isDirty) EditorSceneManager.SaveScene(active);
        var scene = active.path == ScenePath ? active : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        foreach (var crane in Object.FindObjectsByType<ClawCrane>(FindObjectsInactive.Include))
        {
            bool whole = crane.UpgradeId == "claw_crane";
            Set(crane, so =>
            {
                so.FindProperty("liftWholeScrap").boolValue = whole;
                so.FindProperty("feedRate").floatValue = 18f;
            });
            Log.AppendLine($"{crane.UpgradeId}: {(whole ? "whole objects" : "loose pieces")}");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return Log.ToString();
    }

    static void Set(Object target, System.Action<SerializedObject> edit)
    {
        var so = new SerializedObject(target);
        edit(so);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Text;
using ScrapYardKing.EditorTools.Art;
using ScrapYardKing.Progression;
using UnityEditor;
using UnityEngine;

// Revamp 7 builder: the last word on the session's order and numbers, after every other R-builder has run.
//  - Order(): the main task chain in the order of the pacing arc (Docs/QUALITY_PLAN.md): yard → plant → four furnaces
//    → Press → truck orders → Heavy Scrap Yard → specialists and giants → port.
//  - Balance(): numbers changed after the full-session guide-bot runs, each with the reason (Docs/ECONOMY.md).
// Entry point: Assets. Run it last of the data steps; it only reorders and retunes, it creates nothing.
public static class R7_Build
{
    const string DataDir = "Assets/_Project/Data";
    static readonly StringBuilder Log = new();

    public static string Assets()
    {
        Log.Clear();
        Order();
        Balance();
        AssetDatabase.SaveAssets();
        return Log.ToString();
    }

    /// <summary>
    /// The Heavy Scrap Yard used to open before the steel furnace and the Press (it was the M6 finale). The arc wants
    /// the factory finished and shipping first, then the big yard as the next world to conquer.
    /// </summary>
    static void Order()
    {
        var chain = AssetDatabase.LoadAssetAtPath<TaskChain>(DataDir + "/Progression/TaskChain_Area1.asset");
        var so = new SerializedObject(chain);
        var main = so.FindProperty("mainTasks");
        var list = new List<Object>();
        for (int i = 0; i < main.arraySize; i++) list.Add(main.GetArrayElementAtIndex(i).objectReferenceValue);
        string[] heavy = { "t34_heavy_yard", "t35_trucks", "r4_heavy_crane" };
        var block = heavy.Select(id => list.FirstOrDefault(t => t is TaskDefinition d && d.Id == id)).Where(t => t != null).ToList();
        foreach (var t in block) list.Remove(t);
        int after = list.FindIndex(t => t is TaskDefinition d && d.Id == "t43_ship");
        if (after < 0)
        {
            Log.AppendLine("!! chain has no t43_ship");
            return;
        }

        list.InsertRange(after + 1, block);
        ArtAssets.SetArray(chain, "mainTasks", list.ToArray());
        Log.AppendLine("order: " + string.Join(" > ", list.OfType<TaskDefinition>().Select(d => d.Id)));
    }

    /// <summary>Scene settings that belong to the balance: the Market Runner leaves furnace inputs in the bins.</summary>
    public static string Scene()
    {
        if (EditorApplication.isPlaying) return "!! stop Play mode first";
        var route = Object.FindObjectsByType<ScrapYardKing.Workers.PorterRoute>(FindObjectsInactive.Include).FirstOrDefault(r => r.name == "RunnerRoute");
        if (route == null) return "!! no RunnerRoute";
        // Run r7_run1: the Runner carried raw iron to the market while the Iron Furnace starved beside a hired Smelter.
        // Run r7_run2: with full raw bins beside a nearly empty ingot rack, "fullest pad" never chose the ingots. The rack is
        // first in the Runner's list now and wins whenever it holds 4 or more.
        var so = new SerializedObject(route);
        var pads = so.FindProperty("pickupPads");
        var list = new List<Object>();
        for (int i = 0; i < pads.arraySize; i++) list.Add(pads.GetArrayElementAtIndex(i).objectReferenceValue);
        var rack = list.FirstOrDefault(p => p != null && ((Component)p).transform.parent.name == "IngotRack");
        if (rack != null)
        {
            list.Remove(rack);
            list.Insert(0, rack);
            ArtAssets.SetArray(route, "pickupPads", list.ToArray());
        }

        ArtAssets.Set(route, ("leaveMachineInputs", true), ("priorityLoad", 4));
        // Run r7_run2: the Delivery Helper emptied the yard storage into the Sell Desk while "Split 15 raw metal" waited
        // eight minutes for Raw Metal. It leaves 24 pieces for the Splitter once the plant stands.
        var helper = Object.FindObjectsByType<ScrapYardKing.Workers.PorterRoute>(FindObjectsInactive.Include).FirstOrDefault(r => r.name == "DeliveryRoute");
        if (helper != null) ArtAssets.Set(helper, ("machineInputKeep", 24));
        else return "!! no DeliveryRoute";
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        return "runner leaves machine inputs";
    }

    static void Balance()
    {
        // Yard levels. R2 lowered the curve to x1.45 so levels 7-9 would not block the furnaces; the run then reached
        // Lv 10 at minute 24 and no gate held anything. With today's income the old x1.55 puts Lv 9 near minute 32,
        // Lv 10 near 45 and Lv 11 near 60 (Docs/ECONOMY.md section 4): just ahead of the prices. Model, not yet measured.
        ArtAssets.Set(AssetDatabase.LoadAssetAtPath<ProgressionConfig>(DataDir + "/Progression/ProgressionConfig.asset"), ("growth", 1.55f));
        Log.AppendLine("balance: level curve x1.55");
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Text;
using ScrapYardKing.EditorTools.Art;
using ScrapYardKing.Factory;
using ScrapYardKing.Feedback;
using ScrapYardKing.Items;
using ScrapYardKing.Progression;
using ScrapYardKing.UI;
using ScrapYardKing.Workers;
using ScrapYardKing.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Revamp 6 builder: the port works. The cargo ship at the quay becomes a dock station: it takes bars like the truck,
// carries a bigger order, sails when the order is met or the hold is full, pays onto a cash pallet on the quay and
// comes back. It is the same component as the Truck Dock (TruckBay) with the ship as the carrier, so orders, the HUD
// card, the Loader and the guide all work without new code.
//  - Assets: TruckBay_Ship (bigger holds, better pay), catalog entry, the task after the Dockyard opens.
//  - Scene: the Ship Dock in the Dockyard (pad, cash pallet, label, cargo pile on the deck), the ship's course, the
//    Loader's third drop-off, the "coming soon" sign removed, the NavMesh volume widened over the quay.
// Run Assets, Scene, then R1_Build.Bake and UI_Build.Apply. Run after R3_Build (it needs the bars and the Loader route).
public static class R6_Build
{
    const string P = "Assets/_Project";
    const string DataDir = P + "/Data";
    const string ScenePath = P + "/Scenes/Area1_OldScrapYard.unity";

    // Quay layout (blueprint frame). The lane from Gate 4 runs east along z 21.4–26.6 and ends at the pad.
    static readonly Vector3 Pad = new(95.2f, 0f, 24f), CashPile = new(92.9f, 0f, 27.9f), CashPad = new(95.2f, 0f, 27.9f);
    // The ship leaves north-east: forward and away from the quay, out of the camera's view before it is switched off.
    static readonly Vector3 Away = new(135f, -0.7f, 100f);

    static readonly StringBuilder Log = new();

    public static string Assets()
    {
        Log.Clear();
        string path = DataDir + "/Factory/TruckBay_Ship.asset";
        if (AssetDatabase.LoadAssetAtPath<TruckBayDefinition>(path) == null) AssetDatabase.CopyAsset(DataDir + "/Factory/TruckBay_Plant.asset", path);
        var def = AssetDatabase.LoadAssetAtPath<TruckBayDefinition>(path);
        var horn = AssetDatabase.LoadAssetAtPath<SfxDefinition>(DataDir + "/Audio/Sfx_ShipHorn.asset");
        ArtAssets.Set(def, ("id", "ship_dock"), ("displayName", "Cargo Ship"), ("truckSpeed", 7f), ("firstArrivalDelay", 2f), ("contractFill", 0.8f), ("arrivingText", "SHIP COMING"), ("awayText", "AT SEA"), ("arriveSfx", horn),
            ("departSfx", horn), ("contractSfx", horn));
        // A ship is four truckloads and pays about a third better per bar; it is away longer.
        var so = new SerializedObject(def);
        var levels = so.FindProperty("levels");
        (int capacity, float payout, float back, long cost)[] rows = { (60, 1.9f, 14f, 0), (90, 2.1f, 12f, 25000), (130, 2.3f, 10f, 60000) };
        levels.arraySize = rows.Length;
        for (int i = 0; i < rows.Length; i++)
        {
            var e = levels.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("capacity").intValue = rows[i].capacity;
            e.FindPropertyRelative("payoutMultiplier").floatValue = rows[i].payout;
            e.FindPropertyRelative("returnDelay").floatValue = rows[i].back;
            e.FindPropertyRelative("upgradeCost").longValue = rows[i].cost;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(def);

        var catalog = AssetDatabase.LoadAssetAtPath<UpgradeCatalog>(DataDir + "/Progression/UpgradeCatalog.asset");
        var cso = new SerializedObject(catalog);
        var entries = cso.FindProperty("entries");
        int index = -1;
        for (int i = 0; i < entries.arraySize; i++)
            if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("upgradeId").stringValue == "ship_dock") index = i;
        if (index < 0) index = entries.arraySize++;
        var entry = entries.GetArrayElementAtIndex(index);
        entry.FindPropertyRelative("upgradeId").stringValue = "ship_dock";
        entry.FindPropertyRelative("unlockLevel").intValue = 11;
        entry.FindPropertyRelative("inPanel").boolValue = true;
        entry.FindPropertyRelative("group").stringValue = "PORT";
        cso.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(catalog);

        // The session's last task was "Open the Dockyard"; the port now has something to do.
        var task = ArtAssets.LoadOrCreate<TaskDefinition>(DataDir + "/Progression/Tasks/Task_r6_ship.asset");
        ArtAssets.Set(task, ("id", "r6_ship"), ("title", "Load {0} bars on the ship"), ("category", (int)TaskCategory.Main), ("type", (int)TaskType.DeliverItems), ("targetId", "ship_dock"),
            ("amount", 60), ("rewardCash", 15000), ("rewardXp", 600), ("rewardPremium", 10));
        var chain = AssetDatabase.LoadAssetAtPath<TaskChain>(DataDir + "/Progression/TaskChain_Area1.asset");
        var chainSo = new SerializedObject(chain);
        var main = chainSo.FindProperty("mainTasks");
        var list = new List<Object>();
        for (int i = 0; i < main.arraySize; i++) list.Add(main.GetArrayElementAtIndex(i).objectReferenceValue);
        list.RemoveAll(t => t == task);
        int after = list.FindIndex(t => t is TaskDefinition d && d.Id == "t50_dockyard");
        if (after < 0) Log.AppendLine("!! chain has no t50_dockyard");
        else list.Insert(after + 1, task);
        ArtAssets.SetArray(chain, "mainTasks", list.ToArray());
        AssetDatabase.SaveAssets();
        Log.AppendLine($"ship dock data, catalog, task (chain: {list.Count})");
        return Log.ToString();
    }

    public static string Scene()
    {
        Log.Clear();
        if (EditorApplication.isPlaying) return "!! stop Play mode first";
        var active = SceneManager.GetActiveScene();
        if (active.path == ScenePath && active.isDirty) EditorSceneManager.SaveScene(active);
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var gameplay = scene.GetRootGameObjects().First(g => g.name == "_Gameplay").transform;
        var env = scene.GetRootGameObjects().First(g => g.name == "_Environment").transform;
        var content = gameplay.Find("Dockyard/Content");
        var backdrop = env.Find("DockyardBackdrop");
        var ship = backdrop.Find("Prop_Ship");
        var truckDock = gameplay.Find("TruckDock/Content/TruckBay");
        if (content == null || ship == null || truckDock == null) return "!! Dockyard content, the ship or the truck dock is missing";
        bool contentActive = content.Find("Stack_S1").gameObject.activeSelf;

        foreach (string n in new[] { "ShipDock", "ComingSoon" })
        {
            var t = content.Find(n);
            if (t != null) Object.DestroyImmediate(t.gameObject);
        }

        foreach (var t in backdrop.GetComponentsInChildren<Transform>(true).Where(t => t.name is "ShipCargo" or "ShipPayPoint" or "ShipParkPoint" or "ShipAwayPoint").ToArray())
            if (t != null) Object.DestroyImmediate(t.gameObject);

        // A copy of the truck dock's station (pad, cash pallet, label, component), rewired to the ship.
        var prefab = PrefabUtility.GetCorrespondingObjectFromSource(truckDock.gameObject);
        var dock = (prefab != null ? (GameObject)PrefabUtility.InstantiatePrefab(prefab, content) : Object.Instantiate(truckDock.gameObject, content)).transform;
        if (PrefabUtility.IsPartOfPrefabInstance(dock)) PrefabUtility.UnpackPrefabInstance(dock.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        dock.name = "ShipDock";
        dock.SetPositionAndRotation(new Vector3(Pad.x, 0f, Pad.z), Quaternion.identity);
        var truck = dock.Find("Truck");
        var cargo = truck.Find("Cargo");
        var pay = truck.Find("PayPoint");
        // The pile of bars on deck, on the quay side, and where the cash comes from.
        cargo.name = "ShipCargo";
        cargo.SetParent(ship, true);
        // Aft deck (the deck is 3.3 m above the ship's origin; the superstructure starts at z 24).
        cargo.SetPositionAndRotation(new Vector3(102.6f, ship.position.y + 3.42f, 22.65f), Quaternion.identity);
        ArtAssets.Set(cargo.GetComponent<ItemPile>(), ("columns", 6), ("rows", 2), ("cellSize", new Vector3(0.56f, 0.14f, 0.52f)));
        pay.name = "ShipPayPoint";
        pay.SetParent(ship, true);
        pay.position = new Vector3(102f, ship.position.y + 6.4f, 26.5f);
        foreach (string n in new[] { "Truck", "Art", "Lv2_Floodlight", "Lv3_Stock" })
        {
            var t = dock.Find(n);
            if (t != null) Object.DestroyImmediate(t.gameObject);
        }

        if (dock.TryGetComponent(out LevelVisuals lv)) Object.DestroyImmediate(lv);
        dock.Find("Pad").position = Pad;
        dock.Find("CashPile").position = CashPile;
        dock.Find("CashPad").position = CashPad;
        dock.Find("StationLabel").position = new Vector3(Pad.x, 3.4f, Pad.z - 0.6f);
        // The course points live in the backdrop, not under the dock: the Dockyard's reveal pops its content in from
        // scale 0, and points under the dock collapsed onto its origin while the bay was starting (the ship jumped onto
        // the quay).
        var park = dock.Find("ParkPoint");
        park.name = "ShipParkPoint";
        park.SetParent(backdrop, true);
        park.SetPositionAndRotation(ship.position, ship.rotation);
        var away = dock.Find("AwayPoint");
        away.name = "ShipAwayPoint";
        away.SetParent(backdrop, true);
        away.position = Away;

        // The ship bobs on its hull; the root is moved by the dock, so it must not sway by itself.
        if (ship.TryGetComponent(out AmbientMotion sway)) Object.DestroyImmediate(sway);
        var hull = ship.Find("Hull");
        var bay = dock.GetComponent<TruckBay>();
        ArtAssets.Set(bay, ("definition", AssetDatabase.LoadAssetAtPath<TruckBayDefinition>(DataDir + "/Factory/TruckBay_Ship.asset")), ("level", 1), ("truck", ship),
            ("cargo", cargo.GetComponent<ItemPile>()), ("parkPoint", park), ("awayPoint", away), ("truckBody", hull != null ? hull : ship), ("payPoint", pay), ("exhaust", null),
            ("levelVisuals", null), ("startDocked", true), ("easeDistance", 20f), ("crawlSpeed", 0.9f), ("departDelay", 1.6f));
        ArtAssets.SetArray(bay, "wheels", new Object[0]);
        dock.gameObject.SetActive(contentActive);

        // The Loader also loads the ship (after the truck: the truck's pad is first in the list).
        var route = gameplay.Find("Workers/Sites/LoaderRoute");
        var truckPad = truckDock.Find("Pad").GetComponent<TransferPad>();
        if (route != null) ArtAssets.SetArray(route.GetComponent<PorterRoute>(), "extraDropoffs", new Object[] { truckPad, dock.Find("Pad").GetComponent<TransferPad>() });

        // Workers and the guide need ground to walk on out here: the NavMesh volume ended at the plant's east wall.
        var surface = env.Find("NavMesh").GetComponent<NavMeshSurface>();
        surface.center = new Vector3(48f, 1f, 42f);
        surface.size = new Vector3(100f, 6f, 66f);
        EditorUtility.SetDirty(surface);
        var quay = backdrop.Find("Quay");
        if (quay != null)
        {
            if (!quay.TryGetComponent(out NavMeshModifier quayModifier)) quayModifier = quay.gameObject.AddComponent<NavMeshModifier>();
            quayModifier.ignoreFromBuild = false;
            quayModifier.overrideArea = false;
        }

        foreach (var m in new[] { backdrop.GetComponent<NavMeshModifier>(), content.GetComponent<NavMeshModifier>() })
            if (m != null) Log.AppendLine($"  modifier on {m.name}: ignore={m.ignoreFromBuild} override={m.overrideArea}");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Log.AppendLine("scene: Ship Dock, ship course, Loader drop-off, NavMesh volume. Next: R1_Build.Bake, UI_Build.Apply.");
        return Log.ToString();
    }
}

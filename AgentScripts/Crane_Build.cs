using System.Collections.Generic;
using System.Linq;
using System.Text;
using ScrapYardKing.EditorTools.Art;
using ScrapYardKing.Factory;
using ScrapYardKing.Feedback;
using ScrapYardKing.Items;
using ScrapYardKing.Progression;
using ScrapYardKing.Tiles;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using static ScrapYardKing.EditorTools.Art.ArtMaterials;

// Crane builder. Two claw cranes, one component (ClawCrane), one rig:
//  - Yard crane (owner, 2026-10-06): beside the Crusher; picks loose scrap inside its reach and drops it into the hopper.
//    Bought on a tile once the Back Lot is open.
//  - Heavy Yard crane (Revamp 4): beside Gate 3; picks loose scrap in the Heavy Yard and drops it on a belt that runs
//    through the wall and tips it into the scrap pit, inside the yard crane's reach. Heavy scrap reaches the Crusher with
//    nobody carrying it across two areas. Bought on a tile in the Heavy Yard. The belt takes the east 1.3 m of the gate.
// Both have a truss jib built in three sections; each level adds a section (longer reach, bigger grab, faster).
//  - Assets: definitions, catalog entries, tasks. Scene: cranes, tiles, the belt and its spill end, icons.
// Run Assets, Scene, then Art_Machines.Scene (belt art), Env_Build.All and R1_Build.Bake. Idempotent.
public static class Crane_Build
{
    const string P = "Assets/_Project";
    const string DataDir = P + "/Data";
    const string ScenePath = P + "/Scenes/Area1_OldScrapYard.unity";
    const string Model = "Mach_ClawCrane";

    // Yard crane: at the corner of the work floor; the hopper is 4.7 m to the south-east.
    static readonly Vector3 YardPos = new(19.9f, 0f, 29.3f);
    static readonly Vector3 YardTile = new(17.9f, 0f, 21.6f);
    static readonly (float reach, int grab, float speed, long cost)[] YardLevels = { (10f, 4, 1f, 600), (13.5f, 6, 1.25f, 1400), (19.5f, 8, 1.5f, 3000) };

    // Heavy Yard crane: east of the Gate 3 lane. The belt starts beside it and runs through the gate opening, along its
    // east post, into the pit (the wall east of the gate is buried in a junk heap; the first version ran the belt through it).
    static readonly Vector3 HeavyPos = new(15.6f, 0f, 40.6f);
    static readonly Vector3 HeavyTile = new(17.1f, 0f, 43.7f);
    static readonly Vector3 BeltStart = new(14f, 0f, 41.6f), BeltEnd = new(14f, 0f, 36f);
    static readonly (float reach, int grab, float speed, long cost)[] HeavyLevels = { (16f, 6, 1f, 5000), (22f, 9, 1.2f, 9000), (28f, 12, 1.4f, 15000) };

    static readonly StringBuilder Log = new();

    public static string Assets()
    {
        Log.Clear();
        Definition("ClawCrane_Yard", "claw_crane", "Claw Crane", YardLevels, 2.4f);
        Definition("ClawCrane_Heavy", "heavy_crane", "Heavy Yard Crane", HeavyLevels, 3.2f);
        Catalog("claw_crane", 4);
        Catalog("heavy_crane", 9);

        var yardTask = Task("t17b_claw", "Build the claw crane", "claw_crane", 80);
        var heavyTask = Task("r4_heavy_crane", "Build the Heavy Yard crane", "heavy_crane", 200);
        var chain = AssetDatabase.LoadAssetAtPath<TaskChain>(DataDir + "/Progression/TaskChain_Area1.asset");
        var so = new SerializedObject(chain);
        var main = so.FindProperty("mainTasks");
        var list = new List<Object>();
        for (int i = 0; i < main.arraySize; i++) list.Add(main.GetArrayElementAtIndex(i).objectReferenceValue);
        list.RemoveAll(t => t == yardTask || t == heavyTask);
        Insert(list, "t17_back_lot", yardTask);
        // After the first trucks are cut: the player has seen how far the Heavy Yard is from the Crusher.
        Insert(list, "t35_trucks", heavyTask);
        ArtAssets.SetArray(chain, "mainTasks", list.ToArray());
        AssetDatabase.SaveAssets();
        Log.AppendLine($"crane data, catalog, tasks (chain: {list.Count} tasks)");
        return Log.ToString();
    }

    static void Definition(string asset, string id, string name, (float reach, int grab, float speed, long cost)[] rows, float grabRadius)
    {
        var def = ArtAssets.LoadOrCreate<ClawCraneDefinition>($"{DataDir}/Factory/{asset}.asset");
        ArtAssets.Set(def, ("id", id), ("displayName", name), ("grabRadius", grabRadius),
            ("grabSfx", AssetDatabase.LoadAssetAtPath<SfxDefinition>(DataDir + "/Audio/Sfx_Pickup.asset")),
            ("dropSfx", AssetDatabase.LoadAssetAtPath<SfxDefinition>(DataDir + "/Audio/Sfx_MachineOutput.asset")));
        var so = new SerializedObject(def);
        var levels = so.FindProperty("levels");
        levels.arraySize = rows.Length;
        for (int i = 0; i < rows.Length; i++)
        {
            var e = levels.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("reach").floatValue = rows[i].reach;
            e.FindPropertyRelative("grabSize").intValue = rows[i].grab;
            e.FindPropertyRelative("speed").floatValue = rows[i].speed;
            e.FindPropertyRelative("cost").longValue = rows[i].cost;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(def);
    }

    static void Catalog(string id, int level)
    {
        var catalog = AssetDatabase.LoadAssetAtPath<UpgradeCatalog>(DataDir + "/Progression/UpgradeCatalog.asset");
        var so = new SerializedObject(catalog);
        var entries = so.FindProperty("entries");
        int index = -1;
        for (int i = 0; i < entries.arraySize; i++)
            if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("upgradeId").stringValue == id) index = i;
        if (index < 0) index = entries.arraySize++;
        var entry = entries.GetArrayElementAtIndex(index);
        entry.FindPropertyRelative("upgradeId").stringValue = id;
        entry.FindPropertyRelative("unlockLevel").intValue = level;
        entry.FindPropertyRelative("inPanel").boolValue = false;
        entry.FindPropertyRelative("group").stringValue = "";
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(catalog);
    }

    static TaskDefinition Task(string id, string title, string target, int xp)
    {
        var task = ArtAssets.LoadOrCreate<TaskDefinition>($"{DataDir}/Progression/Tasks/Task_{id}.asset");
        ArtAssets.Set(task, ("id", id), ("title", title), ("category", (int)TaskCategory.Main), ("type", (int)TaskType.ReachUpgradeLevel), ("targetId", target), ("amount", 1),
            ("rewardCash", 0), ("rewardXp", xp), ("rewardPremium", 0));
        return task;
    }

    static void Insert(List<Object> list, string afterId, Object task)
    {
        int after = list.FindIndex(t => t is TaskDefinition d && d.Id == afterId);
        if (after < 0) Log.AppendLine("!! chain has no " + afterId);
        else list.Insert(after + 1, task);
    }

    public static string Scene()
    {
        Log.Clear();
        if (EditorApplication.isPlaying) return "!! stop Play mode first";
        var active = SceneManager.GetActiveScene();
        if (active.path == ScenePath && active.isDirty) EditorSceneManager.SaveScene(active);
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var gameplay = scene.GetRootGameObjects().First(g => g.name == "_Gameplay").transform;
        var scrap = AssetDatabase.LoadAssetAtPath<ItemDefinition>(DataDir + "/Items/Item_Scrap.asset");

        // ---------- yard crane ----------
        var stations = gameplay.Find("Stations");
        var crusher = stations.Find("Crusher").GetComponent<Machine>();
        var hopper = new SerializedObject(crusher).FindProperty("hopper").objectReferenceValue as Component;
        Vector3 hopperDrop = (hopper != null ? hopper.transform.position : crusher.transform.position + Vector3.up * 2.4f) + Vector3.up * 1.15f;
        var yardDef = AssetDatabase.LoadAssetAtPath<ClawCraneDefinition>(DataDir + "/Factory/ClawCrane_Yard.asset");
        var yard = BuildCrane(stations, "ClawCrane", YardPos, yardDef, crusher, hopperDrop, 5.75f, 4.2f, YardLevels.Select(l => l.reach).ToArray(), null, "Icon_ClawCrane");
        Tile(gameplay.Find("Tiles"), "Tile_ClawCrane", YardTile, "claw_crane");
        _ = yard;

        // ---------- Heavy Yard crane, belt and spill ----------
        var heavyContent = gameplay.Find("HeavyYard/Content");
        if (heavyContent == null) Log.AppendLine("!! no HeavyYard/Content");
        else
        {
            bool activeContent = heavyContent.Find("Decor").gameObject.activeSelf;
            foreach (string n in new[] { "HeavyCrane", "Conveyor_HeavyToPit", "Spill_Pit", "Tile_HeavyCrane", "BeltPortal" })
            {
                var t = heavyContent.Find(n);
                if (t != null) Object.DestroyImmediate(t.gameObject);
            }

            // The belt's end: tips scrap into the pit, a little south-west of the wall.
            var spillGo = Empty("Spill_Pit", heavyContent, Vector3.zero);
            spillGo.position = BeltEnd + new Vector3(0f, 0.75f, -0.2f);
            var spill = spillGo.gameObject.AddComponent<ItemSpill>();
            ArtAssets.SetArray(spill, "accepts", new Object[] { scrap });
            spillGo.gameObject.SetActive(activeContent);

            var belt = Belt(heavyContent, "Conveyor_HeavyToPit", BeltStart, BeltEnd, spill);
            belt.gameObject.SetActive(activeContent);

            var heavyDef = AssetDatabase.LoadAssetAtPath<ClawCraneDefinition>(DataDir + "/Factory/ClawCrane_Heavy.asset");
            var heavy = BuildCrane(heavyContent, "HeavyCrane", HeavyPos, heavyDef, belt, BeltStart + new Vector3(0f, 1.7f, -0.3f), 7.6f, 5.2f, HeavyLevels.Select(l => l.reach).ToArray(),
                new Object[] { scrap }, "Icon_HeavyCrane");
            heavy.gameObject.SetActive(activeContent);
            Tile(heavyContent, "Tile_HeavyCrane", HeavyTile, "heavy_crane").gameObject.SetActive(activeContent);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Log.AppendLine("cranes built. Next: Art_Machines.Scene (belt art), Env_Build.All, R1_Build.Bake.");
        return Log.ToString();
    }

    static Transform Tile(Transform parent, string name, Vector3 pos, string upgradeId)
    {
        var existing = parent.Find(name);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);
        var tile = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(P + "/Prefabs/Tiles/Tile_Purchase.prefab"), parent);
        tile.name = name;
        tile.transform.SetPositionAndRotation(pos, Quaternion.identity);
        ArtAssets.Set(tile.GetComponent<PurchaseTile>(), ("upgradeId", upgradeId));
        ArtAssets.Set(tile.AddComponent<GuideAnchor>(), ("id", "tile/" + upgradeId));
        return tile.transform;
    }

    /// <summary>
    /// One claw crane: foundation (always there), mast, cab, counter-jib, a truss jib in one section per level, trolley,
    /// cable and claw, wired to a ClawCrane. <paramref name="pivotY"/> is the mast height; taller for the Heavy Yard.
    /// </summary>
    static Transform BuildCrane(Transform parent, string name, Vector3 pos, ClawCraneDefinition def, MonoBehaviour target, Vector3 dropWorld, float pivotY, float travelHeight,
        float[] reaches, Object[] takes, string iconName)
    {
        var old = parent.Find(name);
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var root = Empty(name, parent, Vector3.zero);
        root.position = pos;
        string part = name + "_";

        var yellow = Mat("MachYellow", new Color(1f, 0.76f, 0.1f), 0.5f, 0.05f, Detail.Paint, 0.7f);
        var dark = Mat("MachDark", new Color(0.17f, 0.18f, 0.2f), 0.35f, 0.1f, Detail.Metal, 0.8f);
        var red = Mat("CraneRed", new Color(0.86f, 0.18f, 0.13f), 0.5f, 0.1f, Detail.Paint, 0.7f);
        var concrete = Mat("MachConcrete", new Color(0.72f, 0.7f, 0.67f), 0.15f, 0f, Detail.Concrete, 0.35f);
        var glass = Mat("MachGlass", new Color(0.45f, 0.65f, 0.8f), 0.92f, 0.1f, Detail.Plain);
        var steel = Mat("MachSteel", new Color(0.55f, 0.58f, 0.63f), 0.5f, 0.3f, Detail.Metal, 0.8f);
        var table = new[] { yellow, dark, red, concrete, glass, steel };
        const int Y = 0, D = 1, R = 2, C = 3, G = 4, S = 5;

        // Foundation: always there, so the player sees where the crane will stand (and the NavMesh never changes).
        var k = new MeshKit();
        k.Box(C, new Vector3(0f, 0.25f, 0f), new Vector3(1.7f, 0.5f, 1.7f), 0.06f);
        foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
                k.Box(Y, new Vector3(sx * 0.72f, 0.52f, sz * 0.72f), new Vector3(0.26f, 0.08f, 0.26f), 0.02f);
        k.Cylinder(D, new Vector3(0f, 0.56f, 0f), 0.42f, 0.12f, 14);
        ArtAssets.Part(Model, part + "Foundation", k, table, root).name = "Foundation";
        var box = root.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.6f, 0f);
        box.size = new Vector3(1.7f, 1.2f, 1.7f);
        var obstacle = root.gameObject.AddComponent<NavMeshObstacle>();
        obstacle.carving = true;
        obstacle.center = box.center;
        obstacle.size = box.size;

        var built = Empty("Built", root, Vector3.zero);
        // Mast: four corner posts with cross lacing, a ladder cage up the back.
        k = new MeshKit();
        const float m = 0.27f;
        foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
                k.Box(Y, new Vector3(sx * m, 0.5f + (pivotY - 0.5f) * 0.5f, sz * m), new Vector3(0.11f, pivotY - 0.5f, 0.11f), 0.02f);
        for (float y = 0.9f; y < pivotY - 0.5f; y += 0.9f)
        {
            foreach (int sz in new[] { -1, 1 }) Strut(k, Y, new Vector3(-m, y, sz * m), new Vector3(m, y + 0.9f, sz * m), 0.06f);
            foreach (int sx in new[] { -1, 1 }) Strut(k, Y, new Vector3(sx * m, y, m), new Vector3(sx * m, y + 0.9f, -m), 0.06f);
            k.Box(D, new Vector3(0f, y, 0f), new Vector3(0.66f, 0.07f, 0.66f), 0.02f);
        }

        k.Box(S, new Vector3(0f, pivotY * 0.5f, -m - 0.09f), new Vector3(0.3f, pivotY - 0.9f, 0.04f), 0.01f);
        ArtAssets.Part(Model, part + "Mast", k, table, built).name = "Mast";

        var pivot = Empty("Pivot", built, new Vector3(0f, pivotY, 0f));
        k = new MeshKit();
        k.Cylinder(D, Vector3.zero, 0.55f, 0.18f, 16);
        k.Box(Y, new Vector3(0.62f, 0.5f, 0.2f), new Vector3(0.8f, 0.85f, 1.1f), 0.1f);                 // operator cab beside the jib
        k.Box(G, new Vector3(0.62f, 0.56f, 0.76f), new Vector3(0.64f, 0.5f, 0.04f), 0.02f);
        k.Box(G, new Vector3(1.03f, 0.56f, 0.2f), new Vector3(0.04f, 0.5f, 0.8f), 0.02f);
        k.Box(D, new Vector3(0.62f, 0.96f, 0.2f), new Vector3(0.88f, 0.08f, 1.18f), 0.03f);
        k.Box(D, new Vector3(0f, 0.42f, 0f), new Vector3(0.5f, 0.6f, 0.5f), 0.05f);                     // slewing unit
        // Counter-jib: a short truss with a walkway and the counterweight blocks.
        foreach (int sx in new[] { -1, 1 }) k.Box(Y, new Vector3(sx * 0.2f, 0.62f, -1.7f), new Vector3(0.09f, 0.09f, 2.6f), 0.02f);
        k.Box(S, new Vector3(0f, 0.68f, -1.7f), new Vector3(0.46f, 0.03f, 2.5f), 0.005f);
        for (int i = 0; i < 3; i++) k.Box(D, new Vector3(0f, 0.38f, -2.2f - i * 0.36f), new Vector3(0.84f, 0.74f, 0.3f), 0.05f);
        // Tower head and tie rods to both arms.
        k.Box(Y, new Vector3(0f, 1.6f, 0f), new Vector3(0.2f, 1.8f, 0.2f), 0.04f);
        Strut(k, S, new Vector3(0f, 2.45f, 0f), new Vector3(0f, 0.7f, -2.9f), 0.045f);
        Strut(k, S, new Vector3(0f, 2.45f, 0f), new Vector3(0f, 1.1f, reaches[0] * 0.7f), 0.045f);
        k.Box(R, new Vector3(0f, 2.56f, 0f), new Vector3(0.14f, 0.14f, 0.14f), 0.03f);
        ArtAssets.Part(Model, part + "Cab", k, table, pivot).name = "Cab";

        // Jib: a triangular truss (two bottom chords the trolley runs on, one top chord, lacing), one section per level.
        var sections = new List<Object>();
        float from = 0.3f;
        for (int i = 0; i < reaches.Length; i++)
        {
            float to = reaches[i] + 0.7f;
            var section = Empty("JibSection" + (i + 1), pivot, new Vector3(0f, 0.62f, 0f));
            k = new MeshKit();
            foreach (int sx in new[] { -1, 1 }) k.Box(Y, new Vector3(sx * 0.2f, 0f, (from + to) * 0.5f), new Vector3(0.09f, 0.09f, to - from), 0.02f);
            k.Box(Y, new Vector3(0f, 0.46f, (from + to) * 0.5f), new Vector3(0.09f, 0.09f, to - from), 0.02f);
            int bays = Mathf.Max(1, Mathf.RoundToInt((to - from) / 0.9f));
            float step = (to - from) / bays;
            for (int b = 0; b < bays; b++)
            {
                float z0 = from + b * step, z1 = z0 + step;
                foreach (int sx in new[] { -1, 1 })
                {
                    Strut(k, Y, new Vector3(sx * 0.2f, 0f, z0), new Vector3(0f, 0.46f, (z0 + z1) * 0.5f), 0.05f);
                    Strut(k, Y, new Vector3(0f, 0.46f, (z0 + z1) * 0.5f), new Vector3(sx * 0.2f, 0f, z1), 0.05f);
                }

                k.Box(Y, new Vector3(0f, 0f, z1), new Vector3(0.44f, 0.06f, 0.06f), 0.01f);
            }

            k.Box(R, new Vector3(0f, 0.2f, to), new Vector3(0.5f, 0.56f, 0.08f), 0.02f);   // red end plate of this section
            ArtAssets.Part(Model, part + "Jib" + (i + 1), k, table, section).name = "Truss";
            sections.Add(section.gameObject);
            from = to;
        }

        var trolley = Empty("Trolley", pivot, new Vector3(0f, 0.46f, 3f));
        k = new MeshKit();
        k.Box(D, Vector3.zero, new Vector3(0.56f, 0.2f, 0.66f), 0.04f);
        foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
                k.Cylinder(S, new Vector3(sx * 0.2f, 0.13f, sz * 0.2f), 0.08f, 0.07f, 10, new Vector3(0f, 0f, 90f));
        ArtAssets.Part(Model, "Trolley", k, table, trolley).name = "Mesh";
        var cable = Empty("Cable", trolley, new Vector3(0f, -0.08f, 0f));
        k = new MeshKit();
        foreach (int sx in new[] { -1, 1 }) k.Cylinder(D, new Vector3(sx * 0.1f, -0.5f, 0f), 0.025f, 1f, 6, default, 0f);
        ArtAssets.Part(Model, "Cable", k, table, cable).name = "Mesh";

        var claw = Empty("Claw", pivot, new Vector3(0f, -1.5f, 3f));
        k = new MeshKit();
        k.Box(Y, new Vector3(0f, 0.62f, 0f), new Vector3(0.34f, 0.2f, 0.16f), 0.03f);                  // pulley block
        k.Cylinder(S, new Vector3(0f, 0.45f, 0f), 0.07f, 0.24f, 8);
        k.Cylinder(D, new Vector3(0f, 0.12f, 0f), 0.32f, 0.28f, 14);
        k.Cylinder(R, new Vector3(0f, 0.3f, 0f), 0.22f, 0.12f, 12);
        ArtAssets.Part(Model, "ClawHub", k, table, claw).name = "Hub";
        var fingers = new List<Object>();
        for (int i = 0; i < 4; i++)
        {
            var finger = Empty("Finger" + i, claw, Vector3.zero);
            finger.localRotation = Quaternion.Euler(0f, i * 90f, 0f);
            k = new MeshKit();
            k.Box(R, new Vector3(0f, -0.12f, 0.36f), new Vector3(0.18f, 0.15f, 0.54f), 0.03f, new Vector3(28f, 0f, 0f));
            k.Box(R, new Vector3(0f, -0.5f, 0.53f), new Vector3(0.16f, 0.6f, 0.14f), 0.03f, new Vector3(-12f, 0f, 0f));
            k.Box(D, new Vector3(0f, -0.82f, 0.42f), new Vector3(0.13f, 0.13f, 0.26f), 0.02f, new Vector3(-40f, 0f, 0f));
            k.Cylinder(S, new Vector3(0f, -0.02f, 0.14f), 0.05f, 0.2f, 8, new Vector3(0f, 0f, 90f));
            ArtAssets.Part(Model, "Finger", k, table, finger).name = "Mesh";
            fingers.Add(finger);
        }

        var hold = Empty("Hold", claw, new Vector3(0f, -0.45f, 0f));
        var drop = Empty("DropPoint", root, Vector3.zero);
        drop.position = dropWorld;

        var crane = root.gameObject.AddComponent<ClawCrane>();
        ArtAssets.Set(crane, ("definition", def), ("level", 0), ("target", target), ("dropPoint", drop), ("builtRoot", built.gameObject), ("pivot", pivot), ("trolley", trolley),
            ("cable", cable), ("claw", claw), ("hold", hold), ("travelHeight", travelHeight));
        ArtAssets.SetArray(crane, "fingers", fingers.ToArray());
        ArtAssets.SetArray(crane, "jibSections", sections.ToArray());
        if (takes != null) ArtAssets.SetArray(crane, "takes", takes);

        // Card / tile icon: the built crane with its first jib section.
        ArtAssets.Set(def, ("icon", ArtIcons.Render(iconName, root.gameObject, new Vector3(20f, 215f, 0f), go =>
        {
            go.transform.Find("Built").gameObject.SetActive(true);
            for (int i = 1; i < reaches.Length; i++) go.transform.Find("Built/Pivot/JibSection" + (i + 1)).gameObject.SetActive(false);
        })));
        built.gameObject.SetActive(false);
        return root;
    }

    /// <summary>A lattice member between two points (a box stretched along the line).</summary>
    static void Strut(MeshKit k, int slot, Vector3 a, Vector3 b, float thickness)
    {
        Vector3 d = b - a;
        k.Box(slot, (a + b) * 0.5f, new Vector3(thickness, thickness, d.magnitude), 0.01f, Quaternion.LookRotation(d.normalized, Mathf.Abs(d.normalized.y) > 0.99f ? Vector3.forward : Vector3.up).eulerAngles);
    }

    /// <summary>A bare belt (points, mover, collider); Art_Machines.Scene builds its frame and belt surface.</summary>
    static Conveyor Belt(Transform parent, string name, Vector3 from, Vector3 to, MonoBehaviour destination)
    {
        var root = Empty(name, parent, Vector3.zero);
        root.position = from;
        Vector3 delta = to - from;
        float length = delta.magnitude;
        root.rotation = Quaternion.LookRotation(delta.normalized);
        var start = Empty("Start", root, new Vector3(0f, 0f, 0.15f));
        var end = Empty("End", root, new Vector3(0f, 0f, length - 0.15f));
        var conveyor = root.gameObject.AddComponent<Conveyor>();
        ArtAssets.Set(conveyor, ("speed", 3f), ("spacing", 0.45f), ("rideHeight", 0.57f), ("destination", destination));
        ArtAssets.SetArray(conveyor, "points", new Object[] { start, end });
        var box = root.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.25f, length * 0.5f);
        box.size = new Vector3(1.3f, 0.5f, length);
        var modifier = root.gameObject.AddComponent<NavMeshModifier>();
        modifier.overrideArea = true;
        modifier.area = NavMesh.GetAreaFromName("Not Walkable");
        return conveyor;
    }

    static Transform Empty(string name, Transform parent, Vector3 localPos)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = localPos;
        return t;
    }
}

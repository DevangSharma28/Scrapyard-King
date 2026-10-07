using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ScrapYardKing.EditorTools.Art;
using ScrapYardKing.Factory;
using ScrapYardKing.Feedback;
using ScrapYardKing.Harvest;
using ScrapYardKing.Items;
using ScrapYardKing.Persistence;
using ScrapYardKing.Progression;
using ScrapYardKing.Tiles;
using ScrapYardKing.Workers;
using ScrapYardKing.World;
using TMPro;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using static ScrapYardKing.EditorTools.Art.ArtMaterials;

// Milestone 8 builder: the Dockyard reveal, save/load wiring and the balance pass.
//  - Dockyard: a quay east of the Recycling Plant with a moored cargo ship and two working ship-to-shore cranes. The
//    ship and cranes stand behind the plant wall from the start (the thing to want); Gate 4 in the Furnace hall's east
//    wall opens the quay itself. It is the end of the first session: a reveal, not a new production chain.
//  - Save/load: adds the SaveManager and fills the ItemPool catalog (saved stock is restored by item id).
//  - Balance: the numbers changed after the full-session guide-bot runs live in Balance(), one place, with the reason.
// All art is ArtKit (no Kenney). Entry points, in order: Assets, Prefabs, Scene (or All). Incremental like M3–M7.
// Run it after M7_Build; re-run Scene after M5_Build.Scene or M6_Build.Scene (they rebuild the plant's east wall).
public static class M8_Build
{
    const string P = "Assets/_Project";
    const string MatDir = P + "/Art/Materials";
    const string FontDir = P + "/Art/Fonts";
    const string DataDir = P + "/Data";
    const string PrefabDir = P + "/Prefabs";
    const string PropDir = PrefabDir + "/Props";
    const string AudioDir = "Assets/ThirdParty/Kenney/Audio";
    const string ScenePath = P + "/Scenes/Area1_OldScrapYard.unity";
    const string MeshModel = "M8";

    static readonly Color Navy = new(0.1f, 0.15f, 0.32f);

    // ---------- layout (blueprint frame: origin south-west, +X east, +Z north) ----------
    // Gate 4: an opening in the plant's east wall (x 76), inside the Furnace hall.
    const float WallX = 76f, WallNorth = 42f, WallSouth = 11f, GateZ = 24f, GateHalf = 2.6f;
    static readonly Vector3 DockyardTile = new(72.6f, 0f, GateZ);
    // Quay: concrete apron from the wall to the water's edge.
    // The camera always looks north from above, so everything tall (cranes, ship, high stacks) stands north of the lane
    // that runs from the gate to the water; south of it only low things.
    const float QuayEast = 98f, QuaySouth = 11f, QuayNorth = 47f;
    static readonly Vector3 ShipPos = new(103.8f, -0.7f, 36f);
    static readonly float[] CraneZ = { 31.6f, 41.4f };
    const float CraneX = 93.5f;

    static readonly StringBuilder Log = new();

    static int C(PC c) => (int)c;

    public static string All()
    {
        var sb = new StringBuilder();
        sb.Append(Assets());
        sb.Append(Prefabs());
        sb.Append(Scene());
        return sb.ToString();
    }

    // =====================================================================================
    // ASSETS
    // =====================================================================================

    public static string Assets()
    {
        Log.Clear();
        AssetDatabase.Refresh();

        Clips("ShipHorn", 0.9f, new Vector2(0.3f, 0.32f), "Interface/bong_001");

        var dockyard = LoadOrCreate<ExpansionDefinition>(DataDir + "/World/Expansion_Dockyard.asset");
        SetMany(dockyard, ("id", "dockyard"), ("displayName", "Dockyard"), ("cost", DockyardCost), ("teaser", "SHIPS & CRANES"),
            ("openSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_ShipHorn.asset")));
        UpsertCatalog(Load<UpgradeCatalog>(DataDir + "/Progression/UpgradeCatalog.asset"), ("dockyard", DockyardLevel, false, ""));

        Balance();
        BuildTasks();
        AssetDatabase.SaveAssets();
        return Log.ToString();
    }

    // ---------- balance (see Docs/ARCHITECTURE.md §4 for the measured session) ----------
    // Late-session income measured at about $850 per minute (Loader, both operators, Runner hired): $20,000 is roughly
    // twenty minutes of saving after the last dock task. Not yet confirmed by a clean full run.
    const long DockyardCost = 20000L;
    const int DockyardLevel = 11;

    /// <summary>
    /// Numbers changed after the full-session guide-bot runs. Each line says what the run showed. Everything else keeps
    /// the values its own milestone builder wrote.
    /// </summary>
    static void Balance()
    {
        // Run 2: the bot (and any player) bled cash into hire tiles just by walking across them: $3,275 had drained into
        // the Crusher Operator tile while saving for the Furnace. A tile now needs 0.7 s of standing before it takes money
        // (crossing one takes about 0.45 s).
        var tile = Load<GameObject>(PrefabDir + "/Tiles/Tile_Purchase.prefab");
        SetMany(tile.GetComponent<PurchaseTile>(), ("engageDelay", 0.7f));
        EditorUtility.SetDirty(tile);
    }

    static void BuildTasks()
    {
        string dir = DataDir + "/Progression/Tasks";
        var chain = Load<TaskChain>(DataDir + "/Progression/TaskChain_Area1.asset");
        var so = new SerializedObject(chain);
        var mainProp = so.FindProperty("mainTasks");
        var main = new List<Object>();
        for (int i = 0; i < mainProp.arraySize; i++) main.Add(mainProp.GetArrayElementAtIndex(i).objectReferenceValue);
        main.RemoveAll(t => t == null || IsM8Task(((TaskDefinition)t).Id));

        // Run 5: nothing ever asked for the Market Runner, so the player stocked the market by hand until the Furnace
        // jammed behind a counter full of raw metal. The hire now follows the Hauler.
        main.RemoveAll(t => ((TaskDefinition)t).Id == "t24b_runner");
        int hauler = main.FindIndex(t => ((TaskDefinition)t).Id == "t24_hauler");
        if (hauler >= 0) main.Insert(hauler + 1, Task(dir, "t24b_runner", "Hire a market runner", TaskType.HireWorker, "runner", 1, 0, 60, 0));

        // The chain ended at t47_giant2. M8: grow the dock, then the session's last beat, the Dockyard.
        main.AddRange(new Object[]
        {
            Task(dir, "t48_dock2", "Truck dock to Lv.{0}", TaskType.ReachUpgradeLevel, "truck_bay", 2, 0, 300, 0),
            Task(dir, "t49_ship_ingots", "Load {0} ingots on the truck", TaskType.DeliverItems, "truck_bay", 72, 3000, 400, 0),
            Task(dir, "t50_dockyard", "Open the Dockyard", TaskType.ReachUpgradeLevel, "dockyard", 1, 10000, 600, 25),
        });
        SetArray(chain, "mainTasks", main.ToArray());
    }

    static bool IsM8Task(string id) => id.Length > 3 && id[0] == 't' && int.TryParse(id.Substring(1, 2), out int n) && n >= 48;

    static TaskDefinition Task(string dir, string id, string title, TaskType type, string target, long amount, long cash, int xp, int premium)
    {
        var t = LoadOrCreate<TaskDefinition>($"{dir}/Task_{id}.asset");
        SetMany(t, ("id", id), ("title", title), ("category", (int)TaskCategory.Main), ("type", (int)type), ("targetId", target), ("amount", amount),
            ("rewardCash", cash), ("rewardXp", xp), ("rewardPremium", premium));
        return t;
    }

    static void UpsertCatalog(UpgradeCatalog catalog, params (string id, int level, bool inPanel, string group)[] entries)
    {
        var so = new SerializedObject(catalog);
        var p = so.FindProperty("entries");
        foreach (var (id, level, inPanel, group) in entries)
        {
            int index = -1;
            for (int i = 0; i < p.arraySize; i++)
                if (p.GetArrayElementAtIndex(i).FindPropertyRelative("upgradeId").stringValue == id) index = i;
            if (index < 0)
            {
                index = p.arraySize;
                p.arraySize++;
            }

            var e = p.GetArrayElementAtIndex(index);
            e.FindPropertyRelative("upgradeId").stringValue = id;
            e.FindPropertyRelative("unlockLevel").intValue = level;
            e.FindPropertyRelative("inPanel").boolValue = inPanel;
            e.FindPropertyRelative("group").stringValue = group;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(catalog);
    }

    /// <summary>Sets one field on every element of a struct array (e.g. a level table's upgradeCost column).</summary>
    static void SetColumn(Object target, string array, string field, params object[] values)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(array);
        if (p == null)
        {
            Log.AppendLine($"!! {target.name}.{array} not found");
            return;
        }

        for (int i = 0; i < Mathf.Min(values.Length, p.arraySize); i++)
        {
            var child = p.GetArrayElementAtIndex(i).FindPropertyRelative(field);
            if (child == null) Log.AppendLine($"!! {array}[{i}].{field} not found");
            else if (values[i] != null) Assign(child, values[i], field);
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    // =====================================================================================
    // PREFABS
    // =====================================================================================

    public static string Prefabs()
    {
        Log.Clear();
        Directory.CreateDirectory(PropDir);
        var ship = BuildShip();
        BuildQuayCrane();
        BuildBollard();

        var icon = ArtIcons.Render("Icon_Dockyard", ship, new Vector3(16f, 215f, 0f), null, 0.9f);
        ArtAssets.Set(Load<ExpansionDefinition>(DataDir + "/World/Expansion_Dockyard.asset"), ("icon", icon));
        AssetDatabase.SaveAssets();
        return Log.ToString();
    }

    /// <summary>
    /// Cargo ship, 28 m, bow toward +Z: navy hull with a red waterline, white bridge and red funnel aft, three bays of
    /// containers on deck. Origin at the waterline amidships; it bobs on <see cref="AmbientMotion"/>.
    /// </summary>
    static GameObject BuildShip()
    {
        var k = new MeshKit();
        const float half = 14f, beam = 7.2f, deck = 3.3f;
        // Hull: raked bow, slightly raked stern (side profile extruded across the beam), red boot-topping, white sheer line.
        k.Prism(C(PC.Navy), Vector3.zero, new[]
        {
            new Vector2(-half + 0.8f, -1.6f), new Vector2(-half, deck), new Vector2(half, deck), new Vector2(half - 3.2f, -1.6f),
        }, beam, 0.25f);
        k.Prism(C(PC.RedDeep), Vector3.zero, new[]
        {
            new Vector2(-half + 0.75f, -1.62f), new Vector2(-half + 0.45f, 0.35f), new Vector2(half - 1.5f, 0.35f), new Vector2(half - 3.15f, -1.62f),
        }, beam + 0.06f, 0.2f);
        k.Box(C(PC.White), new Vector3(0f, deck - 0.12f, -0.4f), new Vector3(beam + 0.08f, 0.14f, half * 2f - 1.6f), 0.02f);
        k.Box(C(PC.GrayLight), new Vector3(0f, deck + 0.03f, -0.2f), new Vector3(beam - 0.5f, 0.08f, half * 2f - 2.2f), 0.02f);
        // Bulwark at the bow, anchor, name plate.
        k.Box(C(PC.Navy), new Vector3(0f, deck + 0.45f, half - 1.2f), new Vector3(beam - 0.6f, 0.9f, 1.6f), 0.2f);
        k.Box(C(PC.Charcoal), new Vector3(-beam * 0.5f - 0.03f, deck - 0.9f, half - 2.4f), new Vector3(0.06f, 0.9f, 0.5f), 0.02f);
        k.Box(C(PC.White), new Vector3(-beam * 0.5f - 0.03f, deck - 0.7f, half - 5.2f), new Vector3(0.04f, 0.36f, 2.4f), 0.01f);
        // Superstructure aft: accommodation block, bridge with wings and windows, funnel, mast, lifeboat.
        const float az = -half + 4.2f;
        k.Box(C(PC.White), new Vector3(0f, deck + 1.9f, az), new Vector3(beam - 1.0f, 3.8f, 4.2f), 0.18f);
        k.Box(C(PC.White), new Vector3(0f, deck + 4.5f, az + 0.3f), new Vector3(beam + 0.6f, 1.5f, 2.8f), 0.14f);
        k.Box(C(PC.Glass), new Vector3(0f, deck + 4.65f, az + 1.72f), new Vector3(beam - 0.4f, 0.62f, 0.06f), 0.03f);
        foreach (int side in new[] { -1, 1 })
        {
            k.Box(C(PC.Glass), new Vector3(side * (beam * 0.5f + 0.31f), deck + 4.65f, az + 0.4f), new Vector3(0.06f, 0.62f, 2.0f), 0.03f);
            for (int row = 0; row < 2; row++)
                for (int i = 0; i < 4; i++)
                    k.Box(C(PC.SteelBlue), new Vector3(side * (beam * 0.5f - 0.49f), deck + 1.1f + row * 1.4f, az - 1.3f + i * 0.9f), new Vector3(0.04f, 0.5f, 0.5f), 0.02f);
        }

        k.Box(C(PC.GrayLight), new Vector3(0f, deck + 5.3f, az + 0.3f), new Vector3(beam + 0.7f, 0.1f, 2.9f), 0.03f);
        k.Cylinder(C(PC.Red), new Vector3(0f, deck + 6.3f, az - 1.2f), 0.85f, 2.2f, 12, new Vector3(-6f, 0f, 0f), 0.12f, 0.7f);
        k.Cylinder(C(PC.Charcoal), new Vector3(0f, deck + 7.35f, az - 1.3f), 0.72f, 0.4f, 12, new Vector3(-6f, 0f, 0f), 0.05f);
        k.Box(C(PC.White), new Vector3(0f, deck + 6.35f, az - 1.2f), new Vector3(1.75f, 0.45f, 1.75f), 0.1f);
        k.Cylinder(C(PC.GrayLight), new Vector3(0f, deck + 6.6f, az + 1.0f), 0.07f, 2.6f, 6, default, 0.01f);
        k.Box(C(PC.GrayLight), new Vector3(0f, deck + 7.4f, az + 1.0f), new Vector3(1.6f, 0.07f, 0.07f), 0.01f);
        k.Sphere(C(PC.White), new Vector3(0f, deck + 7.95f, az + 1.0f), new Vector3(0.32f, 0.32f, 0.32f), 8, 5);
        k.Box(C(PC.Orange), new Vector3(beam * 0.5f - 0.7f, deck + 0.75f, az - 2.9f), new Vector3(0.9f, 0.7f, 2.0f), 0.3f);
        // Deck cargo: three bays, three rows across, two or three tiers. The quay-side top slots are free (work in progress).
        PC[] colors = { PC.Red, PC.Blue, PC.Yellow, PC.Teal, PC.Orange, PC.Green, PC.Cream, PC.Purple, PC.SteelBlue };
        var rng = new System.Random(88);
        for (int bay = 0; bay < 3; bay++)
            for (int row = 0; row < 3; row++)
            {
                int tiers = row == 0 && bay == 1 ? 1 : row == 0 ? 2 : 2 + rng.Next(2);
                for (int tier = 0; tier < tiers; tier++)
                    Container(k, new Vector3(-2.2f + row * 2.2f, deck + 0.1f + tier * 1.75f, -3.6f + bay * 5.4f), colors[rng.Next(colors.Length)]);
            }

        // Hatch coamings between the bays and a foremast.
        for (int i = 0; i < 4; i++) k.Box(C(PC.Charcoal), new Vector3(0f, deck + 0.2f, -6.3f + i * 5.4f), new Vector3(beam - 0.8f, 0.3f, 0.3f), 0.04f);
        k.Cylinder(C(PC.GrayLight), new Vector3(0f, deck + 2.2f, half - 2.4f), 0.09f, 3.6f, 6, default, 0.01f);
        k.Sphere(C(PC.Red), new Vector3(0f, deck + 4.1f, half - 2.4f), Vector3.one * 0.16f, 6, 4);

        var root = new GameObject("Prop_Ship");
        var hull = new GameObject("Hull").transform;
        hull.SetParent(root.transform, false);
        ArtAssets.MeshObject("Mesh", hull, ArtAssets.SaveMesh(MeshModel, "Ship", k.ToPaletteMesh("Ship")), new[] { ArtPalette.Material });
        // Slow swell: a small roll about the keel and a gentle heave.
        var roll = hull.gameObject.AddComponent<AmbientMotion>();
        ArtAssets.Set(roll, ("mode", (int)AmbientMotion.Mode.Swing), ("axis", Vector3.forward), ("amount", 1.1f), ("frequency", 0.11f), ("randomPhase", false));
        var heave = root.AddComponent<AmbientMotion>();
        ArtAssets.Set(heave, ("mode", (int)AmbientMotion.Mode.Bob), ("axis", Vector3.up), ("amount", 0.12f), ("frequency", 0.16f), ("randomPhase", false));
        Log.AppendLine("built Prop_Ship");
        return ArtAssets.SavePrefab(root, PropDir + "/Prop_Ship.prefab");
    }

    /// <summary>A 20-foot toy container (2.1 x 1.7 x 5) with door bars and corner castings, long axis along Z.</summary>
    static void Container(MeshKit k, Vector3 pos, PC color)
    {
        k.Push(pos);
        k.Box(C(color), new Vector3(0f, 0.85f, 0f), new Vector3(2.1f, 1.7f, 5.0f), 0.04f);
        for (float z = -2.2f; z <= 2.21f; z += 0.55f)
            foreach (int side in new[] { -1, 1 })
                k.Box(C(color), new Vector3(side * 1.06f, 0.85f, z), new Vector3(0.05f, 1.5f, 0.16f), 0.01f);
        foreach (float y in new[] { 0.05f, 1.66f })
            foreach (int side in new[] { -1, 1 })
                k.Box(C(PC.Charcoal), new Vector3(side * 1.04f, y, 0f), new Vector3(0.1f, 0.1f, 5.02f), 0.02f);
        k.Pop();
    }

    /// <summary>
    /// Ship-to-shore gantry crane. Local frame: rails run along Z, the boom reaches over the water toward +X. A trolley
    /// with a spreader and a container rides the boom on an <see cref="AmbientPath"/> (pausing over ship and quay).
    /// </summary>
    static GameObject BuildQuayCrane()
    {
        const float gauge = 3.6f, span = 3.4f, portal = 9f, boomY = 10.6f, boomOut = 14.5f, boomBack = -7.5f;
        var k = new MeshKit();
        // Bogies and sill beams along the rails.
        foreach (int lx in new[] { -1, 1 })
        {
            float x = lx * gauge;
            k.Box(C(PC.Charcoal), new Vector3(x, 0.3f, 0f), new Vector3(0.7f, 0.5f, span * 2f + 1.6f), 0.08f);
            foreach (int lz in new[] { -1, 1 })
            {
                k.Box(C(PC.Charcoal), new Vector3(x, 0.26f, lz * (span + 0.3f)), new Vector3(0.9f, 0.52f, 1.5f), 0.1f);
                // Legs with a hazard base, and the cross braces that make the portal.
                k.Box(C(PC.Blue), new Vector3(x, portal * 0.5f + 0.4f, lz * span), new Vector3(0.62f, portal, 0.62f), 0.08f);
                for (int s = 0; s < 3; s++)
                    k.Box(C(s % 2 == 0 ? PC.Yellow : PC.Charcoal), new Vector3(x, 0.75f + s * 0.34f, lz * span), new Vector3(0.66f, 0.34f, 0.66f), 0.02f);
            }

            k.Box(C(PC.Blue), new Vector3(x, portal + 0.4f, 0f), new Vector3(0.6f, 0.6f, span * 2f + 0.6f), 0.08f);
            k.Box(C(PC.Blue), new Vector3(x, 4.6f, 0f), new Vector3(0.36f, 0.36f, span * 2f), 0.05f);
            k.Box(C(PC.BlueDeep), new Vector3(x, 6.9f, 0f), new Vector3(0.2f, 0.2f, Mathf.Sqrt(span * span * 4f + 4.4f * 4.4f)), 0.03f,
                new Vector3(Mathf.Atan2(4.4f, span * 2f) * Mathf.Rad2Deg * lx, 0f, 0f));
        }

        foreach (int lz in new[] { -1, 1 })
            k.Box(C(PC.Blue), new Vector3(0f, portal + 0.4f, lz * span), new Vector3(gauge * 2f + 0.6f, 0.6f, 0.6f), 0.08f);
        // Boom (twin girders) from the back reach to the tip over the ship, A-frame with forestays, machinery house, cab.
        foreach (int lz in new[] { -1, 1 })
        {
            float z = lz * 1.05f;
            k.Box(C(PC.Yellow), new Vector3((boomOut + boomBack) * 0.5f, boomY, z), new Vector3(boomOut - boomBack, 0.55f, 0.36f), 0.06f);
            for (float x = boomBack + 1.2f; x < boomOut - 0.5f; x += 2.2f)
                k.Box(C(PC.YellowDeep), new Vector3(x, boomY - 0.02f, z + lz * 0.19f), new Vector3(0.16f, 0.6f, 0.03f), 0.01f);
            k.Box(C(PC.Blue), new Vector3(gauge, portal + 3.3f, z), new Vector3(0.36f, 5.4f, 0.36f), 0.05f, new Vector3(0f, 0f, 8f));
            k.Box(C(PC.Blue), new Vector3(-gauge + 1.0f, portal + 2.6f, z), new Vector3(0.3f, 5.6f, 0.3f), 0.05f, new Vector3(0f, 0f, -38f));
            // Forestay to the boom tip and backstay to the back reach.
            Stay(k, new Vector3(gauge - 0.35f, portal + 5.9f, z), new Vector3(boomOut - 1.2f, boomY + 0.3f, z));
            Stay(k, new Vector3(gauge - 0.35f, portal + 5.9f, z), new Vector3(boomOut * 0.45f, boomY + 0.3f, z));
            Stay(k, new Vector3(gauge - 0.35f, portal + 5.9f, z), new Vector3(boomBack + 1.0f, boomY + 0.3f, z));
        }

        for (float x = boomBack + 0.4f; x <= boomOut; x += 2.9f) k.Box(C(PC.YellowDeep), new Vector3(x, boomY, 0f), new Vector3(0.22f, 0.3f, 2.2f), 0.03f);
        k.Box(C(PC.Blue), new Vector3(gauge - 0.35f, portal + 6.0f, 0f), new Vector3(0.5f, 0.4f, 2.6f), 0.06f);
        k.Box(C(PC.White), new Vector3(boomBack + 2.6f, boomY + 1.35f, 0f), new Vector3(4.2f, 2.0f, 2.9f), 0.15f);
        k.Box(C(PC.Blue), new Vector3(boomBack + 2.6f, boomY + 2.4f, 0f), new Vector3(4.4f, 0.16f, 3.1f), 0.04f);
        for (int i = 0; i < 3; i++) k.Box(C(PC.Glass), new Vector3(boomBack + 1.4f + i * 1.2f, boomY + 1.5f, -1.47f), new Vector3(0.7f, 0.6f, 0.04f), 0.02f);
        k.Box(C(PC.Red), new Vector3(boomOut - 0.2f, boomY + 0.5f, 0f), new Vector3(0.3f, 0.3f, 0.3f), 0.08f);
        // Stair tower on a land-side leg and the big number plate.
        k.Box(C(PC.GrayLight), new Vector3(-gauge - 0.6f, portal * 0.5f, -span), new Vector3(0.5f, portal, 0.5f), 0.03f);
        k.Box(C(PC.White), new Vector3(-gauge, portal - 1.2f, 0f), new Vector3(0.1f, 1.3f, 2.6f), 0.04f);

        var root = new GameObject("Prop_QuayCrane");
        ArtAssets.MeshObject("Frame", root.transform, ArtAssets.SaveMesh(MeshModel, "QuayCrane", k.ToPaletteMesh("QuayCrane")), new[] { ArtPalette.Material });

        // Trolley: operator cab, hoist ropes, spreader, one container.
        var t = new MeshKit();
        t.Box(C(PC.Charcoal), new Vector3(0f, -0.4f, 0f), new Vector3(1.6f, 0.4f, 2.4f), 0.06f);
        t.Box(C(PC.White), new Vector3(0.2f, -1.2f, -1.0f), new Vector3(1.3f, 1.2f, 1.2f), 0.14f);
        t.Box(C(PC.Glass), new Vector3(0.87f, -1.3f, -1.0f), new Vector3(0.04f, 0.8f, 0.95f), 0.03f);
        foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
                t.Cylinder(C(PC.Charcoal), new Vector3(sx * 0.6f, -2.5f, sz * 1.9f + 0.4f), 0.03f, 3.8f, 5, default, 0.005f);
        t.Box(C(PC.Yellow), new Vector3(0f, -4.45f, 0.4f), new Vector3(1.9f, 0.22f, 4.9f), 0.05f);
        t.Box(C(PC.Charcoal), new Vector3(0f, -4.3f, 0.4f), new Vector3(0.9f, 0.3f, 1.2f), 0.05f);
        Container(t, new Vector3(0f, -6.27f, 0.4f), PC.Orange);
        var trolley = ArtAssets.MeshObject("Trolley", root.transform, ArtAssets.SaveMesh(MeshModel, "QuayCraneTrolley", t.ToPaletteMesh("QuayCraneTrolley")),
            new[] { ArtPalette.Material }, new Vector3(-1f, boomY, 0f));
        var route = new GameObject("TrolleyRoute").transform;
        route.SetParent(root.transform, false);
        var points = new List<Object>();
        foreach (float x in new[] { -1f, boomOut - 3.8f })
        {
            var p = new GameObject("P" + points.Count).transform;
            p.SetParent(route, false);
            p.localPosition = new Vector3(x, boomY, 0f);
            points.Add(p);
        }

        var path = trolley.AddComponent<AmbientPath>();
        ArtAssets.Set(path, ("speed", 1.6f), ("pause", 2.4f), ("loop", true), ("wheelRadius", 0.3f));
        ArtAssets.SetArray(path, "points", points.ToArray());
        ArtAssets.SetArray(path, "wheels", Array.Empty<Object>());

        // Only the legs block the player.
        foreach (int lx in new[] { -1, 1 })
            foreach (int lz in new[] { -1, 1 })
            {
                var leg = new GameObject("LegCollider");
                leg.transform.SetParent(root.transform, false);
                leg.transform.localPosition = new Vector3(lx * gauge, 1.2f, lz * span);
                leg.AddComponent<BoxCollider>().size = new Vector3(1.0f, 2.4f, 1.6f);
            }

        Log.AppendLine("built Prop_QuayCrane");
        return ArtAssets.SavePrefab(root, PropDir + "/Prop_QuayCrane.prefab");
    }

    /// <summary>A thin tie rod between two points (crane stays).</summary>
    static void Stay(MeshKit k, Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        k.Box(C(PC.Charcoal), (from + to) * 0.5f, new Vector3(d.magnitude, 0.1f, 0.1f), 0.02f, new Vector3(0f, 0f, angle));
    }

    static void BuildBollard()
    {
        var k = new MeshKit();
        k.Cylinder(C(PC.Charcoal), new Vector3(0f, 0.06f, 0f), 0.34f, 0.12f, 10, default, 0.02f);
        k.Cylinder(C(PC.Yellow), new Vector3(0f, 0.36f, 0f), 0.2f, 0.5f, 10, default, 0.03f, 0.16f);
        k.Cylinder(C(PC.Yellow), new Vector3(0f, 0.66f, 0f), 0.3f, 0.16f, 10, default, 0.05f);
        var root = new GameObject("Prop_Bollard");
        var go = ArtAssets.MeshObject("Mesh", root.transform, ArtAssets.SaveMesh(MeshModel, "Bollard", k.ToPaletteMesh("Bollard")), new[] { ArtPalette.Material });
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        ArtAssets.SavePrefab(root, PropDir + "/Prop_Bollard.prefab");
    }

    // =====================================================================================
    // SCENE
    // =====================================================================================

    public static string Scene()
    {
        Log.Clear();
        var active = SceneManager.GetActiveScene();
        if (active.path == ScenePath && active.isDirty) EditorSceneManager.SaveScene(active);
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var systems = Root(scene, "_Systems");
        var gameplay = Root(scene, "_Gameplay");
        var environment = Root(scene, "_Environment");
        var font = Load<TMP_FontAsset>(FontDir + "/GROBOLD SDF.asset");
        var worldText = Load<Material>(FontDir + "/GROBOLD World.mat");
        var hallContent = gameplay.Find("FurnaceHall/Content");

        // ---------- save/load ----------
        var saveObject = FindOrCreate(systems, "SaveManager");
        saveObject.SetAsFirstSibling();
        if (!saveObject.TryGetComponent(out SaveManager _)) saveObject.gameObject.AddComponent<SaveManager>();
        var items = AssetDatabase.FindAssets("t:ItemDefinition", new[] { DataDir + "/Items" })
            .Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(i => i != null).OrderBy(i => i.Id).Cast<Object>().ToArray();
        SetArray(systems.Find("ItemPool").GetComponent<ItemPool>(), "catalog", items);
        Log.AppendLine($"item catalog: {items.Length} items");

        BuildDockyard(gameplay, environment, hallContent, font, worldText);
        AddDropBlockers(environment, gameplay);
        Rebake(environment, gameplay);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Log.AppendLine("Saved " + ScenePath);
        return Log.ToString();
    }

    /// <summary>
    /// The quay, water, ship and cranes are permanent scenery (visible over the wall from the first minute in the plant);
    /// Gate 4 and what stands on the quay belong to the Dockyard expansion and arrive with the reveal.
    /// </summary>
    static void BuildDockyard(Transform gameplay, Transform environment, Transform hallContent, TMP_FontAsset font, Material worldText)
    {
        var fences = environment.Find("Fences");
        float quayW = QuayEast - WallX, quayD = QuayNorth - QuaySouth, midX = (WallX + QuayEast) * 0.5f, midZ = (QuaySouth + QuayNorth) * 0.5f;

        // ---------- make room: the two backdrop warehouses stood where the quay is; hall decor stood in the gateway ----------
        var backdrop = environment.Find("ArtBackdrop");
        RemoveNear(backdrop, "Prop_Warehouse", new Vector3(86f, 0f, 22f), 2f);
        RemoveNear(backdrop, "Prop_Warehouse", new Vector3(86f, 0f, 36f), 2f);
        var hallDecor = hallContent.Find("ArtDecor");
        MoveNear(hallDecor, "Prop_GasCylinders", new Vector3(73.6f, 0f, 24.4f), new Vector3(74.9f, 0f, 20.9f));
        MoveNear(hallDecor, "Prop_PalletStack", new Vector3(73.8f, 0f, 27.6f), new Vector3(74.4f, 0f, 29.6f));

        // ---------- the plant's east wall becomes wall | gate | wall ----------
        foreach (var n in new[] { "Wall_76_42_76_11", "Wall_76_42_76_27", "Wall_76_21_76_11" })
        {
            var old = fences.Find(n);
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }

        Wall(fences, "Wall_76_42_76_27", new Vector3(WallX, 0f, WallNorth), new Vector3(WallX, 0f, GateZ + GateHalf + 0.2f), 31);
        Wall(fences, "Wall_76_21_76_11", new Vector3(WallX, 0f, GateZ - GateHalf - 0.2f), new Vector3(WallX, 0f, WallSouth), 32);

        var oldGate = environment.Find("Gate_A2_A4");
        if (oldGate != null) Object.DestroyImmediate(oldGate.gameObject);
        var gate = Empty("Gate_A2_A4", environment, new Vector3(WallX, 0f, GateZ));
        gate.localRotation = Quaternion.Euler(0f, 90f, 0f);
        gate.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        var posts = new MeshKit();
        foreach (int side in new[] { -1, 1 })
        {
            posts.Box(C(PC.Yellow), new Vector3(side * GateHalf, 1.3f, 0f), new Vector3(0.5f, 2.6f, 0.5f), 0.06f);
            posts.Box(C(PC.Charcoal), new Vector3(side * GateHalf, 0.2f, 0f), new Vector3(0.62f, 0.4f, 0.62f), 0.05f);
            posts.Cylinder(C(PC.Orange), new Vector3(side * GateHalf, 2.72f, 0f), 0.12f, 0.2f, 8, default, 0.04f);
        }

        ArtAssets.MeshObject("Posts", gate, ArtAssets.SaveMesh(MeshModel, "GatePosts", posts.ToPaletteMesh("GatePosts")), new[] { ArtPalette.Material });
        var barriers = new List<Object>();
        var bars = new MeshKit();
        for (int i = 0; i < 5; i++) bars.Box(C(i % 2 == 0 ? PC.Blue : PC.White), new Vector3(-2f + i, 1.05f, 0f), new Vector3(1f, 0.26f, 0.2f), 0.02f);
        for (int i = 0; i < 5; i++) bars.Box(C(i % 2 == 0 ? PC.White : PC.Blue), new Vector3(-2f + i, 0.55f, 0f), new Vector3(1f, 0.2f, 0.16f), 0.02f);
        var barsGo = ArtAssets.MeshObject("Bars", gate, ArtAssets.SaveMesh(MeshModel, "GateBars", bars.ToPaletteMesh("GateBars")), new[] { ArtPalette.Material });
        var gateBox = barsGo.AddComponent<BoxCollider>();
        gateBox.center = new Vector3(0f, 1f, 0f);
        gateBox.size = new Vector3(GateHalf * 2f - 0.4f, 2f, 0.6f);
        var obstacle = barsGo.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = gateBox.center;
        obstacle.size = gateBox.size;
        obstacle.carving = true;
        barriers.Add(barsGo.transform);

        // ---------- permanent scenery: quay slab, quay wall, water, ship, cranes, edge blockers ----------
        var old2 = environment.Find("DockyardBackdrop");
        if (old2 != null) Object.DestroyImmediate(old2.gameObject);
        var scenery = Empty("DockyardBackdrop", environment, Vector3.zero);
        scenery.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        var slab = new MeshKit();
        slab.Box(0, Vector3.zero, new Vector3(quayW + 0.6f, 0.4f, quayD + 1.2f), 0f);
        var slabGo = ArtAssets.MeshObject("Quay", scenery, ArtAssets.SaveMesh(MeshModel, "Quay", slab.ToMesh("Quay", 1)),
            new[] { Load<Material>(MatDir + "/M_Ground_Concrete.mat") }, new Vector3(midX + 0.3f, -0.2f, midZ), default, false);
        slabGo.AddComponent<BoxCollider>();
        slabGo.layer = LayerMask.NameToLayer("Ground");
        GameObjectUtility.SetStaticEditorFlags(slabGo, StaticEditorFlags.BatchingStatic);
        var edge = new MeshKit();
        // Quay wall with fender tyres, a yellow safety line, crane rails.
        edge.Box(C(PC.Concrete), new Vector3(QuayEast + 0.35f, -0.9f, midZ), new Vector3(0.7f, 2.2f, quayD + 1.4f), 0.06f);
        edge.Box(C(PC.Yellow), new Vector3(QuayEast - 0.25f, 0.012f, midZ), new Vector3(0.22f, 0.02f, quayD), 0f);
        for (float z = QuaySouth + 2f; z < QuayNorth - 1f; z += 3.2f)
            edge.Torus(C(PC.Rubber), new Vector3(QuayEast + 0.78f, -0.5f, z), 0.42f, 0.17f, 12, 6, new Vector3(0f, 0f, 90f));
        foreach (float x in new[] { CraneX - 3.6f, CraneX + 3.6f })
            edge.Box(C(PC.Charcoal), new Vector3(x, 0.03f, midZ), new Vector3(0.16f, 0.06f, quayD - 0.6f), 0.01f);
        ArtAssets.MeshObject("QuayEdge", scenery, ArtAssets.SaveMesh(MeshModel, "QuayEdge", edge.ToPaletteMesh("QuayEdge")), new[] { ArtPalette.Material });

        var water = GameObject.CreatePrimitive(PrimitiveType.Quad);
        water.name = "Water";
        Object.DestroyImmediate(water.GetComponent<Collider>());
        water.transform.SetParent(scenery, false);
        water.transform.SetPositionAndRotation(new Vector3(157f, -1.1f, 30f), Quaternion.Euler(90f, 0f, 0f));
        water.transform.localScale = new Vector3(120f, 150f, 1f);
        water.GetComponent<Renderer>().sharedMaterial = Mat("Water", new Color(0.25f, 0.6f, 0.82f), 0.92f, 0f, Detail.Plain);
        water.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        // Ground north and south of the quay so nothing shows the void behind the wall.
        var shore = new MeshKit();
        shore.Box(0, new Vector3(midX + 0.3f, -0.21f, QuayNorth + 13f), new Vector3(quayW + 0.6f, 0.4f, 25f), 0f);
        ArtAssets.MeshObject("ShoreNorth", scenery, ArtAssets.SaveMesh(MeshModel, "Shore", shore.ToMesh("Shore", 1)),
            new[] { Load<Material>(MatDir + "/M_Ground_Dirt.mat") }, default, default, false);

        var ship = Place("Prop_Ship", scenery, ShipPos, 0f);
        _ = ship;
        foreach (float z in CraneZ) Place("Prop_QuayCrane", scenery, new Vector3(CraneX, 0f, z), 0f);
        Blocker(scenery, "Edge_East", new Vector3(QuayEast - 0.1f, 1f, midZ), new Vector3(0.4f, 2f, quayD + 1f));
        Blocker(scenery, "Edge_North", new Vector3(midX, 1f, QuayNorth - 0.2f), new Vector3(quayW, 2f, 0.4f));
        Blocker(scenery, "Edge_South", new Vector3(midX, 1f, QuaySouth + 0.2f), new Vector3(quayW, 2f, 0.4f));

        // ---------- the expansion ----------
        var root = FindOrCreate(gameplay, "Dockyard");
        root.position = Vector3.zero;
        if (!root.TryGetComponent(out Expansion expansion)) expansion = root.gameObject.AddComponent<Expansion>();
        var locked = ResetChild(root, "LockedOnly");
        locked.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        // Sign on a bracket by the gate's north post, facing the camera (south) like every other sign.
        var signKit = new MeshKit();
        signKit.Box(C(PC.Charcoal), Vector3.zero, new Vector3(4.2f, 1.3f, 0.14f), 0.05f);
        signKit.Box(C(PC.Blue), new Vector3(0f, 0f, -0.06f), new Vector3(4.0f, 1.1f, 0.06f), 0.03f);
        var postKit = new MeshKit();
        foreach (int side in new[] { -1, 1 }) postKit.Box(C(PC.Charcoal), new Vector3(side * 1.8f, 1.35f, 0f), new Vector3(0.16f, 2.7f, 0.16f), 0.03f);
        var signRoot = Empty("Sign", locked, new Vector3(WallX - 2.6f, 0f, GateZ + GateHalf + 0.9f));
        ArtAssets.MeshObject("Posts", signRoot, ArtAssets.SaveMesh(MeshModel, "GateSignPosts", postKit.ToPaletteMesh("GateSignPosts")), new[] { ArtPalette.Material });
        var board = ArtAssets.MeshObject("Board", signRoot, ArtAssets.SaveMesh(MeshModel, "GateSign", signKit.ToPaletteMesh("GateSign")), new[] { ArtPalette.Material },
            new Vector3(0f, 2.8f, 0f), new Vector3(16f, 0f, 0f));
        var text = Text3D("SignText", board.transform, new Vector3(0f, 0f, -0.11f), font, worldText, 5f, Color.white, "<size=75%>DOCKYARD</size>\nLOCKED");
        text.rectTransform.sizeDelta = new Vector2(3.8f, 1f);

        var content = ResetChild(root, "Content");
        content.gameObject.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        // Reveal order: floor markings, container stacks (two blocks leaving a lane from the gate to the water), bollards,
        // lights, forklift, flags, the "more to come" sign.
        var lines = new MeshKit();
        lines.Box(C(PC.Yellow), new Vector3(midX - 2f, 0.012f, GateZ - 2.6f), new Vector3(quayW - 9f, 0.02f, 0.14f), 0f);
        lines.Box(C(PC.Yellow), new Vector3(midX - 2f, 0.012f, GateZ + 2.6f), new Vector3(quayW - 9f, 0.02f, 0.14f), 0f);
        for (int i = 0; i < 7; i++)
            lines.Box(C(PC.White), new Vector3(WallX + 2.4f + i * 2.2f, 0.012f, GateZ), new Vector3(1.1f, 0.02f, 0.2f), 0f);
        ArtAssets.MeshObject("Lines", content, ArtAssets.SaveMesh(MeshModel, "QuayLines", lines.ToPaletteMesh("QuayLines")), new[] { ArtPalette.Material },
            default, default, false);
        string[] cols = { "Blue", "Red", "Green", "Orange", "Yellow", "Teal" };
        var rng = new System.Random(48);
        void Stack(string name, float x, float z, int tiers)
        {
            var stack = Empty(name, content, new Vector3(x, 0f, z));
            for (int t = 0; t < tiers; t++)
                Place("Prop_Container_" + cols[rng.Next(cols.Length)], stack, new Vector3(x + (t % 2) * 0.12f, t * 2.6f, z), 90f + (float)(rng.NextDouble() - 0.5) * 3f);
            var box = stack.gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 1.3f, 0f);
            box.size = new Vector3(6.2f, 2.6f, 2.6f);
        }

        Stack("Stack_S1", 80.4f, 13.6f, 2);
        Stack("Stack_S2", 80.6f, 16.5f, 1);
        Stack("Stack_S3", 87.4f, 13.6f, 1);
        Stack("Stack_N1", 80.4f, 43.6f, 3);
        Stack("Stack_N2", 80.6f, 40.6f, 3);
        Stack("Stack_N3", 80.5f, 37.6f, 2);
        Stack("Stack_N4", 80.6f, 34.4f, 2);
        Stack("Stack_N5", 80.5f, 30.6f, 1);
        var bollards = Empty("Bollards", content, Vector3.zero);
        for (float z = QuaySouth + 3f; z < QuayNorth - 2f; z += 4.2f) Place("Prop_Bollard", bollards, new Vector3(QuayEast - 1.1f, 0f, z), 0f);
        var lights = Empty("Lights", content, Vector3.zero);
        Place("Prop_LightPole", lights, new Vector3(86.4f, 0f, 20.4f), 0f);
        Place("Prop_LightPole", lights, new Vector3(86.4f, 0f, 27.6f), 180f);
        Place("Prop_Forklift", content, new Vector3(91.6f, 0f, 18.6f), 140f);
        Place("Prop_PalletBales", content, new Vector3(93.4f, 0f, 17.0f), 12f);
        Place("Prop_PalletBales", content, new Vector3(94.8f, 0f, 17.6f), -20f);
        var flags = Empty("Flags", content, Vector3.zero);
        Place("Prop_Flag", flags, new Vector3(WallX + 1.2f, 0f, GateZ - 3.4f), 0f);
        Place("Prop_Flag", flags, new Vector3(WallX + 1.2f, 0f, GateZ + 3.4f), 0f);
        // "More to come": a boarded-off slip at the north end.
        var soon = Empty("ComingSoon", content, new Vector3(86.4f, 0f, 29.4f));
        var soonKit = new MeshKit();
        foreach (int side in new[] { -1, 1 }) soonKit.Box(C(PC.Charcoal), new Vector3(side * 2.1f, 1.2f, 0f), new Vector3(0.18f, 2.4f, 0.18f), 0.03f);
        soonKit.Box(C(PC.Charcoal), new Vector3(0f, 2.5f, 0f), new Vector3(4.8f, 1.5f, 0.14f), 0.05f, new Vector3(16f, 0f, 0f));
        soonKit.Box(C(PC.Yellow), new Vector3(0f, 2.5f, -0.06f), new Vector3(4.6f, 1.3f, 0.06f), 0.03f, new Vector3(16f, 0f, 0f));
        for (int i = 0; i < 6; i++)
            soonKit.Box(C(i % 2 == 0 ? PC.Yellow : PC.Charcoal), new Vector3(-2.0f + i * 0.8f, 0.5f, 0.4f), new Vector3(0.8f, 0.22f, 0.12f), 0.02f);
        ArtAssets.MeshObject("Board", soon, ArtAssets.SaveMesh(MeshModel, "SoonSign", soonKit.ToPaletteMesh("SoonSign")), new[] { ArtPalette.Material });
        var soonText = Text3D("Text", soon, new Vector3(0f, 2.5f, -0.14f), font, worldText, 5f, Navy, "<size=70%>PRESS HALL + SHIPPING</size>\nCOMING SOON");
        soonText.transform.localRotation = Quaternion.Euler(16f, 0f, 0f);
        soonText.rectTransform.sizeDelta = new Vector2(4.4f, 1.2f);
        soon.gameObject.AddComponent<BoxCollider>().center = new Vector3(0f, 1f, 0.2f);
        soon.GetComponent<BoxCollider>().size = new Vector3(4.6f, 2f, 0.7f);

        // The purchase tile is inside the hall, in front of the gate.
        PlaceTile(hallContent, "Tile_Dockyard", Load<GameObject>(PrefabDir + "/Tiles/Tile_Purchase.prefab"), DockyardTile, "dockyard", "tile/dockyard");

        var focus = FindOrCreate(root, "Focus");
        focus.position = new Vector3(91.5f, 0f, GateZ + 5f);
        var area = new Bounds(new Vector3(midX, 0f, midZ), new Vector3(quayW - 1f, 4f, quayD - 1f));
        SetMany(expansion, ("definition", Load<ExpansionDefinition>(DataDir + "/World/Expansion_Dockyard.asset")), ("contentRoot", content),
            ("lockedOnly", locked.gameObject), ("area", area), ("focusPoint", focus),
            ("revealVfx", Load<GameObject>(PrefabDir + "/VFX/VFX_ConstructionBurst.prefab").GetComponent<ParticleSystem>()), ("focusHold", 6f),
            ("contentStagger", 0.22f), ("barrierSinkDepth", 2.6f));
        SetArray(expansion, "barriers", barriers.ToArray());
        SetStructArray(expansion, "materialSwaps", Array.Empty<Dictionary<string, object>>());
        foreach (Transform c in content) c.gameObject.SetActive(false);
        hallContent.Find("Tile_Dockyard").gameObject.SetActive(false);
    }

    /// <summary>
    /// Scrap mounds and big props get a <see cref="DropBlocker"/>: loose pieces that land on them hop back to open ground.
    /// (The full-session bot run dead-ended on a piece buried in the mound along the Back Lot's north wall.)
    /// Art_World.Scene does the same sweep after it rebuilds mounds and props.
    /// </summary>
    static void AddDropBlockers(Transform environment, Transform gameplay)
    {
        var roots = new[]
        {
            environment.Find("JunkPiles"), environment.Find("ArtDecor"), gameplay.Find("RecyclingPlant/Content"), gameplay.Find("FurnaceHall/Content"),
            gameplay.Find("HeavyYard/Content/Decor"), gameplay.Find("BackLot/Content"),
        };
        int count = 0;
        foreach (var root in roots)
        {
            if (root == null) continue;
            foreach (var box in root.GetComponentsInChildren<BoxCollider>(true))
            {
                var go = box.gameObject;
                if (box.isTrigger || !(go.name.StartsWith("Heap") || go.name.StartsWith("Prop_"))) continue;
                // Small props are within everyone's pickup reach; only footprints deep enough to bury a piece count.
                Vector3 size = Vector3.Scale(box.size, go.transform.lossyScale);
                if (Mathf.Min(size.x, size.z) < 0.9f || Mathf.Max(size.x, size.z) < 1.8f) continue;
                if (!go.TryGetComponent(out DropBlocker _)) go.AddComponent<DropBlocker>();
                count++;
            }
        }

        Log.AppendLine($"drop blockers: {count}");
    }

    static void Wall(Transform parent, string name, Vector3 from, Vector3 to, int seed)
    {
        var segment = new GameObject(name).transform;
        segment.SetParent(parent, false);
        Vector3 delta = to - from;
        float length = delta.magnitude;
        segment.position = from;
        segment.rotation = Quaternion.LookRotation(delta.normalized) * Quaternion.Euler(0f, -90f, 0f);
        var box = segment.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(length * 0.5f, 1.25f, 0f);
        box.size = new Vector3(length, 2.5f, 0.4f);
        var go = ArtAssets.MeshObject("Sheets", segment, ArtAssets.SaveMesh("Walls", name, CorrugatedWall(length, seed).ToPaletteMesh(name)), new[] { ArtPalette.Material });
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
    }

    /// <summary>Same sheets as Art_World.CorrugatedWall (which re-skins every Wall_* segment when it runs).</summary>
    static MeshKit CorrugatedWall(float length, int seed)
    {
        var rng = new System.Random(seed);
        PC[] colors = { PC.SteelBlue, PC.Gray, PC.Teal, PC.GrayLight, PC.SteelBlue, PC.RustDark, PC.Blue, PC.Cream };
        var k = new MeshKit();
        int panels = Mathf.Max(1, Mathf.CeilToInt(length / 2f));
        float w = length / panels;
        for (int i = 0; i < panels; i++)
        {
            var c = colors[rng.Next(colors.Length)];
            float x = (i + 0.5f) * w;
            float tilt = (float)(rng.NextDouble() - 0.5) * 3f, lean = (float)(rng.NextDouble() - 0.5) * 2f;
            float h = 2.25f + (float)rng.NextDouble() * 0.15f;
            k.Push(new Vector3(x, 0f, 0f), new Vector3(lean, 0f, tilt));
            k.Box(C(c), new Vector3(0f, h * 0.5f, 0f), new Vector3(w - 0.04f, h, 0.05f), 0f);
            for (float rx = -w * 0.5f + 0.16f; rx < w * 0.5f - 0.05f; rx += 0.32f)
                for (int side = -1; side <= 1; side += 2)
                    k.Box(C(c), new Vector3(rx, h * 0.5f, side * 0.045f), new Vector3(0.11f, h - 0.04f, 0.05f), 0f);
            if (rng.NextDouble() < 0.35)
                k.Box(C(PC.Rust), new Vector3((float)(rng.NextDouble() - 0.5) * w * 0.6f, 0.4f + (float)rng.NextDouble() * 0.6f, -0.08f), new Vector3(0.5f, 0.4f, 0.03f), 0.01f);
            k.Pop();
            k.Box(C(PC.Charcoal), new Vector3(i * w, 1.25f, 0f), new Vector3(0.16f, 2.5f, 0.16f), 0.03f);
        }

        k.Box(C(PC.Charcoal), new Vector3(length, 1.25f, 0f), new Vector3(0.16f, 2.5f, 0.16f), 0.03f);
        k.Box(C(PC.Yellow), new Vector3(length * 0.5f, 2.45f, 0f), new Vector3(length, 0.08f, 0.14f), 0.02f);
        return k;
    }

    /// <summary>NavMesh over Areas 1–3 with every expansion's content active (same recipe as M7_Build).</summary>
    static void Rebake(Transform environment, Transform gameplay)
    {
        var surface = environment.Find("NavMesh").GetComponent<NavMeshSurface>();
        var contents = new[] { "RecyclingPlant/Content", "FurnaceHall/Content", "HeavyYard/Content", "BackLot/Content", "TruckDock/Content" }
            .Select(gameplay.Find).Where(t => t != null).ToArray();
        var toggled = new List<GameObject>();
        foreach (var content in contents)
            foreach (Transform c in content)
                if (!c.gameObject.activeSelf)
                {
                    c.gameObject.SetActive(true);
                    toggled.Add(c.gameObject);
                }

        surface.buildHeightMesh = true;
        foreach (var belt in Object.FindObjectsByType<Conveyor>(FindObjectsInactive.Include))
        {
            if (!belt.TryGetComponent(out NavMeshModifier beltModifier)) beltModifier = belt.gameObject.AddComponent<NavMeshModifier>();
            beltModifier.overrideArea = true;
            beltModifier.area = NavMesh.GetAreaFromName("Not Walkable");
        }

        surface.BuildNavMesh();
        foreach (var go in toggled) go.SetActive(false);
        foreach (var content in contents)
            foreach (Transform c in content)
                c.gameObject.SetActive(false);
        string navPath = Path.Combine(Path.GetDirectoryName(ScenePath), Path.GetFileNameWithoutExtension(ScenePath)).Replace('\\', '/') + "/NavMesh-Area1.asset";
        if (File.Exists(navPath)) AssetDatabase.DeleteAsset(navPath);
        AssetDatabase.CreateAsset(surface.navMeshData, navPath);
        EditorUtility.SetDirty(surface);
        Log.AppendLine("navmesh baked; belt top walkable: " + NavMesh.SamplePosition(new Vector3(24f, 0.5f, 23f), out _, 0.25f, NavMesh.AllAreas) + " (must be False)");
    }

    // =====================================================================================
    // Helpers (builders compile one file at a time; geometry comes from ArtKit)
    // =====================================================================================

    static void RemoveNear(Transform parent, string namePrefix, Vector3 position, float radius)
    {
        if (parent == null) return;
        foreach (var t in parent.Cast<Transform>().ToArray())
        {
            if (!t.name.StartsWith(namePrefix)) continue;
            Vector3 d = t.position - position;
            d.y = 0f;
            if (d.magnitude > radius) continue;
            Log.AppendLine("removed " + t.name + " at " + t.position);
            Object.DestroyImmediate(t.gameObject);
        }
    }

    static void MoveNear(Transform parent, string namePrefix, Vector3 from, Vector3 to)
    {
        if (parent == null) return;
        foreach (Transform t in parent)
        {
            Vector3 d = t.position - from;
            d.y = 0f;
            if (t.name.StartsWith(namePrefix) && d.magnitude < 1f) t.position = to;
        }
    }

    static GameObject Place(string prop, Transform parent, Vector3 pos, float yaw)
    {
        var src = Load<GameObject>($"{PropDir}/{prop}.prefab");
        if (src == null) return null;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src, parent);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        return go;
    }

    static void Blocker(Transform parent, string name, Vector3 center, Vector3 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = center;
        go.AddComponent<BoxCollider>().size = size;
    }

    static TMP_Text Text3D(string name, Transform parent, Vector3 localPos, TMP_FontAsset font, Material mat, float size, Color color, string text)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.font = font;
        if (mat != null) tmp.fontSharedMaterial = mat;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.text = text;
        tmp.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        return tmp;
    }

    static Transform Empty(string name, Transform parent, Vector3 localPos)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = localPos;
        return t;
    }

    static void PlaceTile(Transform parent, string name, GameObject prefab, Vector3 position, string upgradeId, string anchorId)
    {
        var existing = parent.Find(name);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.name = name;
        go.transform.SetPositionAndRotation(position, Quaternion.identity);
        SetMany(go.GetComponent<PurchaseTile>(), ("upgradeId", upgradeId));
        SetMany(go.AddComponent<GuideAnchor>(), ("id", anchorId));
    }

    static Transform Root(Scene scene, string name) => scene.GetRootGameObjects().First(g => g.name == name).transform;

    static Transform FindOrCreate(Transform parent, string name)
    {
        var t = parent.Find(name);
        if (t != null) return t;
        t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    static Transform ResetChild(Transform parent, string name)
    {
        var existing = parent.Find(name);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    static T Load<T>(string path, bool warn = true) where T : Object
    {
        var a = AssetDatabase.LoadAssetAtPath<T>(path);
        if (a == null && warn) Log.AppendLine("!! missing " + path);
        return a;
    }

    static T LoadOrCreate<T>(string path) where T : ScriptableObject
    {
        var a = AssetDatabase.LoadAssetAtPath<T>(path);
        if (a != null) return a;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        a = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(a, path);
        return a;
    }

    static void SetMany(Object target, params (string field, object value)[] values)
    {
        var so = new SerializedObject(target);
        foreach (var (field, value) in values)
        {
            var p = so.FindProperty(field);
            if (p == null)
            {
                Log.AppendLine($"!! {target.GetType().Name}.{field} not found");
                continue;
            }

            Assign(p, value, field);
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    static void Assign(SerializedProperty p, object value, string field)
    {
        switch (value)
        {
            case Object o: p.objectReferenceValue = o; break;
            case float f: p.floatValue = f; break;
            case int i when p.propertyType == SerializedPropertyType.Enum: p.enumValueIndex = i; break;
            case int i when p.propertyType == SerializedPropertyType.Float: p.floatValue = i; break;
            case int i: p.intValue = i; break;
            case long l: p.longValue = l; break;
            case bool b: p.boolValue = b; break;
            case string s: p.stringValue = s; break;
            case Color c: p.colorValue = c; break;
            case Vector2 v2: p.vector2Value = v2; break;
            case Vector2Int v2i: p.vector2IntValue = v2i; break;
            case Vector3 v3: p.vector3Value = v3; break;
            case Bounds bounds: p.boundsValue = bounds; break;
            case null: p.objectReferenceValue = null; break;
            default: Log.AppendLine($"!! unsupported value for {field}"); break;
        }
    }

    static void SetArray(Object target, string field, Object[] values)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        if (p == null)
        {
            Log.AppendLine($"!! {target.GetType().Name}.{field} not found");
            return;
        }

        p.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    static void SetLongArray(Object target, string field, long[] values)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        p.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).longValue = values[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    static void SetStructArray(Object target, string field, Dictionary<string, object>[] elements)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        p.arraySize = elements.Length;
        for (int i = 0; i < elements.Length; i++)
        {
            var element = p.GetArrayElementAtIndex(i);
            foreach (var kv in elements[i])
            {
                var child = element.FindPropertyRelative(kv.Key);
                if (child == null) Log.AppendLine($"!! {field}[{i}].{kv.Key} not found");
                else Assign(child, kv.Value, kv.Key);
            }
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    static void Clips(string sfx, float volume, Vector2 pitch, params string[] files)
    {
        var def = LoadOrCreate<SfxDefinition>($"{DataDir}/Audio/Sfx_{sfx}.asset");
        var clips = files.Select(f => Load<AudioClip>($"{AudioDir}/{f}.ogg")).Where(c => c != null).Cast<Object>().ToArray();
        SetArray(def, "clips", clips);
        SetMany(def, ("volume", volume), ("pitchRange", pitch));
    }
}

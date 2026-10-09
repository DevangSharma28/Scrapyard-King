using System.Linq;
using System.Text;
using ScrapYardKing.Boosts;
using ScrapYardKing.Customers;
using ScrapYardKing.Economy;
using ScrapYardKing.EditorTools.Art;
using ScrapYardKing.Factory;
using ScrapYardKing.Progression;
using ScrapYardKing.Tiles;
using ScrapYardKing.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Fun pass F1 (owner's brief, 2026-10-09):
///  - no port: the Dockyard expansion, its ship dock, the quay, water, ship and cranes, Gate 4 and the Dockyard tile are
///    removed; the Furnace hall's east wall is closed again; the grass runs on east where the water was and two
///    warehouses stand there again (as before M8). Catalog entry, task t50 and the 50-diamond milestone go too;
///  - boost pads offer a video after the free burst: the same Overdrive for two minutes (Offer_MachineBoost);
///  - a fixed 2X CASH button on the left edge (Offer_Cash, two minutes), taken out of the rotating offer;
///  - customers are served 0.5 s apart, from a line that is full (7) at every desk level.
/// Run after HudLayout_Build, then R1_Build.Bake (the NavMesh lost the quay and gained the closed wall).
/// Re-running M8_Build or R6_Build brings the port back: run this again after them.
/// Entry points: Assets, Scene, All. Idempotent.
/// </summary>
public static class F1_Build
{
    const string Root = "Assets/_Project";
    const string DataDir = Root + "/Data";
    const string ScenePath = Root + "/Scenes/Area1_OldScrapYard.unity";
    const float WallX = 76f, WallNorth = 42f, WallSouth = 11f;
    static readonly StringBuilder Log = new();

    public static string All() => Assets() + Scene();

    // ================================================================== data

    public static string Assets()
    {
        Log.Clear();

        // the pad's video: Overdrive for two minutes
        var production = Load<BoostDefinition>("Boosts/Boost_Production.asset");
        var machineOffer = Asset<AdOfferDefinition>("Boosts/Offer_MachineBoost.asset");
        SetMany(machineOffer, ("id", "offer_machine_boost"), ("title", "2X MACHINE"), ("icon", production != null ? production.Icon : null),
            ("kind", (int)AdOfferKind.MachineBoost), ("showSeconds", 4f), ("cooldown", 60f), ("minLevel", 1), ("weight", 0f), ("dailyCap", 15),
            ("priority", 0));
        SetMany(Load<OverdriveConfig>("Factory/OverdriveConfig.asset"), ("videoOffer", machineOffer), ("videoSeconds", 120f), ("offerEvery", 90f));

        // 2X CASH lives on its own button now; the rotation keeps the rest
        SetMany(Load<AdOfferDefinition>("Boosts/Offer_Cash.asset"), ("weight", 0f), ("dailyCap", 8), ("cooldown", 480f));

        foreach (var c in new[] { "Customers/CustomerConfig.asset", "Customers/CustomerConfig_Market.asset" })
            SetMany(Load<CustomerConfig>(c), ("nextCustomerGap", 0.5f), ("serveFromDistance", 0.8f));
        // a full line at every level: the 0.5 s rhythm broke when the line ran dry (3–7 s while buyers walked in)
        foreach (var d in new[] { "Factory/SellDesk_Yard.asset", "Factory/SellDesk_Market.asset" })
        {
            var desk = Load<SellDeskDefinition>(d);
            var so = new SerializedObject(desk);
            var levels = so.FindProperty("levels");
            for (int i = 0; i < levels.arraySize; i++) levels.GetArrayElementAtIndex(i).FindPropertyRelative("queueCapacity").intValue = 7;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(desk);
        }

        // the port's data
        RemoveWhere(Load<PremiumEconomyConfig>("Economy/PremiumEconomy.asset"), "milestones", e => e.FindPropertyRelative("expansionId").stringValue == "dockyard");
        RemoveWhere(Load<UpgradeCatalog>("Progression/UpgradeCatalog.asset"), "entries", e => e.FindPropertyRelative("upgradeId").stringValue == "dockyard");
        var t50 = Load<TaskDefinition>("Progression/Tasks/Task_t50_dockyard.asset");
        RemoveWhere(Load<TaskChain>("Progression/TaskChain_Area1.asset"), "mainTasks", e => e.objectReferenceValue == t50);

        AssetDatabase.SaveAssets();
        Log.AppendLine("assets: Offer_MachineBoost (2 min Overdrive), 2X CASH off the rotation, customers 0.5 s apart, port data removed");
        return Log.ToString();
    }

    // ================================================================== scene

    public static string Scene()
    {
        Log.Clear();
        if (EditorApplication.isPlaying) return "!! stop Play mode first";
        var active = SceneManager.GetActiveScene();
        if (active.path == ScenePath && active.isDirty) EditorSceneManager.SaveScene(active);
        var scene = active.path == ScenePath ? active : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject RootGo(string n) => scene.GetRootGameObjects().First(g => g.name == n);
        var environment = RootGo("_Environment").transform;
        var gameplay = RootGo("_Gameplay").transform;
        var hud = RootGo("_UI").transform.Find("Canvas/HUD");

        RemovePort(environment, gameplay);
        CashButton(hud);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Log.AppendLine("scene saved; run R1_Build.Bake next");
        return Log.ToString();
    }

    static void RemovePort(Transform environment, Transform gameplay)
    {
        int removed = 0;
        foreach (var t in new[]
                 {
                     gameplay.Find("Dockyard"), environment.Find("DockyardBackdrop"), environment.Find("Gate_A2_A4"),
                     gameplay.Find("FurnaceHall/Content/Tile_Dockyard")
                 })
        {
            if (t == null) continue;
            Object.DestroyImmediate(t.gameObject);
            removed++;
        }

        // the east wall in one piece again
        var fences = environment.Find("Fences");
        foreach (var n in new[] { "Wall_76_42_76_27", "Wall_76_21_76_11", "Wall_76_42_76_11" })
        {
            var old = fences.Find(n);
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }

        Wall(fences, "Wall_76_42_76_11", new Vector3(WallX, 0f, WallNorth), new Vector3(WallX, 0f, WallSouth), 31);

        // where the quay and water were: grass, and the two warehouses M8 had taken away
        Ground(environment.Find("WorldGround"));
        var backdrop = environment.Find("ArtBackdrop");
        Warehouse(backdrop, "Prop_Warehouse_Blue", new Vector3(86f, 0f, 22f));
        Warehouse(backdrop, "Prop_Warehouse_Cream", new Vector3(86f, 0f, 36f));
        Log.AppendLine($"port: {removed} roots removed, east wall closed, ground to x 200, warehouses back");
    }

    /// <summary>The same slab as Env_Build.WorldGround, whose east edge now runs on past the old quay.</summary>
    static void Ground(Transform slab)
    {
        if (slab == null || !slab.TryGetComponent(out MeshFilter mf)) return;
        const float west = -90f, east = 200f, south = -70f, north = 170f, top = -0.05f, thick = 0.2f;
        slab.localScale = Vector3.one;
        var k = new MeshKit { UvScale = 1f };
        k.Box(0, slab.InverseTransformPoint(new Vector3((west + east) * 0.5f, top - thick * 0.5f, (south + north) * 0.5f)), new Vector3(east - west, thick, north - south), 0f);
        mf.sharedMesh = ArtAssets.SaveMesh("Ground", "WorldGround", k.ToMesh("WorldGround"));
    }

    static void Warehouse(Transform parent, string prefabName, Vector3 at)
    {
        if (parent == null) return;
        foreach (Transform c in parent)
            if (c.name.StartsWith("Prop_Warehouse") && (c.position - at).sqrMagnitude < 4f) return;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Prefabs/Props/{prefabName}.prefab");
        if (prefab == null) return;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, 90f, 0f));
    }

    /// <summary>A copy of the HUD's offer button on the left edge, carrying <see cref="CashBoostButton"/>.</summary>
    static void CashButton(Transform hud)
    {
        var old = hud.Find("CashBoostButton");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var source = hud.Find("OfferButton");
        var copy = Object.Instantiate(source.gameObject, hud);
        copy.name = "CashBoostButton";
        copy.transform.SetSiblingIndex(source.GetSiblingIndex());
        Object.DestroyImmediate(copy.GetComponent<OfferButton>());
        var rt = (RectTransform)copy.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(22f, 150f);

        var root = copy.transform.Find("Root");
        var pulse = root.Find("Pulse");
        foreach (var n in new[] { "TimerTrack", "Timer" })
        {
            var t = pulse.Find(n);
            if (t != null) t.gameObject.SetActive(false);
        }

        var button = copy.AddComponent<CashBoostButton>();
        SetMany(button, ("offer", Load<AdOfferDefinition>("Boosts/Offer_Cash.asset")), ("root", root), ("button", root.GetComponent<Button>()),
            ("icon", pulse.Find("Icon").GetComponent<Image>()), ("title", pulse.Find("Title").GetComponent<TMP_Text>()),
            ("detail", pulse.Find("Detail").GetComponent<TMP_Text>()), ("pulseRoot", pulse), ("fromLevel", 3));
        root.gameObject.SetActive(false);
        Log.AppendLine("hud: 2X CASH button on the left edge");
    }

    // ================================================================== helpers

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

    static int C(PC c) => (int)c;

    /// <summary>Same sheets as M8_Build / Art_World.CorrugatedWall.</summary>
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

    static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>($"{DataDir}/{path}");

    static T Asset<T>(string path) where T : ScriptableObject
    {
        var full = $"{DataDir}/{path}";
        var x = AssetDatabase.LoadAssetAtPath<T>(full);
        if (x != null) return x;
        x = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(x, full);
        return x;
    }

    static void SetMany(Object target, params (string name, object value)[] values)
    {
        if (target == null) { Log.AppendLine("!! missing target"); return; }
        var so = new SerializedObject(target);
        foreach (var (name, value) in values)
        {
            var p = so.FindProperty(name);
            if (p == null) { Log.AppendLine($"!! {target.name}.{name}"); continue; }
            switch (value)
            {
                case null: p.objectReferenceValue = null; break;
                case Object o: p.objectReferenceValue = o; break;
                case string s: p.stringValue = s; break;
                case float f: p.floatValue = f; break;
                case int i when p.propertyType == SerializedPropertyType.Enum: p.enumValueIndex = i; break;
                case int i: p.intValue = i; break;
                case bool b: p.boolValue = b; break;
            }
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    static void RemoveWhere(Object target, string array, System.Func<SerializedProperty, bool> match)
    {
        if (target == null) { Log.AppendLine($"!! missing {array} owner"); return; }
        var so = new SerializedObject(target);
        var p = so.FindProperty(array);
        int removed = 0;
        for (int i = p.arraySize - 1; i >= 0; i--)
        {
            if (!match(p.GetArrayElementAtIndex(i))) continue;
            if (p.GetArrayElementAtIndex(i).propertyType == SerializedPropertyType.ObjectReference)
                p.GetArrayElementAtIndex(i).objectReferenceValue = null;
            p.DeleteArrayElementAtIndex(i);
            removed++;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
        Log.AppendLine($"{target.name}.{array}: {removed} removed");
    }
}

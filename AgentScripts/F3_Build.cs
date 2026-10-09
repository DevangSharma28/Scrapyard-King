using System.Collections.Generic;
using System.Linq;
using System.Text;
using ScrapYardKing.Customers;
using ScrapYardKing.EditorTools.Art;
using ScrapYardKing.Factory;
using ScrapYardKing.Feedback;
using ScrapYardKing.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Fun pass F3 (owner's brief, 2026-10-09): trucks.
///  - one detailed truck model (ArtKit): rounded two-tone cab with wind fairing, light bar, air horns, visor, wipers,
///    mirrors, chrome grille and bumper, twin stacks, tanks, steps, fenders, mud flaps; flatbed with plank deck, rack,
///    stake sides, tail and reverse lights; separate drop-side, roof beacon and reverse lights; detailed wheels;
///  - the Truck Dock's truck is rebuilt with it and given the show: beacon and blinking reverse lights with beeps while
///    it backs in, the side drops open, the bed settles as it fills, then side up, horn, drive-off (TruckBay show fields);
///  - the Metal Market's buyers become trucks: they drive in along the near lane, stop at the counter one after another
///    (0.5 s apart), take their order on the bed and leave into a new side street at x 48 (Customer + VehicleWheels).
///    The ambient road trucks move to the far lanes.
/// Entry points: Assets, Scene, All. Idempotent. Run after F2_Build.
/// </summary>
public static class F3_Build
{
    const string Root = "Assets/_Project";
    const string DataDir = Root + "/Data";
    const string PrefabDir = Root + "/Prefabs";
    const string ScenePath = Root + "/Scenes/Area1_OldScrapYard.unity";
    const float LaneZ = 6.4f;
    static readonly float[] SlotX = { 60.7f, 67.7f, 74.7f, 81.7f };
    static readonly StringBuilder Log = new();

    static int C(PC c) => (int)c;

    public static string All() => Assets() + Scene();

    // ================================================================== assets

    public static string Assets()
    {
        Log.Clear();
        Sfx("Sfx_TruckReverse", "Assets/ThirdParty/Kenney/Audio/Interface/tick_001.ogg", 0.35f, new Vector2(1.5f, 1.6f), 0.2f);
        Sfx("Sfx_TruckGate", "Assets/ThirdParty/Kenney/Audio/Impact/impactMetal_light_003.ogg", 0.7f, new Vector2(0.62f, 0.7f), 0.1f);

        var colors = new (string name, PC cab, PC accent)[] { ("Blue", PC.Blue, PC.White), ("Orange", PC.Orange, PC.Charcoal), ("Green", PC.Green, PC.Cream) };
        var prefabs = new List<Customer>();
        foreach (var (name, cab, accent) in colors) prefabs.Add(CustomerTruck(name, cab, accent));

        var config = AssetDatabase.LoadAssetAtPath<CustomerConfig>(DataDir + "/Customers/CustomerConfig_Market.asset");
        var so = new SerializedObject(config);
        var p = so.FindProperty("prefabs");
        p.arraySize = prefabs.Count;
        for (int i = 0; i < prefabs.Count; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = prefabs[i];
        so.FindProperty("walkSpeed").floatValue = 9f;
        so.FindProperty("arrivalInterval").vector2Value = new Vector2(0.85f, 1.1f);   // 7.5–10 m apart at 9 m/s
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();
        Log.AppendLine("assets: 3 customer trucks, market buyers are trucks, reverse/gate sounds");
        return Log.ToString();
    }

    static void Sfx(string name, string clipPath, float volume, Vector2 pitch, float minInterval)
    {
        string path = $"{DataDir}/Audio/{name}.asset";
        var sfx = AssetDatabase.LoadAssetAtPath<SfxDefinition>(path);
        if (sfx == null)
        {
            sfx = ScriptableObject.CreateInstance<SfxDefinition>();
            AssetDatabase.CreateAsset(sfx, path);
        }

        var so = new SerializedObject(sfx);
        var clips = so.FindProperty("clips");
        clips.arraySize = 1;
        clips.GetArrayElementAtIndex(0).objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
        so.FindProperty("volume").floatValue = volume;
        so.FindProperty("pitchRange").vector2Value = pitch;
        so.FindProperty("minInterval").floatValue = minInterval;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(sfx);
    }

    /// <summary>A truck that drives up to the market counter as a customer: model, bubble, hand point, wheel spin.</summary>
    static Customer CustomerTruck(string name, PC cab, PC accent)
    {
        var root = new GameObject("Customer_Truck_" + name);
        var parts = BuildTruck(root.transform, "F3_Truck" + name, cab, accent);

        // the order bubble of the walking buyers, raised above the cab
        var person = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Customers/Customer_female-a.prefab");
        var bubbleSource = person.GetComponentInChildren<CustomerBubble>(true);
        var bubble = Object.Instantiate(bubbleSource.gameObject, root.transform);
        bubble.name = "Bubble";
        bubble.transform.localPosition = new Vector3(0f, 4.6f, 0.6f);
        bubble.transform.localScale = bubbleSource.transform.localScale * 1.5f;

        var hand = new GameObject("HandPoint").transform;
        hand.SetParent(parts.Body, false);
        hand.localPosition = new Vector3(0f, 1.35f, -0.7f);

        var customer = root.AddComponent<Customer>();
        Set(customer, so =>
        {
            so.FindProperty("animator").objectReferenceValue = null;
            so.FindProperty("bubble").objectReferenceValue = bubble.GetComponent<CustomerBubble>();
            so.FindProperty("handPoint").objectReferenceValue = hand;
            so.FindProperty("turnSpeed").floatValue = 140f;
        });
        var wheels = root.AddComponent<VehicleWheels>();
        Set(wheels, so =>
        {
            var w = so.FindProperty("wheels");
            w.arraySize = parts.Wheels.Count;
            for (int i = 0; i < parts.Wheels.Count; i++) w.GetArrayElementAtIndex(i).objectReferenceValue = parts.Wheels[i];
            so.FindProperty("radius").floatValue = 0.52f;
            so.FindProperty("body").objectReferenceValue = parts.Body;
        });

        // a market truck has its gate shut and its beacon still
        parts.ReverseLights.SetActive(false);
        var saved = ArtAssets.SavePrefab(root, $"{PrefabDir}/Customers/Customer_Truck_{name}.prefab");
        return saved.GetComponent<Customer>();
    }

    // ================================================================== the truck model

    sealed class TruckParts
    {
        public Transform Body, SideGate, Beacon;
        public GameObject ReverseLights;
        public readonly List<Transform> Wheels = new();
    }

    /// <summary>
    /// Builds the truck under <paramref name="root"/> (forward +Z, ground y 0): Body (hull, drop-side, beacon, reverse
    /// lights) and four wheels. Dimensions match the M7 dock truck: wheels at x ±1.06, z 1.95 / -1.5, radius 0.52; bed
    /// top y 1.07, z -2.4..1.0 (the cargo pile sits there); the dock side is -X.
    /// </summary>
    static TruckParts BuildTruck(Transform root, string model, PC cab, PC accent)
    {
        var parts = new TruckParts();
        var body = new GameObject("Body").transform;
        body.SetParent(root, false);
        parts.Body = body;
        var mats = new[] { ArtPalette.Material };

        var h = new MeshKit();
        // ---- chassis, tanks, steps, skirts ----
        h.Box(C(PC.Charcoal), new Vector3(0f, 0.72f, 0.1f), new Vector3(1.3f, 0.24f, 5.4f), 0.04f);
        h.Cylinder(C(PC.Silver), new Vector3(0.98f, 0.66f, 0.55f), 0.27f, 0.95f, 14, new Vector3(90f, 0f, 0f), 0.05f);
        foreach (float z in new[] { 0.2f, 0.9f }) h.Box(C(PC.Charcoal), new Vector3(0.98f, 0.66f, z), new Vector3(0.58f, 0.58f, 0.05f), 0.02f);
        h.Cylinder(C(PC.GrayLight), new Vector3(-0.98f, 0.62f, 0.55f), 0.2f, 0.8f, 12, new Vector3(90f, 0f, 0f), 0.04f);
        foreach (int side in new[] { -1, 1 })
        {
            h.Box(C(PC.Silver), new Vector3(side * 1.2f, 0.52f, 1.95f), new Vector3(0.2f, 0.05f, 0.46f), 0.01f);
            h.Box(C(PC.Silver), new Vector3(side * 1.22f, 0.82f, 1.95f), new Vector3(0.16f, 0.05f, 0.42f), 0.01f);
            h.Box(C(accent), new Vector3(side * 1.18f, 0.74f, 0.2f), new Vector3(0.06f, 0.3f, 1.55f), 0.02f);
            for (int i = 0; i < 5; i++)
                h.Box(C(i % 2 == 0 ? PC.Yellow : PC.Charcoal), new Vector3(side * 1.215f, 0.66f, -0.4f + i * 0.3f), new Vector3(0.02f, 0.1f, 0.3f), 0f);
        }

        // ---- cab: rounded two-tone shell ----
        var shell = new[]
        {
            new Vector2(1.1f, 0.86f), new Vector2(2.92f, 0.86f), new Vector2(2.94f, 1.6f), new Vector2(2.78f, 2.42f),
            new Vector2(2.52f, 3.0f), new Vector2(1.1f, 3.0f)
        };
        h.Prism(C(cab), Vector3.zero, shell, 2.46f, 0.14f);
        h.Box(C(accent), new Vector3(0f, 1.34f, 2.0f), new Vector3(2.5f, 0.22f, 1.86f), 0.05f);
        h.Box(C(PC.Charcoal), new Vector3(0f, 0.96f, 2.0f), new Vector3(2.48f, 0.12f, 1.84f), 0.03f);
        // glass: windscreen (leaning back 24 degrees), side windows, wipers
        h.Box(C(PC.Glass), new Vector3(0f, 2.72f, 2.67f), new Vector3(2.08f, 0.6f, 0.05f), 0.03f, new Vector3(-24f, 0f, 0f));
        foreach (float x in new[] { -0.45f, 0.45f })
            h.Box(C(PC.Charcoal), new Vector3(x, 2.45f, 2.8f), new Vector3(0.6f, 0.03f, 0.03f), 0.005f, new Vector3(0f, 0f, x < 0 ? 18f : -18f));
        foreach (int side in new[] { -1, 1 })
        {
            h.Box(C(PC.Glass), new Vector3(side * 1.235f, 2.42f, 1.95f), new Vector3(0.04f, 0.66f, 1.0f), 0.02f);
            h.Box(C(PC.Charcoal), new Vector3(side * 1.24f, 1.95f, 1.45f), new Vector3(0.03f, 1.9f, 0.03f), 0f);        // door seam
            h.Box(C(PC.Silver), new Vector3(side * 1.255f, 1.78f, 1.6f), new Vector3(0.04f, 0.05f, 0.18f), 0.01f);       // handle
            // mirrors on arms
            h.Box(C(PC.Charcoal), new Vector3(side * 1.36f, 2.5f, 2.5f), new Vector3(0.24f, 0.04f, 0.04f), 0.01f);
            h.Box(C(PC.Charcoal), new Vector3(side * 1.5f, 2.3f, 2.5f), new Vector3(0.1f, 0.46f, 0.22f), 0.03f);
            h.Box(C(PC.Glass), new Vector3(side * 1.5f, 2.3f, 2.39f), new Vector3(0.08f, 0.4f, 0.02f), 0.005f);
            // headlights, turn signals
            h.Box(C(PC.Cream), new Vector3(side * 0.86f, 1.16f, 2.93f), new Vector3(0.44f, 0.24f, 0.06f), 0.04f);
            h.Box(C(PC.Orange), new Vector3(side * 1.16f, 1.16f, 2.92f), new Vector3(0.12f, 0.2f, 0.06f), 0.02f);
            // exhaust stacks behind the cab
            h.Cylinder(C(PC.Silver), new Vector3(side * 1.08f, 2.3f, 1.0f), 0.085f, 2.3f, 10, default, 0.02f);
            h.Cylinder(C(PC.Charcoal), new Vector3(side * 1.08f, 3.5f, 1.0f), 0.1f, 0.16f, 10, default, 0.02f);
            // air horns on the roof
            h.Cylinder(C(PC.Silver), new Vector3(side * 0.55f, 3.08f, 2.1f), 0.06f, 0.55f, 8, new Vector3(90f, 0f, 0f), 0.02f, 0.11f);
            // fenders and mud flaps
            h.Box(C(cab), new Vector3(side * 1.08f, 1.12f, 1.95f), new Vector3(0.48f, 0.1f, 1.3f), 0.05f);
            h.Box(C(PC.Charcoal), new Vector3(side * 1.08f, 1.13f, -1.5f), new Vector3(0.5f, 0.08f, 1.32f), 0.03f);
            h.Box(C(PC.Rubber), new Vector3(side * 1.08f, 0.56f, -2.28f), new Vector3(0.46f, 0.52f, 0.04f), 0.01f);
            h.Box(C(PC.Silver), new Vector3(side * 1.08f, 0.66f, -2.3f), new Vector3(0.22f, 0.08f, 0.01f), 0f);
        }

        // grille, bumper, fog lights, badge
        h.Box(C(PC.Charcoal), new Vector3(0f, 1.3f, 2.94f), new Vector3(1.28f, 0.56f, 0.05f), 0.03f);
        for (int i = 0; i < 5; i++) h.Box(C(PC.Silver), new Vector3(0f, 1.08f + i * 0.11f, 2.97f), new Vector3(1.16f, 0.035f, 0.02f), 0.005f);
        h.Box(C(PC.Yellow), new Vector3(0f, 1.62f, 2.97f), new Vector3(0.36f, 0.1f, 0.02f), 0.01f);
        h.Box(C(PC.Silver), new Vector3(0f, 0.68f, 3.02f), new Vector3(2.56f, 0.32f, 0.26f), 0.08f);
        foreach (int side in new[] { -1, 1 }) h.Box(C(PC.Cream), new Vector3(side * 0.95f, 0.68f, 3.15f), new Vector3(0.2f, 0.12f, 0.02f), 0.01f);
        // roof: wind fairing, visor, light bar
        h.Box(C(accent), new Vector3(0f, 3.22f, 1.55f), new Vector3(2.3f, 0.46f, 0.95f), 0.16f);
        h.Box(C(PC.Charcoal), new Vector3(0f, 3.02f, 2.62f), new Vector3(2.32f, 0.07f, 0.32f), 0.02f);
        for (int i = 0; i < 4; i++) h.Box(C(PC.Orange), new Vector3(-0.66f + i * 0.44f, 3.07f, 2.5f), new Vector3(0.18f, 0.08f, 0.1f), 0.02f);

        // ---- flatbed ----
        const float bedZ = -0.7f, bedLen = 3.4f;
        h.Box(C(PC.Wood), new Vector3(0f, 1.0f, bedZ), new Vector3(2.5f, 0.14f, bedLen), 0.03f);
        for (int i = 0; i < 5; i++) h.Box(C(PC.WoodDark), new Vector3(-1.0f + i * 0.5f, 1.072f, bedZ), new Vector3(0.03f, 0.005f, bedLen - 0.1f), 0f);
        h.Box(C(PC.Charcoal), new Vector3(0f, 0.9f, bedZ), new Vector3(2.56f, 0.1f, bedLen + 0.06f), 0.02f);
        // headboard with a ladder rack
        h.Box(C(accent), new Vector3(0f, 1.6f, bedZ + bedLen * 0.5f + 0.02f), new Vector3(2.5f, 1.06f, 0.08f), 0.03f);
        for (int i = 0; i < 5; i++) h.Box(C(PC.Silver), new Vector3(-1.0f + i * 0.5f, 1.6f, bedZ + bedLen * 0.5f - 0.04f), new Vector3(0.07f, 0.96f, 0.04f), 0.01f);
        h.Box(C(PC.Silver), new Vector3(0f, 2.16f, bedZ + bedLen * 0.5f - 0.04f), new Vector3(2.4f, 0.07f, 0.07f), 0.01f);
        // fixed side (+X) and tailgate
        h.Box(C(accent), new Vector3(1.22f, 1.33f, bedZ), new Vector3(0.06f, 0.52f, bedLen - 0.06f), 0.015f);
        for (int i = 0; i < 4; i++)
            h.Box(C(PC.Silver), new Vector3(1.26f, 1.33f, bedZ - bedLen * 0.5f + 0.2f + i * (bedLen - 0.4f) / 3f), new Vector3(0.04f, 0.54f, 0.07f), 0.01f);
        h.Box(C(accent), new Vector3(0f, 1.33f, bedZ - bedLen * 0.5f + 0.02f), new Vector3(2.5f, 0.52f, 0.06f), 0.015f);
        // rear: tail lights, hazard underride bar
        foreach (int side in new[] { -1, 1 })
        {
            h.Box(C(PC.Red), new Vector3(side * 1.02f, 0.9f, bedZ - bedLen * 0.5f - 0.04f), new Vector3(0.3f, 0.14f, 0.05f), 0.02f);
            h.Box(C(PC.Orange), new Vector3(side * 0.82f, 0.9f, bedZ - bedLen * 0.5f - 0.04f), new Vector3(0.1f, 0.14f, 0.05f), 0.02f);
        }

        for (int i = 0; i < 6; i++)
            h.Box(C(i % 2 == 0 ? PC.Yellow : PC.Charcoal), new Vector3(-1.0f + i * 0.4f, 0.62f, bedZ - bedLen * 0.5f - 0.06f), new Vector3(0.4f, 0.16f, 0.05f), 0.01f);
        ArtAssets.MeshObject("Hull", body, ArtAssets.SaveMesh(model, "Hull", h.ToPaletteMesh(model + "Hull")), mats);

        // ---- drop-side on the dock side (-X): pivot on the bed edge, opens about Z ----
        var gate = new GameObject("SideGate").transform;
        gate.SetParent(body, false);
        gate.localPosition = new Vector3(-1.25f, 1.07f, bedZ);
        var g = new MeshKit();
        g.Box(C(accent), new Vector3(0.03f, 0.26f, 0f), new Vector3(0.06f, 0.52f, bedLen - 0.06f), 0.015f);
        for (int i = 0; i < 4; i++)
            g.Box(C(PC.Silver), new Vector3(-0.01f, 0.26f, -bedLen * 0.5f + 0.2f + i * (bedLen - 0.4f) / 3f), new Vector3(0.04f, 0.54f, 0.07f), 0.01f);
        g.Box(C(PC.Yellow), new Vector3(-0.02f, 0.42f, 0f), new Vector3(0.02f, 0.08f, bedLen - 0.4f), 0f);
        ArtAssets.MeshObject("Gate", gate, ArtAssets.SaveMesh(model, "Gate", g.ToPaletteMesh(model + "Gate")), mats);
        parts.SideGate = gate;

        // ---- roof beacon: spins; the white reflector shows the turn ----
        var beacon = new GameObject("Beacon").transform;
        beacon.SetParent(body, false);
        beacon.localPosition = new Vector3(0f, 3.46f, 1.45f);
        var b = new MeshKit();
        b.Cylinder(C(PC.Charcoal), new Vector3(0f, 0.04f, 0f), 0.15f, 0.08f, 12, default, 0.01f);
        b.Cylinder(C(PC.Orange), new Vector3(0f, 0.17f, 0f), 0.12f, 0.2f, 12, default, 0.04f);
        b.Box(C(PC.White), new Vector3(0.07f, 0.17f, 0f), new Vector3(0.06f, 0.14f, 0.12f), 0.01f);
        ArtAssets.MeshObject("Lamp", beacon, ArtAssets.SaveMesh(model, "Beacon", b.ToPaletteMesh(model + "Beacon")), mats);
        parts.Beacon = beacon;

        // ---- reverse lights: blink while backing in ----
        var rev = new MeshKit();
        foreach (int side in new[] { -1, 1 })
            rev.Box(C(PC.White), new Vector3(side * 0.6f, 0.9f, bedZ - bedLen * 0.5f - 0.05f), new Vector3(0.18f, 0.12f, 0.05f), 0.02f);
        parts.ReverseLights = ArtAssets.MeshObject("ReverseLights", body, ArtAssets.SaveMesh(model, "Reverse", rev.ToPaletteMesh(model + "Reverse")), mats);

        // ---- wheels: tread, rim, hub, nuts ----
        var w = new MeshKit();
        w.Cylinder(C(PC.Rubber), Vector3.zero, 0.52f, 0.42f, 18, new Vector3(0f, 0f, 90f), 0.08f);
        for (int i = 0; i < 14; i++)
        {
            float a = i * 360f / 14f;
            w.Box(C(PC.Charcoal), Quaternion.Euler(a, 0f, 0f) * new Vector3(0f, 0.515f, 0f), new Vector3(0.4f, 0.04f, 0.09f), 0.01f, new Vector3(a, 0f, 0f));
        }

        w.Cylinder(C(PC.Silver), Vector3.zero, 0.3f, 0.44f, 12, new Vector3(0f, 0f, 90f), 0.02f);
        w.Cylinder(C(PC.Charcoal), Vector3.zero, 0.11f, 0.47f, 10, new Vector3(0f, 0f, 90f), 0.01f);
        for (int i = 0; i < 6; i++)
            foreach (int side in new[] { -1, 1 })
            {
                float a = i * 60f;
                w.Cylinder(C(PC.GrayLight), Quaternion.Euler(a, 0f, 0f) * new Vector3(side * 0.225f, 0.18f, 0f), 0.03f, 0.03f, 6, new Vector3(0f, 0f, 90f), 0f);
            }

        var wheelMesh = ArtAssets.SaveMesh(model, "Wheel", w.ToPaletteMesh(model + "Wheel"));
        foreach (int side in new[] { -1, 1 })
            foreach (float z in new[] { 1.95f, -1.5f })
                parts.Wheels.Add(ArtAssets.MeshObject("Wheel", root, wheelMesh, mats, new Vector3(side * 1.06f, 0.52f, z)).transform);
        return parts;
    }

    // ================================================================== scene

    public static string Scene()
    {
        Log.Clear();
        if (EditorApplication.isPlaying) return "!! stop Play mode first";
        var active = SceneManager.GetActiveScene();
        if (active.path == ScenePath && active.isDirty) EditorSceneManager.SaveScene(active);
        var scene = active.path == ScenePath ? active : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var environment = scene.GetRootGameObjects().First(g => g.name == "_Environment").transform;

        DockTruck();
        MarketLane();
        SideStreet(environment);
        AmbientLanes();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return Log.ToString();
    }

    /// <summary>The Truck Dock's truck: the old hull and wheels make way for the new model and its moving parts.</summary>
    static void DockTruck()
    {
        var bay = Object.FindObjectsByType<TruckBay>(FindObjectsInactive.Include).FirstOrDefault(b => b.StationId == "truck_bay");
        if (bay == null) { Log.AppendLine("!! no truck_bay"); return; }
        var root = PrefabUtility.GetOutermostPrefabInstanceRoot(bay.gameObject);
        if (root != null) PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        var truck = bay.transform.Find("Truck");
        var cargo = truck.Find("Cargo");
        if (cargo == null) cargo = truck.Find("Body/Cargo");
        foreach (Transform c in truck.Cast<Transform>().ToArray())
            if (c.name is "Body" or "Wheel")
            {
                if (cargo != null && cargo.IsChildOf(c)) cargo.SetParent(truck, true);
                Object.DestroyImmediate(c.gameObject);
            }

        var parts = BuildTruck(truck, "F3_TruckDock", PC.Teal, PC.White);
        // the cargo rides on the body, so it settles with the springs
        cargo.SetParent(parts.Body, true);
        Set(bay, so =>
        {
            so.FindProperty("truckBody").objectReferenceValue = parts.Body;
            var w = so.FindProperty("wheels");
            w.arraySize = parts.Wheels.Count;
            for (int i = 0; i < parts.Wheels.Count; i++) w.GetArrayElementAtIndex(i).objectReferenceValue = parts.Wheels[i];
            so.FindProperty("sideGate").objectReferenceValue = parts.SideGate;
            so.FindProperty("gateOpenEuler").vector3Value = new Vector3(0f, 0f, 100f);
            so.FindProperty("beacon").objectReferenceValue = parts.Beacon;
            so.FindProperty("reverseLights").objectReferenceValue = parts.ReverseLights;
            so.FindProperty("reverseSfx").objectReferenceValue = AssetDatabase.LoadAssetAtPath<SfxDefinition>(DataDir + "/Audio/Sfx_TruckReverse.asset");
            so.FindProperty("gateSfx").objectReferenceValue = AssetDatabase.LoadAssetAtPath<SfxDefinition>(DataDir + "/Audio/Sfx_TruckGate.asset");
            so.FindProperty("hornSfx").objectReferenceValue = AssetDatabase.LoadAssetAtPath<SfxDefinition>(DataDir + "/Audio/Sfx_TruckHorn.asset");
            so.FindProperty("fullSink").floatValue = 0.12f;
        });
        Log.AppendLine("dock truck: new model, drop-side, beacon, reverse lights");
    }

    /// <summary>Four truck slots along the near lane, nose west; trucks come from the east and leave into the side street.</summary>
    static void MarketLane()
    {
        var queue = Object.FindObjectsByType<CustomerQueue>(FindObjectsInactive.Include).FirstOrDefault(q => q.name == "Customers_Market");
        if (queue == null) { Log.AppendLine("!! no Customers_Market"); return; }
        var old = queue.transform.Find("TruckLane");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var lane = new GameObject("TruckLane").transform;
        lane.SetParent(queue.transform, false);
        var west = Quaternion.Euler(0f, -90f, 0f);

        Transform Point(string name, Vector3 at, Quaternion rot)
        {
            var t = new GameObject(name).transform;
            t.SetParent(lane, false);
            t.SetPositionAndRotation(at, rot);
            return t;
        }

        var slots = SlotX.Select((x, i) => Point("Slot" + i, new Vector3(x, 0f, LaneZ), west)).ToArray();
        var entry = new[] { Point("Entry0", new Vector3(135f, 0f, LaneZ), west), Point("Entry1", new Vector3(SlotX[^1] + 4f, 0f, LaneZ), west) };
        var exit = new[]
        {
            Point("Exit0", new Vector3(52.5f, 0f, LaneZ), west), Point("Exit1", new Vector3(48.4f, 0f, 3.2f), Quaternion.Euler(0f, 180f, 0f)),
            Point("Exit2", new Vector3(48.2f, 0f, -26f), Quaternion.Euler(0f, 180f, 0f))
        };
        Set(queue, so =>
        {
            void Array(string n, Transform[] items)
            {
                var p = so.FindProperty(n);
                p.arraySize = items.Length;
                for (int i = 0; i < items.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
            }

            Array("slots", slots);
            Array("entryPath", entry);
            Array("exitPath", exit);
            // trucks do not cheer: the served one pulls away at once, so the next keeps its distance stepping up
            so.FindProperty("cheerDuration").floatValue = 0.05f;
        });

        // walking buyers already in the scene from an earlier build would keep their old prefab pool: nothing to clear
        Log.AppendLine("market: 4 truck slots on the near lane, exit into the side street");
    }

    /// <summary>Asphalt from the road south past the camera, where the market trucks turn off.</summary>
    static void SideStreet(Transform environment)
    {
        var old = environment.Find("F3_SideStreet");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var road = environment.Find("Road_South");
        var k = new MeshKit { UvScale = 1f };
        k.Box(0, new Vector3(48.3f, -0.05f, -15f), new Vector3(4.2f, 0.1f, 30f), 0f);
        var go = ArtAssets.MeshObject("F3_SideStreet", environment, ArtAssets.SaveMesh("Ground", "SideStreet", k.ToMesh("SideStreet", 1)),
            new[] { road.GetComponent<Renderer>().sharedMaterial });
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        var lines = new MeshKit();
        for (float z = -2f; z > -29f; z -= 3f) lines.Box(C(PC.White), new Vector3(48.3f, 0.005f, z), new Vector3(0.14f, 0.01f, 1.4f), 0f);
        ArtAssets.MeshObject("F3_SideStreetLines", go.transform, ArtAssets.SaveMesh("Ground", "SideStreetLines", lines.ToPaletteMesh("SideStreetLines")),
            new[] { ArtPalette.Material });
        Log.AppendLine("side street at x 48");
    }

    /// <summary>The ambient road trucks keep to the far lanes so they never drive through a market truck.</summary>
    static void AmbientLanes()
    {
        foreach (var path in Object.FindObjectsByType<AmbientPath>(FindObjectsInactive.Include))
        {
            float z = path.name == "Prop_RoadTruck_Red" ? 1.4f : path.name == "Prop_RoadTruck_Blue" ? 3.9f : float.NaN;
            if (float.IsNaN(z)) continue;
            var so = new SerializedObject(path);
            var points = so.FindProperty("points");
            for (int i = 0; i < points.arraySize; i++)
            {
                var t = points.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                if (t == null) continue;
                Undo.RecordObject(t, "lane");
                t.position = new Vector3(t.position.x, t.position.y, z);
            }

            Log.AppendLine($"{path.name}: lane z {z}");
        }
    }

    static void Set(Object target, System.Action<SerializedObject> edit)
    {
        var so = new SerializedObject(target);
        edit(so);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }
}

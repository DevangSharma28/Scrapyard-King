using System.Collections.Generic;
using System.Linq;
using System.Text;
using ScrapYardKing.EditorTools.Art;
using ScrapYardKing.Factory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Fun pass F4 (owner's brief, 2026-10-09): conveyors and overlapping meshes.
///  - every belt is rebuilt: C-channel side rails (yellow web, dark flanges, bolts, hazard blocks), rubber skirt boards,
///    the scrolling belt and a return belt under it, A-frame legs with braces and foot plates, return idlers, and at
///    each free end a drum that turns with the belt (Conveyor.rollers) in bearing blocks; the head end gets a drive motor
///    under a yellow guard. The belt surface stays at 0.4 m, so items ride exactly as before;
///  - where a belt meets another belt (the furnace collector chain) the ends join flush: no overhang, no drum, no seam
///    pile-up. Belts that fan out of one machine (the Splitter's four) are narrower and start later, so their rails no
///    longer cross.
/// Replaces Art_Machines.BuildConveyorVisual (re-running Art_Machines.Scene brings the old belts back: run this after it).
/// Entry point: Scene (then R1_Build.Bake). Idempotent. Run after F3_Build.
/// </summary>
public static class F4_Build
{
    const string Root = "Assets/_Project";
    const string ScenePath = Root + "/Scenes/Area1_OldScrapYard.unity";
    const float Top = 0.4f, DrumR = 0.15f, Overhang = 0.35f, JoinDistance = 0.9f, FanDistance = 2.2f;
    static readonly StringBuilder Log = new();

    static int C(PC c) => (int)c;

    sealed class Belt
    {
        public Conveyor Conveyor;
        public Vector3 A, B;              // world, first and last point, y = 0
        public bool JoinA, JoinB, Fan;
        public float GapA, GapB;          // distance to the neighbour's nearest end (joined ends meet halfway)
    }

    public static string Scene()
    {
        Log.Clear();
        if (EditorApplication.isPlaying) return "!! stop Play mode first";
        var active = SceneManager.GetActiveScene();
        if (active.path == ScenePath && active.isDirty) EditorSceneManager.SaveScene(active);
        var scene = active.path == ScenePath ? active : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var belts = new List<Belt>();
        foreach (var c in Object.FindObjectsByType<Conveyor>(FindObjectsInactive.Include))
        {
            var pts = Points(c);
            if (pts.Length < 2) continue;
            belts.Add(new Belt { Conveyor = c, A = Flat(pts[0].position), B = Flat(pts[^1].position) });
        }

        foreach (var b in belts)
        {
            foreach (var o in belts)
            {
                if (o == b) continue;
                if (Near(b.A, o.A, o.B, JoinDistance))
                {
                    b.JoinA = true;
                    b.GapA = Mathf.Min(Vector3.Distance(b.A, o.A), Vector3.Distance(b.A, o.B));
                }

                if (Near(b.B, o.A, o.B, JoinDistance))
                {
                    b.JoinB = true;
                    b.GapB = Mathf.Min(Vector3.Distance(b.B, o.A), Vector3.Distance(b.B, o.B));
                }
                // belts that leave one machine side by side, at different angles
                Vector3 da = (b.B - b.A).normalized, db = (o.B - o.A).normalized;
                if (Vector3.Distance(b.A, o.A) < FanDistance && Vector3.Angle(da, db) > 8f) b.Fan = true;
            }
        }

        Tidy(scene);
        foreach (var b in belts)
        {
            Build(b);
            Log.AppendLine($"{b.Conveyor.name}: {(b.Fan ? "fan " : "")}{(b.JoinA ? "joined-start " : "")}{(b.JoinB ? "joined-end" : "")}");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Log.AppendLine($"{belts.Count} belts rebuilt; run R1_Build.Bake");
        return Log.ToString();
    }

    // ================================================================== overlaps found by Tools/MeshOverlap

    /// <summary>
    /// Fixes the overlaps the MeshOverlap audit found that were mistakes (not clusters or stacks on purpose): a tank inside
    /// a container, pallets on the Smelter's tile and in the crushed-car pile, a flag pole over the Sell Desk's cash pad,
    /// trees and bushes growing through backdrop warehouses.
    /// </summary>
    static void Tidy(Scene scene)
    {
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
        Transform Find(string name, Vector3 near, float within = 1.5f) =>
            all.Where(t => t != null && t.name == name).OrderBy(t => Vector3.Distance(Flat(t.position), Flat(near)))
                .FirstOrDefault(t => Vector3.Distance(Flat(t.position), Flat(near)) < within);

        // the tank stood half inside the blue container
        Separate(Find("Prop_Tank", new Vector3(36.8f, 0f, 70.5f), 3f), Find("Prop_Container_Blue", new Vector3(36.8f, 0f, 70.5f), 4f));
        // pallets slid into the crushed cars, with bales on the other side: no room, so they go
        var squeezed = Find("Prop_PalletStack", new Vector3(53.9f, 0f, 35.9f), 2.5f);
        if (squeezed != null)
        {
            Object.DestroyImmediate(squeezed.gameObject);
            all.RemoveAll(t => t == null);
            Log.AppendLine("tidy: pallets wedged between crushed cars and bales removed");
        }
        // pallets on the Smelter tile: M8 put them there to clear Gate 4, which is gone; the old gateway is free now
        var pallets = Find("Prop_PalletStack", new Vector3(74.4f, 0f, 29.8f));
        if (pallets != null)
        {
            Undo.RecordObject(pallets, "tidy");
            pallets.position = new Vector3(74.9f, pallets.position.y, 24.2f);
            Log.AppendLine("tidy: pallets off the Smelter tile");
        }

        // flags on both sides of the Sell Desk flew over its stock and cash pads (one pole stood in the crates)
        foreach (var at in new[] { new Vector3(35.2f, 0f, 11.4f), new Vector3(30.0f, 0f, 11.4f) })
        {
            var flag = Find("Prop_Flag", at);
            if (flag == null) continue;
            Object.DestroyImmediate(flag.gameObject);
            all.RemoveAll(t => t == null);
            Log.AppendLine("tidy: a flag over the Sell Desk's pads removed");
        }

        // trees and bushes inside backdrop warehouses
        int trees = 0;
        foreach (var w in all.Where(t => t != null && t.name.StartsWith("Prop_Warehouse")).ToList())
        {
            var box = MeshBounds(w);
            box.Expand(new Vector3(1.2f, 0f, 1.2f));
            foreach (var v in all.Where(t => t != null && (t.name.StartsWith("Prop_Tree") || t.name.StartsWith("Prop_Bush"))).ToList())
            {
                var p = v.position;
                if (p.x < box.min.x || p.x > box.max.x || p.z < box.min.z || p.z > box.max.z) continue;
                Object.DestroyImmediate(v.gameObject);
                trees++;
            }

            all.RemoveAll(t => t == null);
        }

        Log.AppendLine($"tidy: {trees} trees/bushes removed from inside warehouses");
    }

    /// <summary>Moves <paramref name="mover"/> sideways out of <paramref name="fixedOne"/> along the shorter way, plus a 0.2 m gap.</summary>
    static void Separate(Transform mover, Transform fixedOne)
    {
        if (mover == null || fixedOne == null) { Log.AppendLine("!! tidy: pair not found"); return; }
        Bounds a = MeshBounds(mover), b = MeshBounds(fixedOne);
        float right = b.max.x - a.min.x, left = a.max.x - b.min.x, front = b.max.z - a.min.z, back = a.max.z - b.min.z;
        if (right <= 0f || left <= 0f || front <= 0f || back <= 0f) return;
        float m = Mathf.Min(right, left, front, back);
        Vector3 move = m == right ? Vector3.right * right : m == left ? Vector3.left * left : m == front ? Vector3.forward * front : Vector3.back * back;
        Undo.RecordObject(mover, "tidy");
        mover.position += move + move.normalized * 0.2f;
        Log.AppendLine($"tidy: {mover.name} moved {move.magnitude + 0.2f:0.0} m out of {fixedOne.name}");
    }

    /// <summary>World bounds from the meshes (works for hidden expansion content, whose renderers report no bounds).</summary>
    static Bounds MeshBounds(Transform t)
    {
        bool any = false;
        var result = new Bounds(t.position, Vector3.zero);
        foreach (var mf in t.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var lb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var w = mf.transform.TransformPoint(corner);
                if (!any) result = new Bounds(w, Vector3.zero);
                else result.Encapsulate(w);
                any = true;
            }
        }

        return result;
    }

    static bool Near(Vector3 p, Vector3 a, Vector3 b, float d) => Vector3.Distance(p, a) < d || Vector3.Distance(p, b) < d;
    static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

    static Transform[] Points(Conveyor c)
    {
        var so = new SerializedObject(c);
        var p = so.FindProperty("points");
        var list = new Transform[p.arraySize];
        for (int i = 0; i < p.arraySize; i++) list[i] = (Transform)p.GetArrayElementAtIndex(i).objectReferenceValue;
        return list.Where(t => t != null).ToArray();
    }

    static void Build(Belt b)
    {
        var conveyor = b.Conveyor;
        var t = conveyor.transform;
        var keep = new HashSet<Transform>(Points(conveyor));
        var oldBelt = t.Find("Belt");
        var beltMat = oldBelt != null ? oldBelt.GetComponent<Renderer>().sharedMaterial : null;
        if (beltMat == null)   // a new belt (F5): borrow the belt material from any dressed one
            beltMat = Object.FindObjectsByType<Conveyor>(FindObjectsInactive.Include).Select(c => c.transform.Find("Belt")).Where(x => x != null)
                .Select(x => x.GetComponent<Renderer>().sharedMaterial).FirstOrDefault(m => m != null);
        foreach (Transform c in t.Cast<Transform>().ToArray())
            if (!keep.Contains(c)) Object.DestroyImmediate(c.gameObject);

        Vector3 la = t.InverseTransformPoint(b.A), lb = t.InverseTransformPoint(b.B);
        la.y = lb.y = 0f;
        float run = Vector3.Distance(la, lb);
        Vector3 dir = (lb - la).normalized;
        float startTrim = b.JoinA ? b.GapA * 0.5f : b.Fan ? -0.25f : Overhang;   // fanned belts begin a little after the machine
        float endTrim = b.JoinB ? b.GapB * 0.5f : Overhang;
        float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        float w = b.Fan ? 0.8f : 1.0f;

        // a belt into a bin stops at the bin's wall (it used to run through it); the pieces hop in from the head drum
        var destination = new SerializedObject(conveyor).FindProperty("destination").objectReferenceValue as Storage;
        if (destination != null && !b.JoinB)
        {
            var binMesh = destination.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(m => m.name == "Bin");
            var bin = MeshBounds(binMesh != null ? binMesh.transform : destination.transform);
            bin.Expand(new Vector3(0.1f, 100f, 0.1f));
            Vector3 right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            bool Inside(float trim)
            {
                Vector3 end = lb + dir * (trim + 0.1f);   // the motor guard overhangs the frame by 0.1
                for (float x = -(w * 0.5f + 0.17f); x <= w * 0.5f + 0.52f; x += 0.1f)
                    if (bin.Contains(t.TransformPoint(end + right * x))) return true;
                return false;
            }

            while (endTrim > 1f - run && Inside(endTrim)) endTrim -= 0.05f;
            var last = Points(conveyor)[^1];
            Vector3 headAt = t.TransformPoint(lb + dir * (endTrim - DrumR));
            last.position = new Vector3(headAt.x, last.position.y, headAt.z);
        }

        Vector3 s = la - dir * startTrim, e = lb + dir * endTrim;
        float len = Vector3.Distance(s, e);
        Vector3 mid = (s + e) * 0.5f;
        float half = len * 0.5f;

        var k = new MeshKit();
        k.Push(mid, new Vector3(0f, yaw, 0f));
        foreach (int side in new[] { -1, 1 })
        {
            float x = side * (w * 0.5f + 0.05f);
            k.Box(C(PC.Yellow), new Vector3(x, Top - 0.04f, 0f), new Vector3(0.06f, 0.26f, len), 0.01f);                       // web
            k.Box(C(PC.Yellow), new Vector3(side * (w * 0.5f + 0.02f), Top + 0.095f, 0f), new Vector3(0.14f, 0.03f, len), 0.01f);    // top flange
            k.Box(C(PC.Charcoal), new Vector3(side * (w * 0.5f + 0.02f), Top - 0.17f, 0f), new Vector3(0.14f, 0.03f, len), 0.01f);   // bottom flange
            k.Box(C(PC.Rubber), new Vector3(side * (w * 0.5f - 0.05f), Top + 0.08f, 0f), new Vector3(0.03f, 0.11f, len - 0.3f), 0.005f); // skirt board
            for (float z = -half + 0.3f; z < half - 0.2f; z += 0.6f)
            {
                k.Box(C(PC.Charcoal), new Vector3(side * (w * 0.5f + 0.085f), Top - 0.04f, z), new Vector3(0.012f, 0.12f, 0.24f), 0f);   // hazard block
                k.Box(C(PC.Charcoal), new Vector3(side * (w * 0.5f + 0.02f), Top + 0.112f, z + 0.15f), new Vector3(0.142f, 0.005f, 0.12f), 0f); // flange stripe
                k.Cylinder(C(PC.Silver), new Vector3(side * (w * 0.5f + 0.085f), Top + 0.04f, z + 0.3f), 0.022f, 0.02f, 6, new Vector3(0f, 0f, 90f), 0f);
            }
        }

        // return belt and its idlers
        k.Box(C(PC.Rubber), new Vector3(0f, Top - 0.26f, 0f), new Vector3(w - 0.1f, 0.025f, len - 0.4f), 0.005f);
        for (float z = -half + 0.75f; z < half - 0.4f; z += 1.5f)
            k.Cylinder(C(PC.Gray), new Vector3(0f, Top - 0.3f, z), 0.04f, w - 0.06f, 8, new Vector3(0f, 0f, 90f), 0.005f);

        // A-frame legs with a cross brace and foot plates
        int legs = Mathf.Max(2, Mathf.RoundToInt((len - 0.6f) / 1.5f) + 1);
        for (int i = 0; i < legs; i++)
        {
            float z = Mathf.Lerp(-half + 0.35f, half - 0.35f, legs == 1 ? 0.5f : i / (float)(legs - 1));
            foreach (int side in new[] { -1, 1 })
            {
                float x = side * (w * 0.5f - 0.02f);
                k.Box(C(PC.Gray), new Vector3(x + side * 0.04f, (Top - 0.18f) * 0.5f, z), new Vector3(0.07f, Top - 0.18f, 0.07f), 0.01f, new Vector3(0f, 0f, side * -6f));
                k.Box(C(PC.Charcoal), new Vector3(x + side * 0.06f, 0.012f, z), new Vector3(0.2f, 0.025f, 0.2f), 0.005f);
            }

            k.Box(C(PC.Gray), new Vector3(0f, 0.09f, z), new Vector3(w, 0.04f, 0.05f), 0.005f);
        }

        // ends: bearing blocks at free ends, a drive motor under a guard at the head
        foreach (var (z, free, head) in new[] { (-half + DrumR, !b.JoinA, false), (half - DrumR, !b.JoinB, true) })
        {
            if (!free) continue;
            foreach (int side in new[] { -1, 1 })
                k.Box(C(PC.Charcoal), new Vector3(side * (w * 0.5f + 0.12f), Top - DrumR, z), new Vector3(0.1f, 0.2f, 0.24f), 0.02f);
            if (!head) continue;
            float mx = w * 0.5f + 0.36f;
            k.Box(C(PC.Blue), new Vector3(mx, Top - 0.12f, z - 0.05f), new Vector3(0.3f, 0.28f, 0.4f), 0.05f);
            for (int f = 0; f < 4; f++) k.Box(C(PC.BlueDeep), new Vector3(mx, Top - 0.24f + f * 0.08f, z - 0.27f), new Vector3(0.26f, 0.025f, 0.04f), 0f);
            k.Cylinder(C(PC.Charcoal), new Vector3(w * 0.5f + 0.18f, Top - DrumR, z), 0.09f, 0.12f, 10, new Vector3(0f, 0f, 90f), 0.01f);
            k.Box(C(PC.Yellow), new Vector3(w * 0.5f + 0.2f, Top + 0.02f, z), new Vector3(0.2f, 0.06f, 0.5f), 0.03f);   // guard
        }

        k.Pop();
        var mats = new[] { ArtPalette.Material };
        var frame = ArtAssets.MeshObject("Frame", t, ArtAssets.SaveMesh("ConveyorsF4", conveyor.name + "_Frame", k.ToPaletteMesh(conveyor.name + "_Frame")), mats);
        GameObjectUtility.SetStaticEditorFlags(frame, StaticEditorFlags.BatchingStatic);

        // the belt between the drums
        float beltStart = b.JoinA ? -half : -half + DrumR, beltEnd = b.JoinB ? half : half - DrumR;
        float beltLen = beltEnd - beltStart;
        var belt = new MeshKit { UvScale = 1f };
        belt.Push(mid + Vector3.up * (Top + 0.002f), new Vector3(0f, yaw, 0f));
        belt.Plane(0, new Vector3(0f, 0f, (beltStart + beltEnd) * 0.5f), new Vector2(w - 0.08f, beltLen), default, false, new Rect(0f, 0f, 1f, beltLen));
        belt.Pop();
        var beltGo = ArtAssets.MeshObject("Belt", t, ArtAssets.SaveMesh("ConveyorsF4", conveyor.name + "_Belt", belt.ToMesh(conveyor.name + "_Belt", 1)), new[] { beltMat },
            default, default, false);
        beltGo.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

        // drums that turn with the belt
        var drumKit = new MeshKit();
        drumKit.Cylinder(C(PC.Rubber), Vector3.zero, DrumR, w - 0.06f, 14, new Vector3(0f, 0f, 90f), 0.02f);
        for (int i = 0; i < 4; i++)
            drumKit.Box(C(PC.Silver), Quaternion.Euler(i * 90f, 0f, 0f) * new Vector3(0f, DrumR - 0.005f, 0f), new Vector3(w - 0.1f, 0.02f, 0.05f), 0.005f,
                new Vector3(i * 90f, 0f, 0f));
        drumKit.Cylinder(C(PC.Silver), Vector3.zero, 0.05f, w + 0.16f, 8, new Vector3(0f, 0f, 90f), 0.005f);
        var drumMesh = ArtAssets.SaveMesh("ConveyorsF4", "Drum_" + (b.Fan ? "Narrow" : "Wide"), drumKit.ToPaletteMesh("Drum"));
        var rollers = new List<Transform>();
        var rot = Quaternion.Euler(0f, yaw, 0f);
        foreach (var (z, free, name) in new[] { (-half + DrumR, !b.JoinA, "Drum_Tail"), (half - DrumR, !b.JoinB, "Drum_Head") })
        {
            if (!free) continue;
            var d = ArtAssets.MeshObject(name, t, drumMesh, mats, mid + rot * new Vector3(0f, Top - DrumR + 0.002f, z));
            d.transform.localRotation = rot;
            rollers.Add(d.transform);
        }

        var so = new SerializedObject(conveyor);
        so.FindProperty("rideHeight").floatValue = Top + 0.17f;
        var bp = so.FindProperty("belts");
        bp.arraySize = 1;
        bp.GetArrayElementAtIndex(0).objectReferenceValue = beltGo.GetComponent<Renderer>();
        so.FindProperty("beltScroll").vector2Value = new Vector2(0f, -1f);
        var rp = so.FindProperty("rollers");
        rp.arraySize = rollers.Count;
        for (int i = 0; i < rollers.Count; i++) rp.GetArrayElementAtIndex(i).objectReferenceValue = rollers[i];
        so.FindProperty("rollerRadius").floatValue = DrumR;
        so.ApplyModifiedPropertiesWithoutUndo();

        if (conveyor.TryGetComponent(out BoxCollider box))
        {
            box.center = mid + Vector3.up * 0.25f;
            var size = rot * new Vector3(w + 0.3f, 0.5f, len);
            box.size = new Vector3(Mathf.Abs(size.x), 0.5f, Mathf.Abs(size.z));
        }
    }
}

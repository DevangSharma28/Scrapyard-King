using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Lists world objects whose meshes run into each other: stations, conveyors, tiles, pads and props, measured by the
/// combined bounds of their renderers. Reports pairs where the overlap is more than <c>share</c> of the smaller one
/// (default 0.2), largest first. Run in Play mode with the areas open (expansion content is hidden in Edit mode).
///   unity command run_script --file AgentScripts/Tools/MeshOverlap.cs --entry MeshOverlap.Run --args '["0.2"]'
/// </summary>
public static class MeshOverlap
{
    sealed class Unit
    {
        public GameObject Go;
        public Bounds B;
        public string Kind;
    }

    public static string Run(string args)
    {
        float share = float.TryParse(args, out float v) ? v : 0.2f;
        var units = new List<Unit>();
        var seen = new HashSet<GameObject>();
        foreach (var mb in Object.FindObjectsByType<MonoBehaviour>())
        {
            var go = mb.gameObject;
            string kind = mb switch
            {
                ScrapYardKing.Factory.Machine => "machine",
                ScrapYardKing.Factory.Storage => "storage",
                ScrapYardKing.Factory.SellDesk => "desk",
                ScrapYardKing.Factory.TruckBay => "dock",
                ScrapYardKing.Factory.Conveyor => "belt",
                ScrapYardKing.Factory.ClawCrane => "crane",
                ScrapYardKing.Tiles.Tile => "tile",
                ScrapYardKing.Items.TransferPad => "pad",
                _ => null
            };
            if (kind != null && seen.Add(go)) units.Add(new Unit { Go = go, Kind = kind });
        }

        foreach (var t in Object.FindObjectsByType<Transform>())
            if ((t.name.StartsWith("Prop_") || t.name.StartsWith("EnvPB_")) && t.GetComponentInParent<ScrapYardKing.Factory.Machine>() == null && seen.Add(t.gameObject))
                units.Add(new Unit { Go = t.gameObject, Kind = "prop" });

        // a unit's own bounds: its renderers, minus those of units nested inside it
        foreach (var u in units)
        {
            bool any = false;
            foreach (var r in u.Go.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r.GetComponentInParent<Canvas>() != null || !r.enabled) continue;
                var owner = Owner(r.transform, seen);
                if (owner != u.Go) continue;
                if (!any) u.B = r.bounds;
                else u.B.Encapsulate(r.bounds);
                any = true;
            }

            if (!any) u.B = new Bounds(u.Go.transform.position, Vector3.zero);
        }

        units.RemoveAll(u => u.B.size.sqrMagnitude < 0.01f);
        var hits = new List<(float share, string line)>();
        for (int i = 0; i < units.Count; i++)
        for (int j = i + 1; j < units.Count; j++)
        {
            var a = units[i];
            var b = units[j];
            if (a.Go.transform.IsChildOf(b.Go.transform) || b.Go.transform.IsChildOf(a.Go.transform)) continue;
            if (!a.B.Intersects(b.B)) continue;
            var min = Vector3.Max(a.B.min, b.B.min);
            var max = Vector3.Min(a.B.max, b.B.max);
            var d = max - min;
            if (d.x <= 0f || d.y <= 0f || d.z <= 0f) continue;
            // footprints: tiles and pads are flat, judge them on the ground plane
            float areaA = a.B.size.x * a.B.size.z, areaB = b.B.size.x * b.B.size.z;
            float s = d.x * d.z / Mathf.Max(0.01f, Mathf.Min(areaA, areaB));
            if (s < share) continue;
            hits.Add((s, $"{s:P0} {a.Kind} {Path(a.Go)} × {b.Kind} {Path(b.Go)} at ({(min.x + max.x) * 0.5f:0.0},{(min.z + max.z) * 0.5f:0.0})"));
        }

        var sb = new StringBuilder($"{units.Count} units, {hits.Count} overlaps over {share:P0}\n");
        foreach (var h in hits.OrderByDescending(h => h.share).Take(80)) sb.AppendLine(h.line);
        return sb.ToString();
    }

    static GameObject Owner(Transform t, HashSet<GameObject> units)
    {
        for (var p = t; p != null; p = p.parent)
            if (units.Contains(p.gameObject)) return p.gameObject;
        return null;
    }

    static string Path(GameObject go)
    {
        var p = go.transform.parent;
        return (p != null ? p.name + "/" : "") + go.name;
    }
}

using System.Text;
using UnityEngine;

/// <summary>
/// Prints a UI hierarchy (name, active state, anchors, position, size, components) for layout work from the CLI.
///   unity command run_script --file AgentScripts/Tools/UiDump.cs --entry UiDump.Run --args '["_UI/Canvas/HUD 3"]'
/// Argument: "path [depth]" (path from a scene root object, depth default 3).
/// </summary>
public static class UiDump
{
    public static string Run(string args)
    {
        var parts = (args ?? "_UI/Canvas").Split(' ');
        int depth = parts.Length > 1 && int.TryParse(parts[1], out int d) ? d : 3;
        string[] path = parts[0].Split('/');
        var root = GameObject.Find(path[0]);
        if (root == null) return "no root " + path[0];
        var t = root.transform;
        for (int i = 1; i < path.Length && t != null; i++) t = t.Find(path[i]);
        if (t == null) return "no path " + parts[0];
        var sb = new StringBuilder();
        Write(t, 0, depth, sb);
        return sb.ToString();
    }

    static void Write(Transform t, int level, int depth, StringBuilder sb)
    {
        sb.Append(new string(' ', level * 2)).Append(t.name);
        if (!t.gameObject.activeSelf) sb.Append(" (off)");
        if (t is RectTransform rt)
            sb.Append($" a={rt.anchorMin.x:0.##},{rt.anchorMin.y:0.##}-{rt.anchorMax.x:0.##},{rt.anchorMax.y:0.##}")
              .Append($" p={rt.anchoredPosition.x:0},{rt.anchoredPosition.y:0} s={rt.sizeDelta.x:0},{rt.sizeDelta.y:0}");
        foreach (var c in t.GetComponents<Component>())
        {
            if (c is Transform || c is CanvasRenderer) continue;
            sb.Append(" [").Append(c.GetType().Name).Append(']');
        }
        sb.Append('\n');
        if (level >= depth) return;
        foreach (Transform child in t) Write(child, level + 1, depth, sb);
    }
}

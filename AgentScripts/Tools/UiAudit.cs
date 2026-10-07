using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Play-mode UI audit of the screen-space canvas (_UI/Canvas) as it is laid out right now:
///  - OVERLAP: two HUD widgets drawn over each other (modal panels and full-screen dims excluded);
///  - TEXT×TEXT: two texts whose rendered glyphs overlap, anywhere (also inside open panels);
///  - SPILL: a text whose glyphs run outside its own rect (or TMP reports overflow);
///  - OFFSCREEN / UNSAFE: a visible element outside the screen, or a HUD element outside the safe area;
///  - TARGET: a button smaller than 70 canvas units on a side;  TINY: text drawn below 18 units.
///   unity command run_script --file AgentScripts/Tools/UiAudit.cs --entry UiAudit.Run --args '["insets 0 0 0.055 0.0425"]'
///   ... --entry UiAudit.Res --args '["1080 2400"]' first to test another phone shape (Game view resolution).
/// Arguments (optional): "insets L R T B" simulates a notch / gesture bar as screen fractions (SafeArea.Simulated) before
/// measuring; "insets 0 0 0 0" clears it. "list" also prints every HUD widget's bounds.
/// </summary>
public static class UiAudit
{
    sealed class Item
    {
        public Graphic g;
        public Rect r;
        public string owner, path;
        public bool text, modal;
    }

    /// <summary>"W H": sets the Game view's rendering resolution (the layout follows on the next frame). Prints the old one.</summary>
    public static string Res(string args)
    {
        var p = args.Split(' ');
        UnityEditor.PlayModeWindow.GetRenderingResolution(out uint w0, out uint h0);
        UnityEditor.PlayModeWindow.SetCustomRenderingResolution(uint.Parse(p[0]), uint.Parse(p[1]), "UiAudit " + p[0] + "x" + p[1]);
        return $"was {w0}x{h0}, now {p[0]}x{p[1]}";
    }

    public static string Run(string args)
    {
        if (!Application.isPlaying) return "play mode only";
        var parts = (args ?? "").Split(' ').Where(p => p.Length > 0).ToArray();
        int ins = System.Array.IndexOf(parts, "insets");
        if (ins >= 0 && parts.Length >= ins + 5)
        {
            // insets come as fractions of the screen (left, right, top, bottom), e.g. 0 0 0.055 0.0425 for a notch phone
            ScrapYardKing.UI.SafeArea.Simulated = new Vector4(float.Parse(parts[ins + 1]) * Screen.width, float.Parse(parts[ins + 2]) * Screen.width,
                float.Parse(parts[ins + 3]) * Screen.height, float.Parse(parts[ins + 4]) * Screen.height);
            foreach (var s in Object.FindObjectsByType<ScrapYardKing.UI.SafeArea>())
            {
                s.enabled = false;
                s.enabled = true;
            }
        }

        bool list = parts.Contains("list");
        var canvasGo = GameObject.Find("_UI/Canvas");
        if (canvasGo == null) return "no _UI/Canvas";
        Canvas.ForceUpdateCanvases();
        var cr = (RectTransform)canvasGo.transform;
        var screen = new Rect(0f, 0f, cr.rect.width, cr.rect.height);
        var hud = cr.Find("HUD") as RectTransform;
        var safe = hud != null ? ToCanvas(cr, hud, hud.rect) : screen;

        var items = new List<Item>();
        foreach (var g in canvasGo.GetComponentsInChildren<Graphic>(false))
        {
            if (!g.isActiveAndEnabled || g is TMP_SubMeshUI || Alpha(g) < 0.05f) continue;
            if (g.TryGetComponent(out Mask m) && !m.showMaskGraphic) continue;
            var tmp = g as TMP_Text;
            Rect r;
            if (tmp != null)
            {
                if (string.IsNullOrWhiteSpace(tmp.text) || tmp.textInfo == null || tmp.textInfo.characterCount == 0) continue;
                var b = tmp.textBounds;
                if (b.size.x <= 0.5f) continue;
                r = ToCanvas(cr, tmp.rectTransform, new Rect(b.min.x, b.min.y, b.size.x, b.size.y));
            }
            else r = ToCanvas(cr, g.rectTransform, g.rectTransform.rect);

            if (!Clip(cr, g.transform, ref r)) continue;
            var (owner, path) = Owner(cr, g.transform);
            if (owner == null) continue;
            items.Add(new Item { g = g, r = r, owner = owner, path = path, text = tmp != null });
        }

        // a widget that holds a full-screen graphic is a modal panel (dim): overlapping the HUD is its job
        float full = screen.width * screen.height * 0.6f;
        var modalOwners = new HashSet<string>(items.Where(i => i.r.width * i.r.height > full).Select(i => i.owner.Split('/')[0]));
        foreach (var i in items) i.modal = modalOwners.Contains(i.owner.Split('/')[0]) || i.owner.StartsWith("UpgradePanel");
        items.RemoveAll(i => i.r.width * i.r.height > full);
        items.RemoveAll(i => i.owner.StartsWith("FlyLayer") || i.owner.StartsWith("GuidePointer"));

        var issues = new List<string>();
        var seen = new HashSet<string>();
        void Add(string key, string line)
        {
            if (seen.Add(key)) issues.Add(line);
        }

        // HUD widget × HUD widget
        var hudItems = items.Where(i => !i.modal).ToList();
        for (int a = 0; a < hudItems.Count; a++)
        for (int b = a + 1; b < hudItems.Count; b++)
        {
            var A = hudItems[a];
            var B = hudItems[b];
            if (A.owner == B.owner) continue;
            var x = Intersect(A.r, B.r);
            if (x.width < 6f || x.height < 6f) continue;
            string k = string.CompareOrdinal(A.owner, B.owner) < 0 ? A.owner + "|" + B.owner : B.owner + "|" + A.owner;
            Add("o" + k + A.path + B.path, $"OVERLAP {A.path} × {B.path}  {x.width:0}×{x.height:0} at ({x.x:0},{x.y:0})");
        }

        // text × text anywhere (not parent and child)
        var texts = items.Where(i => i.text).ToList();
        for (int a = 0; a < texts.Count; a++)
        for (int b = a + 1; b < texts.Count; b++)
        {
            var A = texts[a];
            var B = texts[b];
            if (A.modal != B.modal) continue;
            if (A.g.transform.IsChildOf(B.g.transform) || B.g.transform.IsChildOf(A.g.transform)) continue;
            var x = Intersect(A.r, B.r);
            if (x.width < 4f || x.height < 4f) continue;
            Add("t" + A.path + B.path, $"TEXT×TEXT '{Short(A)}' ({A.path}) × '{Short(B)}' ({B.path})  {x.width:0}×{x.height:0}");
        }

        foreach (var i in items)
        {
            if (i.text)
            {
                var t = (TMP_Text)i.g;
                var own = ToCanvas(cr, t.rectTransform, t.rectTransform.rect);
                bool spill = i.r.xMin < own.xMin - 4f || i.r.xMax > own.xMax + 4f || i.r.yMin < own.yMin - 6f || i.r.yMax > own.yMax + 6f;
                if (spill || t.isTextOverflowing)
                    Add("s" + i.path, $"SPILL '{Short(i)}' ({i.path}) text {i.r.width:0}×{i.r.height:0} in rect {own.width:0}×{own.height:0}{(t.isTextOverflowing ? " overflow" : "")}");
                float drawn = t.fontSize * t.rectTransform.lossyScale.y / cr.lossyScale.y;
                if (drawn < 18f) Add("f" + i.path, $"TINY '{Short(i)}' ({i.path}) {drawn:0.#} units");
            }

            if (i.r.xMin < screen.xMin - 4f || i.r.xMax > screen.xMax + 4f || i.r.yMin < screen.yMin - 4f || i.r.yMax > screen.yMax + 4f)
                Add("x" + i.path, $"OFFSCREEN {i.path} ({i.r.xMin:0},{i.r.yMin:0})-({i.r.xMax:0},{i.r.yMax:0}) screen {screen.width:0}×{screen.height:0}");
            else if (!i.modal && (i.r.xMin < safe.xMin - 4f || i.r.xMax > safe.xMax + 4f || i.r.yMin < safe.yMin - 4f || i.r.yMax > safe.yMax + 4f))
                Add("u" + i.path, $"UNSAFE {i.path} ({i.r.xMin:0},{i.r.yMin:0})-({i.r.xMax:0},{i.r.yMax:0})");
        }

        foreach (var s in canvasGo.GetComponentsInChildren<Selectable>(false))
        {
            if (!s.isActiveAndEnabled || !s.interactable || s.targetGraphic == null || Alpha(s.targetGraphic) < 0.05f) continue;
            var r = ToCanvas(cr, (RectTransform)s.transform, ((RectTransform)s.transform).rect);
            if (!Clip(cr, s.transform, ref r)) continue;
            if (Mathf.Min(r.width, r.height) < 70f) Add("b" + s.name, $"TARGET {Owner(cr, s.transform).path} {r.width:0}×{r.height:0}");
        }

        var sb = new StringBuilder($"screen {Screen.width}x{Screen.height} canvas {screen.width:0}×{screen.height:0} safe ({safe.xMin:0},{safe.yMin:0})-({safe.xMax:0},{safe.yMax:0}) items {items.Count}\n");
        foreach (var line in issues.OrderBy(l => l)) sb.AppendLine(line);
        if (list)
            foreach (var g in hudItems.GroupBy(i => i.owner))
            {
                var u = g.Select(i => i.r).Aggregate((p, q) => Rect.MinMaxRect(Mathf.Min(p.xMin, q.xMin), Mathf.Min(p.yMin, q.yMin), Mathf.Max(p.xMax, q.xMax), Mathf.Max(p.yMax, q.yMax)));
                sb.AppendLine($"  {g.Key}: x {u.xMin:0}..{u.xMax:0}  y {u.yMin:0}..{u.yMax:0}");
            }

        return sb.ToString();
    }

    /// <summary>
    /// World-space labels, tiles and bubbles as the camera sees them now: every pair whose drawn parts cover each other
    /// on screen by more than 15% of the smaller one. Argument (optional): minimum share, default 0.15.
    /// </summary>
    public static string World(string args)
    {
        if (!Application.isPlaying) return "play mode only";
        float minShare = float.TryParse(args, out float v) ? v : 0.15f;
        var cam = Camera.main;
        var view = new Rect(0f, 0f, cam.pixelWidth, cam.pixelHeight);
        var boxes = new List<(string name, Rect r, Vector3 pos)>();
        foreach (var c in Object.FindObjectsByType<Canvas>())
        {
            if (c.renderMode != RenderMode.WorldSpace || !c.isActiveAndEnabled || c.rootCanvas != c) continue;
            Rect u = Rect.zero;
            bool any = false;
            foreach (var g in c.GetComponentsInChildren<Graphic>(false))
            {
                if (!g.isActiveAndEnabled || g is TMP_SubMeshUI || Alpha(g) < 0.5f) continue;   // faded labels have stepped back
                if (g is TMP_Text t && string.IsNullOrWhiteSpace(t.text)) continue;
                var corners = new Vector3[4];
                g.rectTransform.GetWorldCorners(corners);
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                bool behind = false;
                foreach (var w in corners)
                {
                    var sp = cam.WorldToScreenPoint(w);
                    if (sp.z < 0f) behind = true;
                    x0 = Mathf.Min(x0, sp.x); y0 = Mathf.Min(y0, sp.y); x1 = Mathf.Max(x1, sp.x); y1 = Mathf.Max(y1, sp.y);
                }

                if (behind) continue;
                var r = Rect.MinMaxRect(x0, y0, x1, y1);
                u = any ? Rect.MinMaxRect(Mathf.Min(u.xMin, r.xMin), Mathf.Min(u.yMin, r.yMin), Mathf.Max(u.xMax, r.xMax), Mathf.Max(u.yMax, r.yMax)) : r;
                any = true;
            }

            if (!any || Intersect(u, view).width < 2f) continue;
            string name = c.transform.parent != null ? c.transform.parent.name + "/" + c.name : c.name;
            boxes.Add((name, u, c.transform.position));
        }

        var sb = new StringBuilder($"camera {cam.pixelWidth}x{cam.pixelHeight}, {boxes.Count} world canvases in view\n");
        for (int a = 0; a < boxes.Count; a++)
        for (int b = a + 1; b < boxes.Count; b++)
        {
            var x = Intersect(boxes[a].r, boxes[b].r);
            if (x.width <= 0f) continue;
            float share = x.width * x.height / Mathf.Min(boxes[a].r.width * boxes[a].r.height, boxes[b].r.width * boxes[b].r.height);
            if (share < minShare) continue;
            sb.AppendLine($"WORLD {boxes[a].name} @({boxes[a].pos.x:0.#},{boxes[a].pos.z:0.#}) × {boxes[b].name} @({boxes[b].pos.x:0.#},{boxes[b].pos.z:0.#})  {share:P0}");
        }

        return sb.ToString();
    }

    static float Alpha(Graphic g)
    {
        float a = g.color.a * g.canvasRenderer.GetAlpha();
        if (g is TMP_Text t) a = t.alpha * g.canvasRenderer.GetAlpha();
        foreach (var cg in g.GetComponentsInParent<CanvasGroup>())
        {
            a *= cg.alpha;
            if (cg.ignoreParentGroups) break;
        }

        if (g is Image img && img.sprite == null && img.color.a < 0.3f) a = 0f;   // raycast blockers
        var root = g.canvas != null ? g.canvas.rootCanvas.transform : null;
        float rel = root != null ? g.transform.lossyScale.x / Mathf.Max(1e-6f, root.lossyScale.x) : g.transform.lossyScale.x;
        if (rel < 0.05f) a = 0f;                                                   // popped out (scale 0)
        return a;
    }

    /// <summary>Canvas-space rect (canvas units, origin at the canvas centre... converted to bottom-left) of a local rect.</summary>
    static Rect ToCanvas(RectTransform canvas, RectTransform t, Rect local)
    {
        var c = new Vector3[4];
        c[0] = t.TransformPoint(new Vector3(local.xMin, local.yMin));
        c[1] = t.TransformPoint(new Vector3(local.xMin, local.yMax));
        c[2] = t.TransformPoint(new Vector3(local.xMax, local.yMax));
        c[3] = t.TransformPoint(new Vector3(local.xMax, local.yMin));
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        var off = canvas.rect.min;
        foreach (var w in c)
        {
            var p = (Vector2)canvas.InverseTransformPoint(w) - off;
            x0 = Mathf.Min(x0, p.x); y0 = Mathf.Min(y0, p.y); x1 = Mathf.Max(x1, p.x); y1 = Mathf.Max(y1, p.y);
        }

        return Rect.MinMaxRect(x0, y0, x1, y1);
    }

    /// <summary>Cuts the rect by any RectMask2D / Mask above it (scroll views); false when nothing is left.</summary>
    static bool Clip(RectTransform canvas, Transform t, ref Rect r)
    {
        for (var p = t.parent; p != null && p != canvas; p = p.parent)
        {
            if (!(p.GetComponent<RectMask2D>() != null || p.GetComponent<Mask>() != null)) continue;
            var pr = (RectTransform)p;
            r = Intersect(r, ToCanvas(canvas, pr, pr.rect));
            if (r.width <= 1f || r.height <= 1f) return false;
        }

        return true;
    }

    static Rect Intersect(Rect a, Rect b)
    {
        float x0 = Mathf.Max(a.xMin, b.xMin), y0 = Mathf.Max(a.yMin, b.yMin), x1 = Mathf.Min(a.xMax, b.xMax), y1 = Mathf.Min(a.yMax, b.yMax);
        return x1 > x0 && y1 > y0 ? Rect.MinMaxRect(x0, y0, x1, y1) : Rect.zero;
    }

    /// <summary>(widget, path): the widget is the HUD child (or the child of a stretched HUD container like TaskBanner).</summary>
    static (string owner, string path) Owner(RectTransform canvas, Transform t)
    {
        var names = new List<Transform>();
        for (var p = t; p != null && p != canvas; p = p.parent) names.Insert(0, p);
        if (names.Count == 0) return (null, "");
        string path = string.Join("/", names.Select(n => n.name));
        int start = names[0].name == "HUD" ? 1 : 0;
        if (names.Count <= start) return (null, path);
        var top = names[start];
        string owner = top.name;
        if (top is RectTransform rt && rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.one && top.GetComponent<Graphic>() == null && names.Count > start + 1)
            owner += "/" + names[start + 1].name;
        return (owner, string.Join("/", names.Skip(start).Select(n => n.name)));
    }

    static string Short(Item i)
    {
        string s = ((TMP_Text)i.g).GetParsedText().Replace("\n", " ");
        return s.Length > 24 ? s.Substring(0, 24) + "…" : s;
    }
}

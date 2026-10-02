using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

// Play-mode feel audit: renders the main camera every N seconds of game time into one tiled PNG ("contact sheet"),
// so motion, timing and feedback can be judged from a single image.
// Usage (play mode): unity command run_script --file AgentScripts/Tools/ContactSheet.cs --entry ContactSheet.Start
//   --args '["/abs/out.png 12 0.1 4 1.8"]'   (one JSON string: path, frames, seconds between frames, columns, zoom)
// Time.captureDeltaTime fixes the frame step while recording, so the sheet shows game time, not editor hiccups.
public static class ContactSheet
{
    static Texture2D sheet;
    static RenderTexture rt;
    static Camera lens;
    static float zoom;
    static string path;
    static int frames, columns, taken;
    static float interval, nextAt, previousCaptureDelta;
    const int CellW = 270, CellH = 480;

    public static string Start(string args)
    {
        var a = (args ?? string.Empty).Split(' ');
        path = a.Length > 0 && a[0].Length > 0 ? a[0] : Path.Combine(Path.GetTempPath(), "contact.png");
        frames = a.Length > 1 ? int.Parse(a[1]) : 12;
        interval = a.Length > 2 ? float.Parse(a[2], CultureInfo.InvariantCulture) : 0.1f;
        columns = a.Length > 3 ? int.Parse(a[3]) : 4;
        zoom = a.Length > 4 ? float.Parse(a[4], CultureInfo.InvariantCulture) : 1.8f;
        if (!EditorApplication.isPlaying) return "not playing";

        int rows = Mathf.CeilToInt(frames / (float)columns);
        sheet = new Texture2D(CellW * columns, CellH * rows, TextureFormat.RGB24, false);
        rt = new RenderTexture(CellW, CellH, 24) { antiAliasing = 1 };
        taken = 0;
        nextAt = Time.time;
        previousCaptureDelta = Time.captureDeltaTime;
        Time.captureDeltaTime = 1f / 60f;
        // Portrait framing even when the editor Game view is landscape (CameraController reads cam.aspect).
        if (Camera.main != null) Camera.main.aspect = CellW / (float)CellH;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        return $"recording {frames} frames every {interval}s → {path}";
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying)
        {
            Finish();
            return;
        }

        if (Time.time < nextAt) return;
        nextAt += interval;
        var main = Camera.main;
        if (main == null) return;
        // A tracking clone with a narrower field of view: same framing logic as the game, closer in.
        if (lens == null) lens = new GameObject("ContactSheetLens") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<Camera>();
        lens.CopyFrom(main);
        lens.enabled = false;
        lens.fieldOfView = main.fieldOfView / Mathf.Max(0.1f, zoom);
        lens.aspect = CellW / (float)CellH;
        lens.targetTexture = rt;
        lens.Render();
        lens.targetTexture = null;
        RenderTexture.active = rt;
        int col = taken % columns, row = taken / columns;
        int rows = sheet.height / CellH;
        sheet.ReadPixels(new Rect(0, 0, CellW, CellH), col * CellW, (rows - 1 - row) * CellH);
        RenderTexture.active = null;
        if (++taken >= frames) Finish();
    }

    static void Finish()
    {
        EditorApplication.update -= Tick;
        Time.captureDeltaTime = previousCaptureDelta;
        if (Camera.main != null) Camera.main.ResetAspect();
        if (lens != null) Object.DestroyImmediate(lens.gameObject);
        lens = null;
        if (sheet == null) return;
        sheet.Apply();
        File.WriteAllBytes(path, sheet.EncodeToPNG());
        Object.DestroyImmediate(sheet);
        sheet = null;
        if (rt != null) rt.Release();
        rt = null;
        Debug.Log($"[ContactSheet] saved {taken} frames to {path}");
    }
}

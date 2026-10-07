using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;

// Play-mode performance probe: samples managed allocation per frame, draw calls, SetPass calls, triangles and frame
// time for a few seconds and writes one line of averages / peaks to a file. Editor numbers (the Editor allocates too),
// so use it to compare before / after and busy / quiet, not as device numbers.
// Usage (play mode): unity command run_script --file AgentScripts/Tools/PerfProbe.cs --entry PerfProbe.Start
//   --args '["/abs/out.txt 5 label"]'   (file, seconds of unscaled time, free-text label). The line is appended.
public static class PerfProbe
{
    public static string Start(string args)
    {
        if (!EditorApplication.isPlaying) return "not playing";
        var a = (args ?? string.Empty).Split(' ');
        var go = new GameObject("PerfProbe") { hideFlags = HideFlags.DontSave };
        var runner = go.AddComponent<PerfProbeRunner>();
        runner.Path = a.Length > 0 && a[0].Length > 0 ? a[0] : Path.Combine(Path.GetTempPath(), "perf.txt");
        runner.Duration = a.Length > 1 ? float.Parse(a[1], CultureInfo.InvariantCulture) : 5f;
        runner.Label = a.Length > 2 ? a[2] : "probe";
        return $"probing {runner.Duration}s → {runner.Path}";
    }
}

public sealed class PerfProbeRunner : MonoBehaviour
{
    public string Path, Label;
    public float Duration;

    ProfilerRecorder gc, draws, setPass, tris;
    double gcSum, drawSum, setPassSum, triSum;
    long gcMax;
    float start, worstFrame;
    int frames, skip = 3;

    void OnEnable()
    {
        gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
        draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
        setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
        tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
    }

    void OnDisable()
    {
        gc.Dispose();
        draws.Dispose();
        setPass.Dispose();
        tris.Dispose();
    }

    void Update()
    {
        // The first frames still carry the script compile that started the probe.
        if (skip > 0)
        {
            skip--;
            start = Time.unscaledTime;
            return;
        }

        frames++;
        long g = gc.LastValue;
        gcSum += g;
        if (g > gcMax) gcMax = g;
        drawSum += draws.LastValue;
        setPassSum += setPass.LastValue;
        triSum += tris.LastValue;
        worstFrame = Mathf.Max(worstFrame, Time.unscaledDeltaTime);
        if (Time.unscaledTime - start < Duration) return;

        float seconds = Time.unscaledTime - start;
        var sb = new StringBuilder();
        sb.Append(Label).Append(": frames=").Append(frames)
            .Append(" fps=").Append((frames / seconds).ToString("0.0", CultureInfo.InvariantCulture))
            .Append(" worstFrameMs=").Append((worstFrame * 1000f).ToString("0", CultureInfo.InvariantCulture))
            .Append(" gcBytesPerFrame=").Append((gcSum / frames).ToString("0", CultureInfo.InvariantCulture))
            .Append(" gcMaxFrame=").Append(gcMax)
            .Append(" drawCalls=").Append((drawSum / frames).ToString("0", CultureInfo.InvariantCulture))
            .Append(" setPass=").Append((setPassSum / frames).ToString("0", CultureInfo.InvariantCulture))
            .Append(" tris=").Append((triSum / frames).ToString("0", CultureInfo.InvariantCulture))
            .Append(" gameTime=").Append(Time.time.ToString("0", CultureInfo.InvariantCulture))
            .AppendLine();
        File.AppendAllText(Path, sb.ToString());
        Destroy(gameObject);
    }
}

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ScrapYardKing.Core;
using ScrapYardKing.Economy;
using ScrapYardKing.Player;
using ScrapYardKing.Progression;
using ScrapYardKing.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

// Automated play test: plays the game the way a guided newcomer would, by following GuideDirector (walks to the marker
// along the NavMesh, buys the highlighted upgrade, stands on purchase tiles) plus a shopping trip whenever a panel
// upgrade is affordable. Runs at an accelerated time scale and writes a pacing timeline (tasks, levels, purchases,
// gates, cash) to a markdown report. A task that does not finish in `stallMinutes` is reported as a guide dead end.
//
// Start (play mode):  unity command run_script --file AgentScripts/Tools/GuideBot.cs --entry GuideBot.Start
//                       --args '["/abs/report.md 90 3"]'   (report path, game minutes to play, time scale)
// Stop early:         --entry GuideBot.Stop     Status: --entry GuideBot.Status
// Keep Unity focused while it runs (it freezes in the background), e.g. AgentScripts/Tools/wait.sh in a loop.
public static class GuideBot
{
    const float ArriveDistance = 0.35f, StuckSeconds = 2.5f, ShoppingEvery = 40f, StallMinutes = 6f;

    static readonly StringBuilder Timeline = new();
    static readonly List<string> Stalls = new();
    static readonly List<(float t, long cash, int level)> CashSamples = new();
    static string reportPath;
    static float playMinutes, timeScale, startTime, nextSample, nextShopCheck, lastProgressAt, stuckSince, nextBuy;
    static Vector3 lastPosition, shoppingTarget;
    static Vector3? lastGuideTarget;
    static bool running, shopping;
    static string lastTaskId;
    static NavMeshPath path;

    static PlayerCharacter player;
    static PlayerInputReader input;
    static GuideDirector guide;
    static TaskManager tasks;
    static UpgradeManager upgrades;
    static UpgradePanel panel;
    static ProgressionManager progression;
    static EconomyManager economy;

    public static string Start(string args)
    {
        if (!EditorApplication.isPlaying) return "not playing";
        var a = (args ?? string.Empty).Split(' ');
        reportPath = a.Length > 0 && a[0].Length > 0 ? a[0] : Path.Combine(Path.GetTempPath(), "guidebot.md");
        playMinutes = a.Length > 1 ? float.Parse(a[1], CultureInfo.InvariantCulture) : 90f;
        timeScale = a.Length > 2 ? float.Parse(a[2], CultureInfo.InvariantCulture) : 3f;

        if (!Services.TryGet(out player) || !Services.TryGet(out guide) || !Services.TryGet(out tasks) || !Services.TryGet(out upgrades) ||
            !Services.TryGet(out progression) || !Services.TryGet(out economy))
            return "missing services";
        Services.TryGet(out panel);
        input = player.GetComponent<PlayerInputReader>();
        path = new NavMeshPath();

        Timeline.Clear();
        Stalls.Clear();
        CashSamples.Clear();
        startTime = Time.time;
        lastProgressAt = Time.time;
        nextSample = Time.time;
        nextShopCheck = Time.time + ShoppingEvery;
        lastPosition = player.transform.position;
        stuckSince = -1f;
        shopping = false;
        lastTaskId = tasks.Main != null ? tasks.Main.Definition.Id : null;

        tasks.TaskCompleted += OnTaskCompleted;
        tasks.TaskProgressed += OnTaskProgressed;
        progression.LevelChanged += OnLevel;
        upgrades.Purchased += OnPurchased;
        GameEvents.ExpansionOpened += OnExpansion;

        Time.timeScale = timeScale;
        running = true;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Log($"start: task {lastTaskId}, cash {economy.Cash}, x{timeScale}");
        return $"guide bot running for {playMinutes} game minutes at x{timeScale} → {reportPath}";
    }

    public static string Stop(string _ = null)
    {
        Finish("stopped by command");
        return "stopped";
    }

    public static string Status(string _ = null) =>
        running ? $"running {Minutes():0.0} min, task {(tasks.Main != null ? tasks.Main.Definition.Id : "-")}, lv {progression.Level}, cash {economy.Cash}"
            : "idle";

    static float Minutes() => (Time.time - startTime) / 60f;

    static void Tick()
    {
        if (!running) return;
        if (!EditorApplication.isPlaying || player == null)
        {
            Finish("play mode ended");
            return;
        }

        if (Time.timeScale > 0.5f && !Mathf.Approximately(Time.timeScale, timeScale) && Time.timeScale >= 0.9f) Time.timeScale = timeScale;
        if (Time.time >= nextSample)
        {
            nextSample = Time.time + 30f;
            CashSamples.Add((Minutes(), economy.Cash, progression.Level));
            // The report is rewritten every 30 s of game time, so a long run can be watched from outside.
            WriteReport("running");
        }

        if (Minutes() >= playMinutes)
        {
            Finish("time limit");
            return;
        }

        if (tasks.Main == null && Time.time - lastProgressAt > 10f && lastTaskId != null && tasks.MainIndex >= 0)
        {
            Finish("main chain finished");
            return;
        }

        if (Time.time - lastProgressAt > StallMinutes * 60f)
        {
            string id = tasks.Main != null ? tasks.Main.Definition.Id : "-";
            Stalls.Add($"{Minutes():0.0} min: no task progress for {StallMinutes} min on {id} (guide target {guide.Target?.ToString("0.0") ?? "none"})");
            Log($"STALL on {id}");
            lastProgressAt = Time.time;
        }

        PlanShopping();
        Vector3? target = shopping ? shoppingTarget : guide.Target;
        if (!shopping)
        {
            // The marker hides as soon as the player is close; finish the step onto the pad instead of stopping at its rim.
            Vector3 toLast = lastGuideTarget.HasValue ? lastGuideTarget.Value - player.transform.position : Vector3.zero;
            toLast.y = 0f;
            if (guide.Target.HasValue) lastGuideTarget = guide.Target;
            else if (lastGuideTarget.HasValue && toLast.magnitude < 2f) target = lastGuideTarget;
            else lastGuideTarget = null;
        }

        Steer(target);
        BuyIfPanelOpen();
    }

    // ---------- movement ----------

    static void Steer(Vector3? target)
    {
        var pos = player.transform.position;
        if (!target.HasValue)
        {
            input.ExternalMove = Vector2.zero;
            return;
        }

        Vector3 goal = target.Value;
        Vector3 flat = goal - pos;
        flat.y = 0f;
        if (flat.magnitude <= ArriveDistance)
        {
            input.ExternalMove = Vector2.zero;
            stuckSince = -1f;
            return;
        }

        Vector3 next = NextCorner(pos, goal);
        Vector3 dir = next - pos;
        dir.y = 0f;
        if ((pos - lastPosition).sqrMagnitude > 0.04f || dir.magnitude < 0.5f)
        {
            lastPosition = pos;
            stuckSince = -1f;
        }
        else if (stuckSince < 0f) stuckSince = Time.time;
        else if (Time.time - stuckSince > StuckSeconds)
        {
            // Wiggle sideways out of whatever caught us.
            dir = Quaternion.Euler(0f, Random.value < 0.5f ? 90f : -90f, 0f) * dir;
            stuckSince = Time.time;
        }

        dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.zero;
        // Ease in on the last leg: in the background the game runs at ~10 fps, so a full-speed step can jump a whole pad.
        float approach = next == goal ? Mathf.Clamp(flat.magnitude / 3f, 0.2f, 1f) : 1f;
        input.ExternalMove = new Vector2(dir.x, dir.z) * approach;
    }

    static Vector3 NextCorner(Vector3 from, Vector3 to)
    {
        if (!NavMesh.SamplePosition(from, out var a, 2f, NavMesh.AllAreas)) return to;
        if (!NavMesh.SamplePosition(to, out var b, 3f, NavMesh.AllAreas)) return to;
        if (!NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) || path.corners.Length < 2) return to;
        foreach (var c in path.corners)
        {
            var d = c - from;
            d.y = 0f;
            if (d.magnitude > 0.6f) return c == path.corners[^1] ? to : c;
        }

        return to;
    }

    // ---------- buying ----------

    static void PlanShopping()
    {
        if (shopping || Time.time < nextShopCheck) return;
        nextShopCheck = Time.time + ShoppingEvery;
        if (panel == null || !panel.HasPurchasable) return;
        // Goal-driven: while the current task is a purchase we can't afford yet, save for it instead of shopping.
        var main = tasks.Main?.Definition;
        if (main != null && (main.Type == TaskType.PurchaseUpgrade || main.Type == TaskType.ReachUpgradeLevel || main.Type == TaskType.HireWorker) &&
            upgrades.TryGet(main.TargetId, out var goal) && !goal.IsMaxed && economy.Cash < goal.NextCost)
            return;
        var anchor = NearestUpgradeTile();
        if (!anchor.HasValue) return;
        shopping = true;
        shoppingTarget = anchor.Value;
        Log("shopping trip");
    }

    static Vector3? NearestUpgradeTile()
    {
        Vector3? best = null;
        float bestSqr = float.MaxValue;
        foreach (var id in new[] { "tile/upgrades", "tile/upgrades_plant" })
        {
            if (!GuideAnchor.TryGet(id, out var a)) continue;
            float sqr = (a.transform.position - player.transform.position).sqrMagnitude;
            if (sqr >= bestSqr) continue;
            bestSqr = sqr;
            best = a.transform.position;
        }

        return best;
    }

    static void BuyIfPanelOpen()
    {
        if (panel == null || !panel.IsOpen || Time.unscaledTime < nextBuy) return;
        nextBuy = Time.unscaledTime + 0.4f;
        var cards = Object.FindObjectsByType<UpgradeCard>()
            .Where(c => c.Upgrade != null && c.gameObject.activeInHierarchy && upgrades.CanPurchase(c.Upgrade))
            .OrderBy(c => c.UpgradeId == guide.HighlightedUpgrade ? -1 : 0)
            .ThenBy(c => c.Upgrade.NextCost)
            .ToList();
        if (cards.Count == 0)
        {
            if (shopping)
            {
                shopping = false;
                Log("shopping done");
            }

            return;
        }

        var card = cards[0];
        var button = card.GetComponentInChildren<UnityEngine.UI.Button>();
        if (button != null) button.onClick.Invoke();
    }

    // ---------- logging ----------

    static void OnTaskCompleted(ActiveTask task)
    {
        lastProgressAt = Time.time;
        if (task.Definition.Category == TaskCategory.Main) Log($"task done: {task.Definition.Id} \"{task.Definition.Title.Replace("{0}", task.Definition.Amount.ToString())}\"");
        else Log($"side task done: {task.Definition.Id}");
    }

    static void OnTaskProgressed(ActiveTask task)
    {
        if (task == tasks.Main) lastProgressAt = Time.time;
    }

    static void OnLevel(int level) => Log($"yard level {level}");

    static void OnPurchased(IUpgradeable u) => Log($"bought {u.UpgradeId} → {u.LevelLabel}");

    static void OnExpansion(string id) => Log($"GATE OPEN: {id}");

    static void Log(string line)
    {
        float m = running || Timeline.Length == 0 ? Minutes() : 0f;
        Timeline.AppendLine($"| {(int)m:00}:{(int)(m * 60f % 60f):00} | {line} | {economy?.Cash ?? 0} |");
    }

    static void Finish(string reason)
    {
        if (!running) return;
        running = false;
        EditorApplication.update -= Tick;
        if (input != null) input.ExternalMove = null;
        Time.timeScale = 1f;
        if (tasks != null)
        {
            tasks.TaskCompleted -= OnTaskCompleted;
            tasks.TaskProgressed -= OnTaskProgressed;
        }

        if (progression != null) progression.LevelChanged -= OnLevel;
        if (upgrades != null) upgrades.Purchased -= OnPurchased;
        GameEvents.ExpansionOpened -= OnExpansion;
        WriteReport(reason);
        Debug.Log($"[GuideBot] {reason}; report at {reportPath}");
    }

    static void WriteReport(string reason)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Guide bot pacing report");
        sb.AppendLine();
        sb.AppendLine($"Status: {reason} at {Minutes():0.0} game minutes (x{timeScale}). Final: level {progression?.Level}, cash {economy?.Cash}, task " +
                      $"{(tasks?.Main != null ? tasks.Main.Definition.Id : "none")}.");
        sb.AppendLine();
        sb.AppendLine(Stalls.Count == 0 ? "No stalls: every task progressed within the limit." : "## Stalls\n" + string.Join("\n", Stalls.Select(s => "- " + s)));
        sb.AppendLine();
        sb.AppendLine("## Timeline");
        sb.AppendLine();
        sb.AppendLine("| Time | Event | Cash |");
        sb.AppendLine("|---|---|---|");
        sb.Append(Timeline);
        sb.AppendLine();
        sb.AppendLine("## Cash and level every 30 s");
        sb.AppendLine();
        sb.AppendLine(string.Join(", ", CashSamples.Select(s => $"{s.t:0.0}m ${s.cash} L{s.level}")));
        File.WriteAllText(reportPath, sb.ToString());
    }
}

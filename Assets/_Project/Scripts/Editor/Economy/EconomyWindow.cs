using System;
using System.Collections.Generic;
using System.Linq;
using ScrapYardKing.Boosts;
using ScrapYardKing.Core;
using ScrapYardKing.Economy;
using ScrapYardKing.Factory;
using ScrapYardKing.Progression;
using ScrapYardKing.Shop;
using UnityEditor;
using UnityEngine;

namespace ScrapYardKing.EditorTools
{
    /// <summary>
    /// Developer-only balancing window (brief sections 41–42): the live economy of the running game (cash per minute,
    /// diamond flow, videos, purchases, guardrails), today's prices with time-to-afford, and the four simulated player
    /// profiles over 1 hour to 30 days. Editor code: it never ships and players never see it.
    /// Live numbers come from <see cref="EconomyLedger"/>; prices from the data assets; profiles from <see cref="EconomySimulator"/>.
    /// </summary>
    public sealed class EconomyWindow : EditorWindow
    {
        static readonly string[] Tabs = { "Live", "Prices", "Profiles" };

        int tab;
        Vector2 scroll;
        string report;
        double nextRepaint;

        [MenuItem("Scrap Yard King/Economy Window", priority = 20)]
        static void Open() => GetWindow<EconomyWindow>("Economy").Show();

        void Update()
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup < nextRepaint) return;
            nextRepaint = EditorApplication.timeSinceStartup + 0.5;
            Repaint();
        }

        void OnGUI()
        {
            tab = GUILayout.Toolbar(tab, Tabs);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            switch (tab)
            {
                case 0: Live(); break;
                case 1: Prices(); break;
                default: Profiles(); break;
            }

            EditorGUILayout.EndScrollView();
        }

        // ------------------------------------------------------------------ live

        void Live()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play mode for the live economy. Lifetime totals come from the save (EconomyLedger).", MessageType.Info);
                return;
            }

            if (!Services.TryGet(out EconomyLedger ledger) || !Services.TryGet(out EconomyManager economy))
            {
                EditorGUILayout.HelpBox("No EconomyLedger in the scene: run U6_Build.Scene.", MessageType.Warning);
                return;
            }

            Services.TryGet(out ProgressionManager progression);
            int level = progression != null ? progression.Level : 1;
            float perMin = ledger.CashPerMinute;
            float playedMin = Mathf.Max(1f, ledger.PlaySeconds / 60f);
            int days = ledger.DaysPlayed;

            Header("SOFT CURRENCY");
            Row("Cash", "$" + CurrencyFormat.Short(economy.Cash));
            Row("Income / min (last minute)", "$" + CurrencyFormat.Short((long)perMin));
            if (Services.TryGet(out IdleIncomeManager idle)) Row("Income / min (measured, offline basis)", "$" + CurrencyFormat.Short((long)(idle.RatePerSecond * 60)));
            Row("This session", $"${CurrencyFormat.Short(ledger.SessionCash)} in {Dur(ledger.SessionSeconds)}");
            Row("Lifetime earned / spent", $"${CurrencyFormat.Short(ledger.CashEarned)} / ${CurrencyFormat.Short(ledger.CashSpent)}");
            Row("Orders", $"{ledger.Contracts} paid ${CurrencyFormat.Short(ledger.ContractCash)} ({ledger.ContractsDoubled} doubled)");
            Row("Offline", $"{ledger.OfflineClaims} returns, ${CurrencyFormat.Short(ledger.OfflineCash)} ({ledger.OfflineDoubled} doubled)");
            Row("Upgrades bought", ledger.Upgrades.ToString());

            Header("PREMIUM");
            Row("Diamonds", economy.Premium.ToString());
            Row("Earned / spent", $"{ledger.DiamondsEarned} / {ledger.DiamondsSpent}");
            Row("Per day (earned / spent)", $"{ledger.DiamondsEarned / (float)days:0.#} / {ledger.DiamondsSpent / (float)days:0.#}  over {days} day(s)");
            var today = ledger.Days.Count > 0 ? ledger.Days[^1] : null;
            if (today != null) Row("Average balance today", today.AverageBalance.ToString("0"));
            Row("Average spend", ledger.DiamondsOut.Sum(e => e.count) is int n && n > 0 ? $"{ledger.DiamondsSpent / (float)n:0.#} per spend ({n})" : "-");
            Entries("  in", ledger.DiamondsIn);
            Entries("  out", ledger.DiamondsOut);

            Header("ADS");
            Row("Offered / watched / failed", $"{ledger.RvOffered} / {ledger.RvWatched} / {ledger.RvFailed}");
            Row("Views per session", $"{ledger.RvWatched / (float)Mathf.Max(1, ledger.Sessions):0.#}  (this session {ledger.SessionRv})");
            Row("Video cash (total / per session)", $"${CurrencyFormat.Short(ledger.RvCash)} / ${CurrencyFormat.Short(ledger.RvCash / Mathf.Max(1, ledger.Sessions))}");
            Entries("  by offer", ledger.RvOffers);
            if (Services.TryGet(out OfferDirector offers))
            {
                Row("Cooldown usage", "watched today / cap, time to ready");
                foreach (var o in offers.Offers.Append(offers.DoubleTruck).Append(offers.InstantTruck).Append(offers.OfflineDouble).Where(o => o != null))
                    Row("  " + o.Title, $"{offers.TakenToday(o)} / {(o.DailyCap > 0 ? o.DailyCap.ToString() : "∞")}   {(offers.CooldownLeft(o) > 0 ? Dur(offers.CooldownLeft(o)) : "ready")}");
            }

            Header("IAP");
            Row("Initiated / completed / failed", $"{ledger.IapInitiated} / {ledger.IapCompleted} / {ledger.IapFailed}");
            Row("Purchase conversion (per shop visit)", ledger.ShopOpens > 0 ? (ledger.IapCompleted / (float)ledger.ShopOpens).ToString("P0") : "-");
            Row("Average purchase", ledger.IapCompleted > 0 ? $"${ledger.Usd / ledger.IapCompleted:0.00}" : "-");
            Row("Spent (reference USD)", $"${ledger.Usd:0.00}");
            Entries("  products", ledger.Products);

            Header("GUARDRAILS");
            Row("Cash / min", "$" + CurrencyFormat.Short((long)perMin));
            Row("Diamonds / min played", (ledger.DiamondsEarned / playedMin).ToString("0.00"));
            var next = Cheapest(level);
            if (next != null)
            {
                long missing = Math.Max(0, next.NextCost - economy.Cash);
                Row("Next upgrade", $"{next.DisplayName} ${CurrencyFormat.Short(next.NextCost)}");
                Row("Time to afford", missing == 0 ? "now" : perMin > 1f ? Dur(missing / perMin * 60f) : "∞ (no income)");
            }

            Row("Contract ROI (pay vs market)", ContractRoi());
            Row("Video reward ratio (video cash / all cash)", ledger.CashEarned > 0 ? (ledger.RvCash / (double)ledger.CashEarned).ToString("P1") : "-");
            Row("IAP value (diamonds per $)", BundleRange());
            Row("Offline income / day", "$" + CurrencyFormat.Short(ledger.OfflineCash / days));
            var premium = Load<PremiumEconomyConfig>("Economy/PremiumEconomy.asset");
            if (premium != null && perMin > 1f)
                Row("Diamond worth here (cash / skip)", $"${CurrencyFormat.Short(premium.CashPerDiamond(level))} = {premium.CashPerDiamond(level) / perMin:0.00} min of income · 1 min skip = {premium.SkipCost(60)}");
        }

        static IUpgradeable Cheapest(int level)
        {
            if (!Services.TryGet(out UpgradeManager upgrades)) return null;
            return upgrades.All.Where(u => !u.IsMaxed && upgrades.IsUnlocked(u)).OrderBy(u => u.NextCost).FirstOrDefault();
        }

        // ------------------------------------------------------------------ prices

        void Prices()
        {
            if (EditorApplication.isPlaying && Services.TryGet(out UpgradeManager upgrades))
            {
                Services.TryGet(out EconomyLedger ledger);
                Services.TryGet(out EconomyManager economy);
                float perMin = ledger != null ? Mathf.Max(1f, ledger.CashPerMinute) : 1f;
                Header("UPGRADES AND EXPANSIONS (next level, time to afford at the current income)");
                foreach (var u in upgrades.All.OrderBy(u => u.IsMaxed).ThenBy(u => u.NextCost))
                {
                    string lockText = upgrades.IsUnlocked(u) ? "" : $"  (Lv {upgrades.UnlockLevel(u.UpgradeId)})";
                    long missing = economy != null ? Math.Max(0, u.NextCost - economy.Cash) : u.NextCost;
                    Row($"{u.DisplayName} [{u.UpgradeId}] Lv {u.Level}/{u.MaxLevel}",
                        u.IsMaxed ? "MAX" : $"${CurrencyFormat.Short(u.NextCost)}  {(missing == 0 ? "affordable" : Dur(missing / perMin * 60f))}{lockText}");
                }
            }
            else EditorGUILayout.HelpBox("Upgrade and expansion prices with time-to-afford show in Play mode.", MessageType.Info);

            Header("TRUCK CONTRACTS");
            foreach (var c in AssetDatabase.FindAssets("t:TruckContractDefinition").Select(g => AssetDatabase.LoadAssetAtPath<TruckContractDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(c => c != null))
                Row(c.DisplayName, $"x{c.RewardMultiplier:0.00} market value, from Lv {c.MinLevel}");

            var premium = Load<PremiumEconomyConfig>("Economy/PremiumEconomy.asset");
            var shop = Load<ShopCatalog>("Shop/ShopCatalog.asset");
            if (premium != null)
            {
                Header("DIAMOND PRICES");
                foreach (int min in new[] { 1, 5, 10, 30, 60, 240 }) Row($"Skip {min} min", premium.SkipCost(min * 60f).ToString());
                foreach (int lv in new[] { 1, 5, 10, 15, 20, 30 }) Row($"Cash per diamond, Lv {lv}", "$" + CurrencyFormat.Short(premium.CashPerDiamond(lv)));
            }

            if (shop != null && premium != null)
            {
                Header("SHOP");
                foreach (var p in new[] { shop.Hero }.Concat(shop.Bundles).Concat(shop.Specials).Where(p => p != null))
                    Row($"{p.Title} ({p.Tier})", $"${p.ReferencePriceUsd:0.00}  {p.TotalDiamonds} ◆  {p.DiamondsPerUsd:0} per $  {(p.Enabled ? "" : "(off)")}");
                foreach (var b in shop.Boosts.Where(b => b?.boost != null))
                    Row($"{b.boost.DisplayName} {b.minutes:0} min", $"{ShopCatalog.BoostCost(premium, b)} ◆");
                foreach (var c in shop.Crates.Where(c => c != null))
                    Row(c.title, $"{c.diamonds} ◆ = ${CurrencyFormat.Short(ShopCatalog.CrateCash(premium, c, 10))} at Lv 10");
                Row("Free video", $"{shop.FreeDiamonds} ◆ every {shop.FreeCooldownSeconds / 3600f:0.#} h, {shop.FreeDailyCap} a day");
            }

            Header("VIDEO OFFERS");
            foreach (var o in AssetDatabase.FindAssets("t:AdOfferDefinition").Select(g => AssetDatabase.LoadAssetAtPath<AdOfferDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(o => o != null))
                Row(o.Title, $"{o.Kind}, cooldown {Dur(o.Cooldown)}, {(o.DailyCap > 0 ? o.DailyCap + " a day" : "no cap")}{(o.CashPerLevel > 0 && o.Kind == AdOfferKind.FreeCash ? $", ${o.CashPerLevel} × level" : "")}");
        }

        // ------------------------------------------------------------------ profiles

        void Profiles()
        {
            EditorGUILayout.HelpBox("Free / Light / Mid / High players over 1 hour of play to 30 days, on the current data (EconomySimulator). " +
                                    "Progress is income-equivalent time; 'x' is progress per minute played. The same table is checked by EconomySimulationTests.",
                MessageType.None);
            if (GUILayout.Button("Run simulation"))
            {
                var sim = EconomySimulator.FromProject(SceneModel());
                report = sim.Report(EconomySimulator.StandardProfiles());
            }

            if (GUILayout.Button("Copy as Markdown") && !string.IsNullOrEmpty(report)) EditorGUIUtility.systemCopyBuffer = report;
            if (string.IsNullOrEmpty(report)) return;
            var style = new GUIStyle(EditorStyles.label) { font = EditorStyles.miniFont, wordWrap = false, richText = false };
            foreach (var line in report.Split('\n').Where(l => !l.StartsWith("|---")))
                GUILayout.Label(line.Replace("|", "  "), style);
        }

        /// <summary>The model with the values that live on scene components, when the game scene is open.</summary>
        static EconomySimulator.Model SceneModel()
        {
            var m = new EconomySimulator.Model();
            var director = FindAnyObjectByType<OfferDirector>(FindObjectsInactive.Include);
            if (director != null)
            {
                var so = new SerializedObject(director);
                m.quietSeconds = so.FindProperty("quietSeconds").floatValue;
                m.firstOfferAfter = so.FindProperty("firstOfferAfter").floatValue;
            }

            var idle = FindAnyObjectByType<IdleIncomeManager>(FindObjectsInactive.Include);
            if (idle != null)
            {
                m.offlineEfficiency = idle.Efficiency;
                m.offlineMaxHours = idle.MaxHours;
            }

            return m;
        }

        // ------------------------------------------------------------------ helpers

        static string ContractRoi()
        {
            var all = AssetDatabase.FindAssets("t:TruckContractDefinition").Select(g => AssetDatabase.LoadAssetAtPath<TruckContractDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(c => c != null).ToArray();
            return all.Length == 0 ? "-" : $"x{all.Min(c => c.RewardMultiplier):0.00} – x{all.Max(c => c.RewardMultiplier):0.00}";
        }

        static string BundleRange()
        {
            var shop = Load<ShopCatalog>("Shop/ShopCatalog.asset");
            if (shop == null || shop.Bundles.Length == 0) return "-";
            var b = shop.Bundles.Where(p => p != null).ToArray();
            return $"{b.Min(p => p.DiamondsPerUsd):0} – {b.Max(p => p.DiamondsPerUsd):0} (starter {(shop.Hero != null ? shop.Hero.DiamondsPerUsd : 0):0})";
        }

        static T Load<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>("Assets/_Project/Data/" + path);

        static void Header(string text)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(text, EditorStyles.boldLabel);
        }

        static void Row(string label, string value) => EditorGUILayout.LabelField(label, value);

        static void Entries(string label, IReadOnlyList<EconomyLedger.Entry> list)
        {
            if (list.Count == 0) return;
            Row(label, string.Join(", ", list.OrderByDescending(e => e.amount).ThenByDescending(e => e.count).Select(e => e.amount > 0 ? $"{e.key} {e.amount}" : $"{e.key} ×{e.count}")));
        }

        static string Dur(double seconds)
        {
            if (seconds < 60) return $"{seconds:0}s";
            if (seconds < 3600) return $"{(int)(seconds / 60)}m {(int)(seconds % 60):00}s";
            return $"{(int)(seconds / 3600)}h {(int)(seconds % 3600 / 60):00}m";
        }
    }
}

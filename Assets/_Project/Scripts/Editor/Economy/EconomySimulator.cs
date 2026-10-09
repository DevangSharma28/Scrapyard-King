using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ScrapYardKing.Boosts;
using ScrapYardKing.Economy;
using ScrapYardKing.Progression;
using ScrapYardKing.Shop;
using UnityEditor;
using UnityEngine;

namespace ScrapYardKing.EditorTools
{
    /// <summary>
    /// Plays the premium economy forward for a simulated player (free, light, mid or high spender) and measures what
    /// diamonds, videos and purchases do to progress over 1 hour to 30 days. It reads every price and reward from the
    /// real data assets (premium config, shop, offers, missions, daily calendar, level curve); only the shape of the
    /// game itself (income per minute along the arc, when each area opens, how often orders complete) comes from the
    /// model in <c>Docs/ECONOMY.md</c>, kept in <see cref="Model"/>.
    /// Expected-value simulation without randomness: the same inputs give the same table.
    /// "Minutes gained" is income-equivalent time: a 2X CASH boost of 10 minutes gains 10 minutes of income. It is an
    /// upper bound (a doubled machine only helps while it is the bottleneck), so the guardrails err on the safe side.
    /// </summary>
    public sealed class EconomySimulator
    {
        /// <summary>How a simulated player plays and pays.</summary>
        public sealed class Profile
        {
            public string name;
            public int sessionsPerDay = 2;
            public float sessionMinutes = 15f;
            [Tooltip("Share of offered videos the player watches (HUD offers, 2X cards, free diamonds).")]
            public float videoShare = 0.3f;
            [Tooltip("Share of daily missions completed.")]
            public float dailyShare = 0.67f;
            [Tooltip("Diamonds kept back; the rest is spent on shop boosts.")]
            public int reserve;
            [Tooltip("Diamonds left after a session of boosts buy cash crates (big spenders).")]
            public bool buysCash;
            /// <summary>(day, product id): purchases, made at the start of that day's first session (day 0 = install).</summary>
            public (int day, string productId)[] purchases = Array.Empty<(int, string)>();
            /// <summary>Repeat purchase every N days from <see cref="repeatFrom"/> (0 = none).</summary>
            public int repeatEvery;
            public int repeatFrom = 7;
            public string repeatProduct;
        }

        /// <summary>The game's shape, from Docs/ECONOMY.md section 4 (the measured rows and the model's estimates).</summary>
        public sealed class Model
        {
            /// <summary>(progress minute, income per minute). Linear between points.</summary>
            public (float minute, float income)[] incomeCurve =
            {
                (0, 300), (2, 400), (10, 600), (18, 1200), (25, 2200), (35, 2800), (45, 3000), (55, 4500), (80, 5500), (90, 6000)
            };

            /// <summary>After the arc, upgrades keep lifting income by this much per hour of progress, up to <see cref="incomeCap"/>.</summary>
            public float incomeGrowthPerHour = 600f;
            public float incomeCap = 12000f;

            /// <summary>Progress minute each expansion opens at (milestone gifts, the areas achievement).</summary>
            public (string id, float minute)[] expansions =
            {
                ("back_lot", 5), ("recycling_plant", 9), ("furnace_hall", 15), ("furnace_copper", 20), ("furnace_aluminum", 24),
                ("furnace_steel", 30), ("build_press", 40), ("truck_dock", 48), ("heavy_yard", 58), ("dump_yard", 95)
            };

            /// <summary>Progress minutes of the first-session arc (Heavy Yard, giants, specialists): the whole core game.</summary>
            public float coreGameMinutes = 90f;
            /// <summary>Main-chain tasks that pay diamonds complete around here.</summary>
            public float taskDiamondsMinute = 80f;

            public float orderEveryMinutes = 2.5f;
            /// <summary>An order pays about this many minutes of income.</summary>
            public float orderPayMinutes = 1f;

            /// <summary>Workers hired by progress minute (for the workers achievement).</summary>
            public float[] hireMinutes = { 2.5f, 4, 11, 13, 18, 40, 62, 70, 74, 78, 82, 86, 120, 160, 200, 260, 320, 400, 500, 600 };

            public float scrapPerPlayedMinute = 6f;
            public float itemsSoldPerMinute = 15f;
            public float upgradesPerMinute = 1.2f;
            public float upgradesPerMinuteLate = 0.3f;

            /// <summary>How much of a boost's extra speed turns into progress, by kind (2X CASH doubles income outright).</summary>
            public float Relevance(BoostKind kind) => kind switch
            {
                BoostKind.Cash => 1f,
                BoostKind.Production => 0.6f,
                BoostKind.MoveSpeed => 0.25f,
                _ => 0.3f
            };

            // values that live on scene components (OfferDirector, IdleIncomeManager)
            public float firstOfferAfter = 90f;
            public float quietSeconds = 150f;
            public float offlineEfficiency = 0.3f;
            public float offlineMaxHours = 2f;
            public float offlineMinAwayMinutes = 2f;
        }

        /// <summary>Totals at one checkpoint.</summary>
        public sealed class Snapshot
        {
            public string label;
            public float days;
            public float playedMinutes, progressMinutes;
            public int level;
            public double diamondsEarned, diamondsSpent, balance, maxBalance;
            public Dictionary<string, double> sources = new();
            public double fromVideo, fromIap;
            public int boostsBought, cratesBought;
            public double videosWatched, videosOffered;
            public double hudOffers, hudWatched, orderOffers, returnOffers;
            public double rvCash, rvMinutes, diamondMinutes, rewardMinutes, iapMinutes, awayMinutes;
            public float usd;
            public float coreDoneDay = -1f;

            public double Speed => playedMinutes > 0 ? progressMinutes / playedMinutes : 1;
            /// <summary>Shares of all progress: what diamonds, videos, purchases and time away contributed.</summary>
            public double DiamondShare => Share(diamondMinutes);
            public double VideoShare => Share(rvMinutes);
            public double IapShare => Share(iapMinutes);
            public double AwayShare => Share(awayMinutes);
            public double PlayedShare => Share(playedMinutes);

            double Share(double minutes) => progressMinutes > 0 ? minutes / progressMinutes : 0;

            public Snapshot Copy(string at, float d)
            {
                var s = (Snapshot)MemberwiseClone();
                s.label = at;
                s.days = d;
                s.sources = new Dictionary<string, double>(sources);
                return s;
            }
        }

        public readonly PremiumEconomyConfig premium;
        public readonly ShopCatalog shop;
        public readonly MissionConfig missions;
        public readonly DailyRewardConfig daily;
        public readonly ProgressionConfig progression;
        public readonly AdOfferDefinition[] hudOffers;
        public readonly AdOfferDefinition truckDouble, offlineDouble;
        public readonly TaskChain tasks;
        public readonly Model model;

        public EconomySimulator(PremiumEconomyConfig premium, ShopCatalog shop, MissionConfig missions, DailyRewardConfig daily,
            ProgressionConfig progression, AdOfferDefinition[] hudOffers, AdOfferDefinition truckDouble, AdOfferDefinition offlineDouble,
            TaskChain tasks, Model model = null)
        {
            this.premium = premium;
            this.shop = shop;
            this.missions = missions;
            this.daily = daily;
            this.progression = progression;
            this.hudOffers = hudOffers ?? Array.Empty<AdOfferDefinition>();
            this.truckDouble = truckDouble;
            this.offlineDouble = offlineDouble;
            this.tasks = tasks;
            this.model = model ?? new Model();
        }

        const string Data = "Assets/_Project/Data/";

        /// <summary>A simulator over the project's data assets.</summary>
        public static EconomySimulator FromProject(Model model = null)
        {
            T Load<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(Data + path);
            var offers = new[] { "Offer_Cash", "Offer_Production", "Offer_Speed", "Offer_ScrapRush", "Offer_TruckRush", "Offer_FreeCash" }
                .Select(n => Load<AdOfferDefinition>($"Boosts/{n}.asset")).Where(o => o != null).ToArray();
            return new EconomySimulator(Load<PremiumEconomyConfig>("Economy/PremiumEconomy.asset"), Load<ShopCatalog>("Shop/ShopCatalog.asset"),
                Load<MissionConfig>("Progression/MissionConfig.asset"), Load<DailyRewardConfig>("Progression/DailyRewardConfig.asset"),
                Load<ProgressionConfig>("Progression/ProgressionConfig.asset"), offers,
                Load<AdOfferDefinition>("Boosts/Offer_TruckDouble.asset"), Load<AdOfferDefinition>("Boosts/Offer_OfflineDouble.asset"),
                Load<TaskChain>("Progression/TaskChain_Area1.asset"), model);
        }

        /// <summary>The four players of the brief (section 68).</summary>
        public static Profile[] StandardProfiles() => new[]
        {
            new Profile { name = "Free", sessionsPerDay = 2, sessionMinutes = 15f, videoShare = 0.3f, dailyShare = 0.67f },
            new Profile
            {
                name = "Light", sessionsPerDay = 2, sessionMinutes = 20f, videoShare = 0.5f, dailyShare = 1f,
                purchases = new[] { (1, "syk_starter_pack") }
            },
            new Profile
            {
                name = "Mid", sessionsPerDay = 3, sessionMinutes = 20f, videoShare = 0.5f, dailyShare = 1f,
                purchases = new[] { (0, "syk_starter_pack"), (2, "syk_gems_1200") }, repeatEvery = 7, repeatProduct = "syk_gems_1200",
                buysCash = true, reserve = 100
            },
            new Profile
            {
                name = "High", sessionsPerDay = 4, sessionMinutes = 25f, videoShare = 0.2f, dailyShare = 1f,
                purchases = new[] { (0, "syk_starter_pack"), (0, "syk_gems_6500"), (0, "syk_factory_pack") }, repeatEvery = 7,
                repeatProduct = "syk_gems_15000", buysCash = true, reserve = 200
            }
        };

        /// <summary>Checkpoints of the brief (section 69), in days.</summary>
        public static readonly (string label, float days)[] Checkpoints = { ("1 h", 0f), ("1 d", 1f), ("3 d", 3f), ("7 d", 7f), ("30 d", 30f) };

        // ------------------------------------------------------------------ the run

        sealed class Run
        {
            public Snapshot s = new();
            public double cashSold;
            public double wall;                              // seconds since install
            public readonly Dictionary<string, double> readyAt = new();
            public readonly Dictionary<string, double> takenToday = new();
            public int day = -1;
            public double videoAcc;
            public readonly HashSet<string> opened = new();
            public double scrapCount;
            public double salesMinutes;                      // played + boosted sales: XP, levels and areas follow these
            public double cashAll;                           // every dollar, for the cash achievement
            public readonly int[] tiers = new int[16];
            public double ordersDone, nextOrderAt;
            public double freeVideoReadyAt;
            public double boostUntil;                        // diamond boosts already running (minutes of progress)
            public readonly Dictionary<BoostKind, double> activeUntil = new();
            public bool taskDiamondsPaid;
        }

        /// <summary>Runs <paramref name="p"/> for 30 days; one snapshot per checkpoint.</summary>
        public List<Snapshot> Simulate(Profile p)
        {
            var r = new Run();
            var taken = new Snapshot[Checkpoints.Length];
            double sessionGap = 86400.0 / Mathf.Max(1, p.sessionsPerDay);
            double lastEnd = -1;

            for (int d = 0; d < 30; d++)
            {
                for (int k = 0; k < p.sessionsPerDay; k++)
                {
                    double start = d * 86400.0 + k * sessionGap;
                    r.wall = start;
                    NewDay(r, d, p);
                    if (k == 0) Purchases(r, d, p);
                    if (lastEnd >= 0) Away(r, p, (start - lastEnd) / 60.0);
                    FreeDiamondVideo(r, p);
                    Session(r, p, () =>
                    {
                        // "1 h" is an hour of play, wherever it falls
                        for (int i = 0; i < Checkpoints.Length; i++)
                            if (Checkpoints[i].days == 0f && taken[i] == null && r.s.playedMinutes >= 59.99f)
                                taken[i] = r.s.Copy(Checkpoints[i].label, (float)(r.wall / 86400.0));
                    });
                    lastEnd = r.wall;
                }

                for (int i = 0; i < Checkpoints.Length; i++)
                    if (Checkpoints[i].days > 0f && taken[i] == null && d + 1 >= Checkpoints[i].days)
                        taken[i] = r.s.Copy(Checkpoints[i].label, Checkpoints[i].days);
            }

            return taken.Where(x => x != null).ToList();
        }

        void NewDay(Run r, int d, Profile p)
        {
            if (r.day == d) return;
            r.day = d;
            r.takenToday.Clear();

            // calendar (a streak is assumed) and the day's missions
            if (daily != null && daily.Days.Length > 0) Reward(r, daily.Days[d % daily.Days.Length].reward, "daily_reward", 1.0);
            if (missions != null)
            {
                var pool = missions.DailyPool.Where(m => m != null && r.s.level >= m.minLevel).ToList();
                if (pool.Count > 0)
                    foreach (var m in pool)
                        Reward(r, m.reward, "daily", p.dailyShare * missions.DailyCount / pool.Count);
            }
        }

        void Purchases(Run r, int d, Profile p)
        {
            foreach (var (day, id) in p.purchases)
                if (day == d) Buy(r, id);
            if (p.repeatEvery > 0 && !string.IsNullOrEmpty(p.repeatProduct) && d >= p.repeatFrom && (d - p.repeatFrom) % p.repeatEvery == 0)
                Buy(r, p.repeatProduct);
        }

        void Buy(Run r, string productId)
        {
            var product = shop != null ? shop.Find(productId) : null;
            if (product == null || !product.Enabled) return;
            r.s.usd += product.ReferencePriceUsd;
            Earn(r, product.TotalDiamonds, "iap");
            r.s.fromIap += product.TotalDiamonds;
            double income = Income((float)r.salesMinutes);
            if (product.CashAsDiamonds > 0)
                Gain(r, ref r.s.iapMinutes, product.CashAsDiamonds * premium.CashPerDiamond(r.s.level) / income);
            if (product.Boost != null) Gain(r, ref r.s.iapMinutes, BoostMinutes(product.Boost, product.BoostMinutes), true);
            if (product.SecondBoost != null) Gain(r, ref r.s.iapMinutes, BoostMinutes(product.SecondBoost, product.SecondBoostMinutes), true);
        }

        void Away(Run r, Profile p, double minutes)
        {
            if (minutes < model.offlineMinAwayMinutes) return;
            double gained = model.offlineEfficiency * Math.Min(minutes, model.offlineMaxHours * 60.0);
            Gain(r, ref r.s.awayMinutes, gained);
            if (offlineDouble != null && Ready(r, offlineDouble)) r.s.returnOffers++;
            if (offlineDouble != null && Video(r, p, offlineDouble))
            {
                Gain(r, ref r.s.rvMinutes, gained);
                r.s.rvCash += gained * Income((float)r.salesMinutes);
            }
        }

        void FreeDiamondVideo(Run r, Profile p)
        {
            if (shop == null || r.wall < r.freeVideoReadyAt || Taken(r, "shop_free") >= shop.FreeDailyCap) return;
            r.s.videosOffered++;
            r.videoAcc += p.videoShare;
            if (r.videoAcc < 1.0) return;
            r.videoAcc -= 1.0;
            r.s.videosWatched++;
            r.takenToday["shop_free"] = Taken(r, "shop_free") + 1;
            r.freeVideoReadyAt = r.wall + shop.FreeCooldownSeconds;
            Earn(r, shop.FreeDiamonds, "rv");
            r.s.fromVideo += shop.FreeDiamonds;
        }

        void Session(Run r, Profile p, Action tick)
        {
            Spend(r, p);
            double end = r.wall + p.sessionMinutes * 60.0;
            double nextOffer = r.wall + model.firstOfferAfter;
            const double step = 5.0;
            while (r.wall < end)
            {
                r.wall += step;
                double minutes = step / 60.0;
                r.s.playedMinutes += (float)minutes;
                Advance(r, minutes, true, true);

                if (r.wall >= nextOffer)
                {
                    var o = PickOffer(r);
                    if (o != null)
                    {
                        nextOffer = r.wall + o.ShowSeconds + model.quietSeconds;
                        r.s.hudOffers++;
                        bool watched = Video(r, p, o);
                        if (!watched) r.readyAt[o.Id] = r.wall + o.ShowSeconds + o.Cooldown;
                        else
                        {
                            r.s.hudWatched++;
                            HudReward(r, o);
                        }
                    }
                    else nextOffer = r.wall + 10.0;
                }

                // truck orders and their 2X card
                if (r.salesMinutes >= Expansion("truck_dock") && r.wall >= r.nextOrderAt)
                {
                    r.nextOrderAt = r.wall + model.orderEveryMinutes * 60.0;
                    r.ordersDone++;
                    if (truckDouble != null && Ready(r, truckDouble)) r.s.orderOffers++;
                    if (truckDouble != null && Video(r, p, truckDouble))
                    {
                        Gain(r, ref r.s.rvMinutes, model.orderPayMinutes);
                        r.s.rvCash += model.orderPayMinutes * Income((float)r.salesMinutes);
                    }
                }

                tick();
            }
        }

        AdOfferDefinition PickOffer(Run r)
        {
            AdOfferDefinition best = null;
            foreach (var o in hudOffers)
            {
                if (o.Weight <= 0f || r.s.level < o.MinLevel || !Ready(r, o)) continue;
                if (o.Kind == AdOfferKind.Boost && (o.Boost == null || ActiveUntil(r, o.Boost.Kind) > r.wall)) continue;
                if (best == null || o.Priority > best.Priority || (o.Priority == best.Priority && o.Weight > best.Weight)) best = o;
            }

            return best;
        }

        bool Ready(Run r, AdOfferDefinition o) =>
            (!r.readyAt.TryGetValue(o.Id, out double t) || r.wall >= t) && (o.DailyCap <= 0 || Taken(r, o.Id) < o.DailyCap);

        double Taken(Run r, string id) => r.takenToday.TryGetValue(id, out double n) ? n : 0;
        double ActiveUntil(Run r, BoostKind k) => r.activeUntil.TryGetValue(k, out double t) ? t : 0;

        /// <summary>The player is offered <paramref name="o"/>; true when they watch (an accumulator, no randomness).</summary>
        bool Video(Run r, Profile p, AdOfferDefinition o)
        {
            if (!Ready(r, o)) return false;
            r.s.videosOffered++;
            r.videoAcc += p.videoShare;
            if (r.videoAcc < 1.0) return false;
            r.videoAcc -= 1.0;
            r.s.videosWatched++;
            r.takenToday[o.Id] = Taken(r, o.Id) + 1;
            r.readyAt[o.Id] = r.wall + o.Cooldown;
            return true;
        }

        void HudReward(Run r, AdOfferDefinition o)
        {
            if (o.Kind == AdOfferKind.Boost && o.Boost != null)
            {
                r.activeUntil[o.Boost.Kind] = r.wall + o.Boost.Duration;
                Gain(r, ref r.s.rvMinutes, BoostMinutes(o.Boost, o.Boost.Duration / 60f), true);
            }
            else if (o.Kind == AdOfferKind.FreeCash)
            {
                double cash = o.CashPerLevel * Math.Max(1, r.s.level);
                r.s.rvCash += cash;
                Gain(r, ref r.s.rvMinutes, cash / Income((float)r.salesMinutes));
            }
        }

        /// <summary>Spends diamonds above the reserve on shop boosts, at most one session's worth of boost time.</summary>
        void Spend(Run r, Profile p)
        {
            if (shop == null || premium == null) return;
            var items = shop.Boosts.Where(b => b?.boost != null)
                .OrderByDescending(b => BoostMinutes(b.boost, b.minutes) / Math.Max(1, ShopCatalog.BoostCost(premium, b))).ToList();
            double covered = Math.Max(0, r.boostUntil - r.s.playedMinutes);
            foreach (var item in items)
            {
                int cost = ShopCatalog.BoostCost(premium, item);
                while (cost > 0 && r.s.balance - p.reserve >= cost && covered + item.minutes <= p.sessionMinutes)
                {
                    r.s.balance -= cost;
                    r.s.diamondsSpent += cost;
                    r.s.boostsBought++;
                    covered += item.minutes;
                    Gain(r, ref r.s.diamondMinutes, BoostMinutes(item.boost, item.minutes), true);
                }
            }

            r.boostUntil = r.s.playedMinutes + covered;
            if (!p.buysCash) return;

            // the rest buys the biggest crate that fits, again and again
            foreach (var crate in shop.Crates.Where(c => c != null).OrderByDescending(c => c.diamonds))
                while (r.s.balance - p.reserve >= crate.diamonds)
                {
                    r.s.balance -= crate.diamonds;
                    r.s.diamondsSpent += crate.diamonds;
                    r.s.cratesBought++;
                    Gain(r, ref r.s.diamondMinutes, ShopCatalog.CrateCash(premium, crate, r.s.level) / Income((float)r.salesMinutes));
                }
        }

        double BoostMinutes(BoostDefinition b, float minutes) => minutes * (b.Multiplier - 1f) * model.Relevance(b.Kind);

        /// <summary>
        /// Progress moves on by <paramref name="minutes"/> of income. Only sales (<paramref name="sales"/>: played time
        /// and boosts) give XP, so only they move levels and open areas (the level gates); cash from rewards, crates,
        /// offline and videos buys upgrades but no levels.
        /// </summary>
        void Advance(Run r, double minutes, bool sales, bool played = false)
        {
            r.s.progressMinutes += (float)minutes;
            r.cashAll += Income((float)r.salesMinutes) * minutes;
            if (sales)
            {
                double from = r.salesMinutes;
                r.salesMinutes += minutes;
                r.cashSold += (Income((float)from) + Income((float)r.salesMinutes)) * 0.5 * minutes;
            }

            // levels from sales XP
            int before = r.s.level;
            r.s.level = Math.Max(1, LevelFor(r.cashSold * progression.XpPerCashSold));
            for (int l = before + 1; l <= r.s.level && before > 0; l++)
            {
                int gift = premium.LevelUpReward(l);
                if (gift > 0) Earn(r, gift, "level");
            }

            // area gifts
            foreach (var (id, minute) in model.expansions)
            {
                if (r.salesMinutes < minute || !r.opened.Add(id)) continue;
                var m = premium.MilestoneFor(id);
                if (m != null) Earn(r, m.diamonds, "milestone");
            }

            if (!r.taskDiamondsPaid && r.salesMinutes >= model.taskDiamondsMinute && tasks != null)
            {
                r.taskDiamondsPaid = true;
                int sum = tasks.MainTasks.Where(t => t != null).Sum(t => t.RewardPremium);
                if (sum > 0) Earn(r, sum, "task");
            }

            if (r.s.coreDoneDay < 0f && r.salesMinutes >= model.coreGameMinutes) r.s.coreDoneDay = (float)(r.wall / 86400.0);
            Achievements(r, played ? minutes : 0);
        }

        void Achievements(Run r, double playedMinutes)
        {
            if (missions == null) return;
            r.scrapCount += playedMinutes * model.scrapPerPlayedMinute;
            float pm = (float)r.salesMinutes;
            double upgrades = Math.Min(pm, model.coreGameMinutes) * model.upgradesPerMinute + Math.Max(0, pm - model.coreGameMinutes) * model.upgradesPerMinuteLate;
            var list = missions.Achievements;
            for (int i = 0; i < list.Length && i < r.tiers.Length; i++)
            {
                var a = list[i];
                double count = a.stat switch
                {
                    MissionStat.ScrapBroken => r.scrapCount,
                    MissionStat.CashEarned => r.cashAll,
                    MissionStat.ItemsSold => pm * model.itemsSoldPerMinute,
                    MissionStat.WorkersHired => model.hireMinutes.Count(m => m <= pm),
                    MissionStat.AreasOpened => r.opened.Count,
                    MissionStat.OrdersCompleted => r.ordersDone,
                    MissionStat.UpgradesBought => upgrades,
                    _ => 0
                };
                while (r.tiers[i] < a.targets.Length && count >= a.targets[r.tiers[i]])
                {
                    Earn(r, a.diamonds[Mathf.Min(r.tiers[i], a.diamonds.Length - 1)], "achievement");
                    r.tiers[i]++;
                }
            }
        }

        void Reward(Run r, MissionReward reward, string source, double share)
        {
            if (reward == null || share <= 0) return;
            if (reward.diamonds > 0) Earn(r, reward.diamonds * share, source);
            double income = Income((float)r.salesMinutes);
            if (reward.cashPerLevel > 0) Gain(r, ref r.s.rewardMinutes, share * reward.Cash(r.s.level) / income);
            if (reward.boost != null && reward.boostMinutes > 0f) Gain(r, ref r.s.rewardMinutes, share * BoostMinutes(reward.boost, reward.boostMinutes), true);
        }

        void Earn(Run r, double diamonds, string source)
        {
            r.s.diamondsEarned += diamonds;
            r.s.balance += diamonds;
            r.s.maxBalance = Math.Max(r.s.maxBalance, r.s.balance);
            r.s.sources[source] = (r.s.sources.TryGetValue(source, out double n) ? n : 0) + diamonds;
        }

        /// <summary>Adds gained minutes to a bucket; <paramref name="sales"/> for boosts (more sales), false for cash.</summary>
        void Gain(Run r, ref double bucket, double minutes, bool sales = false)
        {
            if (minutes <= 0) return;
            bucket += minutes;
            Advance(r, minutes, sales);
        }

        public float Expansion(string id)
        {
            foreach (var (eid, minute) in model.expansions)
                if (eid == id) return minute;
            return float.MaxValue;
        }

        /// <summary>Income per minute at a progress minute (model).</summary>
        public double Income(float minute)
        {
            var c = model.incomeCurve;
            if (minute <= c[0].minute) return c[0].income;
            for (int i = 1; i < c.Length; i++)
                if (minute <= c[i].minute)
                    return Mathf.Lerp(c[i - 1].income, c[i].income, (minute - c[i - 1].minute) / (c[i].minute - c[i - 1].minute));
            return Math.Min(model.incomeCap, c[^1].income + (minute - c[^1].minute) / 60f * model.incomeGrowthPerHour);
        }

        /// <summary>Sales minute at which the yard reaches <paramref name="level"/> (model income, XP from sales).</summary>
        public float MinuteForLevel(int level)
        {
            double cash = 0;
            for (float m = 0f; m < 100000f; m += 0.5f)
            {
                if (LevelFor(cash * progression.XpPerCashSold) >= level) return m;
                cash += (Income(m) + Income(m + 0.5f)) * 0.25;
            }

            return float.MaxValue;
        }

        /// <summary>Minutes of income one diamond of cash stands for at <paramref name="level"/> (crates, packs, order skips).</summary>
        public double CashDiamondMinutes(int level) => premium.CashPerDiamond(level) / Income(MinuteForLevel(level));

        int LevelFor(double xp)
        {
            int level = 1;
            while (level < progression.MaxLevel && xp >= progression.XpToNext(level))
            {
                xp -= progression.XpToNext(level);
                level++;
            }

            return level;
        }

        // ------------------------------------------------------------------ report

        /// <summary>Markdown tables for every profile at every checkpoint.</summary>
        public string Report(IEnumerable<Profile> profiles)
        {
            var sb = new StringBuilder();
            sb.AppendLine("| Profile | At | Played | Progress | Lv | ◆ earned | ◆ spent | ◆ left | Max held | from video | from IAP | Boosts / crates | Videos | RV cash | Progress from play / away / ◆ / video / IAP | $ |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var p in profiles)
                foreach (var s in Simulate(p))
                    sb.AppendLine($"| {p.name} | {s.label} | {Dur(s.playedMinutes)} | {Dur(s.progressMinutes)} (x{s.Speed:0.00}) | {s.level} | {s.diamondsEarned:0} | " +
                                  $"{s.diamondsSpent:0} | {s.balance:0} | {s.maxBalance:0} | {s.fromVideo:0} | {s.fromIap:0} | {s.boostsBought} / {s.cratesBought} | " +
                                  $"{s.videosWatched:0} / {s.videosOffered:0} | ${Short(s.rvCash)} | {s.PlayedShare:P0} / {s.AwayShare:P0} / {s.DiamondShare:P0} / {s.VideoShare:P0} / {s.IapShare:P0} | {s.usd:0.##} |");
            return sb.ToString();
        }

        static string Dur(double minutes) => minutes < 120 ? $"{minutes:0} min" : $"{minutes / 60:0.#} h";
        static string Short(double n) => CurrencyFormat.Short((long)n);
    }
}

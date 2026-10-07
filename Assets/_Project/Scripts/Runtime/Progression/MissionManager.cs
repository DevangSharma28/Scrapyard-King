using System;
using System.Collections.Generic;
using ScrapYardKing.Boosts;
using ScrapYardKing.Core;
using ScrapYardKing.Economy;
using ScrapYardKing.Items;
using ScrapYardKing.Persistence;
using UnityEngine;

namespace ScrapYardKing.Progression
{
    /// <summary>
    /// Daily missions and achievements. Keeps lifetime counts of what the player does (from <see cref="GameEvents"/>,
    /// never called by gameplay), draws the day's missions from <see cref="MissionConfig"/> at the UTC day change and
    /// measures each from the count it started at. Rewards are paid on claim, once: a claimed daily or tier is recorded
    /// before the reward is paid. The main task chain stays with <see cref="TaskManager"/>.
    /// </summary>
    [DefaultExecutionOrder(-505)]
    public sealed class MissionManager : ServiceBehaviour<MissionManager>, ISaveable
    {
        [Serializable]
        sealed class State
        {
            public List<long> counts = new();
            public int day = -1;
            public List<string> dailyIds = new();
            public List<long> dailyStart = new();
            public List<long> dailyTarget = new();
            public List<bool> dailyClaimed = new();
            public List<string> achievementIds = new();
            public List<int> achievementTier = new();
        }

        [SerializeField] MissionConfig config;

        State state = new();
        float nextDayCheck;

        public MissionConfig Config => config;
        public event Action Changed;

        static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        public static int Today => (int)(Now / 86400);
        /// <summary>Seconds until the next set of daily missions.</summary>
        public static long SecondsToNewDay => 86400 - Now % 86400;

        void OnEnable()
        {
            GameEvents.ScrapBroken += OnScrap;
            GameEvents.ItemsCollected += OnCollected;
            GameEvents.ItemsProcessed += OnProcessed;
            GameEvents.ItemsSold += OnSold;
            GameEvents.CashEarned += OnCash;
            GameEvents.CustomerServed += OnServed;
            GameEvents.MachineOverdrive += OnOverdrive;
            GameEvents.ContractChanged += OnContract;
            GameEvents.UpgradePurchased += OnUpgrade;
            GameEvents.WorkerHired += OnHired;
            GameEvents.ExpansionOpened += OnArea;
        }

        void OnDisable()
        {
            GameEvents.ScrapBroken -= OnScrap;
            GameEvents.ItemsCollected -= OnCollected;
            GameEvents.ItemsProcessed -= OnProcessed;
            GameEvents.ItemsSold -= OnSold;
            GameEvents.CashEarned -= OnCash;
            GameEvents.CustomerServed -= OnServed;
            GameEvents.MachineOverdrive -= OnOverdrive;
            GameEvents.ContractChanged -= OnContract;
            GameEvents.UpgradePurchased -= OnUpgrade;
            GameEvents.WorkerHired -= OnHired;
            GameEvents.ExpansionOpened -= OnArea;
        }

        void Start()
        {
            SaveRegistry.Register(this);
            RollIfNewDay();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            SaveRegistry.Unregister(this);
        }

        string ISaveable.SaveKey => "missions";
        string ISaveable.CaptureState() => JsonUtility.ToJson(state);

        void ISaveable.RestoreState(string json)
        {
            state = JsonUtility.FromJson<State>(json) ?? new State();
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ counting

        void OnScrap(ScrapBrokenEvent e) => Add(MissionStat.ScrapBroken, 1);
        void OnCollected(ItemDefinition item, int count) => Add(MissionStat.ItemsCollected, count);
        void OnProcessed(ItemsProcessedEvent e) => Add(MissionStat.ItemsProcessed, e.InputCount);
        void OnSold(ItemDefinition item, int units, long cash) => Add(MissionStat.ItemsSold, units);
        void OnCash(long amount) => Add(MissionStat.CashEarned, amount);
        void OnServed(string station, int units) => Add(MissionStat.CustomersServed, 1);
        void OnOverdrive(string machine) => Add(MissionStat.Overdrives, 1);
        void OnUpgrade(string id, int level) => Add(MissionStat.UpgradesBought, 1);
        void OnHired(string id, int total) => Add(MissionStat.WorkersHired, 1);
        void OnArea(string id) => Add(MissionStat.AreasOpened, 1);

        void OnContract(ContractStatus status)
        {
            if (status.Completed) Add(MissionStat.OrdersCompleted, 1);
        }

        void Add(MissionStat stat, long amount)
        {
            if (amount <= 0 || SaveRegistry.IsRestoring) return;
            int i = (int)stat;
            while (state.counts.Count <= i) state.counts.Add(0);
            state.counts[i] += amount;
            Changed?.Invoke();
        }

        public long Count(MissionStat stat)
        {
            int i = (int)stat;
            return i < state.counts.Count ? state.counts[i] : 0;
        }

        void Update()
        {
            if (Time.unscaledTime < nextDayCheck) return;
            nextDayCheck = Time.unscaledTime + 5f;
            RollIfNewDay();
        }

        // ------------------------------------------------------------------ dailies

        void RollIfNewDay()
        {
            if (config == null || state.day == Today) return;
            state.day = Today;
            state.dailyIds.Clear();
            state.dailyStart.Clear();
            state.dailyTarget.Clear();
            state.dailyClaimed.Clear();
            int level = Level;
            var pool = new List<MissionConfig.DailyMission>();
            foreach (var m in config.DailyPool)
                if (m != null && level >= m.minLevel) pool.Add(m);

            // the same day draws the same missions (seeded by the day), with no stat twice
            var random = new System.Random(Today * 7919 + 17);
            var usedStats = new HashSet<MissionStat>();
            while (state.dailyIds.Count < config.DailyCount && pool.Count > 0)
            {
                var pick = pool[random.Next(pool.Count)];
                pool.Remove(pick);
                if (!usedStats.Add(pick.stat)) continue;
                state.dailyIds.Add(pick.id);
                state.dailyStart.Add(Count(pick.stat));
                state.dailyTarget.Add(pick.Target(level));
                state.dailyClaimed.Add(false);
            }

            Changed?.Invoke();
        }

        public int DailyCount => state.dailyIds.Count;
        public MissionConfig.DailyMission Daily(int i) => config != null ? config.FindDaily(state.dailyIds[i]) : null;
        public long DailyTarget(int i) => state.dailyTarget[i];
        public long DailyProgress(int i)
        {
            var m = Daily(i);
            return m == null ? 0 : Math.Min(DailyTarget(i), Count(m.stat) - state.dailyStart[i]);
        }

        public bool DailyClaimed(int i) => state.dailyClaimed[i];
        public bool DailyClaimable(int i) => !DailyClaimed(i) && DailyProgress(i) >= DailyTarget(i);

        public bool ClaimDaily(int i, CurrencyOrigin origin)
        {
            if (i < 0 || i >= DailyCount || !DailyClaimable(i)) return false;
            state.dailyClaimed[i] = true;
            Pay(Daily(i).reward, origin, "daily:" + state.dailyIds[i]);
            Changed?.Invoke();
            return true;
        }

        // ------------------------------------------------------------------ achievements

        public int AchievementTier(int i)
        {
            var a = config.Achievements[i];
            int k = state.achievementIds.IndexOf(a.id);
            return k < 0 ? 0 : state.achievementTier[k];
        }

        public bool AchievementDone(int i) => AchievementTier(i) >= (config.Achievements[i].targets?.Length ?? 0);
        public long AchievementTarget(int i) => AchievementDone(i) ? config.Achievements[i].targets[^1] : config.Achievements[i].targets[AchievementTier(i)];
        public long AchievementProgress(int i) => Math.Min(AchievementTarget(i), Count(config.Achievements[i].stat));
        public int AchievementReward(int i) => AchievementDone(i) ? 0 : config.Achievements[i].diamonds[Mathf.Min(AchievementTier(i), config.Achievements[i].diamonds.Length - 1)];
        public bool AchievementClaimable(int i) => !AchievementDone(i) && Count(config.Achievements[i].stat) >= AchievementTarget(i);

        public bool ClaimAchievement(int i, CurrencyOrigin origin)
        {
            if (!AchievementClaimable(i)) return false;
            var a = config.Achievements[i];
            int reward = AchievementReward(i);
            int k = state.achievementIds.IndexOf(a.id);
            if (k < 0)
            {
                state.achievementIds.Add(a.id);
                state.achievementTier.Add(0);
                k = state.achievementIds.Count - 1;
            }

            state.achievementTier[k]++;
            Pay(new MissionReward { diamonds = reward }, origin, "achievement:" + a.id);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Dailies and achievement tiers waiting to be claimed (the badge on MISSIONS).</summary>
        public int Claimable
        {
            get
            {
                if (config == null) return 0;
                int n = 0;
                for (int i = 0; i < DailyCount; i++)
                    if (DailyClaimable(i)) n++;
                for (int i = 0; i < config.Achievements.Length; i++)
                    if (AchievementClaimable(i)) n++;
                return n;
            }
        }

        // ------------------------------------------------------------------ paying

        static int Level => Services.TryGet(out ProgressionManager p) ? p.Level : 1;

        /// <summary>Pays a reward (cash, diamonds, a boost), flying from <paramref name="origin"/>, then saves.</summary>
        public static void Pay(MissionReward reward, CurrencyOrigin origin, string reason)
        {
            if (reward == null) return;
            if (Services.TryGet(out EconomyManager economy))
            {
                long cash = reward.Cash(Level);
                if (cash > 0) economy.AddCash(cash, origin);
                if (reward.diamonds > 0) economy.AddPremium(reward.diamonds, reason, origin);
            }

            if (reward.boost != null && reward.boostMinutes > 0f && Services.TryGet(out BoostManager boosts))
                boosts.Activate(reward.boost, reward.boostMinutes * 60f);
            Analytics.Log(AnalyticsEvents.RewardClaimed, ("reason", reason));
            if (Services.TryGet(out SaveManager save)) save.Save();
        }

        /// <summary>Fills a mission title: {0} = the target, {s} = "S" unless the target is 1 ("HIRE 1 WORKER").</summary>
        public static string Title(string format, long target, string shown) =>
            string.Format((format ?? "").Replace("{s}", target == 1 ? "" : "S"), shown);

        /// <summary>"$2.4K", "5", "2X CASH 10 MIN": the short label of a reward at the current level.</summary>
        public static string Label(MissionReward reward)
        {
            if (reward == null) return "";
            if (reward.diamonds > 0 && reward.cashPerLevel <= 0) return reward.diamonds.ToString();
            if (reward.cashPerLevel > 0 && reward.diamonds <= 0) return "$" + CurrencyFormat.Short(reward.Cash(Level));
            if (reward.cashPerLevel > 0) return "$" + CurrencyFormat.Short(reward.Cash(Level)) + " + " + reward.diamonds + " DIAMONDS";
            return reward.boost != null ? $"{reward.boost.DisplayName} {reward.boostMinutes:0} MIN" : "";
        }
    }
}

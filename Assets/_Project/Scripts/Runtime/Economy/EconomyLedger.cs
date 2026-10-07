using System;
using System.Collections.Generic;
using ScrapYardKing.Core;
using ScrapYardKing.Persistence;
using UnityEngine;

namespace ScrapYardKing.Economy
{
    /// <summary>
    /// The numbers behind the developer economy window (Window → Scrap Yard King → Economy): cash per minute, where
    /// diamonds come from and go to, videos offered and watched, purchases, offline and order cash. It listens only:
    /// wallet events for cash and the <see cref="Analytics"/> stream (as a sink) for everything else, so no gameplay
    /// code knows it exists. Lifetime totals and the last 30 days are saved; nothing here is ever shown to players.
    /// </summary>
    [DefaultExecutionOrder(-590)]
    public sealed class EconomyLedger : ServiceBehaviour<EconomyLedger>, ISaveable, IAnalyticsSink
    {
        const int KeepDays = 30;
        const float RateWindow = 60f;

        /// <summary>Totals for one UTC day.</summary>
        [Serializable]
        public sealed class Day
        {
            public int day;
            public float playSeconds;
            public long cashEarned, cashSpent, rvCash;
            public long diamondsIn, diamondsOut;
            /// <summary>Diamond balance × seconds played: divided by <see cref="playSeconds"/> it is the average balance.</summary>
            public double balanceSeconds;
            public int rvOffered, rvWatched, iapCount;
            public float usd;

            public double AverageBalance => playSeconds > 0f ? balanceSeconds / playSeconds : 0;
        }

        /// <summary>An amount and a count under a key (a diamond source, an offer id, a product id).</summary>
        [Serializable]
        public sealed class Entry
        {
            public string key;
            public long amount;
            public int count;
        }

        [Serializable]
        sealed class State
        {
            public int firstDay = -1;
            public int sessions;
            public float playSeconds;
            public long cashEarned, cashSpent, rvCash, offlineCash, contractCash, crateCash;
            public int contracts, contractsDoubled, offlineClaims, offlineDoubled, upgrades, boosts, shopOpens;
            public int rvOffered, rvWatched, rvFailed;
            public int iapInitiated, iapCompleted, iapFailed;
            public float usd;
            public List<Entry> diamondsIn = new();
            public List<Entry> diamondsOut = new();
            public List<Entry> rvOffers = new();
            public List<Entry> products = new();
            public List<Day> days = new();
        }

        State state = new();
        EconomyManager economy;
        readonly Queue<(float time, long amount)> recent = new();
        long recentSum;
        float sessionStart;
        long sessionCash;
        int sessionRv;
        bool sessionCounted, inReward;
        long notFromVideo;

        // ------------------------------------------------------------------ read by the window

        public int Sessions => state.sessions;
        public float PlaySeconds => state.playSeconds;
        public long CashEarned => state.cashEarned;
        public long CashSpent => state.cashSpent;
        public long RvCash => state.rvCash;
        public long OfflineCash => state.offlineCash;
        public long ContractCash => state.contractCash;
        public long CrateCash => state.crateCash;
        public int Contracts => state.contracts;
        public int ContractsDoubled => state.contractsDoubled;
        public int OfflineClaims => state.offlineClaims;
        public int OfflineDoubled => state.offlineDoubled;
        public int Upgrades => state.upgrades;
        public int Boosts => state.boosts;
        public int ShopOpens => state.shopOpens;
        public int RvOffered => state.rvOffered;
        public int RvWatched => state.rvWatched;
        public int RvFailed => state.rvFailed;
        public int IapInitiated => state.iapInitiated;
        public int IapCompleted => state.iapCompleted;
        public int IapFailed => state.iapFailed;
        public float Usd => state.usd;
        public IReadOnlyList<Entry> DiamondsIn => state.diamondsIn;
        public IReadOnlyList<Entry> DiamondsOut => state.diamondsOut;
        public IReadOnlyList<Entry> RvOffers => state.rvOffers;
        public IReadOnlyList<Entry> Products => state.products;
        public IReadOnlyList<Day> Days => state.days;

        /// <summary>Days since the first session, counting today (at least 1).</summary>
        public int DaysPlayed => state.firstDay < 0 ? 1 : Mathf.Max(1, Today - state.firstDay + 1);

        public float SessionSeconds => Time.unscaledTime - sessionStart;
        public long SessionCash => sessionCash;
        public int SessionRv => sessionRv;

        /// <summary>Cash earned over the last minute of play (scaled up while the session is younger than a minute).</summary>
        public float CashPerMinute
        {
            get
            {
                Trim();
                float span = Mathf.Min(RateWindow, Mathf.Max(5f, Time.time - sessionStart));
                return recentSum * 60f / span;
            }
        }

        public long DiamondsEarned => Sum(state.diamondsIn);
        public long DiamondsSpent => Sum(state.diamondsOut);

        static int Today => (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 86400);

        // ------------------------------------------------------------------ wiring

        void OnEnable()
        {
            Analytics.AddSink(this);
            GameEvents.CashEarned += OnCashEarned;
        }

        void OnDisable()
        {
            Analytics.RemoveSink(this);
            GameEvents.CashEarned -= OnCashEarned;
            if (economy != null) economy.CashChanged -= OnCashChanged;
        }

        void Start()
        {
            sessionStart = Time.unscaledTime;
            SaveRegistry.Register(this);
            if (Services.TryGet(out economy)) economy.CashChanged += OnCashChanged;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            SaveRegistry.Unregister(this);
        }

        string ISaveable.SaveKey => "ledger";
        string ISaveable.CaptureState() => JsonUtility.ToJson(state);

        void ISaveable.RestoreState(string json) => state = JsonUtility.FromJson<State>(json) ?? new State();

        void Update()
        {
            if (!sessionCounted)
            {
                sessionCounted = true;
                state.sessions++;
                if (state.firstDay < 0) state.firstDay = Today;
            }

            float dt = Time.unscaledDeltaTime;
            state.playSeconds += dt;
            var d = DayEntry();
            d.playSeconds += dt;
            if (economy != null) d.balanceSeconds += economy.Premium * (double)dt;
        }

        // ------------------------------------------------------------------ cash

        void OnCashEarned(long amount)
        {
            if (amount <= 0 || SaveRegistry.IsRestoring) return;
            state.cashEarned += amount;
            sessionCash += amount;
            DayEntry().cashEarned += amount;
            recent.Enqueue((Time.time, amount));
            recentSum += amount;
            if (inReward)
            {
                long fromVideo = Math.Max(0, amount - notFromVideo);
                notFromVideo = 0;
                state.rvCash += fromVideo;
                DayEntry().rvCash += fromVideo;
            }
        }

        void OnCashChanged(long balance, long delta, CurrencyOrigin origin)
        {
            if (delta >= 0) return;
            state.cashSpent -= delta;
            DayEntry().cashSpent -= delta;
        }

        void Trim()
        {
            while (recent.Count > 0 && Time.time - recent.Peek().time > RateWindow) recentSum -= recent.Dequeue().amount;
        }

        // ------------------------------------------------------------------ analytics stream

        void IAnalyticsSink.Log(string eventName, IReadOnlyDictionary<string, object> data)
        {
            var d = DayEntry();
            switch (eventName)
            {
                case AnalyticsEvents.DiamondEarned:
                    long gained = Long(data, "amount");
                    Add(state.diamondsIn, Source(Text(data, "reason")), gained);
                    d.diamondsIn += gained;
                    break;
                case AnalyticsEvents.DiamondSpent:
                    long spent = Long(data, "amount");
                    Add(state.diamondsOut, Source(Text(data, "reason")), spent);
                    d.diamondsOut += spent;
                    break;
                case AnalyticsEvents.RvOffered:
                    state.rvOffered++;
                    d.rvOffered++;
                    break;
                // cash that arrives between these two events is the video's reward
                case AnalyticsEvents.RvCompleted:
                    state.rvWatched++;
                    d.rvWatched++;
                    sessionRv++;
                    Add(state.rvOffers, Text(data, "offer"), 0);
                    inReward = true;
                    break;
                case AnalyticsEvents.RvRewardGranted:
                    inReward = false;
                    if (data.ContainsKey("placement"))
                    {
                        // the shop's free diamonds do not go through OfferDirector.Watch
                        state.rvWatched++;
                        d.rvWatched++;
                        sessionRv++;
                        Add(state.rvOffers, Text(data, "placement"), 0);
                    }

                    break;
                case AnalyticsEvents.RvFailed:
                    state.rvFailed++;
                    inReward = false;
                    break;
                case AnalyticsEvents.IapInitiated:
                    state.iapInitiated++;
                    break;
                case AnalyticsEvents.IapCompleted:
                    float usd = (float)Double(data, "usd");
                    state.iapCompleted++;
                    state.usd += usd;
                    d.iapCount++;
                    d.usd += usd;
                    Add(state.products, Text(data, "product"), 0);
                    break;
                case AnalyticsEvents.IapFailed:
                    state.iapFailed++;
                    break;
                case AnalyticsEvents.OfflineClaimed:
                    state.offlineClaims++;
                    state.offlineCash += Long(data, "cash");
                    break;
                case AnalyticsEvents.OfflineDoubled:
                    state.offlineClaims++;
                    state.offlineDoubled++;
                    state.offlineCash += Long(data, "cash");
                    notFromVideo = Long(data, "cash") / 2;   // the first half was the player's anyway
                    break;
                case AnalyticsEvents.ContractCompleted:
                    state.contracts++;
                    state.contractCash += Long(data, "reward");
                    break;
                case AnalyticsEvents.ContractDoubled:
                    state.contractsDoubled++;
                    break;
                case AnalyticsEvents.UpgradePurchased:
                    state.upgrades++;
                    break;
                case AnalyticsEvents.BoostActivated:
                    state.boosts++;
                    break;
                case AnalyticsEvents.ShopOpened:
                    state.shopOpens++;
                    break;
            }
        }

        /// <summary>"task:t20_plant" → "task", "iap:gems_500" → "iap": the source group of a diamond reason.</summary>
        public static string Source(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return "other";
            int colon = reason.IndexOf(':');
            return colon > 0 ? reason.Substring(0, colon) : reason;
        }

        Day DayEntry()
        {
            int today = Today;
            var days = state.days;
            if (days.Count > 0 && days[^1].day == today) return days[^1];
            var d = new Day { day = today };
            days.Add(d);
            while (days.Count > KeepDays) days.RemoveAt(0);
            return d;
        }

        static void Add(List<Entry> list, string key, long amount)
        {
            key = string.IsNullOrEmpty(key) ? "other" : key;
            var e = list.Find(x => x.key == key);
            if (e == null) list.Add(e = new Entry { key = key });
            e.amount += amount;
            e.count++;
        }

        static long Sum(List<Entry> list)
        {
            long n = 0;
            foreach (var e in list) n += e.amount;
            return n;
        }

        static string Text(IReadOnlyDictionary<string, object> data, string key) =>
            data != null && data.TryGetValue(key, out var v) && v != null ? v.ToString() : "";

        static long Long(IReadOnlyDictionary<string, object> data, string key) =>
            data != null && data.TryGetValue(key, out var v) && v != null ? Convert.ToInt64(v) : 0;

        static double Double(IReadOnlyDictionary<string, object> data, string key) =>
            data != null && data.TryGetValue(key, out var v) && v != null ? Convert.ToDouble(v) : 0;
    }
}

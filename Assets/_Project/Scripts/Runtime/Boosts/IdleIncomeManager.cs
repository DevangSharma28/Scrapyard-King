using System;
using ScrapYardKing.Core;
using ScrapYardKing.Economy;
using UnityEngine;

namespace ScrapYardKing.Boosts
{
    /// <summary>
    /// Offline income. While the game runs it keeps a smoothed measure of what the yard earns per second; that rate and
    /// the time of saving go into the save. On the next start it turns the time away into a cash gift (a share of what the
    /// yard would have earned, capped) that waits to be claimed, plain or doubled by a rewarded video. The yard is not
    /// simulated while closed: stock, trucks and workers are where they were.
    /// </summary>
    [DefaultExecutionOrder(-435)]
    public sealed class IdleIncomeManager : ServiceBehaviour<IdleIncomeManager>, ISaveable
    {
        [Serializable]
        sealed class State
        {
            public double ratePerSecond;
            public long savedAtUnix;
            public long pending;
            public double awaySeconds;
        }

        [Tooltip("Share of the measured income the yard is credited with while the game is closed.")]
        [SerializeField, Range(0f, 1f)] float efficiency = 0.3f;
        [Tooltip("Time away beyond this earns nothing more, hours.")]
        [SerializeField, Min(0.1f)] float maxHours = 4f;
        [Tooltip("Shorter absences give nothing (and show nothing), seconds.")]
        [SerializeField, Min(0f)] float minAwaySeconds = 120f;
        [Tooltip("Seconds the income measure looks back over.")]
        [SerializeField, Min(10f)] float window = 180f;

        double rate;
        long bucket;
        float bucketStart;

        /// <summary>Cash waiting to be claimed (0 when there is nothing).</summary>
        public long Pending { get; private set; }
        /// <summary>Seconds the player was away, as counted for <see cref="Pending"/>.</summary>
        public double AwaySeconds { get; private set; }
        /// <summary>Smoothed cash per second the yard earns right now.</summary>
        public double RatePerSecond => rate;

        void OnEnable() => GameEvents.CashEarned += OnCash;
        void OnDisable() => GameEvents.CashEarned -= OnCash;

        void Start()
        {
            bucketStart = Time.time;
            SaveRegistry.Register(this);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            SaveRegistry.Unregister(this);
        }

        void OnCash(long amount) => bucket += amount;

        void Update()
        {
            // Every 10 s the last bucket is folded into an exponential average over the window.
            float span = Time.time - bucketStart;
            if (span < 10f) return;
            double sample = bucket / (double)span;
            double weight = Mathf.Clamp01(span / window);
            rate = rate <= 0.0 ? sample : rate + (sample - rate) * weight;
            bucket = 0;
            bucketStart = Time.time;
        }

        /// <summary>Share of the measured income credited while away (for the welcome-back card).</summary>
        public float Efficiency => efficiency;
        public float MaxHours => maxHours;

        /// <summary>Pays the waiting cash, twice if <paramref name="doubled"/>, flying from <paramref name="origin"/>.</summary>
        public void Claim(bool doubled, CurrencyOrigin origin = default)
        {
            long amount = Pending * (doubled ? 2 : 1);
            if (amount <= 0) return;
            Pending = 0;
            AwaySeconds = 0;
            Analytics.Log(doubled ? AnalyticsEvents.OfflineDoubled : AnalyticsEvents.OfflineClaimed, ("cash", amount));
            if (Services.TryGet(out EconomyManager economy)) economy.AddCash(amount, origin);
        }

        string ISaveable.SaveKey => "idle";

        // An unclaimed gift is saved with it, so closing the game on the welcome-back card loses nothing.
        string ISaveable.CaptureState() => JsonUtility.ToJson(new State
        {
            ratePerSecond = rate, savedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), pending = Pending, awaySeconds = AwaySeconds
        });

        void ISaveable.RestoreState(string json)
        {
            var state = JsonUtility.FromJson<State>(json);
            if (state == null) return;
            rate = Math.Max(0.0, state.ratePerSecond);
            Pending = Math.Max(0, state.pending);
            AwaySeconds = Math.Max(0.0, state.awaySeconds);
            double away = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - state.savedAtUnix;
            if (state.savedAtUnix <= 0 || away < minAwaySeconds) return;
            double counted = Math.Min(away, maxHours * 3600.0);
            AwaySeconds = Math.Min(AwaySeconds + counted, maxHours * 3600.0);
            Pending += (long)Math.Floor(rate * counted * efficiency);
        }
    }
}

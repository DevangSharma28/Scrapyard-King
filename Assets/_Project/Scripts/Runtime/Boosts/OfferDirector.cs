using System;
using System.Collections.Generic;
using ScrapYardKing.Core;
using ScrapYardKing.Economy;
using ScrapYardKing.Persistence;
using ScrapYardKing.Progression;
using ScrapYardKing.UI;
using UnityEngine;

namespace ScrapYardKing.Boosts
{
    /// <summary>
    /// The one rewarded-video gate. Every offer (<see cref="AdOfferDefinition"/>) is asked here whether it may be shown
    /// (<see cref="Ready"/>: own cooldown in wall-clock time, a daily cap, the ad network ready) and is played here
    /// (<see cref="Watch"/>: analytics, cooldown, count, reward only after the video completed). Cooldowns and daily
    /// counts are saved, so quitting does not reset them.
    ///
    /// Placements:
    /// - the rotating HUD button: at most one offer at a time, a quiet gap between offers, never while a popup or
    ///   another video button is on screen, never a boost that already runs;
    /// - right after a completed truck order: the ORDER COMPLETE card with "2X" (only when that offer is ready, so a
    ///   busy dock does not raise a card for every truck);
    /// - <see cref="InstantTruck"/> and <see cref="OfflineDouble"/>, which their own UI shows through <see cref="Ready"/>.
    /// Ignoring any offer costs nothing; balance holds with every offer ignored.
    /// </summary>
    [DefaultExecutionOrder(-430)]
    public sealed class OfferDirector : ServiceBehaviour<OfferDirector>, ISaveable
    {
        [Serializable]
        sealed class State
        {
            public List<string> ids = new();
            public List<long> readyAt = new();
            public int day;
            public List<string> takenIds = new();
            public List<int> taken = new();
        }

        [Tooltip("Offers that rotate on the HUD button.")]
        [SerializeField] AdOfferDefinition[] offers;
        [Tooltip("Offered on the ORDER COMPLETE card.")]
        [SerializeField] AdOfferDefinition doubleTruckOffer;
        [SerializeField] AdOfferDefinition instantTruckOffer;
        [SerializeField] AdOfferDefinition offlineOffer;
        [Tooltip("Seconds of play before the first HUD offer.")]
        [SerializeField, Min(0f)] float firstOfferAfter = 90f;
        [Tooltip("Seconds with no HUD offer between two offers.")]
        [SerializeField, Min(0f)] float quietSeconds = 70f;
        [Tooltip("An order must pay at least this much for the ORDER COMPLETE card to offer doubling it.")]
        [SerializeField, Min(0)] long minDoubleReward = 400;
        [SerializeField] Sprite videoIcon;
        [SerializeField] Sprite cashIcon;

        readonly Dictionary<string, long> readyAt = new();
        readonly Dictionary<string, int> taken = new();
        int day;
        float nextOfferAt, expiresAt;

        /// <summary>The HUD offer on screen, or null.</summary>
        public AdOfferDefinition Current { get; private set; }
        public float TimeLeft01 => Current == null ? 0f : Mathf.Clamp01((expiresAt - Time.time) / Mathf.Max(0.1f, Current.ShowSeconds));
        public long CurrentCash => Current != null && Current.Kind == AdOfferKind.FreeCash ? FreeCash(Current) : 0;
        public AdOfferDefinition InstantTruck => instantTruckOffer;
        public AdOfferDefinition DoubleTruck => doubleTruckOffer;
        /// <summary>The rotating HUD offers.</summary>
        public IReadOnlyList<AdOfferDefinition> Offers => offers ?? Array.Empty<AdOfferDefinition>();
        public float QuietSeconds => quietSeconds;

        /// <summary>Seconds until <paramref name="o"/> is off cooldown (0 = now).</summary>
        public long CooldownLeft(AdOfferDefinition o) =>
            o != null && readyAt.TryGetValue(o.Id, out long t) ? Math.Max(0, t - Now) : 0;
        public AdOfferDefinition OfflineDouble => offlineOffer;
        public Sprite VideoIcon => videoIcon;
        /// <summary>Set by a HUD element that shows its own video button; the rotating offer waits meanwhile.</summary>
        public bool ContextualVisible { get; set; }

        static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        static int Today => (int)(Now / 86400);

        void OnEnable() => GameEvents.ContractChanged += OnContract;
        void OnDisable() => GameEvents.ContractChanged -= OnContract;

        void Start()
        {
            nextOfferAt = Time.time + firstOfferAfter;
            SaveRegistry.Register(this);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            SaveRegistry.Unregister(this);
        }

        string ISaveable.SaveKey => "offers";

        string ISaveable.CaptureState()
        {
            var s = new State { day = day };
            foreach (var pair in readyAt)
            {
                s.ids.Add(pair.Key);
                s.readyAt.Add(pair.Value);
            }

            foreach (var pair in taken)
            {
                s.takenIds.Add(pair.Key);
                s.taken.Add(pair.Value);
            }

            return JsonUtility.ToJson(s);
        }

        void ISaveable.RestoreState(string json)
        {
            var s = JsonUtility.FromJson<State>(json);
            if (s == null) return;
            readyAt.Clear();
            taken.Clear();
            for (int i = 0; i < s.ids.Count && i < s.readyAt.Count; i++) readyAt[s.ids[i]] = s.readyAt[i];
            day = s.day;
            for (int i = 0; i < s.takenIds.Count && i < s.taken.Count; i++) taken[s.takenIds[i]] = s.taken[i];
        }

        // ------------------------------------------------------------------ the gate

        /// <summary>Videos of this offer watched today (UTC).</summary>
        public int TakenToday(AdOfferDefinition o)
        {
            if (day != Today)
            {
                day = Today;
                taken.Clear();
            }

            return taken.TryGetValue(o.Id, out int n) ? n : 0;
        }

        /// <summary>This offer may be shown now: off cooldown, under its daily cap, and a video can play.</summary>
        public bool Ready(AdOfferDefinition o)
        {
            if (o == null) return false;
            if (readyAt.TryGetValue(o.Id, out long t) && Now < t) return false;
            if (o.DailyCap > 0 && TakenToday(o) >= o.DailyCap) return false;
            return Services.TryGet(out AdService ads) && ads.Ready;
        }

        /// <summary>
        /// Plays the video for <paramref name="o"/> and runs <paramref name="reward"/> only after it completed. The
        /// offer's cooldown and daily count start when the reward is granted.
        /// </summary>
        public bool Watch(AdOfferDefinition o, Action reward, Action failed = null)
        {
            if (!Ready(o) || !Services.TryGet(out AdService ads)) return false;
            ads.ShowRewarded(o.Id, () =>
            {
                Analytics.Log(AnalyticsEvents.RvCompleted, ("offer", o.Id));
                readyAt[o.Id] = Now + (long)o.Cooldown;
                taken[o.Id] = TakenToday(o) + 1;
                reward?.Invoke();
                Analytics.Log(AnalyticsEvents.RvRewardGranted, ("offer", o.Id));
                if (Services.TryGet(out SaveManager save)) save.RequestSave();
            }, () =>
            {
                Analytics.Log(AnalyticsEvents.RvFailed, ("offer", o.Id));
                failed?.Invoke();
            });
            return true;
        }

        // ------------------------------------------------------------------ order complete

        void OnContract(ContractStatus status)
        {
            if (!status.Completed) return;
            Analytics.Log(AnalyticsEvents.ContractCompleted, ("station", status.StationId), ("reward", status.Reward));
            if (status.Reward < minDoubleReward || !Ready(doubleTruckOffer) || !Services.TryGet(out PopupManager popups)) return;
            long reward = status.Reward;
            Analytics.Log(AnalyticsEvents.RvOffered, ("offer", doubleTruckOffer.Id));
            popups.Show(new PopupRequest
            {
                Style = PopupStyle.Reward,
                Title = "ORDER COMPLETE!",
                Subtitle = "PAID ON THE DOCK PALLET",
                Icon = status.Icon != null ? status.Icon : cashIcon,
                Amount = "$" + CurrencyFormat.Short(reward),
                AmountColor = new Color(0.62f, 1f, 0.5f),
                CountTo = reward,
                AmountFormat = x => "$" + CurrencyFormat.Short(x),
                PrimaryLabel = "NICE!",
                SecondaryLabel = "2X",
                SecondarySub = "+$" + CurrencyFormat.Short(reward),
                SecondaryIcon = videoIcon,
                SecondaryIsBetter = true,
                Delay = 0.8f,
                MaxWait = 8f,
                OnSecondary = origin => Watch(doubleTruckOffer, () =>
                {
                    Analytics.Log(AnalyticsEvents.ContractDoubled, ("reward", reward));
                    if (Services.TryGet(out EconomyManager economy)) economy.AddCash(reward, origin);
                })
            });
        }

        // ------------------------------------------------------------------ rotating HUD offer

        void Update()
        {
            if (Current != null)
            {
                if (Time.time >= expiresAt) Clear();
                return;
            }

            if (Time.time < nextOfferAt || offers == null) return;
            bool blocked = ContextualVisible || (Services.TryGet(out PopupManager popups) && popups.IsShowing);
            var pick = blocked ? null : Pick();
            if (pick != null) Show(pick);
            else nextOfferAt = Time.time + 10f;
        }

        AdOfferDefinition Pick()
        {
            int level = Services.TryGet(out ProgressionManager progression) ? progression.Level : 1;
            Services.TryGet(out BoostManager boosts);
            int best = int.MinValue;
            foreach (var o in offers)
                if (Eligible(o, level, boosts)) best = Mathf.Max(best, o.Priority);
            if (best == int.MinValue) return null;

            float total = 0f;
            foreach (var o in offers)
                if (Eligible(o, level, boosts) && o.Priority == best) total += o.Weight;
            float roll = UnityEngine.Random.value * total;
            foreach (var o in offers)
            {
                if (!Eligible(o, level, boosts) || o.Priority != best) continue;
                roll -= o.Weight;
                if (roll <= 0f) return o;
            }

            return null;
        }

        bool Eligible(AdOfferDefinition o, int level, BoostManager boosts)
        {
            if (o == null || o.Weight <= 0f || level < o.MinLevel || !Ready(o)) return false;
            if (o.Kind == AdOfferKind.Boost && (o.Boost == null || boosts == null || boosts.IsActive(o.Boost.Kind))) return false;
            return o.Kind is AdOfferKind.Boost or AdOfferKind.FreeCash;
        }

        void Show(AdOfferDefinition offer)
        {
            Current = offer;
            expiresAt = Time.time + offer.ShowSeconds;
            // a shown offer cools down even if ignored, so the same one does not come straight back
            readyAt[offer.Id] = Now + (long)(offer.ShowSeconds + offer.Cooldown);
            Analytics.Log(AnalyticsEvents.RvOffered, ("offer", offer.Id));
        }

        void Clear()
        {
            Current = null;
            nextOfferAt = Time.time + quietSeconds;
        }

        long FreeCash(AdOfferDefinition offer) => offer.CashPerLevel * (Services.TryGet(out ProgressionManager progression) ? progression.Level : 1);

        /// <summary>The player tapped the HUD offer: play the video, then pay out.</summary>
        public void Accept()
        {
            var offer = Current;
            if (offer == null) return;
            long cash = CurrentCash;
            Clear();
            readyAt.Remove(offer.Id);   // the shown-offer cooldown gives way to the watched-offer cooldown
            Watch(offer, () =>
            {
                switch (offer.Kind)
                {
                    case AdOfferKind.Boost:
                        if (Services.TryGet(out BoostManager boosts)) boosts.Activate(offer.Boost);
                        break;
                    case AdOfferKind.FreeCash:
                        if (cash > 0 && Services.TryGet(out EconomyManager economy)) economy.AddCash(cash);
                        break;
                }
            });
        }
    }
}

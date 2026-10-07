using System;
using ScrapYardKing.Core;
using UnityEngine;

namespace ScrapYardKing.Economy
{
    /// <summary>Wallet for soft (cash) and premium currency. The only place balances change.</summary>
    [DefaultExecutionOrder(-600)]
    public sealed class EconomyManager : ServiceBehaviour<EconomyManager>, ISaveable
    {
        [Serializable]
        sealed class State
        {
            public long cash, premium;
        }

        [SerializeField] EconomyConfig config;

        long cash, premium;

        /// <summary>(new balance, delta, where the cash came from: a world position, a screen point or nowhere).</summary>
        public event Action<long, long, CurrencyOrigin> CashChanged;
        /// <summary>(new balance, delta, where the diamonds came from).</summary>
        public event Action<long, long, CurrencyOrigin> PremiumChanged;

        public long Cash => cash;
        public long Premium => premium;
        public EconomyConfig Config => config;

        protected override void Awake()
        {
            base.Awake();
            if (config == null)
            {
                Debug.LogError("[EconomyManager] EconomyConfig is not assigned.", this);
                return;
            }

            cash = config.StartingCash;
            premium = config.StartingPremium;
        }

        void Start() => SaveRegistry.Register(this);

        protected override void OnDestroy()
        {
            base.OnDestroy();
            SaveRegistry.Unregister(this);
        }

        string ISaveable.SaveKey => "economy";

        string ISaveable.CaptureState() => JsonUtility.ToJson(new State { cash = cash, premium = premium });

        void ISaveable.RestoreState(string state)
        {
            var s = JsonUtility.FromJson<State>(state);
            cash = Math.Max(0, s.cash);
            premium = Math.Max(0, s.premium);
            CashChanged?.Invoke(cash, 0, default);
            PremiumChanged?.Invoke(premium, 0, default);
        }

        public void AddCash(long amount, Vector3? worldSource = null) =>
            AddCash(amount, worldSource.HasValue ? CurrencyOrigin.FromWorld(worldSource.Value) : default);

        /// <summary>Adds cash that comes from a UI element (a reward popup): the HUD flies coins from that point.</summary>
        public void AddCash(long amount, CurrencyOrigin origin)
        {
            if (amount <= 0) return;
            cash += amount;
            CashChanged?.Invoke(cash, amount, origin);
            GameEvents.RaiseCashEarned(amount);
        }

        public bool CanAfford(long amount) => cash >= amount;

        public bool TrySpendCash(long amount)
        {
            if (amount < 0 || cash < amount) return false;
            cash -= amount;
            CashChanged?.Invoke(cash, -amount, default);
            return true;
        }

        public bool CanAffordPremium(long amount) => premium >= amount;

        /// <summary>
        /// Adds diamonds. <paramref name="reason"/> names the source for analytics ("milestone:recycling_plant",
        /// "task:t20_plant", "iap:gems_500"). Every diamond enters the game through here.
        /// </summary>
        public void AddPremium(long amount, string reason, CurrencyOrigin origin = default)
        {
            if (amount <= 0) return;
            premium += amount;
            PremiumChanged?.Invoke(premium, amount, origin);
            Analytics.Log(AnalyticsEvents.DiamondEarned, ("amount", amount), ("reason", reason), ("balance", premium));
            if (Services.TryGet(out Persistence.SaveManager save)) save.RequestSave();
        }

        /// <summary>Spends diamonds once, or nothing at all. Every diamond leaves the game through here.</summary>
        public bool TrySpendPremium(long amount, string reason)
        {
            if (amount <= 0 || premium < amount) return false;
            premium -= amount;
            PremiumChanged?.Invoke(premium, -amount, default);
            Analytics.Log(AnalyticsEvents.DiamondSpent, ("amount", amount), ("reason", reason), ("balance", premium));
            if (Services.TryGet(out Persistence.SaveManager save)) save.Save();
            return true;
        }
    }
}

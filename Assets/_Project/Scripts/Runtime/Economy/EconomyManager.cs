using System;
using ScrapYardKing.Core;
using UnityEngine;

namespace ScrapYardKing.Economy
{
    /// <summary>Wallet for soft (cash) and premium currency. The only place balances change.</summary>
    [DefaultExecutionOrder(-600)]
    public sealed class EconomyManager : ServiceBehaviour<EconomyManager>
    {
        [SerializeField] EconomyConfig config;

        long cash, premium;

        /// <summary>(new balance, delta, world position the cash came from or null).</summary>
        public event Action<long, long, Vector3?> CashChanged;
        public event Action<long, long> PremiumChanged;

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

        public void AddCash(long amount, Vector3? worldSource = null)
        {
            if (amount <= 0) return;
            cash += amount;
            CashChanged?.Invoke(cash, amount, worldSource);
            GameEvents.RaiseCashEarned(amount);
        }

        public bool CanAfford(long amount) => cash >= amount;

        public bool TrySpendCash(long amount)
        {
            if (amount < 0 || cash < amount) return false;
            cash -= amount;
            CashChanged?.Invoke(cash, -amount, null);
            return true;
        }

        public void AddPremium(long amount)
        {
            if (amount <= 0) return;
            premium += amount;
            PremiumChanged?.Invoke(premium, amount);
        }
    }
}

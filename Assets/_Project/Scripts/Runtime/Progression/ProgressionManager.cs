using System;
using ScrapYardKing.Core;
using ScrapYardKing.Economy;
using ScrapYardKing.Feedback;
using UnityEngine;

namespace ScrapYardKing.Progression
{
    /// <summary>Yard XP and level. Listens to gameplay events; nothing calls it except tasks granting rewards.</summary>
    [DefaultExecutionOrder(-450)]
    public sealed class ProgressionManager : ServiceBehaviour<ProgressionManager>
    {
        [SerializeField] ProgressionConfig config;

        float xpFromCash;

        public event Action<int, Vector3?> XpGained;
        public event Action<int> LevelChanged;

        public int Level { get; private set; } = 1;
        public int Xp { get; private set; }
        public int XpToNext => config != null ? config.XpToNext(Level) : 1;
        public float Progress01 => Mathf.Clamp01(Xp / (float)XpToNext);
        public ProgressionConfig Config => config;

        void OnEnable()
        {
            GameEvents.ScrapBroken += OnScrapBroken;
            GameEvents.ItemsProcessed += OnItemsProcessed;
            GameEvents.ItemsSold += OnItemsSold;
            GameEvents.UpgradePurchased += OnUpgradePurchased;
            GameEvents.WorkerHired += OnWorkerHired;
        }

        void OnDisable()
        {
            GameEvents.ScrapBroken -= OnScrapBroken;
            GameEvents.ItemsProcessed -= OnItemsProcessed;
            GameEvents.ItemsSold -= OnItemsSold;
            GameEvents.UpgradePurchased -= OnUpgradePurchased;
            GameEvents.WorkerHired -= OnWorkerHired;
        }

        public void AddXp(int amount, Vector3? source = null)
        {
            if (amount <= 0 || config == null || Level >= config.MaxLevel) return;

            Xp += amount;
            XpGained?.Invoke(amount, source);
            while (Level < config.MaxLevel && Xp >= XpToNext)
            {
                Xp -= XpToNext;
                Level++;
                OnLevelUp();
            }
        }

        void OnLevelUp()
        {
            if (Services.TryGet(out EconomyManager economy)) economy.AddCash(config.CashRewardForLevel(Level));
            GameFeedback.Sfx(config.LevelUpSfx);
            LevelChanged?.Invoke(Level);
            GameEvents.RaiseLevelUp(Level);
        }

        void OnScrapBroken(ScrapBrokenEvent e)
        {
            if (e.Definition != null) AddXp(e.Definition.XpReward, e.Position + Vector3.up);
        }

        void OnItemsProcessed(ItemsProcessedEvent e)
        {
            if (config != null) AddXp(e.InputCount * config.XpPerItemProcessed);
        }

        void OnItemsSold(Items.ItemDefinition item, int units, long cash)
        {
            if (config == null || units <= 0) return;
            // Fractional XP from cash carries over between sales so small sales still count.
            xpFromCash += cash * config.XpPerCashSold;
            int whole = (int)xpFromCash;
            xpFromCash -= whole;
            AddXp(config.XpPerSale + whole);
        }

        void OnUpgradePurchased(string id, int level)
        {
            if (config != null) AddXp(config.XpPerUpgrade);
        }

        void OnWorkerHired(string id, int total)
        {
            if (config != null) AddXp(config.XpPerWorkerHired);
        }
    }
}

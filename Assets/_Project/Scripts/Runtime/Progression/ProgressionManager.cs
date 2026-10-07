using System;
using ScrapYardKing.Core;
using ScrapYardKing.Economy;
using ScrapYardKing.Feedback;
using UnityEngine;

namespace ScrapYardKing.Progression
{
    /// <summary>Yard XP and level. Listens to gameplay events; nothing calls it except tasks granting rewards.</summary>
    [DefaultExecutionOrder(-450)]
    public sealed class ProgressionManager : ServiceBehaviour<ProgressionManager>, ISaveable
    {
        [Serializable]
        sealed class State
        {
            public int level = 1, xp;
            public float xpFromCash;
        }

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

        void Start() => SaveRegistry.Register(this);

        protected override void OnDestroy()
        {
            base.OnDestroy();
            SaveRegistry.Unregister(this);
        }

        string ISaveable.SaveKey => "progression";

        string ISaveable.CaptureState() => JsonUtility.ToJson(new State { level = Level, xp = Xp, xpFromCash = xpFromCash });

        void ISaveable.RestoreState(string state)
        {
            var s = JsonUtility.FromJson<State>(state);
            int max = config != null ? config.MaxLevel : s.level;
            Level = Mathf.Clamp(s.level, 1, Mathf.Max(1, max));
            Xp = Mathf.Clamp(s.xp, 0, Mathf.Max(0, XpToNext - 1));
            xpFromCash = Mathf.Clamp01(s.xpFromCash);
            // No level-up reward or jingle: the level was earned in an earlier session.
            LevelChanged?.Invoke(Level);
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

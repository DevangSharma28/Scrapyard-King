using System;
using System.Collections.Generic;
using ScrapYardKing.Core;
using ScrapYardKing.Economy;
using ScrapYardKing.Persistence;
using UnityEngine;

namespace ScrapYardKing.UI
{
    /// <summary>
    /// The free diamond sources of <see cref="PremiumEconomyConfig"/>: a one-time gift when an area opens and a few
    /// diamonds on some level-ups. Each gift becomes a reward popup; the diamonds enter the wallet when the player taps
    /// CLAIM and fly from the popup to the HUD. A gift not yet claimed is saved and offered again on the next start,
    /// and a gift is recorded as given before it is offered, so it can never be paid twice. The very first gift of a
    /// save carries one line that says what diamonds are for.
    /// </summary>
    [DefaultExecutionOrder(-515)]
    public sealed class DiamondRewards : ServiceBehaviour<DiamondRewards>, ISaveable
    {
        [Serializable]
        sealed class Gift
        {
            public string id;
            public int diamonds;
            public string title, line;
        }

        [Serializable]
        sealed class State
        {
            public List<string> given = new();
            public List<Gift> pending = new();
            public bool everEarned;
        }

        [SerializeField] PremiumEconomyConfig config;
        [SerializeField] Sprite diamondIcon;
        [Tooltip("Seconds to wait after an area opens before the gift appears (its reveal plays first).")]
        [SerializeField, Min(0f)] float afterReveal = 3f;

        State state = new();

        public PremiumEconomyConfig Config => config;
        public Sprite DiamondIcon => diamondIcon;
        /// <summary>The player has seen diamonds (the HUD shows the diamond counter from then on).</summary>
        public bool DiamondsRevealed => state.everEarned || (Services.TryGet(out EconomyManager e) && e.Premium > 0);

        public event Action Revealed;

        void OnEnable()
        {
            GameEvents.ExpansionOpened += OnExpansion;
            GameEvents.LevelUp += OnLevelUp;
        }

        void OnDisable()
        {
            GameEvents.ExpansionOpened -= OnExpansion;
            GameEvents.LevelUp -= OnLevelUp;
        }

        void Start()
        {
            SaveRegistry.Register(this);
            foreach (var gift in state.pending) Offer(gift, 1f);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            SaveRegistry.Unregister(this);
        }

        string ISaveable.SaveKey => "diamond_rewards";
        string ISaveable.CaptureState() => JsonUtility.ToJson(state);

        void ISaveable.RestoreState(string json)
        {
            state = JsonUtility.FromJson<State>(json) ?? new State();
            state.given ??= new List<string>();
            state.pending ??= new List<Gift>();
        }

        void OnExpansion(string id)
        {
            if (SaveRegistry.IsRestoring || config == null) return;
            var m = config.MilestoneFor(id);
            if (m != null) Grant("area:" + id, m.diamonds, m.title, m.line, afterReveal);
        }

        void OnLevelUp(int level)
        {
            if (SaveRegistry.IsRestoring || config == null) return;
            int diamonds = config.LevelUpReward(level);
            if (diamonds > 0) Grant("level:" + level, diamonds, "LEVEL BONUS", $"YARD LEVEL {level}", 1.5f);
        }

        void Grant(string id, int diamonds, string title, string line, float delay)
        {
            if (state.given.Contains(id)) return;
            state.given.Add(id);
            var gift = new Gift { id = id, diamonds = diamonds, title = title, line = line };
            state.pending.Add(gift);
            if (Services.TryGet(out SaveManager save)) save.RequestSave();
            Offer(gift, delay);
        }

        void Offer(Gift gift, float delay)
        {
            if (!Services.TryGet(out PopupManager popups))
            {
                Claim(gift, default);
                return;
            }

            bool first = !state.everEarned && !(Services.TryGet(out EconomyManager e) && e.Premium > 0);
            popups.Show(new PopupRequest
            {
                Style = PopupStyle.Reward,
                Title = gift.title,
                Subtitle = first && config != null ? config.FirstDiamondLesson : gift.line,
                Icon = diamondIcon,
                SpinIcon = true,
                Amount = "+" + gift.diamonds,
                AmountColor = new Color(0.75f, 0.9f, 1f),
                CountTo = gift.diamonds,
                AmountFormat = x => "+" + (long)Math.Round(x),
                PrimaryLabel = "CLAIM",
                Delay = delay,
                OnPrimary = origin => Claim(gift, origin)
            });
        }

        void Claim(Gift gift, CurrencyOrigin origin)
        {
            if (!state.pending.Remove(gift)) return;   // claimed already
            bool first = !state.everEarned;
            state.everEarned = true;
            if (Services.TryGet(out EconomyManager economy)) economy.AddPremium(gift.diamonds, gift.id, origin);
            if (first) Revealed?.Invoke();
        }
    }
}

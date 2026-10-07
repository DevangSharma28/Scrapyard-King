using System;
using UnityEngine;

namespace ScrapYardKing.Economy
{
    /// <summary>
    /// Every diamond number in one place: what time and cash are worth in diamonds, when a spend asks "are you sure",
    /// and the free sources (milestones, level-ups). UI and gameplay read prices from here and never hard-code one.
    /// Diamonds buy time and convenience, never power: anything a diamond does, playing does too.
    /// Model and reasoning: <c>Docs/ECONOMY.md</c>, "Diamonds".
    /// </summary>
    [CreateAssetMenu(fileName = "PremiumEconomy", menuName = "Scrap Yard King/Economy/Premium Economy Config")]
    public sealed class PremiumEconomyConfig : ScriptableObject
    {
        /// <summary>A one-time diamond gift for reaching a point in the game.</summary>
        [Serializable]
        public sealed class Milestone
        {
            [Tooltip("Expansion id that triggers it (ExpansionOpened).")]
            public string expansionId;
            [Min(1)] public int diamonds = 10;
            [Tooltip("Popup title, e.g. NEW AREA BONUS.")]
            public string title = "BONUS!";
            [Tooltip("Second line of the popup.")]
            public string line;
        }

        [Header("Time is worth diamonds")]
        [Tooltip("Cheapest skip, however short the wait.")]
        [SerializeField, Min(1)] int minSkipCost = 2;
        [Tooltip("Diamonds per minute skipped, before the bulk discount.")]
        [SerializeField, Min(0.1f)] float diamondsPerMinute = 3f;
        [Tooltip("Below 1, long skips cost less per minute (cost = perMinute * minutes ^ exponent).")]
        [SerializeField, Range(0.5f, 1f)] float bulkExponent = 0.85f;
        [SerializeField, Min(1)] int maxSkipCost = 400;

        [Header("Cash is worth diamonds")]
        [Tooltip("Cash one diamond stands for at yard level 1 (premium actions that create cash, e.g. a special order).")]
        [SerializeField, Min(1)] long cashPerDiamond = 60;
        [Tooltip("x per yard level, so a diamond keeps its worth as income grows.")]
        [SerializeField, Min(1f)] float cashPerDiamondGrowth = 1.3f;
        [Tooltip("Up to this level income grows as fast as the levels (the first-session arc)...")]
        [SerializeField, Min(1)] int cashGrowthUntilLevel = 12;
        [Tooltip("...after it levels come faster than income, so a diamond's cash grows only this much per level. " +
                 "Without it a Lv 25 diamond bought 2.7 minutes of income instead of about 0.2 (EconomySimulator).")]
        [SerializeField, Min(1f)] float lateCashGrowth = 1.08f;

        [Header("Confirmation")]
        [Tooltip("Spends above this many diamonds ask first; smaller ones happen on the tap.")]
        [SerializeField, Min(0)] int confirmAbove = 20;

        [Header("Truck skips")]
        [Tooltip("FINISH NOW shows when at least this share of the order is still missing...")]
        [SerializeField, Range(0f, 1f)] float finishMinMissing = 0.25f;
        [Tooltip("...and the truck has waited at least this long, seconds...")]
        [SerializeField, Min(0f)] float finishMinDocked = 20f;
        [Tooltip("...and loading has begun: at least this share is on the bed. A video never fills an empty truck.")]
        [SerializeField, Range(0f, 1f)] float finishMinLoaded = 0.25f;
        [Tooltip("BRING NOW shows while the next truck is at least this far away, seconds.")]
        [SerializeField, Min(0f)] float callMinSeconds = 15f;

        [Header("Free sources")]
        [SerializeField] Milestone[] milestones;
        [Tooltip("Diamonds on a level-up (from levelUpFrom, every levelUpEvery levels). 0 = none.")]
        [SerializeField, Min(0)] int levelUpDiamonds = 5;
        [SerializeField, Min(1)] int levelUpFrom = 6;
        [SerializeField, Min(1)] int levelUpEvery = 2;
        [Tooltip("Shown under the very first diamond reward of a save.")]
        [SerializeField] string firstDiamondLesson = "DIAMONDS SPEED THINGS UP";

        public int ConfirmAbove => confirmAbove;
        public float FinishMinMissing => finishMinMissing;
        public float FinishMinDocked => finishMinDocked;
        public float FinishMinLoaded => finishMinLoaded;
        public float CallMinSeconds => callMinSeconds;
        public Milestone[] Milestones => milestones ?? Array.Empty<Milestone>();
        public string FirstDiamondLesson => firstDiamondLesson;

        /// <summary>Diamonds to skip <paramref name="seconds"/> of waiting.</summary>
        public int SkipCost(float seconds)
        {
            if (seconds <= 0f) return 0;
            float minutes = seconds / 60f;
            int cost = Mathf.CeilToInt(diamondsPerMinute * Mathf.Pow(minutes, bulkExponent));
            return Mathf.Clamp(cost, minSkipCost, maxSkipCost);
        }

        /// <summary>Cash one diamond is worth at <paramref name="level"/>.</summary>
        public long CashPerDiamond(int level)
        {
            int early = Mathf.Clamp(level, 1, cashGrowthUntilLevel) - 1;
            int late = Mathf.Max(0, level - cashGrowthUntilLevel);
            return (long)Math.Round(cashPerDiamond * Math.Pow(cashPerDiamondGrowth, early) * Math.Pow(lateCashGrowth, late));
        }

        /// <summary>Diamonds for a premium action worth <paramref name="cash"/> at <paramref name="level"/>.</summary>
        public int CashCost(long cash, int level) =>
            cash <= 0 ? 0 : Mathf.Max(1, (int)Math.Ceiling(cash / (double)CashPerDiamond(level)));

        /// <summary>Diamonds for reaching <paramref name="level"/> (0 if that level gives none).</summary>
        public int LevelUpReward(int level) =>
            levelUpDiamonds > 0 && level >= levelUpFrom && (level - levelUpFrom) % levelUpEvery == 0 ? levelUpDiamonds : 0;

        public bool NeedsConfirm(int diamonds) => diamonds > confirmAbove;

        public Milestone MilestoneFor(string expansionId)
        {
            foreach (var m in Milestones)
                if (m != null && m.expansionId == expansionId) return m;
            return null;
        }
    }
}

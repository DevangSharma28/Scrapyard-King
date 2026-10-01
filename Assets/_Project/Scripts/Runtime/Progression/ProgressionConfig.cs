using ScrapYardKing.Feedback;
using UnityEngine;
using UnityEngine.Serialization;

namespace ScrapYardKing.Progression
{
    /// <summary>
    /// Yard level curve and XP sources. Blueprint target: Yard Lv.10 at the end of the 90-minute session, reached
    /// from cutting, processing, selling, upgrading and tasks.
    /// </summary>
    [CreateAssetMenu(fileName = "ProgressionConfig", menuName = "Scrap Yard King/Progression/Progression Config")]
    public sealed class ProgressionConfig : ScriptableObject
    {
        [Header("Level curve")]
        [Tooltip("XP needed to go from level 1 to 2.")]
        [SerializeField, Min(1)] int baseXp = 50;
        [Tooltip("Each level needs this much more XP than the previous one.")]
        [SerializeField, Min(1f)] float growth = 1.32f;
        [SerializeField, Min(2)] int maxLevel = 30;

        [Header("XP sources (scrap XP lives on each ScrapDefinition)")]
        [SerializeField, Min(0)] int xpPerItemProcessed = 1;
        [Tooltip("XP per customer served (one sale), regardless of how many units they bought.")]
        [FormerlySerializedAs("xpPerUnitSold")]
        [SerializeField, Min(0)] int xpPerSale = 2;
        [SerializeField, Min(0)] int xpPerUpgrade = 10;
        [SerializeField, Min(0)] int xpPerWorkerHired = 25;

        [Header("Level-up reward")]
        [SerializeField, Min(0)] int cashPerLevel = 25;
        [SerializeField] SfxDefinition levelUpSfx;

        public int MaxLevel => maxLevel;
        public int XpPerItemProcessed => xpPerItemProcessed;
        public int XpPerSale => xpPerSale;
        public int XpPerUpgrade => xpPerUpgrade;
        public int XpPerWorkerHired => xpPerWorkerHired;
        public SfxDefinition LevelUpSfx => levelUpSfx;

        /// <summary>XP needed to advance from <paramref name="level"/> to the next one.</summary>
        public int XpToNext(int level) => Mathf.Max(1, Mathf.RoundToInt(baseXp * Mathf.Pow(growth, Mathf.Max(0, level - 1))));

        public long CashRewardForLevel(int level) => (long)cashPerLevel * level;
    }
}

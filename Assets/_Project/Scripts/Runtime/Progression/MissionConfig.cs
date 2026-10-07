using System;
using ScrapYardKing.Boosts;
using UnityEngine;

namespace ScrapYardKing.Progression
{
    /// <summary>A lifetime count the game keeps for missions and achievements (fed by <see cref="Core.GameEvents"/>).</summary>
    public enum MissionStat
    {
        ScrapBroken,
        ItemsCollected,
        ItemsProcessed,
        ItemsSold,
        CashEarned,
        CustomersServed,
        Overdrives,
        OrdersCompleted,
        UpgradesBought,
        WorkersHired,
        AreasOpened
    }

    /// <summary>What a mission, achievement tier or daily reward pays. Cash scales with the yard level so it stays worth having.</summary>
    [Serializable]
    public sealed class MissionReward
    {
        [Min(0)] public long cashPerLevel;
        [Min(0)] public int diamonds;
        public BoostDefinition boost;
        [Min(0f)] public float boostMinutes;

        public long Cash(int level) => cashPerLevel * Mathf.Max(1, level);
        public bool IsEmpty => cashPerLevel <= 0 && diamonds <= 0 && boost == null;
    }

    /// <summary>
    /// Daily missions and achievements (MissionRewardConfig in the brief). Three dailies a day are drawn from the pool
    /// (by the UTC day, so everyone gets the same three on the same day); targets grow with the yard level. Achievements
    /// are tiers over lifetime counts. Every reward is listed before the player starts, and every number is here.
    /// </summary>
    [CreateAssetMenu(fileName = "MissionConfig", menuName = "Scrap Yard King/Progression/Mission Config")]
    public sealed class MissionConfig : ScriptableObject
    {
        [Serializable]
        public sealed class DailyMission
        {
            public string id;
            [Tooltip("{0} = the target, e.g. BREAK {0} SCRAP.")]
            public string title;
            public Sprite icon;
            public MissionStat stat;
            [Min(1)] public int baseTarget = 10;
            [Min(0)] public int targetPerLevel;
            [Min(1)] public int minLevel = 1;
            public MissionReward reward = new();

            public long Target(int level) => baseTarget + (long)targetPerLevel * Mathf.Max(0, level - 1);
        }

        [Serializable]
        public sealed class Achievement
        {
            public string id;
            [Tooltip("{0} = the tier's target.")]
            public string title;
            public Sprite icon;
            public MissionStat stat;
            public long[] targets;
            public int[] diamonds;
        }

        [SerializeField, Min(1)] int dailyCount = 3;
        [SerializeField] DailyMission[] dailyPool;
        [SerializeField] Achievement[] achievements;

        public int DailyCount => dailyCount;
        public DailyMission[] DailyPool => dailyPool ?? Array.Empty<DailyMission>();
        public Achievement[] Achievements => achievements ?? Array.Empty<Achievement>();

        public DailyMission FindDaily(string id)
        {
            foreach (var m in DailyPool)
                if (m != null && m.id == id) return m;
            return null;
        }
    }
}

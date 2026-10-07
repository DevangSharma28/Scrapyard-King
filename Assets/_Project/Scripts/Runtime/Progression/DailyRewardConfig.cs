using System;
using UnityEngine;

namespace ScrapYardKing.Progression
{
    /// <summary>The 7-day login calendar. Day 7 is the big one. A missed day does not reset the cycle.</summary>
    [CreateAssetMenu(fileName = "DailyRewardConfig", menuName = "Scrap Yard King/Progression/Daily Reward Config")]
    public sealed class DailyRewardConfig : ScriptableObject
    {
        [Serializable]
        public sealed class Day
        {
            public string title = "DAY";
            public Sprite icon;
            public MissionReward reward = new();
        }

        [SerializeField] Day[] days = new Day[7];

        public Day[] Days => days ?? Array.Empty<Day>();
    }
}

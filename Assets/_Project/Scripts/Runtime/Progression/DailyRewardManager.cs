using System;
using ScrapYardKing.Core;
using ScrapYardKing.Economy;
using UnityEngine;

namespace ScrapYardKing.Progression
{
    /// <summary>
    /// The 7-day login calendar (<see cref="DailyRewardConfig"/>). One claim per UTC day; the next claim is the next day
    /// of the cycle, and a missed day does not reset it (no streak to lose, nothing to feel bad about). The claim is
    /// recorded before the reward is paid.
    /// </summary>
    [DefaultExecutionOrder(-504)]
    public sealed class DailyRewardManager : ServiceBehaviour<DailyRewardManager>, ISaveable
    {
        [Serializable]
        sealed class State
        {
            public int next;
            public int lastDay = -1;
        }

        [SerializeField] DailyRewardConfig config;

        State state = new();

        public DailyRewardConfig Config => config;
        /// <summary>Index (0..6) of the day the next claim pays.</summary>
        public int NextDay => config == null || config.Days.Length == 0 ? 0 : state.next % config.Days.Length;
        public bool ClaimedToday => state.lastDay == MissionManager.Today;
        public bool CanClaim => config != null && config.Days.Length > 0 && !ClaimedToday;
        /// <summary>Index of the day claimed today, or -1.</summary>
        public int ClaimedTodayIndex => ClaimedToday && config.Days.Length > 0 ? (state.next + config.Days.Length - 1) % config.Days.Length : -1;

        public event Action Changed;

        void Start() => SaveRegistry.Register(this);

        protected override void OnDestroy()
        {
            base.OnDestroy();
            SaveRegistry.Unregister(this);
        }

        string ISaveable.SaveKey => "daily_reward";
        string ISaveable.CaptureState() => JsonUtility.ToJson(state);

        void ISaveable.RestoreState(string json)
        {
            state = JsonUtility.FromJson<State>(json) ?? new State();
            Changed?.Invoke();
        }

        public bool Claim(CurrencyOrigin origin)
        {
            if (!CanClaim) return false;
            int day = NextDay;
            state.lastDay = MissionManager.Today;
            state.next = (state.next + 1) % config.Days.Length;
            MissionManager.Pay(config.Days[day].reward, origin, "daily_reward:" + (day + 1));
            Changed?.Invoke();
            return true;
        }
    }
}

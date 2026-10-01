using UnityEngine;

namespace ScrapYardKing.Progression
{
    public enum TaskType
    {
        /// <summary>Break N scrap objects (targetId = scrap definition id, empty = any).</summary>
        BreakScrap,
        /// <summary>Pick up N loose items from the ground (targetId = item id).</summary>
        CollectItems,
        /// <summary>Process N inputs (targetId = machine id).</summary>
        ProcessItems,
        /// <summary>Deliver N items into a station (targetId = station id).</summary>
        DeliverItems,
        /// <summary>Sell N units (targetId = item id, empty = any).</summary>
        SellItems,
        /// <summary>Earn N cash from any source.</summary>
        EarnCash,
        /// <summary>Buy N levels of an upgrade (targetId = upgrade id, empty = any).</summary>
        PurchaseUpgrade,
        /// <summary>Have an upgrade at level N or higher (targetId = upgrade id).</summary>
        ReachUpgradeLevel,
        /// <summary>Have N workers of a kind hired (targetId = worker id).</summary>
        HireWorker,
        /// <summary>Reach yard level N.</summary>
        ReachLevel,
        /// <summary>Serve N customers to completion (targetId = sell desk id, empty = any).</summary>
        ServeCustomers,
        /// <summary>Start Active Overdrive N times (targetId = machine id, empty = any).</summary>
        Overdrive
    }

    public enum TaskCategory
    {
        /// <summary>Drives progression; exactly one is active at a time.</summary>
        Main,
        /// <summary>Optional cash/XP bonus.</summary>
        Side,
        /// <summary>Premium currency, refreshed daily (needs save/load, milestone 8).</summary>
        Daily
    }

    /// <summary>One objective (blueprint task system: main, side, daily, stage milestone).</summary>
    [CreateAssetMenu(fileName = "Task_", menuName = "Scrap Yard King/Progression/Task Definition")]
    public sealed class TaskDefinition : ScriptableObject
    {
        [SerializeField] string id;
        [Tooltip("Shown on the banner. {0} = target amount.")]
        [SerializeField] string title = "Cut {0} scrap";
        [SerializeField] TaskCategory category;
        [SerializeField] TaskType type;
        [SerializeField] string targetId;
        [SerializeField, Min(1)] long amount = 1;

        [Header("Rewards")]
        [SerializeField, Min(0)] long rewardCash;
        [SerializeField, Min(0)] int rewardXp = 10;
        [SerializeField, Min(0)] int rewardPremium;

        public string Id => id;
        public string Title => string.Format(title, amount);
        public TaskCategory Category => category;
        public TaskType Type => type;
        public string TargetId => targetId;
        public long Amount => amount;
        public long RewardCash => rewardCash;
        public int RewardXp => rewardXp;
        public int RewardPremium => rewardPremium;

        /// <summary>State tasks complete from current game state; the rest count events from when they start.</summary>
        public bool IsStateTask => type is TaskType.ReachUpgradeLevel or TaskType.HireWorker or TaskType.ReachLevel;

        public bool Targets(string candidateId) => string.IsNullOrEmpty(targetId) || targetId == candidateId;

        void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(id)) id = name;
        }
    }
}

using System;
using System.Collections.Generic;
using ScrapYardKing.Core;
using ScrapYardKing.Economy;
using ScrapYardKing.Items;
using UnityEngine;

namespace ScrapYardKing.Progression
{
    /// <summary>A task in progress.</summary>
    public sealed class ActiveTask
    {
        public ActiveTask(TaskDefinition definition) => Definition = definition;

        public TaskDefinition Definition { get; }
        public long Progress { get; internal set; }
        public bool IsComplete { get; internal set; }
        public float Progress01 => Mathf.Clamp01(Progress / (float)Mathf.Max(1, Definition.Amount));
    }

    /// <summary>
    /// Runs the main task chain (one at a time) and rotating side tasks. Progress comes only from
    /// <see cref="GameEvents"/>; rewards go through the economy and progression services.
    /// </summary>
    [DefaultExecutionOrder(-400)]
    public sealed class TaskManager : ServiceBehaviour<TaskManager>
    {
        [SerializeField] TaskChain chain;
        [Tooltip("Seconds the completed main task stays on screen before the next one starts.")]
        [SerializeField, Min(0f)] float nextTaskDelay = 1.6f;

        readonly List<ActiveTask> sideTasks = new();
        EconomyManager economy;
        ProgressionManager progression;
        UpgradeManager upgrades;
        int mainIndex = -1, sideCursor;
        float nextMainAt = -1f;

        public event Action<ActiveTask> TaskStarted;
        public event Action<ActiveTask> TaskProgressed;
        public event Action<ActiveTask> TaskCompleted;

        /// <summary>Current main task, or null between tasks and after the chain ends.</summary>
        public ActiveTask Main { get; private set; }
        public IReadOnlyList<ActiveTask> SideTasks => sideTasks;
        public int MainIndex => mainIndex;

        void OnEnable()
        {
            GameEvents.ScrapBroken += OnScrapBroken;
            GameEvents.ItemsCollected += OnItemsCollected;
            GameEvents.ItemsProcessed += OnItemsProcessed;
            GameEvents.ItemsDelivered += OnItemsDelivered;
            GameEvents.ItemsSold += OnItemsSold;
            GameEvents.CashEarned += OnCashEarned;
            GameEvents.UpgradePurchased += OnUpgradePurchased;
            GameEvents.WorkerHired += OnWorkerHired;
            GameEvents.LevelUp += OnLevelUp;
            GameEvents.CustomerServed += OnCustomerServed;
            GameEvents.MachineOverdrive += OnMachineOverdrive;
        }

        void OnDisable()
        {
            GameEvents.ScrapBroken -= OnScrapBroken;
            GameEvents.ItemsCollected -= OnItemsCollected;
            GameEvents.ItemsProcessed -= OnItemsProcessed;
            GameEvents.ItemsDelivered -= OnItemsDelivered;
            GameEvents.ItemsSold -= OnItemsSold;
            GameEvents.CashEarned -= OnCashEarned;
            GameEvents.UpgradePurchased -= OnUpgradePurchased;
            GameEvents.WorkerHired -= OnWorkerHired;
            GameEvents.LevelUp -= OnLevelUp;
            GameEvents.CustomerServed -= OnCustomerServed;
            GameEvents.MachineOverdrive -= OnMachineOverdrive;
        }

        void Start()
        {
            Services.TryGet(out economy);
            Services.TryGet(out progression);
            Services.TryGet(out upgrades);
            StartMain(0);
        }

        void Update()
        {
            if (nextMainAt >= 0f && Time.time >= nextMainAt)
            {
                nextMainAt = -1f;
                StartMain(mainIndex + 1);
            }
        }

        void StartMain(int index)
        {
            mainIndex = index;
            Main = null;
            if (chain == null || chain.MainTasks == null || index >= chain.MainTasks.Length) return;

            Main = new ActiveTask(chain.MainTasks[index]);
            TaskStarted?.Invoke(Main);
            EvaluateState(Main);
            RefillSideTasks();
        }

        void RefillSideTasks()
        {
            if (chain == null || chain.SideTasks == null || chain.SideTasks.Length == 0 || mainIndex < chain.SideTasksFromMainIndex) return;
            while (sideTasks.Count < chain.MaxActiveSideTasks)
            {
                var task = new ActiveTask(chain.SideTasks[sideCursor % chain.SideTasks.Length]);
                sideCursor++;
                sideTasks.Add(task);
                TaskStarted?.Invoke(task);
                EvaluateState(task);
            }
        }

        // ---------- event routing ----------

        void OnScrapBroken(ScrapBrokenEvent e) => Advance(TaskType.BreakScrap, e.Definition != null ? e.Definition.Id : null, 1);
        void OnItemsCollected(ItemDefinition item, int count) => Advance(TaskType.CollectItems, item != null ? item.Id : null, count);
        void OnItemsProcessed(ItemsProcessedEvent e) => Advance(TaskType.ProcessItems, e.MachineId, e.InputCount);
        void OnItemsDelivered(string stationId, ItemDefinition item, int count) => Advance(TaskType.DeliverItems, stationId, count);
        void OnItemsSold(ItemDefinition item, int units, long cash) => Advance(TaskType.SellItems, item != null ? item.Id : null, units);
        void OnCashEarned(long amount) => Advance(TaskType.EarnCash, null, amount);
        void OnCustomerServed(string stationId, int units) => Advance(TaskType.ServeCustomers, stationId, 1);
        void OnMachineOverdrive(string machineId) => Advance(TaskType.Overdrive, machineId, 1);

        void OnUpgradePurchased(string id, int level)
        {
            Advance(TaskType.PurchaseUpgrade, id, 1);
            EvaluateAllState();
        }

        void OnWorkerHired(string id, int total) => EvaluateAllState();
        void OnLevelUp(int level) => EvaluateAllState();

        void Advance(TaskType type, string id, long amount)
        {
            if (Main != null) Advance(Main, type, id, amount);
            for (int i = sideTasks.Count - 1; i >= 0; i--) Advance(sideTasks[i], type, id, amount);
        }

        void Advance(ActiveTask task, TaskType type, string id, long amount)
        {
            if (task.IsComplete || task.Definition.Type != type || task.Definition.IsStateTask || !task.Definition.Targets(id)) return;
            task.Progress = Math.Min(task.Definition.Amount, task.Progress + amount);
            TaskProgressed?.Invoke(task);
            if (task.Progress >= task.Definition.Amount) Complete(task);
        }

        void EvaluateAllState()
        {
            if (Main != null) EvaluateState(Main);
            for (int i = sideTasks.Count - 1; i >= 0; i--) EvaluateState(sideTasks[i]);
        }

        void EvaluateState(ActiveTask task)
        {
            if (task.IsComplete || !task.Definition.IsStateTask) return;
            var d = task.Definition;
            long value = d.Type switch
            {
                TaskType.ReachLevel => progression != null ? progression.Level : 1,
                TaskType.ReachUpgradeLevel => upgrades != null ? upgrades.LevelOf(d.TargetId) : 0,
                TaskType.HireWorker => upgrades != null ? upgrades.LevelOf(d.TargetId) : 0,
                _ => 0
            };

            if (value == task.Progress) return;
            task.Progress = Math.Min(d.Amount, value);
            TaskProgressed?.Invoke(task);
            if (task.Progress >= d.Amount) Complete(task);
        }

        void Complete(ActiveTask task)
        {
            task.IsComplete = true;
            var d = task.Definition;
            if (economy != null)
            {
                if (d.RewardCash > 0) economy.AddCash(d.RewardCash);
                if (d.RewardPremium > 0) economy.AddPremium(d.RewardPremium);
            }

            if (progression != null && d.RewardXp > 0) progression.AddXp(d.RewardXp);

            TaskCompleted?.Invoke(task);
            GameEvents.RaiseTaskCompleted(d.Id);

            if (task == Main) nextMainAt = Time.time + nextTaskDelay;
            else
            {
                sideTasks.Remove(task);
                RefillSideTasks();
            }
        }
    }
}

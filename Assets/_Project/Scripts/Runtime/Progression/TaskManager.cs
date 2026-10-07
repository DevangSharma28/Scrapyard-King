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
    public sealed class TaskManager : ServiceBehaviour<TaskManager>, ISaveable
    {
        [Serializable]
        sealed class TaskEntry
        {
            public string id;
            public long progress;
        }

        [Serializable]
        sealed class State
        {
            /// <summary>Id of the current main task (survives tasks being inserted into the chain); empty when the chain is done.</summary>
            public string mainId;
            public int mainIndex;
            public long mainProgress;
            public int sideCursor;
            public List<TaskEntry> side = new();
        }

        [SerializeField] TaskChain chain;
        [Tooltip("Seconds the completed main task stays on screen before the next one starts.")]
        [SerializeField, Min(0f)] float nextTaskDelay = 1.6f;

        readonly List<ActiveTask> sideTasks = new();
        // The next main task already counts while the finished one is still on screen, so nothing the player does in
        // that short gap is lost (e.g. a customer served right after "Stock the sell desk").
        ActiveTask pendingMain;
        EconomyManager economy;
        ProgressionManager progression;
        UpgradeManager upgrades;
        int mainIndex = -1, sideCursor, stateCheckFrames;
        float nextMainAt = -1f;
        bool restored;

        public event Action<ActiveTask> TaskStarted;
        public event Action<ActiveTask> TaskProgressed;
        public event Action<ActiveTask> TaskCompleted;
        /// <summary>The last main task was just finished (not raised when a save with a finished chain loads).</summary>
        public event Action ChainCompleted;

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
            SaveRegistry.Register(this);
            if (!restored) StartMain(0);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            SaveRegistry.Unregister(this);
        }

        string ISaveable.SaveKey => "tasks";

        string ISaveable.CaptureState()
        {
            var state = new State { sideCursor = sideCursor };
            // A finished task that is still on screen counts as done: save the one that follows it.
            var current = Main != null && Main.IsComplete ? pendingMain : Main;
            if (current != null)
            {
                state.mainId = current.Definition.Id;
                state.mainIndex = current == Main ? mainIndex : mainIndex + 1;
                state.mainProgress = current.Progress;
            }
            else
            {
                state.mainId = string.Empty;
                state.mainIndex = chain != null && chain.MainTasks != null ? chain.MainTasks.Length : 0;
            }

            foreach (var task in sideTasks)
                if (!task.IsComplete) state.side.Add(new TaskEntry { id = task.Definition.Id, progress = task.Progress });
            return JsonUtility.ToJson(state);
        }

        void ISaveable.RestoreState(string json)
        {
            var state = JsonUtility.FromJson<State>(json);
            var main = chain != null ? chain.MainTasks : null;
            if (state == null || main == null) return;

            int index = string.IsNullOrEmpty(state.mainId) ? -1 : Array.FindIndex(main, t => t != null && t.Id == state.mainId);
            bool sameTask = index >= 0;
            // The saved task no longer exists (chain edited): continue from the same position.
            if (index < 0) index = Mathf.Clamp(state.mainIndex, 0, main.Length);

            restored = true;
            sideCursor = Mathf.Max(0, state.sideCursor);
            sideTasks.Clear();
            if (chain.SideTasks != null && state.side != null)
                foreach (var entry in state.side)
                {
                    var definition = Array.Find(chain.SideTasks, t => t != null && t.Id == entry.id);
                    if (definition == null) continue;
                    sideTasks.Add(new ActiveTask(definition) { Progress = Math.Min(definition.Amount, Math.Max(0, entry.progress)) });
                }

            pendingMain = index < main.Length && sameTask
                ? new ActiveTask(main[index]) { Progress = Math.Min(main[index].Amount - 1, Math.Max(0, state.mainProgress)) }
                : null;
            StartMain(index);
            // Upgrades restore as their owners register (this frame and, for locked areas, the next): look at state tasks after that.
            stateCheckFrames = 3;
        }

        void Update()
        {
            if (stateCheckFrames > 0 && --stateCheckFrames == 0) EvaluateAllState();

            if (nextMainAt >= 0f && Time.time >= nextMainAt)
            {
                nextMainAt = -1f;
                StartMain(mainIndex + 1);
                if (Main == null) ChainCompleted?.Invoke();
            }
        }

        void StartMain(int index)
        {
            mainIndex = index;
            Main = null;
            if (chain == null || chain.MainTasks == null || index >= chain.MainTasks.Length)
            {
                pendingMain = null;
                return;
            }

            var definition = chain.MainTasks[index];
            Main = pendingMain != null && pendingMain.Definition == definition ? pendingMain : new ActiveTask(definition);
            pendingMain = null;
            TaskStarted?.Invoke(Main);
            if (!Main.Definition.IsStateTask && Main.Progress >= Main.Definition.Amount) Complete(Main);
            else EvaluateState(Main);
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
            if (pendingMain != null) AdvanceSilently(pendingMain, type, id, amount);
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

        static void AdvanceSilently(ActiveTask task, TaskType type, string id, long amount)
        {
            var d = task.Definition;
            if (d.Type != type || d.IsStateTask || !d.Targets(id)) return;
            task.Progress = Math.Min(d.Amount, task.Progress + amount);
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
                if (d.RewardPremium > 0) economy.AddPremium(d.RewardPremium, "task:" + d.Id);
            }

            if (progression != null && d.RewardXp > 0) progression.AddXp(d.RewardXp);

            TaskCompleted?.Invoke(task);
            GameEvents.RaiseTaskCompleted(d.Id);

            if (task == Main)
            {
                nextMainAt = Time.time + nextTaskDelay;
                int next = mainIndex + 1;
                pendingMain = chain != null && chain.MainTasks != null && next < chain.MainTasks.Length ? new ActiveTask(chain.MainTasks[next]) : null;
            }
            else
            {
                sideTasks.Remove(task);
                RefillSideTasks();
            }
        }
    }
}

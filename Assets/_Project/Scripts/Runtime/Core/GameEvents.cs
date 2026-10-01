using System;
using ScrapYardKing.Harvest;
using ScrapYardKing.Items;
using UnityEngine;

namespace ScrapYardKing.Core
{
    /// <summary>
    /// Global gameplay events. Progression systems (tasks, XP, analytics) listen here instead of
    /// being called by gameplay components, so new listeners never touch gameplay code.
    /// </summary>
    public static class GameEvents
    {
        public static event Action<ScrapBrokenEvent> ScrapBroken;
        public static event Action<ItemDefinition, int> ItemsCollected;
        public static event Action<ItemsProcessedEvent> ItemsProcessed;
        /// <summary>(item, units sold, cash earned).</summary>
        public static event Action<ItemDefinition, int, long> ItemsSold;
        public static event Action<long> CashEarned;
        /// <summary>(station id, item, count) whenever an item enters a station, whoever delivered it.</summary>
        public static event Action<string, ItemDefinition, int> ItemsDelivered;
        /// <summary>(upgrade id, new level).</summary>
        public static event Action<string, int> UpgradePurchased;
        /// <summary>(worker definition id, total hired of that kind).</summary>
        public static event Action<string, int> WorkerHired;
        public static event Action<int> LevelUp;
        public static event Action<string> TaskCompleted;
        /// <summary>(station id, units the customer received) when a customer leaves satisfied.</summary>
        public static event Action<string, int> CustomerServed;
        public static event Action<string> ExpansionOpened;
        /// <summary>(machine id) when the player starts Active Overdrive on a machine.</summary>
        public static event Action<string> MachineOverdrive;

        public static void RaiseScrapBroken(ScrapBrokenEvent e) => ScrapBroken?.Invoke(e);
        public static void RaiseItemsCollected(ItemDefinition item, int amount) => ItemsCollected?.Invoke(item, amount);
        public static void RaiseItemsProcessed(ItemsProcessedEvent e) => ItemsProcessed?.Invoke(e);
        public static void RaiseItemsSold(ItemDefinition item, int units, long cash) => ItemsSold?.Invoke(item, units, cash);
        public static void RaiseCashEarned(long amount) => CashEarned?.Invoke(amount);
        public static void RaiseItemsDelivered(string stationId, ItemDefinition item, int count) => ItemsDelivered?.Invoke(stationId, item, count);
        public static void RaiseUpgradePurchased(string upgradeId, int level) => UpgradePurchased?.Invoke(upgradeId, level);
        public static void RaiseWorkerHired(string workerId, int total) => WorkerHired?.Invoke(workerId, total);
        public static void RaiseLevelUp(int level) => LevelUp?.Invoke(level);
        public static void RaiseTaskCompleted(string taskId) => TaskCompleted?.Invoke(taskId);
        public static void RaiseCustomerServed(string stationId, int units) => CustomerServed?.Invoke(stationId, units);
        public static void RaiseExpansionOpened(string expansionId) => ExpansionOpened?.Invoke(expansionId);
        public static void RaiseMachineOverdrive(string machineId) => MachineOverdrive?.Invoke(machineId);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            ScrapBroken = null;
            ItemsCollected = null;
            ItemsProcessed = null;
            ItemsSold = null;
            CashEarned = null;
            ItemsDelivered = null;
            UpgradePurchased = null;
            WorkerHired = null;
            LevelUp = null;
            TaskCompleted = null;
            CustomerServed = null;
            ExpansionOpened = null;
            MachineOverdrive = null;
        }
    }

    public readonly struct ScrapBrokenEvent
    {
        public readonly ScrapDefinition Definition;
        public readonly Vector3 Position;
        public readonly int PiecesDropped;

        public ScrapBrokenEvent(ScrapDefinition definition, Vector3 position, int piecesDropped)
        {
            Definition = definition;
            Position = position;
            PiecesDropped = piecesDropped;
        }
    }

    public readonly struct ItemsProcessedEvent
    {
        public readonly string MachineId;
        public readonly ItemDefinition Input;
        public readonly int InputCount;
        public readonly ItemDefinition Output;
        public readonly int OutputCount;

        public ItemsProcessedEvent(string machineId, ItemDefinition input, int inputCount, ItemDefinition output, int outputCount)
        {
            MachineId = machineId;
            Input = input;
            InputCount = inputCount;
            Output = output;
            OutputCount = outputCount;
        }
    }
}

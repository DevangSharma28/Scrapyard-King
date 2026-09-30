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
        /// <summary>(machine id, input item, inputs consumed, output item, outputs produced).</summary>
        public static event Action<ItemsProcessedEvent> ItemsProcessed;
        /// <summary>(item, units sold, cash earned).</summary>
        public static event Action<ItemDefinition, int, long> ItemsSold;
        public static event Action<long> CashEarned;

        public static void RaiseScrapBroken(ScrapBrokenEvent e) => ScrapBroken?.Invoke(e);
        public static void RaiseItemsCollected(ItemDefinition item, int amount) => ItemsCollected?.Invoke(item, amount);
        public static void RaiseItemsProcessed(ItemsProcessedEvent e) => ItemsProcessed?.Invoke(e);
        public static void RaiseItemsSold(ItemDefinition item, int units, long cash) => ItemsSold?.Invoke(item, units, cash);
        public static void RaiseCashEarned(long amount) => CashEarned?.Invoke(amount);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            ScrapBroken = null;
            ItemsCollected = null;
            ItemsProcessed = null;
            ItemsSold = null;
            CashEarned = null;
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

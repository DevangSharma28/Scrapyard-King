using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

namespace ScrapYardKing.Items
{
    [Serializable]
    public sealed class StockLine
    {
        public string itemId;
        public int count;
    }

    /// <summary>Saved contents of a pile or station (plus cash waiting on its pallet, where it has one).</summary>
    [Serializable]
    public sealed class StockState
    {
        public List<StockLine> lines = new();
        public long cash;
    }

    /// <summary>
    /// A neat grid of <see cref="WorldItem"/>s: machine hoppers, storage bins, sell counters, output trays.
    /// Fills column → row → layer so stock level is readable at a glance.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ItemPile : MonoBehaviour, IItemReceiver, IItemSource
    {
        [Tooltip("Item types this pile takes. Empty = any.")]
        [SerializeField] ItemDefinition[] accepted;
        [SerializeField, Min(0)] int capacity = 40;
        [SerializeField, Min(1)] int columns = 5;
        [SerializeField, Min(1)] int rows = 4;
        [SerializeField] Vector3 cellSize = new(0.5f, 0.32f, 0.5f);
        [SerializeField, Min(0.05f)] float arriveDuration = 0.35f;
        [SerializeField, Min(0f)] float arriveArc = 1.2f;
        [SerializeField, Range(0f, 30f)] float yawJitter = 6f;

        readonly List<WorldItem> items = new();

        public event Action<ItemPile> Changed;

        public int Count => items.Count;
        public bool IsFull => items.Count >= capacity;
        public int FreeSpace => Mathf.Max(0, capacity - items.Count);

        public int Capacity
        {
            get => capacity;
            set
            {
                capacity = Mathf.Max(0, value);
                Changed?.Invoke(this);
            }
        }

        public bool Accepts(ItemDefinition item)
        {
            if (item == null) return false;
            if (accepted == null || accepted.Length == 0) return true;
            return Array.IndexOf(accepted, item) >= 0;
        }

        public bool CanAccept(ItemDefinition item) => !IsFull && Accepts(item);

        public void Accept(WorldItem item)
        {
            int index = items.Count;
            items.Add(item);
            item.MoveTo(transform, SlotPosition(index), SlotRotation(), arriveDuration, arriveArc, OnArrived);
            Changed?.Invoke(this);
        }

        /// <summary>Adds an item instantly (no flight), e.g. for pre-stocked piles.</summary>
        public void Place(WorldItem item)
        {
            int index = items.Count;
            items.Add(item);
            item.Place(transform, SlotPosition(index), SlotRotation());
            Changed?.Invoke(this);
        }

        public WorldItem Take(Func<ItemDefinition, bool> filter)
        {
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var item = items[i];
                if (filter != null && !filter(item.Definition)) continue;

                items.RemoveAt(i);
                item.transform.DOKill(true);
                Reflow(i);
                Changed?.Invoke(this);
                return item;
            }

            return null;
        }

        /// <summary>What the pile holds, as item id + count lines (for saving).</summary>
        public StockState CaptureStock()
        {
            var state = new StockState();
            foreach (var item in items)
            {
                if (item == null || item.Definition == null) continue;
                var line = state.lines.Find(l => l.itemId == item.Definition.Id);
                if (line == null) state.lines.Add(line = new StockLine { itemId = item.Definition.Id });
                line.count++;
            }

            return state;
        }

        /// <summary>
        /// Puts saved stock back, instantly and regardless of the current capacity (a station's level may be restored
        /// after its stock). Items the pool does not know are skipped.
        /// </summary>
        public void RestoreStock(StockState state, ItemPool pool, Action<WorldItem> onPlaced = null)
        {
            if (state == null || state.lines == null || pool == null) return;
            foreach (var line in state.lines)
            {
                var definition = pool.Find(line.itemId);
                if (definition == null) continue;
                for (int i = 0; i < line.count; i++)
                {
                    var item = pool.Get(definition, transform.position, Quaternion.identity);
                    if (item == null) break;
                    Place(item);
                    onPlaced?.Invoke(item);
                }
            }
        }

        /// <summary>Items matching <paramref name="filter"/> (null = all).</summary>
        public int CountOf(Func<ItemDefinition, bool> filter)
        {
            if (filter == null) return items.Count;
            int n = 0;
            foreach (var item in items)
                if (filter(item.Definition)) n++;
            return n;
        }

        /// <summary>Highest base value among items matching <paramref name="filter"/>, or -1 when none.</summary>
        public int HighestValueOf(Func<ItemDefinition, bool> filter)
        {
            int best = -1;
            foreach (var item in items)
                if (filter == null || filter(item.Definition))
                    best = Math.Max(best, item.Definition.BaseValue);
            return best;
        }

        /// <summary>Local position of slot <paramref name="index"/> (column-major within a layer, layers stack up).</summary>
        public Vector3 SlotPosition(int index)
        {
            int perLayer = columns * rows;
            int layer = index / perLayer;
            int inLayer = index % perLayer;
            int x = inLayer % columns;
            int z = inLayer / columns;
            return new Vector3((x - (columns - 1) * 0.5f) * cellSize.x, layer * cellSize.y, (z - (rows - 1) * 0.5f) * cellSize.z);
        }

        /// <summary>World position just above the current top of the pile.</summary>
        public Vector3 TopPosition => transform.TransformPoint(SlotPosition(Mathf.Max(0, items.Count - 1)) + Vector3.up * cellSize.y);

        Quaternion SlotRotation() => Quaternion.Euler(0f, UnityEngine.Random.Range(-yawJitter, yawJitter), 0f);

        void OnArrived(WorldItem item)
        {
            item.SetHeld();
            item.transform.DOPunchScale(Vector3.one * 0.18f, 0.2f, 6, 0.6f);
        }

        void Reflow(int fromIndex)
        {
            for (int i = fromIndex; i < items.Count; i++)
            {
                var item = items[i];
                var target = SlotPosition(i);
                if (item.CurrentState == WorldItem.State.Moving) item.RetargetMove(target);
                else item.transform.DOLocalMove(target, 0.15f).SetEase(Ease.OutQuad);
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.6f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(new Vector3(0f, cellSize.y * 0.5f, 0f), new Vector3(columns * cellSize.x, cellSize.y, rows * cellSize.z));
        }
    }
}

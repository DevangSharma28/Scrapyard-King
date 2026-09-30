using System;
using ScrapYardKing.Items;
using ScrapYardKing.UI;
using UnityEngine;

namespace ScrapYardKing.Factory
{
    /// <summary>Levelled storage bin. Receives from conveyors, hands items to carriers through a withdraw pad.</summary>
    [DisallowMultipleComponent]
    public sealed class Storage : MonoBehaviour, IItemReceiver, IItemSource
    {
        [SerializeField] StorageDefinition definition;
        [SerializeField, Min(1)] int level = 1;
        [SerializeField] ItemPile pile;
        [SerializeField] StationLabel label;
        [Tooltip("Withdraw pad whose speed follows the storage level's pickup interval.")]
        [SerializeField] TransferPad withdrawPad;

        public StorageDefinition Definition => definition;
        public int Level => level;
        public int Count => pile.Count;
        public int Capacity => pile.Capacity;

        void Awake() => ApplyLevel();

        void OnEnable() => pile.Changed += OnPileChanged;

        void OnDisable() => pile.Changed -= OnPileChanged;

        void Start() => RefreshLabel();

        public void SetLevel(int newLevel)
        {
            level = Mathf.Clamp(newLevel, 1, definition.MaxLevel);
            ApplyLevel();
        }

        public bool CanAccept(ItemDefinition item) => pile.CanAccept(item);

        public void Accept(WorldItem item) => pile.Accept(item);

        public WorldItem Take(Func<ItemDefinition, bool> filter) => pile.Take(filter);

        void ApplyLevel()
        {
            if (definition == null || pile == null) return;
            var stats = definition.GetLevel(level);
            pile.Capacity = stats.capacity;
            if (withdrawPad != null) withdrawPad.Interval = stats.pickupInterval;
            RefreshLabel();
        }

        void OnPileChanged(ItemPile _) => RefreshLabel();

        void RefreshLabel()
        {
            if (label != null && definition != null) label.Set(definition.DisplayName, level, pile.Count, pile.Capacity);
        }
    }
}

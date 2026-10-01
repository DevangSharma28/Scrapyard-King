using System;
using ScrapYardKing.Core;
using ScrapYardKing.Items;
using ScrapYardKing.Progression;
using ScrapYardKing.UI;
using UnityEngine;

namespace ScrapYardKing.Factory
{
    /// <summary>
    /// Levelled storage bin. Receives from conveyors, hands items to carriers through a withdraw pad. A row of bins can
    /// share one upgrade: followers take their level from <see cref="levelSource"/> and keep their own station id.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Storage : MonoBehaviour, IItemReceiver, IItemSource, IStation, IUpgradeable
    {
        [SerializeField] StorageDefinition definition;
        [SerializeField, Min(1)] int level = 1;
        [Tooltip("Station id for guidance (e.g. bin_iron). Empty = the definition id.")]
        [SerializeField] string stationId;
        [Tooltip("Label title. Empty = the definition name.")]
        [SerializeField] string labelTitle;
        [Tooltip("Takes its level from this storage instead of being upgraded on its own.")]
        [SerializeField] Storage levelSource;
        [SerializeField] ItemPile pile;
        [SerializeField] StationLabel label;
        [Tooltip("Withdraw pad whose speed follows the storage level's pickup interval.")]
        [SerializeField] TransferPad withdrawPad;
        [SerializeField] LevelVisuals levelVisuals;

        public event Action<IUpgradeable> UpgradeChanged;

        public StorageDefinition Definition => definition;
        public int Level => level;
        public int Count => pile.Count;
        public int Capacity => pile.Capacity;

        public string StationId => !string.IsNullOrEmpty(stationId) ? stationId : definition != null ? definition.Id : name;
        public string UpgradeId => definition != null ? definition.Id : name;
        public string DisplayName => definition.DisplayName;
        public Sprite Icon => definition.Icon;
        public int MaxLevel => definition.MaxLevel;
        public bool IsMaxed => level >= MaxLevel;
        public long NextCost => IsMaxed ? 0 : definition.GetLevel(level + 1).upgradeCost;
        public string LevelLabel => $"Lv.{level}";
        public string NextEffect => IsMaxed ? string.Empty : $"{definition.GetLevel(level).capacity} → {definition.GetLevel(level + 1).capacity}";
        public Vector3? FeedbackPosition => transform.position + Vector3.up * 1.5f;

        void Awake() => ApplyLevel();

        void OnEnable() => pile.Changed += OnPileChanged;

        void OnDisable() => pile.Changed -= OnPileChanged;

        void Start()
        {
            RefreshLabel();
            StationRegistry.Register(this);
            if (levelSource != null)
            {
                levelSource.UpgradeChanged += OnSourceChanged;
                if (levelSource.Level != level) SetLevel(levelSource.Level);
                return;
            }

            if (Services.TryGet(out UpgradeManager upgrades)) upgrades.Register(this);
        }

        void OnDestroy()
        {
            StationRegistry.Unregister(this);
            if (levelSource != null) levelSource.UpgradeChanged -= OnSourceChanged;
            else if (Services.TryGet(out UpgradeManager upgrades)) upgrades.Unregister(this);
        }

        void OnSourceChanged(IUpgradeable source) => SetLevel(source.Level);

        /// <summary>Items in the bin matching <paramref name="filter"/>.</summary>
        public int CountOf(Func<ItemDefinition, bool> filter) => pile.CountOf(filter);

        /// <summary>Highest base value among stored items matching <paramref name="filter"/>, or -1 when none.</summary>
        public int HighestValueOf(Func<ItemDefinition, bool> filter) => pile.HighestValueOf(filter);

        public void SetLevel(int newLevel)
        {
            level = Mathf.Clamp(newLevel, 1, definition.MaxLevel);
            ApplyLevel(true);
            UpgradeChanged?.Invoke(this);
        }

        void IUpgradeable.ApplyLevel(int newLevel) => SetLevel(newLevel);

        public bool CanAccept(ItemDefinition item) => pile.CanAccept(item);

        public void Accept(WorldItem item)
        {
            pile.Accept(item);
            GameEvents.RaiseItemsDelivered(StationId, item.Definition, 1);
        }

        public WorldItem Take(Func<ItemDefinition, bool> filter) => pile.Take(filter);

        void ApplyLevel(bool animate = false)
        {
            if (definition == null || pile == null) return;
            var stats = definition.GetLevel(level);
            pile.Capacity = stats.capacity;
            if (withdrawPad != null) withdrawPad.Interval = stats.pickupInterval;
            if (levelVisuals != null) levelVisuals.Apply(level, animate);
            RefreshLabel();
        }

        void OnPileChanged(ItemPile _) => RefreshLabel();

        void RefreshLabel()
        {
            if (label != null && definition != null)
                label.Set(!string.IsNullOrEmpty(labelTitle) ? labelTitle : definition.DisplayName, level, pile.Count, pile.Capacity);
        }
    }
}

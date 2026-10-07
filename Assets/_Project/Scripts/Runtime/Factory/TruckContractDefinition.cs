using System;
using ScrapYardKing.Items;
using UnityEngine;

namespace ScrapYardKing.Factory
{
    /// <summary>One line of an order: a product and its share of the order.</summary>
    [Serializable]
    public struct TruckContractLine
    {
        public ItemDefinition item;
        [Tooltip("Share of the order. Shares are relative: 3 and 1 means three quarters and one quarter.")]
        [Min(0.01f)] public float share;
    }

    /// <summary>
    /// An order a truck can arrive with: which bars it wants, in what proportion, and how much better it pays for them.
    /// The amounts scale with the dock: an order asks for a share of the truck's bed (see
    /// <see cref="TruckBayDefinition.ContractFill"/>), so one asset serves every dock level.
    /// </summary>
    [CreateAssetMenu(menuName = "Scrap Yard King/Factory/Truck Contract", fileName = "Contract_")]
    public sealed class TruckContractDefinition : ScriptableObject
    {
        [SerializeField] string id;
        [Tooltip("Short, shown on the HUD and the dock label, e.g. STEEL ORDER.")]
        [SerializeField] string displayName;
        [Tooltip("Empty = the first line's item icon.")]
        [SerializeField] Sprite icon;
        [SerializeField] TruckContractLine[] lines;
        [Tooltip("On top of the dock level's payout multiplier, for goods the order asked for.")]
        [SerializeField, Min(1f)] float rewardMultiplier = 1.15f;
        [Tooltip("Dock level from which this order can come.")]
        [SerializeField, Min(1)] int minLevel = 1;
        [Tooltip("How often it comes relative to the others.")]
        [SerializeField, Min(0f)] float weight = 1f;

        public string Id => id;
        public string DisplayName => displayName;
        public Sprite Icon => icon != null ? icon : lines != null && lines.Length > 0 && lines[0].item != null ? lines[0].item.Icon : null;
        public TruckContractLine[] Lines => lines;
        public float RewardMultiplier => rewardMultiplier;
        public int MinLevel => minLevel;
        public float Weight => weight;

        void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(id)) id = name;
            if (string.IsNullOrWhiteSpace(displayName)) displayName = name;
        }
    }
}

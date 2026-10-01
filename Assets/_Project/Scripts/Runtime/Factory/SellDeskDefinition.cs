using System;
using ScrapYardKing.Feedback;
using UnityEngine;

namespace ScrapYardKing.Factory
{
    [Serializable]
    public struct SellDeskLevel
    {
        [Tooltip("Seconds between customers served (blueprint Lv.1: 1 customer / 8 sec).")]
        [Min(0.2f)] public float saleInterval;
        [Tooltip("Units one customer buys (inclusive range).")]
        public Vector2Int unitsPerSale;
        [Tooltip("Multiplier on the item's base value.")]
        [Min(0f)] public float priceMultiplier;
        [Min(1)] public int counterCapacity;
        [Tooltip("Customers allowed to wait in line (used by the customer queue).")]
        [Min(1)] public int queueCapacity;
        [Min(0)] public int upgradeCost;
    }

    [CreateAssetMenu(fileName = "SellDesk_", menuName = "Scrap Yard King/Factory/Sell Desk Definition")]
    public sealed class SellDeskDefinition : ScriptableObject
    {
        [SerializeField] string id;
        [SerializeField] string displayName;
        [SerializeField] Sprite icon;
        [Tooltip("Items this desk sells. Empty = anything with a price.")]
        [SerializeField] Items.ItemDefinition[] sells;
        [SerializeField] SellDeskLevel[] levels =
        {
            new() { saleInterval = 8f, unitsPerSale = new Vector2Int(2, 4), priceMultiplier = 1f, counterCapacity = 12, queueCapacity = 3 }
        };
        [Tooltip("Delay before the first sale after stock lands on an empty counter, so the first payoff is quick.")]
        [SerializeField, Min(0f)] float firstSaleDelay = 1.2f;
        [SerializeField] SfxDefinition saleSfx;

        public string Id => id;
        public string DisplayName => displayName;
        public Sprite Icon => icon;
        public int MaxLevel => levels.Length;

        /// <summary>True when the desk sells the item with <paramref name="itemId"/> (any item for an open desk).</summary>
        public bool SellsId(string itemId)
        {
            if (sells == null || sells.Length == 0) return true;
            foreach (var item in sells)
                if (item != null && item.Id == itemId) return true;
            return false;
        }

        public bool Sells(Items.ItemDefinition item)
        {
            if (item == null || item.BaseValue <= 0) return false;
            if (sells == null || sells.Length == 0) return true;
            return System.Array.IndexOf(sells, item) >= 0;
        }
        public float FirstSaleDelay => firstSaleDelay;
        public SfxDefinition SaleSfx => saleSfx;

        public SellDeskLevel GetLevel(int level) => levels[Mathf.Clamp(level - 1, 0, levels.Length - 1)];

        void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(id)) id = name;
            if (string.IsNullOrWhiteSpace(displayName)) displayName = name;
        }
    }
}

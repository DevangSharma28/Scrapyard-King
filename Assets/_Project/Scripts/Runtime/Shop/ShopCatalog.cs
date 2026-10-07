using System;
using ScrapYardKing.Boosts;
using UnityEngine;

namespace ScrapYardKing.Shop
{
    /// <summary>
    /// What the shop sells and where. Real-money products are <see cref="IAPProductConfig"/> assets; diamond items
    /// (boosts, cash crates) are priced from <see cref="Economy.PremiumEconomyConfig"/> so no price is typed in twice:
    /// a boost costs the diamonds of the time it saves, a crate the cash value of its diamonds. Machines and workers
    /// are never sold here: they are bought in the yard.
    /// </summary>
    [CreateAssetMenu(fileName = "ShopCatalog", menuName = "Scrap Yard King/Shop/Shop Catalog")]
    public sealed class ShopCatalog : ScriptableObject
    {
        /// <summary>A boost bought with diamonds.</summary>
        [Serializable]
        public sealed class BoostItem
        {
            public BoostDefinition boost;
            [Min(1f)] public float minutes = 10f;
        }

        /// <summary>Cash bought with diamonds; bigger crates give a little more per diamond.</summary>
        [Serializable]
        public sealed class CashCrate
        {
            public string title = "CASH CRATE";
            [Min(1)] public int diamonds = 20;
            [Min(1f)] public float bonus = 1f;
            public Sprite icon;
        }

        [Header("Real money")]
        [Tooltip("Big card on top of the Diamonds and Special tabs while not owned.")]
        [SerializeField] IAPProductConfig hero;
        [Tooltip("Diamond bundles, cheapest first.")]
        [SerializeField] IAPProductConfig[] bundles;
        [Tooltip("Other products on the Special tab (boost packs, no ads).")]
        [SerializeField] IAPProductConfig[] specials;

        [Header("Diamonds")]
        [SerializeField] BoostItem[] boosts;
        [SerializeField] CashCrate[] crates;

        [Header("Free")]
        [Tooltip("Diamonds for one rewarded video on the Free tab.")]
        [SerializeField, Min(1)] int freeDiamonds = 5;
        [SerializeField, Min(0.1f)] float freeCooldownHours = 4f;
        [SerializeField, Min(1)] int freeDailyCap = 3;
        [Tooltip("The shop (and the + buttons) appear from the first diamond on.")]
        [SerializeField] bool unlockWithFirstDiamond = true;

        public IAPProductConfig Hero => hero;
        public IAPProductConfig[] Bundles => bundles ?? Array.Empty<IAPProductConfig>();
        public IAPProductConfig[] Specials => specials ?? Array.Empty<IAPProductConfig>();
        public BoostItem[] Boosts => boosts ?? Array.Empty<BoostItem>();
        public CashCrate[] Crates => crates ?? Array.Empty<CashCrate>();
        public int FreeDiamonds => freeDiamonds;
        public float FreeCooldownSeconds => freeCooldownHours * 3600f;
        public int FreeDailyCap => freeDailyCap;
        public bool UnlockWithFirstDiamond => unlockWithFirstDiamond;

        public IAPProductConfig Find(string productId)
        {
            if (hero != null && hero.ProductId == productId) return hero;
            foreach (var p in Bundles) if (p != null && p.ProductId == productId) return p;
            foreach (var p in Specials) if (p != null && p.ProductId == productId) return p;
            return null;
        }

        /// <summary>Diamond price of a boost: the diamonds of the extra time it gives (2x for 10 min = 10 min saved).</summary>
        public static int BoostCost(Economy.PremiumEconomyConfig premium, BoostItem item)
        {
            if (premium == null || item?.boost == null) return 0;
            float extra = Mathf.Max(0.1f, item.boost.Multiplier - 1f);
            return premium.SkipCost(item.minutes * 60f * extra);
        }

        /// <summary>Cash a crate pays at <paramref name="level"/>.</summary>
        public static long CrateCash(Economy.PremiumEconomyConfig premium, CashCrate crate, int level) =>
            premium == null || crate == null ? 0 : (long)(crate.diamonds * premium.CashPerDiamond(level) * crate.bonus);
    }
}

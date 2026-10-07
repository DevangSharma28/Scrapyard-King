using ScrapYardKing.Boosts;
using UnityEngine;

namespace ScrapYardKing.Shop
{
    public enum StoreProductType
    {
        /// <summary>Can be bought again (diamond bundles).</summary>
        Consumable,
        /// <summary>Bought once, restorable (starter pack, no ads).</summary>
        NonConsumable
    }

    /// <summary>Where a product sits in the price ladder; the shop styles its card by it.</summary>
    public enum ShopTier
    {
        Starter,
        Value,
        Power,
        Mega
    }

    /// <summary>
    /// One real-money product: its store id, what it contains and how the shop presents it. The price the player sees
    /// always comes from the store (localised); <see cref="fallbackPrice"/> is shown only by the Editor stand-in store.
    /// <see cref="referencePriceUsd"/> is for the value checks in tests and the economy window, never shown.
    /// </summary>
    [CreateAssetMenu(fileName = "IAP_", menuName = "Scrap Yard King/Shop/IAP Product")]
    public sealed class IAPProductConfig : ScriptableObject
    {
        [Tooltip("Product id in App Store Connect / Google Play.")]
        [SerializeField] string productId;
        [SerializeField] StoreProductType type;
        [SerializeField] ShopTier tier;
        [SerializeField] string title;
        [SerializeField] Sprite icon;
        [Tooltip("Off = not sold (e.g. No Ads while the game has no forced ads to remove).")]
        [SerializeField] bool enabled = true;

        [Header("Contents")]
        [SerializeField, Min(0)] int diamonds;
        [Tooltip("Shown as '+N BONUS' on top of the base diamonds.")]
        [SerializeField, Min(0)] int bonusDiamonds;
        [Tooltip("Cash worth this many diamonds at the buyer's yard level (PremiumEconomyConfig.CashPerDiamond).")]
        [SerializeField, Min(0)] int cashAsDiamonds;
        [SerializeField] BoostDefinition boost;
        [SerializeField, Min(0f)] float boostMinutes;
        [SerializeField] BoostDefinition secondBoost;
        [SerializeField, Min(0f)] float secondBoostMinutes;
        [SerializeField] bool removesAds;

        [Header("Presentation")]
        [Tooltip("Ribbon on the card: BEST VALUE, POPULAR, ONE TIME. Empty = none.")]
        [SerializeField] string badge;
        [SerializeField] string fallbackPrice = "$0.99";
        [SerializeField, Min(0f)] float referencePriceUsd = 0.99f;

        public string ProductId => productId;
        public StoreProductType Type => type;
        public ShopTier Tier => tier;
        public string Title => title;
        public Sprite Icon => icon;
        public bool Enabled => enabled;
        public int Diamonds => diamonds;
        public int BonusDiamonds => bonusDiamonds;
        public int TotalDiamonds => diamonds + bonusDiamonds;
        public int CashAsDiamonds => cashAsDiamonds;
        public BoostDefinition Boost => boost;
        public float BoostMinutes => boostMinutes;
        public BoostDefinition SecondBoost => secondBoost;
        public float SecondBoostMinutes => secondBoostMinutes;
        public bool RemovesAds => removesAds;
        public string Badge => badge;
        public string FallbackPrice => fallbackPrice;
        public float ReferencePriceUsd => referencePriceUsd;

        /// <summary>Diamonds per dollar, counting the bonus (value checks only).</summary>
        public float DiamondsPerUsd => referencePriceUsd > 0f ? TotalDiamonds / referencePriceUsd : 0f;
    }
}

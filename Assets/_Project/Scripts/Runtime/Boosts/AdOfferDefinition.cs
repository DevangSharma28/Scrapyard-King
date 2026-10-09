using UnityEngine;

namespace ScrapYardKing.Boosts
{
    public enum AdOfferKind
    {
        /// <summary>Switches a boost on.</summary>
        Boost,
        /// <summary>A lump of cash that grows with the yard level.</summary>
        FreeCash,
        /// <summary>Pays the reward of the order a truck just left with a second time. Offered only right after an order.</summary>
        DoubleTruck,
        /// <summary>Completes a loading truck's order at once. Offered on the HUD next to the order card.</summary>
        InstantTruck,
        /// <summary>Doubles the cash earned while away. Offered once per return on the welcome-back card.</summary>
        OfflineDouble,
        /// <summary>Keeps a machine's Overdrive running for minutes. Offered by its boost pad after the free burst.</summary>
        MachineBoost
    }

    /// <summary>An optional rewarded-video offer on the HUD: what the player gets, how long it stays, how soon it may return.</summary>
    [CreateAssetMenu(menuName = "Scrap Yard King/Boosts/Ad Offer", fileName = "Offer_")]
    public sealed class AdOfferDefinition : ScriptableObject
    {
        [SerializeField] string id;
        [Tooltip("Shown on the offer button, e.g. 2X CASH.")]
        [SerializeField] string title;
        [SerializeField] Sprite icon;
        [SerializeField] AdOfferKind kind;
        [SerializeField] BoostDefinition boost;
        [Tooltip("Free cash: this much per yard level.")]
        [SerializeField, Min(0)] long cashPerLevel = 200;
        [Tooltip("Seconds the offer stays on the HUD before it goes away untaken.")]
        [SerializeField, Min(3f)] float showSeconds = 25f;
        [Tooltip("Seconds before this same offer may come back after it was shown.")]
        [SerializeField, Min(0f)] float cooldown = 240f;
        [SerializeField, Min(1)] int minLevel = 2;
        [SerializeField, Min(0f)] float weight = 1f;
        [Tooltip("Most times this offer may be taken per day (UTC). 0 = no cap.")]
        [SerializeField, Min(0)] int dailyCap;
        [Tooltip("Rotating HUD offers: a higher priority is picked first when it is eligible.")]
        [SerializeField] int priority;

        public string Id => id;
        public string Title => title;
        public Sprite Icon => icon != null ? icon : boost != null ? boost.Icon : null;
        public AdOfferKind Kind => kind;
        public BoostDefinition Boost => boost;
        public long CashPerLevel => cashPerLevel;
        public float ShowSeconds => showSeconds;
        public float Cooldown => cooldown;
        public int MinLevel => minLevel;
        public float Weight => weight;
        public int DailyCap => dailyCap;
        public int Priority => priority;

        void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(id)) id = name;
            if (string.IsNullOrWhiteSpace(title)) title = name;
        }
    }
}

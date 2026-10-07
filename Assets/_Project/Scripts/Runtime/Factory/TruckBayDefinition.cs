using System;
using ScrapYardKing.Feedback;
using ScrapYardKing.Items;
using UnityEngine;

namespace ScrapYardKing.Factory
{
    [Serializable]
    public struct TruckBayLevel
    {
        [Tooltip("Crates one truck takes. It leaves (and pays) when full.")]
        [Min(1)] public int capacity;
        [Tooltip("Bulk-buyer premium on the cargo's base value.")]
        [Min(0.1f)] public float payoutMultiplier;
        [Tooltip("Seconds between a truck driving off and the next one backing in.")]
        [Min(0f)] public float returnDelay;
        [Min(0)] public int upgradeCost;
    }

    /// <summary>
    /// A loading dock where finished goods leave by truck (blueprint sales pillar: "customers, stock and truck loading").
    /// The truck is one big order: it pays for the whole load at a premium once it is full, so it rewards steady supply
    /// (a Loader) where the market stall rewards variety.
    /// </summary>
    [CreateAssetMenu(fileName = "TruckBay_", menuName = "Scrap Yard King/Factory/Truck Bay Definition")]
    public sealed class TruckBayDefinition : ScriptableObject
    {
        [SerializeField] string id;
        [SerializeField] string displayName;
        [SerializeField] Sprite icon;
        [Tooltip("Goods the truck buys. Empty = anything.")]
        [SerializeField] ItemDefinition[] accepts;
        [Tooltip("Orders a truck can arrive with. Empty = trucks just buy a full load, as before contracts.")]
        [SerializeField] TruckContractDefinition[] contracts;
        [Tooltip("Share of the bed an order asks for. The rest is room for other goods, paid at the plain rate.")]
        [SerializeField, Range(0.2f, 1f)] float contractFill = 0.75f;
        [SerializeField] TruckBayLevel[] levels = { new() { capacity = 12, payoutMultiplier = 1.4f, returnDelay = 8f } };
        [Tooltip("Road speed in m/s. With the return delay this sets how long the dock stays empty between loads.")]
        [SerializeField, Min(0.5f)] float truckSpeed = 9f;
        [Tooltip("Seconds until the first truck shows up after the dock opens.")]
        [SerializeField, Min(0f)] float firstArrivalDelay = 1.5f;

        [Header("Feedback")]
        [SerializeField] SfxDefinition arriveSfx;
        [SerializeField] SfxDefinition departSfx;
        [SerializeField] SfxDefinition payoutSfx;
        [Tooltip("Dock label while the carrier is on its way in / away. A ship is not on the road.")]
        [SerializeField] string arrivingText = "TRUCK COMING";
        [SerializeField] string awayText = "ON THE ROAD";
        [Tooltip("Horn when an order is complete.")]
        [SerializeField] SfxDefinition contractSfx;

        public string Id => id;
        public string DisplayName => displayName;
        public Sprite Icon => icon;
        public int MaxLevel => levels.Length;
        public float TruckSpeed => truckSpeed;
        public float FirstArrivalDelay => firstArrivalDelay;
        public SfxDefinition ArriveSfx => arriveSfx;
        public SfxDefinition DepartSfx => departSfx;
        public SfxDefinition PayoutSfx => payoutSfx;
        public SfxDefinition ContractSfx => contractSfx;
        public string ArrivingText => arrivingText;
        public string AwayText => awayText;
        public TruckContractDefinition[] Contracts => contracts ?? Array.Empty<TruckContractDefinition>();
        public float ContractFill => contractFill;

        public TruckBayLevel GetLevel(int level) => levels[Mathf.Clamp(level - 1, 0, levels.Length - 1)];

        public bool Accepts(ItemDefinition item)
        {
            if (item == null) return false;
            if (accepts == null || accepts.Length == 0) return true;
            return Array.IndexOf(accepts, item) >= 0;
        }

        void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(id)) id = name;
            if (string.IsNullOrWhiteSpace(displayName)) displayName = name;
        }
    }

    /// <summary>Pure payout maths for a truck load.</summary>
    public static class TruckBayMath
    {
        /// <summary>Cash for a load worth <paramref name="cargoValue"/> at base prices.</summary>
        public static long Payout(long cargoValue, float multiplier) =>
            cargoValue <= 0 ? 0 : Math.Max(1, (long)Math.Round(cargoValue * (double)Math.Max(0f, multiplier)));

        /// <summary>Part of <paramref name="total"/> earned by goods worth <paramref name="lineValue"/> of the cargo (rounded down).</summary>
        public static long Share(long total, long lineValue, long cargoValue) =>
            cargoValue <= 0 || lineValue <= 0 ? 0 : total * Math.Min(lineValue, cargoValue) / cargoValue;

        /// <summary>
        /// Truck speed at <paramref name="distance"/> metres from the parking spot: it crawls while docking and reaches
        /// road speed <paramref name="easeDistance"/> metres out.
        /// </summary>
        public static float SpeedAt(float distance, float roadSpeed, float crawlSpeed, float easeDistance) =>
            Mathf.Lerp(Mathf.Min(crawlSpeed, roadSpeed), roadSpeed, easeDistance <= 0f ? 1f : Mathf.Clamp01(distance / easeDistance));
    }
}

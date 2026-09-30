using UnityEngine;

namespace ScrapYardKing.Economy
{
    /// <summary>Global economy tuning. Per-station prices and rates live on their own definitions.</summary>
    [CreateAssetMenu(fileName = "EconomyConfig", menuName = "Scrap Yard King/Economy/Economy Config")]
    public sealed class EconomyConfig : ScriptableObject
    {
        [SerializeField, Min(0)] long startingCash;
        [SerializeField, Min(0)] long startingPremium;
        [Tooltip("Cash represented by one physical bundle in a cash pile. Bigger sales stack more bundles.")]
        [SerializeField, Min(1)] int cashBundleValue = 5;

        public long StartingCash => startingCash;
        public long StartingPremium => startingPremium;
        public int CashBundleValue => cashBundleValue;
    }
}

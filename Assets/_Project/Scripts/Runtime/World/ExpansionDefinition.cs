using ScrapYardKing.Feedback;
using UnityEngine;

namespace ScrapYardKing.World
{
    /// <summary>
    /// A fenced-off part of the yard that opens for cash (blueprint: first expansion = 1,500 cash). The yard level it
    /// unlocks at lives in the <see cref="Progression.UpgradeCatalog"/> like every other purchase.
    /// </summary>
    [CreateAssetMenu(fileName = "Expansion_", menuName = "Scrap Yard King/World/Expansion Definition")]
    public sealed class ExpansionDefinition : ScriptableObject
    {
        [SerializeField] string id;
        [SerializeField] string displayName;
        [SerializeField] Sprite icon;
        [SerializeField, Min(0)] long cost = 1500;
        [Tooltip("Short line on the purchase tile, e.g. FRIDGES & KARTS.")]
        [SerializeField] string teaser;
        [SerializeField] SfxDefinition openSfx;

        public string Id => id;
        public string DisplayName => displayName;
        public Sprite Icon => icon;
        public long Cost => cost;
        public string Teaser => teaser;
        public SfxDefinition OpenSfx => openSfx;

        void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(id)) id = name;
            if (string.IsNullOrWhiteSpace(displayName)) displayName = name;
        }
    }
}

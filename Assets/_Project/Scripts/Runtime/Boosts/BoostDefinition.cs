using ScrapYardKing.Feedback;
using UnityEngine;

namespace ScrapYardKing.Boosts
{
    /// <summary>What a timed boost multiplies.</summary>
    public enum BoostKind
    {
        /// <summary>Cash that sales put on cash piles (counters, the truck).</summary>
        Cash,
        /// <summary>Every machine's speed.</summary>
        Production,
        /// <summary>The player's walking speed.</summary>
        MoveSpeed,
        /// <summary>How fast broken scrap comes back.</summary>
        ScrapSpawn,
        /// <summary>How soon the next truck arrives.</summary>
        TruckTempo
    }

    /// <summary>A timed multiplier the player can switch on (rewarded ad, gift, purchase). One asset per boost.</summary>
    [CreateAssetMenu(menuName = "Scrap Yard King/Boosts/Boost", fileName = "Boost_")]
    public sealed class BoostDefinition : ScriptableObject
    {
        [SerializeField] string id;
        [Tooltip("Short, shown on the HUD chip and offers, e.g. 2X CASH.")]
        [SerializeField] string displayName;
        [SerializeField] Sprite icon;
        [SerializeField] BoostKind kind;
        [SerializeField, Min(1f)] float multiplier = 2f;
        [SerializeField, Min(1f)] float duration = 120f;
        [Tooltip("Activating again while it runs adds time, up to this many durations in total.")]
        [SerializeField, Min(1f)] float maxStack = 3f;
        [SerializeField] Color color = new(0.35f, 0.85f, 0.4f);
        [SerializeField] SfxDefinition startSfx;
        [SerializeField] SfxDefinition endSfx;

        public string Id => id;
        public string DisplayName => displayName;
        public Sprite Icon => icon;
        public BoostKind Kind => kind;
        public float Multiplier => multiplier;
        public float Duration => duration;
        public float MaxSeconds => duration * maxStack;
        public Color Color => color;
        public SfxDefinition StartSfx => startSfx;
        public SfxDefinition EndSfx => endSfx;

        void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(id)) id = name;
            if (string.IsNullOrWhiteSpace(displayName)) displayName = name;
        }
    }
}

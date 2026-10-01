using ScrapYardKing.Core;
using ScrapYardKing.Player;
using UnityEngine;

namespace ScrapYardKing.Progression
{
    /// <summary>
    /// A levelled player stat upgrade (blueprint "Player" category: cut power, speed, carry, pickup radius).
    /// Each level replaces the previous modifier with <see cref="ModifierAt"/>, so values are cumulative totals.
    /// </summary>
    [CreateAssetMenu(fileName = "Upgrade_", menuName = "Scrap Yard King/Progression/Player Stat Upgrade")]
    public sealed class PlayerStatUpgradeDefinition : ScriptableObject
    {
        [SerializeField] string id;
        [SerializeField] string displayName;
        [SerializeField] Sprite icon;
        [SerializeField] PlayerStat stat;
        [SerializeField] StatModifierType modifierType = StatModifierType.Flat;
        [Tooltip("Total modifier at each level. Element 0 is level 1 (usually 0).")]
        [SerializeField] float[] totalModifierPerLevel = { 0f, 5f, 10f };
        [Tooltip("Cash to reach level 2, 3, ... (one entry fewer than levels).")]
        [SerializeField] long[] costs = { 60, 150 };
        [Tooltip("Card effect text. {0} = current value, {1} = next value.")]
        [SerializeField] string effectFormat = "{0:0} → {1:0}";

        public string Id => id;
        public string DisplayName => displayName;
        public Sprite Icon => icon;
        public PlayerStat Stat => stat;
        public StatModifierType ModifierType => modifierType;
        public int MaxLevel => totalModifierPerLevel.Length;
        public string EffectFormat => effectFormat;

        public float ModifierAt(int level) => totalModifierPerLevel[Mathf.Clamp(level - 1, 0, totalModifierPerLevel.Length - 1)];

        public long CostToReach(int level) => costs[Mathf.Clamp(level - 2, 0, costs.Length - 1)];

        void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(id)) id = name;
            if (string.IsNullOrWhiteSpace(displayName)) displayName = name;
        }
    }
}

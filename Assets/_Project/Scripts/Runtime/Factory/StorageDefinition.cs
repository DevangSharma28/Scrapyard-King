using System;
using UnityEngine;

namespace ScrapYardKing.Factory
{
    [Serializable]
    public struct StorageLevel
    {
        [Min(1)] public int capacity;
        [Tooltip("Seconds between items when a carrier withdraws from this storage.")]
        [Min(0.01f)] public float pickupInterval;
        [Min(0)] public int upgradeCost;
    }

    /// <summary>Buffer between production and selling (blueprint: Storage 40 units at start).</summary>
    [CreateAssetMenu(fileName = "Storage_", menuName = "Scrap Yard King/Factory/Storage Definition")]
    public sealed class StorageDefinition : ScriptableObject
    {
        [SerializeField] string id;
        [SerializeField] string displayName;
        [SerializeField] Sprite icon;
        [SerializeField] StorageLevel[] levels = { new() { capacity = 40, pickupInterval = 0.07f } };

        public string Id => id;
        public string DisplayName => displayName;
        public Sprite Icon => icon;
        public int MaxLevel => levels.Length;

        public StorageLevel GetLevel(int level) => levels[Mathf.Clamp(level - 1, 0, levels.Length - 1)];

        void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(id)) id = name;
            if (string.IsNullOrWhiteSpace(displayName)) displayName = name;
        }
    }
}

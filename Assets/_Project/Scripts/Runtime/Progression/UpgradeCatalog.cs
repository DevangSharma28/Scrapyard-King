using System;
using UnityEngine;

namespace ScrapYardKing.Progression
{
    /// <summary>
    /// Every purchasable id with the yard level it unlocks at. Entries flagged <see cref="Entry.inPanel"/> appear in the
    /// upgrade panel (grouped by <see cref="Entry.group"/>, in order); the rest are bought on world tiles (hires, expansions).
    /// </summary>
    [CreateAssetMenu(fileName = "UpgradeCatalog", menuName = "Scrap Yard King/Progression/Upgrade Catalog")]
    public sealed class UpgradeCatalog : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            [Tooltip("UpgradeId of a station definition, player stat upgrade or worker definition.")]
            public string upgradeId;
            [Min(1)] public int unlockLevel;
            [Tooltip("Shown in the upgrade panel (false = bought on its own world tile).")]
            public bool inPanel;
            [Tooltip("Panel section header, e.g. YOU or YARD.")]
            public string group;
        }

        [SerializeField] Entry[] entries;

        public Entry[] Entries => entries;

        public int UnlockLevel(string id)
        {
            if (entries == null) return 1;
            foreach (var e in entries)
                if (e.upgradeId == id) return Mathf.Max(1, e.unlockLevel);
            return 1;
        }
    }
}

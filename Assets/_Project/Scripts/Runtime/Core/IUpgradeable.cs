using System;
using UnityEngine;

namespace ScrapYardKing.Core
{
    /// <summary>
    /// Anything the player can spend cash on to level up: player stats, stations, worker hires. Stations implement it
    /// directly from their own level data; the upgrade system only sees this contract.
    /// </summary>
    public interface IUpgradeable
    {
        string UpgradeId { get; }
        string DisplayName { get; }
        Sprite Icon { get; }
        int Level { get; }
        int MaxLevel { get; }
        bool IsMaxed { get; }

        /// <summary>Cash needed to reach the next level. Only meaningful while not maxed.</summary>
        long NextCost { get; }

        /// <summary>Card caption for the current level, e.g. "Lv.3" or "1/2".</summary>
        string LevelLabel { get; }

        /// <summary>What the next level changes, e.g. "75 → 92 /min". Empty when maxed.</summary>
        string NextEffect { get; }

        /// <summary>World point for purchase feedback, or null for upgrades without a world presence.</summary>
        Vector3? FeedbackPosition { get; }

        event Action<IUpgradeable> UpgradeChanged;

        void ApplyLevel(int level);
    }
}

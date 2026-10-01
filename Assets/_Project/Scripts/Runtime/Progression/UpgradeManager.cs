using System;
using System.Collections.Generic;
using ScrapYardKing.Core;
using ScrapYardKing.Economy;
using ScrapYardKing.Feedback;
using UnityEngine;

namespace ScrapYardKing.Progression
{
    /// <summary>
    /// Registry of everything purchasable and the single place cash turns into levels. Upgradeables register
    /// themselves (stations in Start, player upgrades, worker hires); UI only talks to this class.
    /// </summary>
    [DefaultExecutionOrder(-450)]
    public sealed class UpgradeManager : ServiceBehaviour<UpgradeManager>
    {
        [SerializeField] UpgradeCatalog catalog;
        [SerializeField] SfxDefinition purchaseSfx;
        [SerializeField] SfxDefinition deniedSfx;
        [SerializeField] ParticleSystem purchaseVfx;

        readonly Dictionary<string, IUpgradeable> upgrades = new();
        EconomyManager economy;
        ProgressionManager progression;

        /// <summary>Raised when an upgradeable registers (UI binds late registrations through this).</summary>
        public event Action<IUpgradeable> Registered;
        public event Action<IUpgradeable> Purchased;

        public UpgradeCatalog Catalog => catalog;

        void Start()
        {
            Services.TryGet(out economy);
            Services.TryGet(out progression);
        }

        public void Register(IUpgradeable upgradeable)
        {
            if (upgradeable == null || string.IsNullOrEmpty(upgradeable.UpgradeId)) return;
            upgrades[upgradeable.UpgradeId] = upgradeable;
            Registered?.Invoke(upgradeable);
        }

        public void Unregister(IUpgradeable upgradeable)
        {
            if (upgradeable != null && upgrades.TryGetValue(upgradeable.UpgradeId, out var existing) && existing == upgradeable)
                upgrades.Remove(upgradeable.UpgradeId);
        }

        public bool TryGet(string id, out IUpgradeable upgradeable) => upgrades.TryGetValue(id ?? string.Empty, out upgradeable);

        public int LevelOf(string id) => TryGet(id, out var u) ? u.Level : 0;

        /// <summary>Player level needed before the upgrade can be bought (from the catalog; 1 when not listed).</summary>
        public int UnlockLevel(string id) => catalog != null ? catalog.UnlockLevel(id) : 1;

        public bool IsUnlocked(IUpgradeable u) => progression == null || progression.Level >= UnlockLevel(u.UpgradeId);

        public bool CanAfford(IUpgradeable u) => u != null && !u.IsMaxed && economy != null && economy.CanAfford(u.NextCost);

        public bool CanPurchase(IUpgradeable u) => CanAfford(u) && IsUnlocked(u);

        public bool TryPurchase(IUpgradeable u)
        {
            if (u == null || u.IsMaxed || !IsUnlocked(u) || economy == null || !economy.TrySpendCash(u.NextCost))
            {
                GameFeedback.Sfx(deniedSfx);
                return false;
            }

            Complete(u);
            return true;
        }

        /// <summary>Applies the next level of an upgrade whose price was already collected (pay-over-time tiles).</summary>
        public void CompletePrepaid(IUpgradeable u)
        {
            if (u != null && !u.IsMaxed) Complete(u);
        }

        void Complete(IUpgradeable u)
        {
            u.ApplyLevel(u.Level + 1);

            GameFeedback.Sfx(purchaseSfx);
            if (u.FeedbackPosition.HasValue)
            {
                GameFeedback.Vfx(purchaseVfx, u.FeedbackPosition.Value, Quaternion.identity);
                GameFeedback.CameraPunch(0.6f);
                var config = GameFeedback.Config;
                GameFeedback.Popup("UPGRADE!", u.FeedbackPosition.Value + Vector3.up, config != null ? config.PositivePopupColor : Color.yellow, 1.2f);
            }

            Purchased?.Invoke(u);
            GameEvents.RaiseUpgradePurchased(u.UpgradeId, u.Level);
        }
    }
}

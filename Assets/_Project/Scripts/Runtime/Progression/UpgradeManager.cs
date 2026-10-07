using System;
using System.Collections.Generic;
using DG.Tweening;
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
    public sealed class UpgradeManager : ServiceBehaviour<UpgradeManager>, ISaveable
    {
        [Serializable]
        sealed class LevelEntry
        {
            public string id;
            public int level;
        }

        [Serializable]
        sealed class State
        {
            public List<LevelEntry> levels = new();
        }

        [SerializeField] UpgradeCatalog catalog;
        [SerializeField] SfxDefinition purchaseSfx;
        [SerializeField] SfxDefinition deniedSfx;
        [SerializeField] ParticleSystem purchaseVfx;

        readonly Dictionary<string, IUpgradeable> upgrades = new();
        // Levels from the save. Applied when the upgradeable registers, which for content inside a locked area is the
        // moment that area is restored. Ids that never register (content not built) are written back unchanged.
        readonly Dictionary<string, int> savedLevels = new();
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
            SaveRegistry.Register(this);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            SaveRegistry.Unregister(this);
        }

        public void Register(IUpgradeable upgradeable)
        {
            if (upgradeable == null || string.IsNullOrEmpty(upgradeable.UpgradeId)) return;
            upgrades[upgradeable.UpgradeId] = upgradeable;
            ApplySaved(upgradeable);
            Registered?.Invoke(upgradeable);
        }

        string ISaveable.SaveKey => "upgrades";

        string ISaveable.CaptureState()
        {
            var levels = new Dictionary<string, int>(savedLevels);
            foreach (var kv in upgrades) levels[kv.Key] = kv.Value.Level;
            var state = new State();
            foreach (var kv in levels) state.levels.Add(new LevelEntry { id = kv.Key, level = kv.Value });
            state.levels.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            return JsonUtility.ToJson(state);
        }

        void ISaveable.RestoreState(string state)
        {
            var s = JsonUtility.FromJson<State>(state);
            savedLevels.Clear();
            if (s?.levels == null) return;
            foreach (var e in s.levels)
                if (e != null && !string.IsNullOrEmpty(e.id)) savedLevels[e.id] = e.level;
            foreach (var u in new List<IUpgradeable>(upgrades.Values)) ApplySaved(u);
        }

        /// <summary>Raises a registered upgrade to its saved level without cost, feedback or purchase events.</summary>
        void ApplySaved(IUpgradeable u)
        {
            if (!savedLevels.TryGetValue(u.UpgradeId, out int level)) return;
            level = Mathf.Min(level, u.MaxLevel);
            if (level <= u.Level) return;
            using var scope = SaveRegistry.BeginRestore();
            u.ApplyLevel(level);
        }

        public void Unregister(IUpgradeable upgradeable)
        {
            if (upgradeable != null && upgrades.TryGetValue(upgradeable.UpgradeId, out var existing) && existing == upgradeable)
                upgrades.Remove(upgradeable.UpgradeId);
        }

        public bool TryGet(string id, out IUpgradeable upgradeable) => upgrades.TryGetValue(id ?? string.Empty, out upgradeable);

        /// <summary>Everything buyable that has registered (the economy window lists their prices).</summary>
        public IEnumerable<IUpgradeable> All => upgrades.Values;

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
            string gain = UpgradeGain.Describe(u, u.NextEffect);   // read before the level changes
            long cost = u.NextCost;
            u.ApplyLevel(u.Level + 1);

            // the purchase lands on the station itself: sound, sparks, a squash, then "LEVEL 8!" and what got better
            GameFeedback.Sfx(purchaseSfx);
            if (u.FeedbackPosition.HasValue)
            {
                var at = u.FeedbackPosition.Value;
                GameFeedback.Vfx(purchaseVfx, at, Quaternion.identity);
                GameFeedback.CameraPunch(0.6f);
                var config = GameFeedback.Config;
                var color = config != null ? config.PositivePopupColor : Color.yellow;
                bool hire = gain is "HIRE" or "+1 WORKER";
                string head = hire ? "HIRED!" : u.Level <= 1 ? "BUILT!" : $"LEVEL {u.Level}!";
                if (hire) gain = string.Empty;
                GameFeedback.Popup(head, at + Vector3.up, color, 1.35f);
                if (!string.IsNullOrEmpty(gain))
                    DG.Tweening.DOVirtual.DelayedCall(0.35f, () => GameFeedback.Popup(gain, at + Vector3.up * 2.1f, Color.white, 1.05f));
                if (u is Component station && u is not World.Expansion && station.transform.localScale.sqrMagnitude > 0.01f)
                {
                    var t = station.transform;
                    var rest = t.localScale;
                    DG.Tweening.DOTween.Kill(t, true);
                    DG.Tweening.ShortcutExtensions.DOPunchScale(t, rest * 0.08f, 0.4f, 6, 0.6f)
                        .OnComplete(() => t.localScale = rest);
                }
            }

            Analytics.Log(AnalyticsEvents.UpgradePurchased, ("id", u.UpgradeId), ("level", u.Level), ("cost", cost));
            Purchased?.Invoke(u);
            GameEvents.RaiseUpgradePurchased(u.UpgradeId, u.Level);
        }
    }
}

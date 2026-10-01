using System;
using DG.Tweening;
using ScrapYardKing.Core;
using UnityEngine;

namespace ScrapYardKing.Progression
{
    /// <summary>
    /// Swaps materials on a set of renderers as an upgrade levels up (e.g. the chainsaw goes orange → red → gold),
    /// so upgrades the player carries are visible on the character too.
    /// </summary>
    public sealed class UpgradeVisualTiers : MonoBehaviour
    {
        [Serializable]
        struct Tier
        {
            [Min(1)] public int minLevel;
            public Material material;
        }

        [SerializeField] string upgradeId;
        [SerializeField] Renderer[] renderers;
        [SerializeField] Transform punchTarget;
        [SerializeField] Tier[] tiers;

        IUpgradeable bound;
        int appliedTier = -1;

        void Start()
        {
            if (!Services.TryGet(out UpgradeManager manager)) return;
            if (manager.TryGet(upgradeId, out var u)) Bind(u);
            else manager.Registered += OnRegistered;
        }

        void OnDestroy()
        {
            if (bound != null) bound.UpgradeChanged -= OnChanged;
            if (Services.TryGet(out UpgradeManager manager)) manager.Registered -= OnRegistered;
        }

        void OnRegistered(IUpgradeable u)
        {
            if (u.UpgradeId == upgradeId) Bind(u);
        }

        void Bind(IUpgradeable u)
        {
            bound = u;
            u.UpgradeChanged += OnChanged;
            Apply(u.Level, false);
        }

        void OnChanged(IUpgradeable u) => Apply(u.Level, true);

        void Apply(int level, bool animate)
        {
            int tier = -1;
            for (int i = 0; i < tiers.Length; i++)
                if (level >= tiers[i].minLevel) tier = i;
            if (tier < 0 || tier == appliedTier) return;

            appliedTier = tier;
            foreach (var r in renderers)
                if (r != null) r.sharedMaterial = tiers[tier].material;

            if (!animate || punchTarget == null) return;
            punchTarget.DOKill(true);
            punchTarget.DOPunchScale(Vector3.one * 0.35f, 0.4f, 6, 0.6f);
        }
    }
}

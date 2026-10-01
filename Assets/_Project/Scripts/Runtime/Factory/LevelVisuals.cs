using System;
using DG.Tweening;
using UnityEngine;

namespace ScrapYardKing.Factory
{
    /// <summary>
    /// Extra geometry that appears as a station levels up (exhaust pipes, extra cogs, beacons...), so every upgrade
    /// changes the world, not just a number.
    /// </summary>
    public sealed class LevelVisuals : MonoBehaviour
    {
        [Serializable]
        struct Tier
        {
            [Min(1)] public int minLevel;
            public GameObject[] objects;
        }

        [SerializeField] Tier[] tiers;

        int appliedLevel = -1;

        public void Apply(int level, bool animate)
        {
            if (tiers == null) return;
            foreach (var tier in tiers)
            {
                bool on = level >= tier.minLevel;
                bool wasOn = appliedLevel >= tier.minLevel;
                foreach (var go in tier.objects)
                {
                    if (go == null) continue;
                    go.SetActive(on);
                    if (!on || wasOn || !animate) continue;

                    var t = go.transform;
                    var scale = t.localScale;
                    t.DOKill(true);
                    t.localScale = Vector3.zero;
                    t.DOScale(scale, 0.45f).SetEase(Ease.OutBack).SetDelay(0.1f);
                }
            }

            appliedLevel = level;
        }
    }
}

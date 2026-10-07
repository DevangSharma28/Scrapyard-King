using System;
using System.Collections.Generic;
using ScrapYardKing.Core;
using ScrapYardKing.Player;
using UnityEngine;

namespace ScrapYardKing.Progression
{
    /// <summary>Turns <see cref="PlayerStatUpgradeDefinition"/>s into purchasable upgrades that modify <see cref="PlayerStats"/>.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerUpgrades : MonoBehaviour
    {
        sealed class StatUpgrade : IUpgradeable
        {
            readonly PlayerStatUpgradeDefinition definition;
            readonly PlayerStats stats;
            readonly Transform owner;

            public StatUpgrade(PlayerStatUpgradeDefinition definition, PlayerStats stats, Transform owner)
            {
                this.definition = definition;
                this.stats = stats;
                this.owner = owner;
            }

            public event Action<IUpgradeable> UpgradeChanged;

            public string UpgradeId => definition.Id;
            public string DisplayName => definition.DisplayName;
            public Sprite Icon => definition.Icon;
            public int Level { get; private set; } = 1;
            public int MaxLevel => definition.MaxLevel;
            public bool IsMaxed => Level >= MaxLevel;
            public long NextCost => IsMaxed ? 0 : definition.CostToReach(Level + 1);
            public string LevelLabel => $"Lv.{Level}";
            public Vector3? FeedbackPosition => owner.position + Vector3.up * 2f;

            public string NextEffect
            {
                get
                {
                    if (IsMaxed) return string.Empty;
                    var stat = stats.Get(definition.Stat);
                    // both sides from the base: a running boost (FAST BOOTS) made it read "10.2 → 7.3 speed"
                    float now = Preview(stat.BaseValue, Level);
                    float next = Preview(stat.BaseValue, Level + 1);
                    return string.Format(definition.EffectFormat, now, next);
                }
            }

            public void ApplyLevel(int level)
            {
                Level = Mathf.Clamp(level, 1, MaxLevel);
                var stat = stats.Get(definition.Stat);
                stat.RemoveModifiersFrom(this);
                float amount = definition.ModifierAt(Level);
                if (!Mathf.Approximately(amount, 0f)) stat.AddModifier(new StatModifier(this, definition.ModifierType, amount));
                UpgradeChanged?.Invoke(this);
            }

            // Approximation for the card text: applies only this upgrade's modifier on top of the base.
            float Preview(float baseValue, int level)
            {
                float m = definition.ModifierAt(level);
                return definition.ModifierType switch
                {
                    StatModifierType.Flat => baseValue + m,
                    StatModifierType.PercentAdd => baseValue * (1f + m),
                    _ => baseValue * (Mathf.Approximately(m, 0f) ? 1f : m)
                };
            }
        }

        [SerializeField] PlayerStats stats;
        [SerializeField] PlayerStatUpgradeDefinition[] upgrades;

        readonly List<StatUpgrade> runtime = new();

        void Start()
        {
            if (stats == null || upgrades == null || !Services.TryGet(out UpgradeManager manager)) return;
            foreach (var definition in upgrades)
            {
                if (definition == null) continue;
                var upgrade = new StatUpgrade(definition, stats, transform);
                upgrade.ApplyLevel(1);
                runtime.Add(upgrade);
                manager.Register(upgrade);
            }
        }

        void OnDestroy()
        {
            if (!Services.TryGet(out UpgradeManager manager)) return;
            foreach (var u in runtime) manager.Unregister(u);
        }
    }
}

using System;
using System.Collections.Generic;

namespace ScrapYardKing.Core
{
    public enum StatModifierType
    {
        /// <summary>Added to the base value.</summary>
        Flat,
        /// <summary>Summed with other PercentAdd modifiers, then applied once (0.25 = +25%).</summary>
        PercentAdd,
        /// <summary>Multiplied in after everything else (2 = double, e.g. Active Overdrive).</summary>
        Multiply
    }

    public readonly struct StatModifier
    {
        public readonly object Source;
        public readonly StatModifierType Type;
        public readonly float Amount;

        public StatModifier(object source, StatModifierType type, float amount)
        {
            Source = source;
            Type = type;
            Amount = amount;
        }
    }

    /// <summary>
    /// A numeric gameplay stat: (base + flat) * (1 + percentAdd) * product(multiply).
    /// Upgrades, overdrive and temporary buffs add modifiers keyed by their source and remove them by source.
    /// </summary>
    public sealed class ModifiableStat
    {
        readonly List<StatModifier> modifiers = new();
        float baseValue;

        public ModifiableStat(float baseValue)
        {
            this.baseValue = baseValue;
            Value = baseValue;
        }

        public event Action<ModifiableStat> Changed;

        public float BaseValue => baseValue;
        public float Value { get; private set; }
        public int IntValue => (int)MathF.Round(Value);
        public int ModifierCount => modifiers.Count;

        public void SetBase(float value)
        {
            if (baseValue.Equals(value)) return;
            baseValue = value;
            Recalculate();
        }

        public void AddModifier(StatModifier modifier)
        {
            modifiers.Add(modifier);
            Recalculate();
        }

        public bool RemoveModifiersFrom(object source)
        {
            int removed = modifiers.RemoveAll(m => Equals(m.Source, source));
            if (removed > 0) Recalculate();
            return removed > 0;
        }

        void Recalculate()
        {
            float flat = 0f, percent = 0f, multiply = 1f;
            foreach (var m in modifiers)
            {
                switch (m.Type)
                {
                    case StatModifierType.Flat: flat += m.Amount; break;
                    case StatModifierType.PercentAdd: percent += m.Amount; break;
                    case StatModifierType.Multiply: multiply *= m.Amount; break;
                }
            }

            float value = (baseValue + flat) * (1f + percent) * multiply;
            if (value.Equals(Value)) return;
            Value = value;
            Changed?.Invoke(this);
        }
    }
}


using System;
using ScrapYardKing.Feedback;
using ScrapYardKing.Items;
using UnityEngine;

namespace ScrapYardKing.Factory
{
    [Serializable]
    public struct MachineLevel
    {
        [Tooltip("Items the hopper holds.")]
        [Min(1)] public int inputCapacity;
        [Tooltip("Seconds per cycle at 1x speed.")]
        [Min(0.05f)] public float cycleTime;
        [Min(1)] public int inputsPerCycle;
        [Tooltip("Output yield per cycle.")]
        [Min(1)] public int outputsPerCycle;
        [Tooltip("Cash to reach this level from the previous one. Ignored for level 1.")]
        [Min(0)] public int upgradeCost;
    }

    /// <summary>One possible product of a splitting machine (Sorter) and its share of the output.</summary>
    [Serializable]
    public struct WeightedOutput
    {
        public ItemDefinition item;
        [Min(0f)] public float weight;
    }

    /// <summary>One input → product pair of a multi-input machine (Furnace: iron → iron ingot, copper → copper ingot).</summary>
    [Serializable]
    public struct MachineRecipe
    {
        public ItemDefinition input;
        public ItemDefinition output;
    }

    /// <summary>
    /// Data for one processing machine (Crusher, Sorter, Furnace, Press...). New machine types are new assets,
    /// not new code: input item, output item and per-level numbers are all here.
    /// </summary>
    [CreateAssetMenu(fileName = "Machine_", menuName = "Scrap Yard King/Factory/Machine Definition")]
    public sealed class MachineDefinition : ScriptableObject
    {
        [SerializeField] string id;
        [SerializeField] string displayName;
        [SerializeField] Sprite icon;
        [SerializeField] ItemDefinition input;
        [SerializeField] ItemDefinition output;
        [Tooltip("Splitting machines (Sorter): each cycle makes one of these, spread evenly by weight. Empty = always Output.")]
        [SerializeField] WeightedOutput[] outputMix;
        [Tooltip("Multi-input machines (Furnace): every input listed here is accepted and makes its own product. " +
                 "Empty = Input → Output (or Output Mix).")]
        [SerializeField] MachineRecipe[] recipes;
        [SerializeField] MachineLevel[] levels = { new() { inputCapacity = 10, cycleTime = 0.8f, inputsPerCycle = 1, outputsPerCycle = 1 } };

        [Header("Feedback")]
        [SerializeField] SfxDefinition cycleSfx;
        [SerializeField] SfxDefinition outputSfx;

        public string Id => id;
        public string DisplayName => displayName;
        public Sprite Icon => icon;
        public ItemDefinition Input => input;
        public ItemDefinition Output => output;
        public WeightedOutput[] OutputMix => outputMix;
        public bool HasOutputMix => outputMix != null && outputMix.Length > 0;
        public MachineRecipe[] Recipes => recipes;
        public bool HasRecipes => recipes != null && recipes.Length > 0;

        /// <summary>True when <paramref name="item"/> goes into this machine (the input, or any recipe input).</summary>
        public bool Takes(ItemDefinition item)
        {
            if (item == null) return false;
            if (!HasRecipes) return item == input;
            foreach (var r in recipes)
                if (r.input == item) return true;
            return false;
        }

        /// <summary>Product of one cycle fed with <paramref name="item"/>. Null when this machine does not take it.</summary>
        public ItemDefinition OutputFor(ItemDefinition item)
        {
            if (!HasRecipes) return item == input ? output : null;
            foreach (var r in recipes)
                if (r.input == item) return r.output;
            return null;
        }

        /// <summary>True when this machine can produce <paramref name="item"/>.</summary>
        public bool Produces(ItemDefinition item) => item != null && ProducesAny(i => i == item);

        /// <summary>True when any product of this machine matches <paramref name="filter"/>.</summary>
        public bool ProducesAny(Func<ItemDefinition, bool> filter)
        {
            if (HasRecipes)
            {
                foreach (var r in recipes)
                    if (r.output != null && filter(r.output)) return true;
                return false;
            }

            if (!HasOutputMix) return output != null && filter(output);
            foreach (var o in outputMix)
                if (o.item != null && o.weight > 0f && filter(o.item)) return true;
            return false;
        }
        public int MaxLevel => levels.Length;
        public SfxDefinition CycleSfx => cycleSfx;
        public SfxDefinition OutputSfx => outputSfx;

        /// <summary>Stats for a 1-based level, clamped to the defined range.</summary>
        public MachineLevel GetLevel(int level) => levels[Mathf.Clamp(level - 1, 0, levels.Length - 1)];

        /// <summary>Inputs per minute at 1x speed. Drives bottleneck comparisons and UI.</summary>
        public float InputsPerMinute(int level)
        {
            var l = GetLevel(level);
            return 60f / l.cycleTime * l.inputsPerCycle;
        }

        void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(id)) id = name;
            if (string.IsNullOrWhiteSpace(displayName)) displayName = name;
            if (levels == null || levels.Length == 0) levels = new[] { new MachineLevel { inputCapacity = 10, cycleTime = 0.8f, inputsPerCycle = 1, outputsPerCycle = 1 } };
        }
    }
}

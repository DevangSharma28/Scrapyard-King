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
        [SerializeField] MachineLevel[] levels = { new() { inputCapacity = 10, cycleTime = 0.8f, inputsPerCycle = 1, outputsPerCycle = 1 } };

        [Header("Feedback")]
        [SerializeField] SfxDefinition cycleSfx;
        [SerializeField] SfxDefinition outputSfx;

        public string Id => id;
        public string DisplayName => displayName;
        public Sprite Icon => icon;
        public ItemDefinition Input => input;
        public ItemDefinition Output => output;
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

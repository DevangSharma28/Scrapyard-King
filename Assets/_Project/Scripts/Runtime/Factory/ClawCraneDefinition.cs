using System;
using ScrapYardKing.Feedback;
using UnityEngine;

namespace ScrapYardKing.Factory
{
    /// <summary>One level of a claw crane. Level 1 is the build itself.</summary>
    [Serializable]
    public struct ClawCraneLevel
    {
        [Tooltip("How far from the mast the claw can pick up, metres.")]
        [Min(1f)] public float reach;
        [Tooltip("Pieces per grab.")]
        [Min(1)] public int grabSize;
        [Tooltip("Multiplier on every motion (swing, trolley, hoist).")]
        [Min(0.1f)] public float speed;
        [Min(0)] public long cost;
    }

    /// <summary>
    /// Balance data for a claw crane: a station that feeds a machine by itself. It picks loose items the machine takes
    /// from the ground inside its reach and drops them into the hopper.
    /// </summary>
    [CreateAssetMenu(menuName = "Scrap Yard King/Factory/Claw Crane", fileName = "ClawCrane_")]
    public sealed class ClawCraneDefinition : ScriptableObject
    {
        [SerializeField] string id = "claw_crane";
        [SerializeField] string displayName = "Claw Crane";
        [SerializeField] Sprite icon;
        [SerializeField] ClawCraneLevel[] levels = { new() { reach = 10f, grabSize = 4, speed = 1f, cost = 600 } };
        [Tooltip("Pieces around the first one that the same grab takes, metres.")]
        [SerializeField, Min(0.2f)] float grabRadius = 2.4f;
        [SerializeField] SfxDefinition grabSfx;
        [SerializeField] SfxDefinition dropSfx;

        public string Id => id;
        public string DisplayName => displayName;
        public Sprite Icon => icon;
        public int MaxLevel => levels.Length;
        public float GrabRadius => grabRadius;
        public SfxDefinition GrabSfx => grabSfx;
        public SfxDefinition DropSfx => dropSfx;

        /// <summary>Stats of a level, 1-based and clamped.</summary>
        public ClawCraneLevel GetLevel(int level) => levels[Mathf.Clamp(level - 1, 0, levels.Length - 1)];
    }
}

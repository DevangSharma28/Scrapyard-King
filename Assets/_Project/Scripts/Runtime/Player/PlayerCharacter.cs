using System;
using ScrapYardKing.Core;
using ScrapYardKing.Harvest;
using ScrapYardKing.Items;
using UnityEngine;

namespace ScrapYardKing.Player
{
    /// <summary>
    /// Player root. Wires <see cref="PlayerStats"/> into the role-agnostic components (stack capacity, pickup radius)
    /// and exposes the player to other systems through <see cref="Services"/>.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class PlayerCharacter : ServiceBehaviour<PlayerCharacter>
    {
        [SerializeField] PlayerStats stats;
        [SerializeField] PlayerController controller;
        [SerializeField] HarvestTool harvestTool;
        [SerializeField] CarryStack carryStack;
        [SerializeField] ItemCollector itemCollector;

        public PlayerStats Stats => stats;
        public PlayerController Controller => controller;
        public HarvestTool HarvestTool => harvestTool;
        public CarryStack CarryStack => carryStack;
        public ItemCollector ItemCollector => itemCollector;

        void Start()
        {
            Bind(PlayerStat.CarryCapacity, s => carryStack.Capacity = Mathf.Max(0, s.IntValue));
            Bind(PlayerStat.PickupRadius, s => itemCollector.Radius = s.Value);
        }

        void Bind(PlayerStat stat, Action<ModifiableStat> apply)
        {
            var modifiable = stats.Get(stat);
            apply(modifiable);
            modifiable.Changed += apply;
        }

        void Reset()
        {
            stats = GetComponent<PlayerStats>();
            controller = GetComponent<PlayerController>();
            harvestTool = GetComponentInChildren<HarvestTool>();
            carryStack = GetComponentInChildren<CarryStack>();
            itemCollector = GetComponentInChildren<ItemCollector>();
        }
    }
}

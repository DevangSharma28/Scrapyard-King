using System;
using ScrapYardKing.Core;
using ScrapYardKing.Harvest;
using UnityEngine;

namespace ScrapYardKing.Items
{
    /// <summary>
    /// The end of a belt that just tips its load onto the ground: every item it is given becomes a loose item again,
    /// thrown a little way from <see cref="point"/>. Used where a long haul ends in a work area instead of a machine
    /// (the Heavy Yard's belt drops scrap into the pit, where the Claw Crane, the porters and the player pick it up).
    /// It stops accepting while too much already lies around, so the belt backs up visibly instead of burying the pit.
    /// </summary>
    public sealed class ItemSpill : MonoBehaviour, IItemReceiver
    {
        [Tooltip("Where items leave the belt. Empty = this transform.")]
        [SerializeField] Transform point;
        [Tooltip("Items it passes on. Empty = any.")]
        [SerializeField] ItemDefinition[] accepts;
        [SerializeField] Vector3 throwVelocity = new(-0.6f, 2.6f, -2.4f);
        [SerializeField, Min(0f)] float scatter = 1.1f;
        [SerializeField, Min(0f)] float collectDelay = 0.35f;
        [Tooltip("No more items are tipped out while this many loose items lie in the world.")]
        [SerializeField, Min(1)] int maxLooseItems = 70;

        HarvestManager harvest;

        public bool CanAccept(ItemDefinition item)
        {
            if (item == null) return false;
            if (accepts != null && accepts.Length > 0 && Array.IndexOf(accepts, item) < 0) return false;
            if (harvest == null && !Services.TryGet(out harvest)) return false;
            return harvest.LooseCount < maxLooseItems;
        }

        public void Accept(WorldItem item)
        {
            if (harvest == null && !Services.TryGet(out harvest)) return;
            item.transform.position = point != null ? point.position : transform.position;
            Vector2 side = UnityEngine.Random.insideUnitCircle * scatter;
            harvest.Drop(item, throwVelocity + new Vector3(side.x, 0f, side.y), collectDelay);
        }
    }
}

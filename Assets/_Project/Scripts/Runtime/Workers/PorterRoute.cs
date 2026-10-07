using System;
using ScrapYardKing.Factory;
using ScrapYardKing.Items;
using UnityEngine;

namespace ScrapYardKing.Workers
{
    /// <summary>
    /// Carrier route. Two pickup modes:
    /// <list type="bullet">
    /// <item>Zones (Scrap Porter, blueprint "spawn → machine"): gather loose items inside any pickup zone.</item>
    /// <item>Pads (Delivery Helper "storage → sell desk", Market Runner "bins → market"): stand on a withdraw pad
    /// until loaded; with several pads the fullest one is picked, which keeps bins from overflowing.</item>
    /// </list>
    /// Either way the load goes to the drop-off pad, which does the transfer exactly as it does for the player.
    /// </summary>
    public sealed class PorterRoute : WorkerSite
    {
        [Serializable]
        public struct Zone
        {
            public Transform center;
            [Min(1f)] public float radius;
        }

        [Tooltip("Porter for scrap and stock carriers; Loader for the truck bay crew (same carrier loop, different role).")]
        [SerializeField] WorkerRole role = WorkerRole.Porter;
        [SerializeField] Transform pickupCenter;
        [SerializeField, Min(1f)] float pickupRadius = 10f;
        [Tooltip("More pickup zones, e.g. an expansion area. Zones behind a closed fence simply have no reachable items.")]
        [SerializeField] Zone[] extraZones;
        [Tooltip("When set, the worker loads from these withdraw pads instead of collecting loose items.")]
        [SerializeField] TransferPad[] pickupPads;
        [SerializeField] TransferPad dropoff;
        [Tooltip("More drop-offs for a carrier that serves several machines (the Smelter: one furnace per metal). The worker " +
                 "loads only what a built drop-off takes and walks to the one that takes what it carries.")]
        [SerializeField] TransferPad[] extraDropoffs;
        [Tooltip("Leave stock that a built machine takes as its input (the Market Runner: once a furnace for a metal stands, " +
                 "that metal belongs to the furnace, not to the market counter).")]
        [SerializeField] bool leaveMachineInputs;
        [Tooltip("Keep this many units of a machine's input back in a storage and carry only what is above it (the Delivery " +
                 "Helper: the Splitter comes first, the Sell Desk gets the surplus). 0 = carry everything.")]
        [SerializeField, Min(0)] int machineInputKeep;
        [Tooltip("When above 0, pickup pads are tried in list order and the first one holding at least this many usable units " +
                 "wins, before the fullest-pad rule (the Market Runner: ingots before raw metal).")]
        [SerializeField, Min(0)] int priorityLoad;

        public override WorkerRole Role => role;
        public Vector3 PickupCenter => pickupCenter != null ? pickupCenter.position : transform.position;
        public float PickupRadius => pickupRadius;
        public TransferPad Dropoff => dropoff;
        public bool UsesPad => pickupPads != null && pickupPads.Length > 0;

        /// <summary>The pickup pad with the most stock, or null when all are empty.</summary>
        bool HasChoice => extraDropoffs != null && extraDropoffs.Length > 0;

        /// <summary>True when a worker on this route delivers to <paramref name="receiver"/> (any of its drop-offs).</summary>
        public bool DropsAt(IItemReceiver receiver)
        {
            if (receiver == null) return false;
            if (dropoff != null && dropoff.Receiver == receiver) return true;
            if (extraDropoffs != null)
                foreach (var pad in extraDropoffs)
                    if (pad != null && pad.Receiver == receiver) return true;
            return false;
        }

        static bool IsMachineInput(ItemDefinition item)
        {
            foreach (var station in StationRegistry.All)
                if (station is Machine machine && machine.Definition != null && machine.Definition.Takes(item)) return true;
            return false;
        }

        /// <summary>
        /// Where the load goes: with several drop-offs, the first built one whose station takes something in the stack
        /// (by item type, so a full hopper keeps the worker waiting at the right machine); otherwise the route's one pad.
        /// </summary>
        public TransferPad DropoffFor(CarryStack stack)
        {
            if (!HasChoice || stack == null || stack.Count == 0) return dropoff;
            if (Takes(dropoff, stack)) return dropoff;
            foreach (var pad in extraDropoffs)
                if (Takes(pad, stack)) return pad;
            return dropoff;
        }

        static bool Takes(TransferPad pad, CarryStack stack) => pad != null && pad.isActiveAndEnabled && stack.Contains(item => Wants(pad, item));

        /// <summary>True when the pad's station handles this item at all (type, not free room).</summary>
        static bool Wants(TransferPad pad, ItemDefinition item)
        {
            if (pad == null || !pad.isActiveAndEnabled || pad.Receiver == null) return false;
            return pad.Receiver is Machine machine && machine.Definition != null ? machine.Definition.Takes(item) : pad.Receiver.CanAccept(item);
        }

        bool AnyDropoffWants(ItemDefinition item)
        {
            if (Wants(dropoff, item)) return true;
            foreach (var pad in extraDropoffs)
                if (Wants(pad, item)) return true;
            return false;
        }

        /// <summary>Units at this pad that the route's workers may carry (see the reserve and drop-off rules).</summary>
        int Usable(TransferPad pad)
        {
            int count = pad != null && pad.Source != null ? pad.Source.Count : 0;
            if (count == 0 || pad.Source is not Storage storage) return count;
            // Several drop-offs: only stock that a built one takes counts (no loading steel before there is a steel furnace).
            if (HasChoice) return storage.CountOf(AnyDropoffWants);
            if (!leaveMachineInputs && machineInputKeep <= 0) return count;
            int inputs = storage.CountOf(IsMachineInput);
            return count - inputs + (leaveMachineInputs ? 0 : Mathf.Max(0, inputs - machineInputKeep));
        }

        public TransferPad FullestPad()
        {
            TransferPad best = null;
            int bestCount = 0;
            if (pickupPads == null) return null;
            if (priorityLoad > 0)
                foreach (var pad in pickupPads)
                    if (Usable(pad) >= priorityLoad) return pad;
            foreach (var pad in pickupPads)
            {
                int count = Usable(pad);
                if (count <= bestCount) continue;
                bestCount = count;
                best = pad;
            }

            return best;
        }

        /// <summary>Radius that covers every zone from <paramref name="from"/>, for a single nearest-item search.</summary>
        public float SearchRadius(Vector3 from)
        {
            float r = Distance(from, PickupCenter) + pickupRadius;
            if (extraZones != null)
                foreach (var z in extraZones)
                    if (z.center != null) r = Mathf.Max(r, Distance(from, z.center.position) + z.radius);
            return r;
        }

        public bool InAnyZone(Vector3 position)
        {
            if (Distance(position, PickupCenter) <= pickupRadius) return true;
            if (extraZones == null) return false;
            foreach (var z in extraZones)
                if (z.center != null && Distance(position, z.center.position) <= z.radius) return true;
            return false;
        }

        static float Distance(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.6f);
            if (UsesPad)
            {
                foreach (var pad in pickupPads)
                    if (pad != null) Gizmos.DrawLine(pad.transform.position, dropoff != null ? dropoff.transform.position : transform.position);
            }
            else
            {
                Gizmos.DrawWireSphere(PickupCenter, pickupRadius);
                if (extraZones != null)
                    foreach (var z in extraZones)
                        if (z.center != null) Gizmos.DrawWireSphere(z.center.position, z.radius);
                if (dropoff != null) Gizmos.DrawLine(PickupCenter, dropoff.transform.position);
            }
        }
    }
}

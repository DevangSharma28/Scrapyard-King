using System;
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

        [SerializeField] Transform pickupCenter;
        [SerializeField, Min(1f)] float pickupRadius = 10f;
        [Tooltip("More pickup zones, e.g. an expansion area. Zones behind a closed fence simply have no reachable items.")]
        [SerializeField] Zone[] extraZones;
        [Tooltip("When set, the worker loads from these withdraw pads instead of collecting loose items.")]
        [SerializeField] TransferPad[] pickupPads;
        [SerializeField] TransferPad dropoff;

        public override WorkerRole Role => WorkerRole.Porter;
        public Vector3 PickupCenter => pickupCenter != null ? pickupCenter.position : transform.position;
        public float PickupRadius => pickupRadius;
        public TransferPad Dropoff => dropoff;
        public bool UsesPad => pickupPads != null && pickupPads.Length > 0;

        /// <summary>The pickup pad with the most stock, or null when all are empty.</summary>
        public TransferPad FullestPad()
        {
            TransferPad best = null;
            int bestCount = 0;
            if (pickupPads == null) return null;
            foreach (var pad in pickupPads)
            {
                int count = pad != null && pad.Source != null ? pad.Source.Count : 0;
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

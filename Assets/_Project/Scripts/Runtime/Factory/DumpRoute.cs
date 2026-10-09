using System.Collections.Generic;
using UnityEngine;

namespace ScrapYardKing.Factory
{
    /// <summary>
    /// The loop the dump trucks drive: a list of points with one loading bay (the excavator fills the truck there), one
    /// tip spot (the load goes onto the dump pile) and one holding point where a truck waits while another uses the bay.
    /// <see cref="reverseInto"/> marks points a truck backs into (the bay is a dead end). The stretch from the holding
    /// point to the point after the bay is one-truck-at-a-time. Points live outside popped-in content (read every frame).
    /// </summary>
    public sealed class DumpRoute : MonoBehaviour
    {
        [SerializeField] Transform[] points;
        [Tooltip("Same length as points: the truck backs into this point (faces away from where it goes).")]
        [SerializeField] bool[] reverseInto;
        [SerializeField] int loadIndex;
        [SerializeField] int tipIndex;
        [SerializeField] int holdIndex;

        readonly List<DumpTruck> trucks = new();
        DumpTruck bayOwner;

        public int Count => points != null ? points.Length : 0;
        public int LoadIndex => loadIndex;
        public int TipIndex => tipIndex;
        public int HoldIndex => holdIndex;
        public Vector3 Point(int i) => points[(i % Count + Count) % Count].position;
        public bool Reverse(int i) => reverseInto != null && i >= 0 && i < reverseInto.Length && reverseInto[i];

        /// <summary>The truck standing in the bay with room on its bed (the excavator's target), or null.</summary>
        public DumpTruck Loading
        {
            get
            {
                foreach (var t in trucks)
                    if (t != null && t.IsLoading) return t;
                return null;
            }
        }

        public void Join(DumpTruck truck)
        {
            if (!trucks.Contains(truck)) trucks.Add(truck);
        }

        public void Leave(DumpTruck truck)
        {
            trucks.Remove(truck);
            if (bayOwner == truck) bayOwner = null;
        }

        /// <summary>A truck at the holding point asks for the bay stretch; true when it is free (or already its own).</summary>
        public bool TryClaimBay(DumpTruck truck)
        {
            if (bayOwner != null && bayOwner != truck && bayOwner.isActiveAndEnabled) return false;
            bayOwner = truck;
            return true;
        }

        public void ReleaseBay(DumpTruck truck)
        {
            if (bayOwner == truck) bayOwner = null;
        }

        void OnDrawGizmosSelected()
        {
            if (points == null) return;
            for (int i = 0; i < points.Length; i++)
            {
                if (points[i] == null) continue;
                Gizmos.color = i == loadIndex ? Color.green : i == tipIndex ? Color.red : i == holdIndex ? Color.yellow : Color.cyan;
                Gizmos.DrawWireSphere(points[i].position, 0.4f);
                var next = points[(i + 1) % points.Length];
                if (next != null) Gizmos.DrawLine(points[i].position, next.position);
            }
        }
    }
}

using UnityEngine;

namespace ScrapYardKing.Harvest
{
    /// <summary>Anything that can cut scrap: the player now, drones or cutter upgrades later.</summary>
    public interface IHarvesterStats
    {
        /// <summary>Damage per hit.</summary>
        float CutPower { get; }

        /// <summary>Hits per second.</summary>
        float CutRate { get; }

        /// <summary>Max distance from the cutter to the object's surface.</summary>
        float CutRange { get; }
    }

    public readonly struct ScrapHit
    {
        public readonly float Damage;
        public readonly Vector3 Point;
        /// <summary>Horizontal direction from the cutter toward the object.</summary>
        public readonly Vector3 Direction;
        public readonly Object Source;

        public ScrapHit(float damage, Vector3 point, Vector3 direction, Object source)
        {
            Damage = damage;
            Point = point;
            Direction = direction;
            Source = source;
        }
    }

    /// <summary>Pure helpers for splitting an object's drops between detachable parts and the final burst.</summary>
    public static class DropMath
    {
        /// <summary>Pieces released when part <paramref name="partIndex"/> comes off. Remainders go to the earliest parts.</summary>
        public static int PiecesForPart(int totalPieces, float partShare, int partCount, int partIndex)
        {
            if (partCount <= 0 || partIndex < 0 || partIndex >= partCount || totalPieces <= 0) return 0;

            // Epsilon keeps 20 * 0.35f at 7 instead of flooring float error down to 6.
            int pool = Mathf.FloorToInt(totalPieces * Mathf.Clamp01(partShare) + 0.0001f);
            int baseCount = pool / partCount;
            int remainder = pool % partCount;
            return baseCount + (partIndex < remainder ? 1 : 0);
        }

        /// <summary>Normalised health at or below which part <paramref name="partIndex"/> detaches. Evenly spaced, never 0 or 1.</summary>
        public static float PartThreshold(int partIndex, int partCount) => 1f - (partIndex + 1f) / (partCount + 1f);
    }
}

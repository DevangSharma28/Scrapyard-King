using System.Collections.Generic;

namespace ScrapYardKing.Factory
{
    /// <summary>
    /// Smooth weighted round-robin: hands out indices in proportion to their weights, evenly interleaved instead of
    /// random streaks (weights 7:3 give I I C I I C I I C I...). Used by sorters so the split reads as steady.
    /// </summary>
    public sealed class WeightedSpread
    {
        readonly float[] weights;
        readonly float[] credit;
        readonly float total;

        public WeightedSpread(IReadOnlyList<float> weights)
        {
            this.weights = new float[weights.Count];
            credit = new float[weights.Count];
            for (int i = 0; i < weights.Count; i++)
            {
                this.weights[i] = weights[i] > 0f ? weights[i] : 0f;
                total += this.weights[i];
            }
        }

        public int Count => weights.Length;

        /// <summary>Next index, or -1 when every weight is zero.</summary>
        public int Next()
        {
            if (total <= 0f) return -1;
            int best = -1;
            for (int i = 0; i < weights.Length; i++)
            {
                credit[i] += weights[i];
                if (weights[i] > 0f && (best < 0 || credit[i] > credit[best])) best = i;
            }

            credit[best] -= total;
            return best;
        }
    }
}

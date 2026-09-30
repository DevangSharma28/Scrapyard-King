using UnityEngine;

namespace ScrapYardKing.Core
{
    public static class Easing
    {
        public static float OutCubic(float t)
        {
            t = 1f - Mathf.Clamp01(t);
            return 1f - t * t * t;
        }

        public static float InQuad(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t;
        }

        public static float OutBack(float t, float overshoot = 1.70158f)
        {
            t = Mathf.Clamp01(t) - 1f;
            return 1f + t * t * ((overshoot + 1f) * t + overshoot);
        }

        /// <summary>0 at t=0 and t=1, 1 at t=0.5. Used for arc heights.</summary>
        public static float Arc(float t)
        {
            t = Mathf.Clamp01(t);
            return 4f * t * (1f - t);
        }

        /// <summary>Framerate-independent exponential smoothing factor.</summary>
        public static float Damp(float sharpness, float deltaTime) => 1f - Mathf.Exp(-sharpness * deltaTime);
    }
}

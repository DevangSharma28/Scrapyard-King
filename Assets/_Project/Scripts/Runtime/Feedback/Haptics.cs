using ScrapYardKing.Settings;
using UnityEngine;

namespace ScrapYardKing.Feedback
{
    /// <summary>
    /// Vibrations for taps and rewards, behind the Vibration setting and a rate limit. Unity's only portable call is one
    /// long buzz, which is wrong for a button tap: <see cref="Light"/> therefore does nothing until a native haptics
    /// plugin fills it in, and <see cref="Heavy"/> (the buzz) is kept for rare big moments (a big reward, a purchase of
    /// diamonds). Callers already ask for the right strength, so a plugin only changes these two bodies.
    /// </summary>
    public static class Haptics
    {
        static float nextAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => nextAt = 0f;

        /// <summary>Tap-sized tick. Needs a native haptics plugin; no-op until one is added.</summary>
        public static void Light() { }

        /// <summary>One buzz for a big moment, at most every 1.5 s.</summary>
        public static void Heavy() => Buzz(1.5f);

        static void Buzz(float minGap)
        {
            if (!GameSettings.Vibration || Time.unscaledTime < nextAt) return;
            nextAt = Time.unscaledTime + minGap;
#if UNITY_ANDROID || UNITY_IOS
            if (Application.isMobilePlatform) Handheld.Vibrate();
#endif
        }
    }
}

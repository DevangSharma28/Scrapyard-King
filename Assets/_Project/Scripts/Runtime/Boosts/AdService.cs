using System;
using System.Collections;
using ScrapYardKing.Core;
using UnityEngine;

namespace ScrapYardKing.Boosts
{
    /// <summary>What an ad network has to offer the game: one rewarded video at a time.</summary>
    public interface IRewardedAdProvider
    {
        bool IsReady { get; }

        /// <summary>Shows a rewarded video for <paramref name="placement"/>; calls back with true when the reward is earned.</summary>
        void Show(string placement, Action<bool> done);
    }

    /// <summary>
    /// The one door to rewarded video. Game code asks <see cref="ShowRewarded"/> and gets the reward callback; which
    /// network plays the video is the provider's business. Until an SDK is integrated (<see cref="SetProvider"/>), a
    /// built-in stand-in grants the reward after a short pause, so every offer works in the Editor and in test builds.
    /// It never shows anything by itself: an ad plays only because the player tapped an offer.
    /// </summary>
    [DefaultExecutionOrder(-445)]
    public sealed class AdService : ServiceBehaviour<AdService>
    {
        [Tooltip("Stand-in for a video while no ad SDK is integrated, seconds (unscaled).")]
        [SerializeField, Min(0f)] float simulatedSeconds = 0.8f;

        IRewardedAdProvider provider;

        /// <summary>A video is on screen (or the stand-in pause is running).</summary>
        public bool Busy { get; private set; }

        /// <summary>True while the built-in stand-in is used instead of a real network.</summary>
        public bool Simulated => provider == null;
        public bool Ready => !Busy && (provider == null || provider.IsReady);
        public int RewardsGranted { get; private set; }

        /// <summary>Plugs in a real ad network. Call once at startup from the SDK's own bootstrap.</summary>
        public void SetProvider(IRewardedAdProvider adProvider) => provider = adProvider;

        /// <summary>Plays a rewarded video and runs <paramref name="onReward"/> if the player earned it.</summary>
        public void ShowRewarded(string placement, Action onReward) => ShowRewarded(placement, onReward, null);

        /// <summary>As above; <paramref name="onFail"/> runs when the video was closed early or failed to play.</summary>
        public void ShowRewarded(string placement, Action onReward, Action onFail)
        {
            if (!Ready || onReward == null)
            {
                onFail?.Invoke();
                return;
            }

            Busy = true;
            void Done(bool earned)
            {
                Busy = false;
                if (!earned)
                {
                    onFail?.Invoke();
                    return;
                }

                RewardsGranted++;
                onReward();
            }

            if (provider != null) provider.Show(placement, Done);
            else StartCoroutine(Simulate(Done));
        }

        IEnumerator Simulate(Action<bool> done)
        {
            yield return new WaitForSecondsRealtime(simulatedSeconds);
            done(true);
        }
    }
}

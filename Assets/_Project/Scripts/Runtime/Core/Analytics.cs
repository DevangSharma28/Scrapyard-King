using System.Collections.Generic;
using UnityEngine;

namespace ScrapYardKing.Core
{
    /// <summary>Something that forwards game events to an analytics backend (Firebase, GameAnalytics, ...).</summary>
    public interface IAnalyticsSink
    {
        void Log(string eventName, IReadOnlyDictionary<string, object> data);
    }

    /// <summary>
    /// The game's one analytics door. Game code logs named events with a few values; which backend receives them is
    /// decided by the sinks plugged in at startup (<see cref="AddSink"/>). With no sink nothing leaves the device; the
    /// counts per event are kept for the developer economy window.
    /// </summary>
    public static class Analytics
    {
        static readonly List<IAnalyticsSink> sinks = new();
        static readonly Dictionary<string, int> counts = new();
        static readonly Dictionary<string, object> scratch = new();

        /// <summary>Also prints every event to the console (Editor and development builds only).</summary>
        public static bool EchoToConsole { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            sinks.Clear();
            counts.Clear();
            EchoToConsole = false;
        }

        public static void AddSink(IAnalyticsSink sink)
        {
            if (sink != null && !sinks.Contains(sink)) sinks.Add(sink);
        }

        public static void RemoveSink(IAnalyticsSink sink) => sinks.Remove(sink);

        /// <summary>How often <paramref name="eventName"/> was logged this session.</summary>
        public static int Count(string eventName) => counts.TryGetValue(eventName, out int n) ? n : 0;

        public static IReadOnlyDictionary<string, int> Counts => counts;

        public static void Log(string eventName, params (string key, object value)[] data)
        {
            if (string.IsNullOrEmpty(eventName)) return;
            counts[eventName] = Count(eventName) + 1;
            if (sinks.Count == 0 && !EchoToConsole) return;

            scratch.Clear();
            if (data != null)
                foreach (var (key, value) in data)
                    if (!string.IsNullOrEmpty(key)) scratch[key] = value;

            foreach (var sink in sinks)
            {
                try { sink.Log(eventName, scratch); }
                catch (System.Exception e) { Debug.LogException(e); }
            }

            if (EchoToConsole && Debug.isDebugBuild)
            {
                var line = new System.Text.StringBuilder("[Analytics] ").Append(eventName);
                foreach (var pair in scratch) line.Append(' ').Append(pair.Key).Append('=').Append(pair.Value);
                Debug.Log(line.ToString());
            }
        }
    }

    /// <summary>Event names, so a typo cannot split one event into two in the dashboard.</summary>
    public static class AnalyticsEvents
    {
        public const string ShopOpened = "shop_opened";
        public const string DiamondEarned = "diamond_earned";
        public const string DiamondSpent = "diamond_spent";
        public const string UpgradePurchased = "upgrade_purchased";
        public const string RvOffered = "rv_offered";
        public const string RvCompleted = "rv_completed";
        public const string RvFailed = "rv_failed";
        public const string RvRewardGranted = "rv_reward_granted";
        public const string IapOpened = "iap_opened";
        public const string IapInitiated = "iap_initiated";
        public const string IapCompleted = "iap_completed";
        public const string IapFailed = "iap_failed";
        public const string OfflineClaimed = "offline_reward_claimed";
        public const string OfflineDoubled = "offline_2x_accepted";
        public const string ContractCompleted = "contract_completed";
        public const string ContractDoubled = "contract_2x_accepted";
        public const string BoostActivated = "boost_activated";
        public const string OfferShown = "premium_offer_shown";
        public const string OfferPurchased = "premium_offer_purchased";
        public const string RewardClaimed = "reward_claimed";
        public const string SettingsChanged = "settings_changed";
        public const string ProgressReset = "progress_reset";
    }
}

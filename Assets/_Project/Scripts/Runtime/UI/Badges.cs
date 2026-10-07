using System;
using System.Collections.Generic;
using ScrapYardKing.Core;
using ScrapYardKing.Economy;
using ScrapYardKing.Progression;
using ScrapYardKing.Shop;
using UnityEngine;

namespace ScrapYardKing.UI
{
    /// <summary>
    /// One badge per navigation destination ("shop", "missions", "daily"), so the HUD never sprouts dots everywhere. A
    /// destination's count comes from one provider; <see cref="BadgeDot"/> shows it. The default providers are wired here:
    /// missions = claimable dailies and achievement tiers, daily = today's reward not taken, shop = free diamonds ready.
    /// Extras (shop, missions, daily rewards) unlock together with the first diamond.
    /// </summary>
    public static class Badges
    {
        static readonly Dictionary<string, Func<int>> providers = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => providers.Clear();

        public static void Register(string key, Func<int> provider) => providers[key] = provider;

        public static int Count(string key)
        {
            if (!ExtrasUnlocked) return 0;
            if (providers.TryGetValue(key, out var p)) return p();
            return key switch
            {
                "missions" => Services.TryGet(out MissionManager m) ? m.Claimable : 0,
                "daily" => Services.TryGet(out DailyRewardManager d) && d.CanClaim ? 1 : 0,
                "shop" => Services.TryGet(out StoreService s) && s.FreeReady ? 1 : 0,
                _ => 0
            };
        }

        /// <summary>The shop, missions and daily rewards are shown from the first diamond on (not in the first minutes).</summary>
        public static bool ExtrasUnlocked =>
            (Services.TryGet(out EconomyManager e) && e.Premium > 0) || (Services.TryGet(out DiamondRewards r) && r.DiamondsRevealed);
    }
}

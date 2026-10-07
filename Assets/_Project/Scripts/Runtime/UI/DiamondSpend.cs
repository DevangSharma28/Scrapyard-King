using System;
using ScrapYardKing.Core;
using ScrapYardKing.Economy;
using UnityEngine;

namespace ScrapYardKing.UI
{
    /// <summary>
    /// The one way UI spends diamonds. Small spends happen on the tap; spends above
    /// <see cref="PremiumEconomyConfig.ConfirmAbove"/> show what is bought and what it saves first ("SPEED UP? Finish
    /// 12m 30s for 45, SAVE 12m 30s"). Not enough diamonds says so plainly (and, once there is a shop, offers it). The
    /// wallet takes the diamonds once (<see cref="EconomyManager.TrySpendPremium"/>) before anything is granted.
    /// </summary>
    public static class DiamondSpend
    {
        /// <summary>Called when the player lacks diamonds and a shop exists; set by the shop when it starts.</summary>
        public static Action<long> OpenShop;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => OpenShop = null;

        /// <param name="cost">Diamonds.</param>
        /// <param name="question">Title of the confirmation, e.g. "SPEED UP?".</param>
        /// <param name="what">What is bought, e.g. "Finish the truck now".</param>
        /// <param name="saves">Value line, e.g. "SAVE 12m 30s" (optional).</param>
        /// <param name="reason">Analytics reason, e.g. "instant_truck".</param>
        /// <param name="icon">Picture of what is bought.</param>
        /// <param name="onSpent">Runs once, after the diamonds have left the wallet.</param>
        public static void Request(int cost, string question, string what, string saves, string reason, Sprite icon, Action onSpent)
        {
            if (cost <= 0 || !Services.TryGet(out EconomyManager economy)) return;
            Services.TryGet(out PopupManager popups);
            var config = Services.TryGet(out DiamondRewards rewards) ? rewards.Config : null;

            if (!economy.CanAffordPremium(cost))
            {
                long missing = cost - economy.Premium;
                if (popups == null) return;
                bool shop = OpenShop != null;
                popups.Show(new PopupRequest
                {
                    Style = PopupStyle.Info,
                    Title = "NOT ENOUGH",
                    Subtitle = $"YOU NEED {missing} MORE DIAMONDS",
                    Icon = rewards != null ? rewards.DiamondIcon : null,
                    PrimaryLabel = shop ? "GET DIAMONDS" : "OK",
                    OnPrimary = _ => { if (shop) OpenShop(missing); },
                    Closable = shop
                });
                return;
            }

            void Spend()
            {
                if (economy.TrySpendPremium(cost, reason)) onSpent?.Invoke();
            }

            if (popups == null || config == null || !config.NeedsConfirm(cost))
            {
                Spend();
                return;
            }

            popups.Show(new PopupRequest
            {
                Style = PopupStyle.Confirm,
                Title = question,
                Subtitle = what,
                Icon = icon,
                Amount = saves,
                AmountColor = new Color(0.62f, 1f, 0.5f),
                PrimaryLabel = $"SPEND {cost}",
                PrimaryIcon = rewards != null ? rewards.DiamondIcon : null,
                OnPrimary = _ => Spend(),
                Closable = true
            });
        }

        /// <summary>"12m 30s", "45s", "1h 05m".</summary>
        public static string Duration(float seconds)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            if (s >= 3600) return $"{s / 3600}h {s % 3600 / 60:00}m";
            if (s >= 60) return $"{s / 60}m {s % 60:00}s";
            return $"{s}s";
        }
    }
}

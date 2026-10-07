using System;
using ScrapYardKing.Boosts;
using ScrapYardKing.Core;
using ScrapYardKing.Economy;
using UnityEngine;

namespace ScrapYardKing.UI
{
    /// <summary>
    /// "WELCOME BACK!": after loading, if <see cref="IdleIncomeManager"/> has cash waiting, one reward card shows how
    /// long the yard worked, at what rate, and the total rolling up. CLAIM pays it; "2X" plays a video first (the
    /// strongest video placement, offered once per return through <see cref="OfferDirector.OfflineDouble"/>). If the
    /// video fails, the plain amount is paid anyway. Unclaimed cash stays in the save until claimed.
    /// </summary>
    public sealed class WelcomeBack : MonoBehaviour
    {
        [SerializeField] Sprite cashIcon;
        [Tooltip("Seconds after start before checking (the save is loaded and the loading cover is fading).")]
        [SerializeField, Min(0f)] float delay = 1f;

        bool done;

        void Update()
        {
            if (done || Time.unscaledTime < delay) return;
            if (!Services.TryGet(out IdleIncomeManager idle) || !Services.TryGet(out PopupManager popups)) return;
            done = true;
            long pending = idle.Pending;
            if (pending <= 0) return;

            Services.TryGet(out OfferDirector director);
            var offer = director != null ? director.OfflineDouble : null;
            bool canDouble = director != null && director.Ready(offer);
            int minutes = Mathf.Max(1, (int)(idle.AwaySeconds / 60.0));
            string away = minutes >= 60 ? $"{minutes / 60} H {minutes % 60:00} MIN" : $"{minutes} MIN";
            double perMinute = idle.RatePerSecond * 60.0 * idle.Efficiency;
            if (canDouble) Analytics.Log(AnalyticsEvents.RvOffered, ("offer", offer.Id));

            popups.Show(new PopupRequest
            {
                Style = PopupStyle.Reward,
                Title = "WELCOME BACK!",
                Subtitle = "YOUR YARD KEPT WORKING FOR " + away,
                Body = $"${CurrencyFormat.Short(perMinute)} A MINUTE WHILE YOU WERE AWAY\n(UP TO {idle.MaxHours:0} HOURS)",
                Icon = cashIcon,
                Amount = "$" + CurrencyFormat.Short(pending),
                AmountColor = new Color(0.62f, 1f, 0.5f),
                CountTo = pending,
                AmountFormat = x => "$" + CurrencyFormat.Short(x),
                PrimaryLabel = "CLAIM",
                PrimarySub = "$" + CurrencyFormat.Short(pending),
                OnPrimary = origin => idle.Claim(false, origin),
                SecondaryLabel = canDouble ? "2X" : null,
                SecondarySub = "$" + CurrencyFormat.Short(pending * 2),
                SecondaryIcon = director != null ? director.VideoIcon : null,
                SecondaryIsBetter = true,
                OnSecondary = origin =>
                {
                    if (!director.Watch(offer, () => idle.Claim(true, origin), () => idle.Claim(false, origin)))
                        idle.Claim(false, origin);
                }
            });
        }
    }
}

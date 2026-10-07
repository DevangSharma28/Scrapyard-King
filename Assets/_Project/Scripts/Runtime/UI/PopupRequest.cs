using System;
using ScrapYardKing.Economy;
using UnityEngine;

namespace ScrapYardKing.UI
{
    public enum PopupStyle
    {
        /// <summary>Something was earned: rays, sparkles, the icon pops, the amount rolls up.</summary>
        Reward,
        /// <summary>A question with a price (spend diamonds?).</summary>
        Confirm,
        /// <summary>An action that cannot be undone: red, primary button must be held.</summary>
        Danger,
        /// <summary>Plain information (help, credits, a message).</summary>
        Info
    }

    /// <summary>
    /// Everything one popup shows. Fill in what is needed; empty fields hide their part of the card. Button callbacks
    /// receive the screen point of the card's icon, so a reward can fly from there to the HUD.
    /// </summary>
    public sealed class PopupRequest
    {
        public PopupStyle Style = PopupStyle.Reward;
        public string Title;
        public string Subtitle;
        /// <summary>Several lines of smaller text (help, what a reset deletes).</summary>
        public string Body;
        public Sprite Icon;
        public bool SpinIcon;
        /// <summary>Big number under the icon, e.g. "+25" or "45".</summary>
        public string Amount;
        public Color AmountColor = Color.white;
        /// <summary>Optional: count the amount up to this value (with <see cref="AmountFormat"/>).</summary>
        public long CountTo;
        public Func<double, string> AmountFormat;

        public string PrimaryLabel = "CLAIM";
        /// <summary>Second line on the primary button (a price, "WATCH").</summary>
        public string PrimarySub;
        public Sprite PrimaryIcon;
        public Action<CurrencyOrigin> OnPrimary;

        public string SecondaryLabel;
        public string SecondarySub;
        public Sprite SecondaryIcon;
        public Action<CurrencyOrigin> OnSecondary;
        /// <summary>Make the secondary button the green one (a rewarded-video double that is the better deal).</summary>
        public bool SecondaryIsBetter;

        /// <summary>Show a close X; closing runs <see cref="OnClose"/>.</summary>
        public bool Closable;
        public Action OnClose;

        /// <summary>Seconds to wait before showing (let a world reveal play first).</summary>
        public float Delay;
        /// <summary>A time-sensitive card (an offer about the truck that just left) is dropped if it would show more
        /// than this many seconds late. 0 = it waits as long as needed (rewards always wait).</summary>
        public float MaxWait;
        /// <summary>Played on the reveal.</summary>
        public Feedback.SfxDefinition Sound;
    }
}

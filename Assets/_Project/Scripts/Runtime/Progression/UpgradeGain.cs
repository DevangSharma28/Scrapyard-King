using System.Globalization;
using System.Text.RegularExpressions;
using ScrapYardKing.Core;
using ScrapYardKing.Factory;

namespace ScrapYardKing.Progression
{
    /// <summary>
    /// Turns an upgrade's "next effect" text ("40 → 52/min", "60 → 90", "8 → 11 m reach") into the line that floats
    /// out of the station after the purchase ("+30% SPEED"), so the player reads what just got better, not two numbers.
    /// Effects without an arrow ("+1 WORKER", "FEEDS THE CRUSHER") are shown as they are.
    /// </summary>
    public static class UpgradeGain
    {
        static readonly Regex Number = new(@"\d+(?:\.\d+)?", RegexOptions.Compiled);

        public static string Describe(IUpgradeable upgrade, string effect)
        {
            if (string.IsNullOrWhiteSpace(effect)) return string.Empty;
            int arrow = effect.IndexOf('→');
            if (arrow < 0) return effect.ToUpperInvariant();
            var before = Number.Matches(effect.Substring(0, arrow));
            var after = Number.Match(effect, arrow);
            if (before.Count == 0 || !after.Success) return effect.ToUpperInvariant();
            float a = float.Parse(before[before.Count - 1].Value, CultureInfo.InvariantCulture);
            float b = float.Parse(after.Value, CultureInfo.InvariantCulture);
            if (a <= 0f || b <= a) return effect.ToUpperInvariant();
            int percent = (int)System.Math.Round((b / a - 1f) * 100f);
            return $"+{percent}% {Word(upgrade, effect)}".Trim();
        }

        static string Word(IUpgradeable upgrade, string effect) => upgrade switch
        {
            Machine => "SPEED",
            SellDesk => "SALES",
            Storage => "CAPACITY",
            TruckBay => "CAPACITY",
            ClawCrane => "REACH",
            _ => effect.Contains("/s") || effect.Contains("m/s") ? "SPEED" : string.Empty
        };
    }
}

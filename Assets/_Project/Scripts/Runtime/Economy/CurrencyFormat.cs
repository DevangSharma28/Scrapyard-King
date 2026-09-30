using System.Globalization;

namespace ScrapYardKing.Economy
{
    public static class CurrencyFormat
    {
        static readonly string[] Suffixes = { "", "K", "M", "B", "T" };

        /// <summary>Compact mobile-style amount: 950, 1.2K, 12.4K, 3.5M.</summary>
        public static string Short(double value)
        {
            if (value < 1000d) return ((long)value).ToString(CultureInfo.InvariantCulture);

            int tier = 0;
            while (value >= 1000d && tier < Suffixes.Length - 1)
            {
                value /= 1000d;
                tier++;
            }

            string format = value < 100d ? "0.#" : "0";
            return value.ToString(format, CultureInfo.InvariantCulture) + Suffixes[tier];
        }
    }
}

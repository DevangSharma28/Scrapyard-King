using NUnit.Framework;
using ScrapYardKing.Progression;

namespace ScrapYardKing.Tests
{
    /// <summary>The line that floats out of a station after an upgrade.</summary>
    public sealed class UpgradeGainTests
    {
        [Test]
        public void Arrow_BecomesPercent()
        {
            Assert.AreEqual("+20%", UpgradeGain.Describe(null, "200 → 240/min"));
            Assert.AreEqual("+50%", UpgradeGain.Describe(null, "60 → 90"));
        }

        [Test]
        public void Decimals_AndExtraParts_AreRead()
        {
            Assert.AreEqual("+7% SPEED", UpgradeGain.Describe(null, "6.8 → 7.3 m/s"));
            Assert.AreEqual("+50%", UpgradeGain.Describe(null, "60 → 90 · x1.2"));
        }

        [Test]
        public void NoArrow_OrNoGain_ShowsTheTextItself()
        {
            Assert.AreEqual("+1 WORKER", UpgradeGain.Describe(null, "+1 worker"));
            Assert.AreEqual("90 → 60", UpgradeGain.Describe(null, "90 → 60"));
            Assert.AreEqual(string.Empty, UpgradeGain.Describe(null, ""));
        }
    }
}

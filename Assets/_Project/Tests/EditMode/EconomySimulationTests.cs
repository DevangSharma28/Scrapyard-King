using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ScrapYardKing.EditorTools;

namespace ScrapYardKing.Tests
{
    /// <summary>
    /// The brief's economy guardrails (sections 42, 68–71), checked by playing the four player profiles forward over
    /// 30 days on the real data (<see cref="EconomySimulator"/>). A failure here means a data change broke the economy's
    /// shape: free players hoarding or starving, videos carrying progress, purchases not helping. The full table goes to
    /// the test output and to Docs/ECONOMY.md.
    /// </summary>
    public sealed class EconomySimulationTests
    {
        EconomySimulator sim;
        Dictionary<string, List<EconomySimulator.Snapshot>> runs;

        static EconomySimulator.Profile AllVideos => new()
            { name = "AllVideos", sessionsPerDay = 3, sessionMinutes = 20f, videoShare = 1f, dailyShare = 1f };

        [OneTimeSetUp]
        public void Run()
        {
            sim = EconomySimulator.FromProject();
            Assert.IsNotNull(sim.premium, "run U1_Build.Assets");
            Assert.IsNotNull(sim.shop, "run U2_Build.Assets");
            Assert.IsNotNull(sim.missions, "run U4_Build.Assets");
            Assert.IsNotEmpty(sim.hudOffers, "run R5_Build.Assets");
            var profiles = EconomySimulator.StandardProfiles().Append(AllVideos).ToArray();
            runs = profiles.ToDictionary(p => p.name, p => sim.Simulate(p));
            TestContext.WriteLine(sim.Report(profiles));
        }

        EconomySimulator.Snapshot At(string profile, string label) => runs[profile].First(s => s.label == label);

        [Test]
        public void EveryCheckpointIsMeasured()
        {
            foreach (var run in runs.Values)
                CollectionAssert.AreEqual(EconomySimulator.Checkpoints.Select(c => c.label), run.Select(s => s.label));
        }

        [Test]
        public void FreePlayer_SeesTheWholeCoreGame()
        {
            float day = At("Free", "30 d").coreDoneDay;
            Assert.GreaterOrEqual(day, 0f, "a free player never reached the dockyard");
            Assert.LessOrEqual(day, 3f, "a free player needs more than three days for the first-session arc");
        }

        [Test]
        public void FreeDiamonds_NeitherAPileNorStingy()
        {
            var hour = At("Free", "1 h");
            var week = At("Free", "7 d");
            var month = At("Free", "30 d");
            Assert.LessOrEqual(hour.diamondsEarned, 400, "too many free diamonds in the first hour of play");
            Assert.That(month.diamondsEarned, Is.InRange(600, 1600), "free diamonds over 30 days");
            double perDay = (month.diamondsEarned - week.diamondsEarned) / 23.0;
            Assert.That(perDay, Is.InRange(10, 50), "free diamonds a day after the first week");
            Assert.LessOrEqual(month.maxBalance, 500, "a free player who spends still piles up diamonds");
        }

        [Test]
        public void FreeDiamonds_HelpButNeverCarry()
        {
            foreach (var s in runs["Free"]) Assert.LessOrEqual(s.DiamondShare, 0.2, s.label);
            Assert.GreaterOrEqual(At("Free", "30 d").DiamondShare, 0.03, "free diamonds are worthless");
        }

        [Test]
        public void Videos_AccelerateButNeverCarry()
        {
            foreach (var s in runs["Free"]) Assert.LessOrEqual(s.VideoShare, 0.35, "Free " + s.label);
            foreach (var s in runs["AllVideos"]) Assert.LessOrEqual(s.VideoShare, 0.5, "AllVideos " + s.label);
        }

        [Test]
        public void HudOffers_AreNotSpam()
        {
            var s = At("Free", "30 d");
            Assert.LessOrEqual(s.hudOffers / (s.playedMinutes / 60.0), 24.0, "HUD video offers per hour of play");
        }

        [Test]
        public void ActivePlay_StillMatters()
        {
            foreach (var p in new[] { "Free", "Light", "Mid", "AllVideos" })
                Assert.GreaterOrEqual(At(p, "30 d").PlayedShare, 0.15, p + ": time away and rewards make playing pointless");
        }

        [Test]
        public void Spenders_GetMoreHelp_InOrder()
        {
            double Paid(string p) => At(p, "7 d").DiamondShare + At(p, "7 d").IapShare;
            Assert.GreaterOrEqual(Paid("Light"), Paid("Free"));
            Assert.GreaterOrEqual(Paid("Mid"), Paid("Light"));
            Assert.GreaterOrEqual(Paid("High"), Paid("Mid"));
        }

        [Test]
        public void CashForDiamonds_KeepsItsWorthAtEveryLevel()
        {
            for (int level = 1; level <= sim.progression.MaxLevel; level++)
                Assert.That(sim.CashDiamondMinutes(level), Is.InRange(0.1, 0.4), $"minutes of income per diamond at Lv {level}");
        }

        [Test]
        public void Bundles_StayInOneValueBand()
        {
            var bundles = sim.shop.Bundles.Where(b => b != null && b.Enabled).ToArray();
            float smallest = bundles.First().DiamondsPerUsd, biggest = bundles.Last().DiamondsPerUsd;
            Assert.LessOrEqual(biggest, smallest * 2.5f, "the biggest bundle makes the small ones pointless");
        }

        [Test]
        public void Simulation_IsDeterministic()
        {
            var again = sim.Simulate(EconomySimulator.StandardProfiles()[0]);
            var first = runs["Free"];
            for (int i = 0; i < first.Count; i++)
            {
                Assert.AreEqual(first[i].diamondsEarned, again[i].diamondsEarned, 1e-6);
                Assert.AreEqual(first[i].progressMinutes, again[i].progressMinutes, 1e-3);
            }
        }
    }
}

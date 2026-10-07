using NUnit.Framework;
using ScrapYardKing.Core;
using ScrapYardKing.Economy;
using UnityEngine;

namespace ScrapYardKing.Tests
{
    /// <summary>Diamond prices follow time saved and cash value, never a number picked per button.</summary>
    public sealed class PremiumEconomyTests
    {
        PremiumEconomyConfig config;

        [SetUp]
        public void SetUp() => config = ScriptableObject.CreateInstance<PremiumEconomyConfig>();   // defaults: 2 min, 3/min, ^0.85, cap 400

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(config);

        [Test]
        public void SkipCost_ShortWait_CostsTheMinimum()
        {
            Assert.AreEqual(0, config.SkipCost(0f));
            Assert.AreEqual(2, config.SkipCost(5f));
            Assert.AreEqual(3, config.SkipCost(60f));
        }

        [Test]
        public void SkipCost_LongerWaits_CostMoreButLessPerMinute()
        {
            int ten = config.SkipCost(600f), sixty = config.SkipCost(3600f);
            Assert.Greater(sixty, ten);
            Assert.Less(sixty / 60f, ten / 10f, "bulk discount");
            Assert.LessOrEqual(config.SkipCost(10f * 3600f), 400, "cap");
        }

        [Test]
        public void CashCost_KeepsDiamondWorthAsLevelsRise()
        {
            Assert.AreEqual(0, config.CashCost(0, 5));
            Assert.AreEqual(1, config.CashCost(1, 1));
            int early = config.CashCost(10000, 3), late = config.CashCost(10000, 10);
            Assert.Greater(early, late, "the same cash is fewer diamonds later");
        }

        [Test]
        public void LevelUpReward_OnlyOnScheduledLevels()
        {
            Assert.AreEqual(0, config.LevelUpReward(5));
            Assert.AreEqual(5, config.LevelUpReward(6));
            Assert.AreEqual(0, config.LevelUpReward(7));
            Assert.AreEqual(5, config.LevelUpReward(8));
        }

        [Test]
        public void NeedsConfirm_OnlyAboveThreshold()
        {
            Assert.IsFalse(config.NeedsConfirm(20));
            Assert.IsTrue(config.NeedsConfirm(21));
        }

        [Test]
        public void Analytics_CountsEventsWithoutASink()
        {
            int before = Analytics.Count("test_event");
            Analytics.Log("test_event", ("a", 1));
            Analytics.Log("test_event");
            Assert.AreEqual(before + 2, Analytics.Count("test_event"));
        }
    }
}

using NUnit.Framework;
using ScrapYardKing.Economy;
using ScrapYardKing.Progression;
using UnityEngine;

namespace ScrapYardKing.Tests
{
    public sealed class CurrencyFormatTests
    {
        [TestCase(0, "0")]
        [TestCase(950, "950")]
        [TestCase(1200, "1.2K")]
        [TestCase(12400, "12.4K")]
        [TestCase(150000, "150K")]
        [TestCase(1000000, "1M")]
        [TestCase(3550000, "3.6M")]
        public void Short_UsesMobileSuffixes(long value, string expected) => Assert.AreEqual(expected, CurrencyFormat.Short(value));
    }

    public sealed class ProgressionConfigTests
    {
        [Test]
        public void XpToNext_GrowsGeometricallyFromBase()
        {
            var config = ScriptableObject.CreateInstance<ProgressionConfig>();
            try
            {
                int first = config.XpToNext(1);
                int second = config.XpToNext(2);
                int tenth = config.XpToNext(10);
                Assert.Greater(first, 0);
                Assert.Greater(second, first);
                Assert.Greater(tenth, second * 5);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void CashRewardScalesWithLevel()
        {
            var config = ScriptableObject.CreateInstance<ProgressionConfig>();
            try
            {
                Assert.AreEqual(config.CashRewardForLevel(2) * 2, config.CashRewardForLevel(4));
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }
    }

    public sealed class TaskAndCatalogTests
    {
        [Test]
        public void TaskWithoutTarget_MatchesAnyId()
        {
            var task = ScriptableObject.CreateInstance<TaskDefinition>();
            try
            {
                Assert.IsTrue(task.Targets("car_wreck"));
                Assert.IsTrue(task.Targets(null));
            }
            finally
            {
                Object.DestroyImmediate(task);
            }
        }

        [Test]
        public void UnknownUpgrade_IsUnlockedFromLevelOne()
        {
            var catalog = ScriptableObject.CreateInstance<UpgradeCatalog>();
            try
            {
                Assert.AreEqual(1, catalog.UnlockLevel("does_not_exist"));
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }
    }
}

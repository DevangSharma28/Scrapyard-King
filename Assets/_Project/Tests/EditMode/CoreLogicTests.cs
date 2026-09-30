using NUnit.Framework;
using ScrapYardKing.Core;
using ScrapYardKing.Harvest;

namespace ScrapYardKing.Tests
{
    public sealed class ModifiableStatTests
    {
        [Test]
        public void Value_AppliesFlatThenPercentThenMultiply()
        {
            var stat = new ModifiableStat(10f);
            stat.AddModifier(new StatModifier("upgrade", StatModifierType.Flat, 5f));
            stat.AddModifier(new StatModifier("perk", StatModifierType.PercentAdd, 0.5f));
            stat.AddModifier(new StatModifier("overdrive", StatModifierType.Multiply, 2f));

            Assert.AreEqual((10f + 5f) * 1.5f * 2f, stat.Value, 0.0001f);
        }

        [Test]
        public void RemoveModifiersFrom_RestoresValueAndRaisesChanged()
        {
            var stat = new ModifiableStat(8f);
            var source = new object();
            stat.AddModifier(new StatModifier(source, StatModifierType.Flat, 4f));

            int changes = 0;
            stat.Changed += _ => changes++;

            Assert.IsTrue(stat.RemoveModifiersFrom(source));
            Assert.AreEqual(8f, stat.Value, 0.0001f);
            Assert.AreEqual(1, changes);
            Assert.IsFalse(stat.RemoveModifiersFrom(source));
        }

        [Test]
        public void SetBase_DoesNotRaiseChangedWhenValueIsUnchanged()
        {
            var stat = new ModifiableStat(3f);
            int changes = 0;
            stat.Changed += _ => changes++;

            stat.SetBase(3f);

            Assert.AreEqual(0, changes);
        }
    }

    public sealed class DropMathTests
    {
        [TestCase(20, 0.35f, 5, 7)]
        [TestCase(7, 0.5f, 3, 3)]
        [TestCase(60, 0.4f, 4, 24)]
        [TestCase(4, 0.35f, 6, 1)]
        public void PartShares_SumToTheirPool(int total, float share, int parts, int expectedPool)
        {
            int sum = 0;
            for (int i = 0; i < parts; i++) sum += DropMath.PiecesForPart(total, share, parts, i);

            Assert.AreEqual(expectedPool, sum);
            Assert.LessOrEqual(sum, total);
        }

        [Test]
        public void PartShares_DistributeRemainderToEarliestParts()
        {
            Assert.AreEqual(3, DropMath.PiecesForPart(10, 1f, 4, 0));
            Assert.AreEqual(3, DropMath.PiecesForPart(10, 1f, 4, 1));
            Assert.AreEqual(2, DropMath.PiecesForPart(10, 1f, 4, 2));
            Assert.AreEqual(2, DropMath.PiecesForPart(10, 1f, 4, 3));
        }

        [Test]
        public void PartThresholds_AreEvenlySpacedAndExclusive()
        {
            Assert.AreEqual(0.75f, DropMath.PartThreshold(0, 3), 0.0001f);
            Assert.AreEqual(0.5f, DropMath.PartThreshold(1, 3), 0.0001f);
            Assert.AreEqual(0.25f, DropMath.PartThreshold(2, 3), 0.0001f);
        }

        [Test]
        public void InvalidPartIndex_DropsNothing()
        {
            Assert.AreEqual(0, DropMath.PiecesForPart(20, 0.5f, 0, 0));
            Assert.AreEqual(0, DropMath.PiecesForPart(20, 0.5f, 3, 3));
        }
    }
}

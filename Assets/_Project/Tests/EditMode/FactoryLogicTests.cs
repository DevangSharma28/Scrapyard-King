using NUnit.Framework;
using ScrapYardKing.Customers;
using ScrapYardKing.Factory;
using ScrapYardKing.Items;
using UnityEngine;

namespace ScrapYardKing.Tests
{
    public sealed class WeightedSpreadTests
    {
        [Test]
        public void Next_HandsOutExactSharesEvenlyInterleaved()
        {
            var spread = new WeightedSpread(new[] { 0.7f, 0.3f });
            int iron = 0, copper = 0, copperRun = 0, longestCopperRun = 0;
            for (int i = 0; i < 100; i++)
            {
                if (spread.Next() == 0)
                {
                    iron++;
                    copperRun = 0;
                }
                else
                {
                    copper++;
                    longestCopperRun = Mathf.Max(longestCopperRun, ++copperRun);
                }
            }

            Assert.AreEqual(70, iron, 1, "float credit may land one pick either side");
            Assert.AreEqual(100, iron + copper);
            Assert.AreEqual(1, longestCopperRun, "the minority output never comes twice in a row at 7:3");
        }

        [Test]
        public void Next_SkipsZeroWeightsAndReportsAllZero()
        {
            var spread = new WeightedSpread(new[] { 0f, 1f });
            for (int i = 0; i < 10; i++) Assert.AreEqual(1, spread.Next());
            Assert.AreEqual(-1, new WeightedSpread(new[] { 0f, 0f }).Next());
        }
    }

    public sealed class FactoryDataTests
    {
        static ItemDefinition Item(string id, int value)
        {
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            Reflect.Set(item, "id", id);
            Reflect.Set(item, "baseValue", value);
            return item;
        }

        [Test]
        public void SellDesk_SellsOnlyListedPricedItems()
        {
            var iron = Item("iron", 12);
            var copper = Item("copper", 26);
            var scrap = Item("scrap", 0);
            var open = ScriptableObject.CreateInstance<SellDeskDefinition>();
            var market = ScriptableObject.CreateInstance<SellDeskDefinition>();
            try
            {
                Reflect.Set(market, "sells", new[] { iron, copper });
                Assert.IsTrue(open.Sells(iron));
                Assert.IsFalse(open.Sells(scrap), "unpriced items never sell");
                Assert.IsTrue(market.Sells(copper));
                Assert.IsFalse(market.Sells(Item("mixed_metal", 6)));
                Assert.IsTrue(market.SellsId("iron"));
                Assert.IsFalse(market.SellsId("mixed_metal"));
            }
            finally
            {
                foreach (var o in new Object[] { iron, copper, scrap, open, market }) Object.DestroyImmediate(o);
            }
        }

        [Test]
        public void Machine_ProducesEveryWeightedOutput()
        {
            var iron = Item("iron", 12);
            var copper = Item("copper", 26);
            var gold = Item("gold", 99);
            var sorter = ScriptableObject.CreateInstance<MachineDefinition>();
            try
            {
                Reflect.Set(sorter, "output", iron);
                Reflect.Set(sorter, "outputMix", new[]
                {
                    new WeightedOutput { item = iron, weight = 0.7f }, new WeightedOutput { item = copper, weight = 0.3f },
                    new WeightedOutput { item = gold, weight = 0f }
                });
                Assert.IsTrue(sorter.HasOutputMix);
                Assert.IsTrue(sorter.Produces(iron));
                Assert.IsTrue(sorter.Produces(copper));
                Assert.IsFalse(sorter.Produces(gold), "zero-weight outputs are switched off");
            }
            finally
            {
                foreach (var o in new Object[] { iron, copper, gold, sorter }) Object.DestroyImmediate(o);
            }
        }

        [Test]
        public void RollOrder_PrefersStockAndFallsBackToOrderItem()
        {
            var iron = Item("iron", 12);
            var copper = Item("copper", 26);
            var config = ScriptableObject.CreateInstance<CustomerConfig>();
            try
            {
                Reflect.Set(config, "orderItem", iron);
                Assert.AreSame(iron, config.RollOrder(_ => 0), "no options: everyone buys the order item");

                Reflect.Set(config, "orderOptions", new[]
                {
                    new CustomerConfig.OrderOption { item = iron, weight = 0.9f }, new CustomerConfig.OrderOption { item = copper, weight = 0.1f }
                });
                Reflect.Set(config, "preferInStock", 1f);
                for (int i = 0; i < 50; i++) Assert.AreSame(copper, config.RollOrder(item => item == copper ? 3 : 0));

                int copperPicks = 0;
                for (int i = 0; i < 400; i++)
                    if (config.RollOrder(_ => 0) == copper) copperPicks++;
                Assert.Less(copperPicks, 100, "with nothing in stock the weights decide (10% copper)");
            }
            finally
            {
                foreach (var o in new Object[] { iron, copper, config }) Object.DestroyImmediate(o);
            }
        }
    }
}

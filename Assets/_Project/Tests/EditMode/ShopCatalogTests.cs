using System.Collections.Generic;
using NUnit.Framework;
using ScrapYardKing.Economy;
using ScrapYardKing.Shop;
using UnityEditor;

namespace ScrapYardKing.Tests
{
    /// <summary>The shipped catalog keeps its promises: bigger packs are better value, nothing is free or duplicated.</summary>
    public sealed class ShopCatalogTests
    {
        const string CatalogPath = "Assets/_Project/Data/Shop/ShopCatalog.asset";
        const string PremiumPath = "Assets/_Project/Data/Economy/PremiumEconomy.asset";

        ShopCatalog catalog;
        PremiumEconomyConfig premium;

        [SetUp]
        public void SetUp()
        {
            catalog = AssetDatabase.LoadAssetAtPath<ShopCatalog>(CatalogPath);
            premium = AssetDatabase.LoadAssetAtPath<PremiumEconomyConfig>(PremiumPath);
            Assert.IsNotNull(catalog, "run U2_Build.Assets");
            Assert.IsNotNull(premium, "run U1_Build.Assets");
        }

        [Test]
        public void Bundles_GiveMoreDiamondsPerDollarAsTheyGrow()
        {
            float last = 0f;
            foreach (var p in catalog.Bundles)
            {
                Assert.Greater(p.DiamondsPerUsd, last, p.name);
                last = p.DiamondsPerUsd;
            }
        }

        [Test]
        public void StarterPack_BeatsEveryBundle_AndIsOneTime()
        {
            var hero = catalog.Hero;
            Assert.AreEqual(StoreProductType.NonConsumable, hero.Type);
            foreach (var p in catalog.Bundles) Assert.Greater(hero.DiamondsPerUsd, p.DiamondsPerUsd, p.name);
        }

        [Test]
        public void Products_HaveUniqueIdsAndAPrice()
        {
            var ids = new HashSet<string>();
            var all = new List<IAPProductConfig>(catalog.Bundles) { catalog.Hero };
            all.AddRange(catalog.Specials);
            foreach (var p in all)
            {
                Assert.IsTrue(ids.Add(p.ProductId), "duplicate " + p.ProductId);
                Assert.Greater(p.ReferencePriceUsd, 0f, p.name);
                Assert.IsFalse(string.IsNullOrEmpty(p.FallbackPrice), p.name);
            }
        }

        [Test]
        public void NoAds_IsNotSoldWhileThereAreNoAdsToRemove()
        {
            foreach (var p in catalog.Specials)
                if (p.RemovesAds) Assert.IsFalse(p.Enabled, "the game shows no forced ads yet");
        }

        [Test]
        public void DiamondItems_ArePricedFromTheConfig()
        {
            foreach (var item in catalog.Boosts) Assert.Greater(ShopCatalog.BoostCost(premium, item), 0, item.boost.name);
            double lastPerDiamond = 0;
            foreach (var crate in catalog.Crates)
            {
                double perDiamond = ShopCatalog.CrateCash(premium, crate, 5) / (double)crate.diamonds;
                Assert.GreaterOrEqual(perDiamond, lastPerDiamond, crate.title);
                lastPerDiamond = perDiamond;
            }
        }
    }
}

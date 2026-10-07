using System.Reflection;
using NUnit.Framework;
using ScrapYardKing.Core;
using ScrapYardKing.Customers;
using ScrapYardKing.Harvest;
using ScrapYardKing.Progression;
using ScrapYardKing.Workers;
using ScrapYardKing.World;
using UnityEngine;

namespace ScrapYardKing.Tests
{
    static class Reflect
    {
        public static void Set(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    }

    public sealed class DropBoundsTests
    {
        [Test]
        public void ExpandDropBounds_EncapsulatesAreaAndKeepsHeight()
        {
            var go = new GameObject("Harvest");
            try
            {
                var harvest = go.AddComponent<HarvestManager>();
                harvest.DropBounds = new Bounds(new Vector3(20f, 0f, 24f), new Vector3(38f, 20f, 26f));
                harvest.ExpandDropBounds(new Bounds(new Vector3(29f, 0f, 34f), new Vector3(21f, 4f, 8f)));

                var b = harvest.DropBounds;
                Assert.LessOrEqual(b.min.x, 1f + 1e-4f);
                Assert.GreaterOrEqual(b.max.x, 39.5f - 1e-4f);
                Assert.GreaterOrEqual(b.max.z, 38f - 1e-4f);
                Assert.AreEqual(20f, b.size.y, 1e-4f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }

    public sealed class PorterRouteTests
    {
        [Test]
        public void Zones_CoverPrimaryAndExtraAreasOnly()
        {
            var go = new GameObject("Route");
            var extra = new GameObject("Extra");
            try
            {
                extra.transform.position = new Vector3(30f, 0f, 34f);
                var route = go.AddComponent<PorterRoute>();
                Reflect.Set(route, "pickupRadius", 5f);
                Reflect.Set(route, "extraZones", new[] { new PorterRoute.Zone { center = extra.transform, radius = 3f } });

                Assert.IsTrue(route.InAnyZone(new Vector3(4f, 0f, 0f)));
                Assert.IsTrue(route.InAnyZone(new Vector3(31f, 2f, 35f)));
                Assert.IsFalse(route.InAnyZone(new Vector3(15f, 0f, 15f)));
                // From the primary centre, the search must reach the far edge of the extra zone.
                Assert.GreaterOrEqual(route.SearchRadius(Vector3.zero), new Vector2(30f, 34f).magnitude + 3f - 1e-3f);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(extra);
            }
        }
    }

    public sealed class CustomerConfigTests
    {
        [Test]
        public void PickPrefab_NeverRepeatsTheAvoidedLookWhenThereIsAChoice()
        {
            var config = ScriptableObject.CreateInstance<CustomerConfig>();
            var a = new GameObject("A").AddComponent<Customer>();
            var b = new GameObject("B").AddComponent<Customer>();
            var c = new GameObject("C").AddComponent<Customer>();
            try
            {
                Reflect.Set(config, "prefabs", new[] { a, b, c });
                for (int i = 0; i < 200; i++) Assert.AreNotSame(a, config.PickPrefab(a));
            }
            finally
            {
                Object.DestroyImmediate(config);
                Object.DestroyImmediate(a.gameObject);
                Object.DestroyImmediate(b.gameObject);
                Object.DestroyImmediate(c.gameObject);
            }
        }

        [Test]
        public void NextArrivalDelay_StaysInRange()
        {
            var config = ScriptableObject.CreateInstance<CustomerConfig>();
            try
            {
                Reflect.Set(config, "arrivalInterval", new Vector2(3f, 1f));
                for (int i = 0; i < 100; i++)
                {
                    float d = config.NextArrivalDelay();
                    Assert.GreaterOrEqual(d, 1f);
                    Assert.LessOrEqual(d, 3f);
                }
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }
    }

    public sealed class ExpansionTests
    {
        [Test]
        public void LockedExpansion_IsAOneLevelUpgradeAtItsCost()
        {
            var definition = ScriptableObject.CreateInstance<ExpansionDefinition>();
            var go = new GameObject("Lot");
            try
            {
                Reflect.Set(definition, "id", "back_lot");
                Reflect.Set(definition, "cost", 1500L);
                var expansion = go.AddComponent<Expansion>();
                Reflect.Set(expansion, "definition", definition);
                IUpgradeable u = expansion;

                Assert.AreEqual("back_lot", u.UpgradeId);
                Assert.AreEqual(0, u.Level);
                Assert.AreEqual(1, u.MaxLevel);
                Assert.IsFalse(u.IsMaxed);
                Assert.AreEqual(1500L, u.NextCost);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(definition);
            }
        }
    }

    public sealed class WorkerDefinitionTests
    {
        [Test]
        public void HireCost_ClampsToLastPrice()
        {
            var definition = ScriptableObject.CreateInstance<WorkerDefinition>();
            try
            {
                Reflect.Set(definition, "hireCosts", new long[] { 800, 2000 });
                Assert.AreEqual(2, definition.MaxCount);
                Assert.AreEqual(800, definition.HireCost(0));
                Assert.AreEqual(2000, definition.HireCost(1));
                Assert.AreEqual(2000, definition.HireCost(5));
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }
    }

#if UNITY_EDITOR
    public sealed class CatalogDataTests
    {
        [Test]
        public void PanelEntries_AllHaveAGroup()
        {
            var catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<UpgradeCatalog>("Assets/_Project/Data/Progression/UpgradeCatalog.asset");
            Assert.IsNotNull(catalog);
            foreach (var e in catalog.Entries)
                if (e.inPanel) Assert.IsFalse(string.IsNullOrEmpty(e.group), $"{e.upgradeId} is in the panel without a group");
        }
    }
#endif
    public sealed class GiantEventTests
    {
        [Test]
        public void ChargeNeeded_FirstGiantComesSoonerThanRepeats()
        {
            Assert.AreEqual(3, GiantEventMath.ChargeNeeded(0, 3, 8));
            Assert.AreEqual(8, GiantEventMath.ChargeNeeded(1, 3, 8));
            Assert.AreEqual(8, GiantEventMath.ChargeNeeded(5, 3, 8));
            Assert.AreEqual(1, GiantEventMath.ChargeNeeded(0, 0, 8), "never free");
        }

        [Test]
        public void AddCharge_StopsAtWhatIsNeeded()
        {
            Assert.AreEqual(1, GiantEventMath.AddCharge(0, 3));
            Assert.AreEqual(3, GiantEventMath.AddCharge(2, 3));
            Assert.AreEqual(3, GiantEventMath.AddCharge(3, 3));
            Assert.AreEqual(1, GiantEventMath.AddCharge(-5, 3));
        }

        [Test]
        public void IsFeeder_HeavyTiersOnlyAndNeverTheGiantItself()
        {
            var config = ScriptableObject.CreateInstance<GiantEventConfig>();
            var giant = ScriptableObject.CreateInstance<ScrapDefinition>();
            var heavy = ScriptableObject.CreateInstance<ScrapDefinition>();
            var car = ScriptableObject.CreateInstance<ScrapDefinition>();
            try
            {
                Reflect.Set(giant, "tier", 5);
                Reflect.Set(heavy, "tier", 3);
                Reflect.Set(car, "tier", 1);
                Reflect.Set(config, "giant", giant);
                Reflect.Set(config, "feederMinTier", 3);

                Assert.IsTrue(config.IsFeeder(heavy));
                Assert.IsFalse(config.IsFeeder(car));
                Assert.IsFalse(config.IsFeeder(giant));
                Assert.IsFalse(config.IsFeeder(null));
            }
            finally
            {
                Object.DestroyImmediate(config);
                Object.DestroyImmediate(giant);
                Object.DestroyImmediate(heavy);
                Object.DestroyImmediate(car);
            }
        }
    }

    public sealed class SpecialistSiteTests
    {
        [Test]
        public void PorterRoute_RoleIsDataSoTheLoaderReusesTheCarrierLoop()
        {
            var go = new GameObject("Route");
            try
            {
                var route = go.AddComponent<PorterRoute>();
                Assert.AreEqual(WorkerRole.Porter, route.Role);
                Reflect.Set(route, "role", WorkerRole.Loader);
                Assert.AreEqual(WorkerRole.Loader, route.Role);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void WorkPost_LooksAheadWhenNoLookTargetIsSet()
        {
            var go = new GameObject("Post");
            try
            {
                go.transform.SetPositionAndRotation(new Vector3(3f, 0f, 4f), Quaternion.Euler(0f, 90f, 0f));
                var post = go.AddComponent<WorkPost>();
                Reflect.Set(post, "role", WorkerRole.Seller);

                Assert.AreEqual(WorkerRole.Seller, post.Role);
                Assert.AreEqual(4f, post.LookPoint.x, 1e-4f);
                Assert.AreEqual(4f, post.LookPoint.z, 1e-4f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}

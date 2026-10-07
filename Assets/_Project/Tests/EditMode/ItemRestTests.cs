using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ScrapYardKing.Items;
using UnityEngine;

namespace ScrapYardKing.Tests
{
    public sealed class ItemNoRestTests
    {
        static readonly MethodInfo Step = typeof(WorldItem).GetMethod("SimulateFlight", BindingFlags.NonPublic | BindingFlags.Instance);

        static Vector3 Settle(WorldItem item)
        {
            for (int i = 0; i < 2000 && item.CurrentState == WorldItem.State.Flying; i++) Step.Invoke(item, new object[] { 0.02f });
            return item.transform.position;
        }

        [Test]
        public void ItemThatLandsInANoRestArea_HopsBackTowardWhereItCameFrom()
        {
            var go = new GameObject("Item");
            try
            {
                var item = go.AddComponent<WorldItem>();
                var yard = new Bounds(Vector3.zero, new Vector3(40f, 20f, 40f));
                // A mound 3 to 7 m east of the launch point; the throw lands in the middle of it.
                var noRest = new List<Bounds> { new(new Vector3(5f, 0f, 0f), new Vector3(4f, 4f, 6f)) };
                item.Launch(new Vector3(0f, 1f, 0f), new Vector3(5.5f, 6f, 0f), 0f, 0f, yard, null, noRest);

                Vector3 rest = Settle(item);

                Assert.AreEqual(WorldItem.State.Grounded, item.CurrentState);
                Assert.Less(rest.x, 3f, "the piece must not stay on the mound");
                Assert.Greater(rest.x, -6f, "one hop home, not a launch across the yard");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ItemOnOpenGround_SettlesWhereItLands()
        {
            var go = new GameObject("Item");
            try
            {
                var item = go.AddComponent<WorldItem>();
                var yard = new Bounds(Vector3.zero, new Vector3(40f, 20f, 40f));
                var noRest = new List<Bounds> { new(new Vector3(12f, 0f, 0f), new Vector3(4f, 4f, 6f)) };
                item.Launch(new Vector3(0f, 1f, 0f), new Vector3(3f, 6f, 0f), 0f, 0f, yard, null, noRest);

                Vector3 rest = Settle(item);

                Assert.AreEqual(WorldItem.State.Grounded, item.CurrentState);
                Assert.Greater(rest.x, 0.5f);
                Assert.Less(rest.x, 10f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ItemLaunchedFromInsideANoRestArea_StillComesToRest()
        {
            var go = new GameObject("Item");
            try
            {
                var item = go.AddComponent<WorldItem>();
                var yard = new Bounds(Vector3.zero, new Vector3(40f, 20f, 40f));
                // Everything is no-rest: after a few hops the piece settles anyway instead of bouncing forever.
                var noRest = new List<Bounds> { new(Vector3.zero, new Vector3(100f, 4f, 100f)) };
                item.Launch(new Vector3(0f, 1f, 0f), new Vector3(1f, 5f, 0f), 0f, 0f, yard, null, noRest);

                Settle(item);

                Assert.AreEqual(WorldItem.State.Grounded, item.CurrentState);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }

    public sealed class CarryStackSwayTests
    {
        [Test]
        public void StepSway_LongFramesStayFiniteAndBounded()
        {
            // The values on the Player prefab. 0.2 s frames are what an unfocused Editor at x2 produces; one explicit
            // step of that size used to blow the spring up to NaN within seconds.
            Vector3 sway = Vector3.zero, velocity = Vector3.zero;
            var target = new Vector3(0.5f, 0f, -0.3f);
            for (int i = 0; i < 600; i++)
            {
                CarryStack.StepSway(ref sway, ref velocity, i % 40 < 20 ? target : -target, 90f, 11f, 0.2f);
                Assert.IsTrue(float.IsFinite(sway.x) && float.IsFinite(sway.z), "frame " + i);
                Assert.Less(sway.magnitude, 3f, "frame " + i);
            }
        }

        [Test]
        public void StepSway_SettlesOnTheTarget()
        {
            Vector3 sway = Vector3.zero, velocity = Vector3.zero;
            var target = new Vector3(0.4f, 0f, 0.1f);
            for (int i = 0; i < 300; i++) CarryStack.StepSway(ref sway, ref velocity, target, 90f, 11f, 1f / 60f);

            Assert.AreEqual(target.x, sway.x, 0.01f);
            Assert.AreEqual(target.z, sway.z, 0.01f);
        }

        [Test]
        public void StepSway_RecoversFromBrokenState()
        {
            var sway = new Vector3(float.NaN, 0f, float.PositiveInfinity);
            var velocity = new Vector3(float.NaN, 0f, 0f);

            CarryStack.StepSway(ref sway, ref velocity, Vector3.zero, 90f, 11f, 0.016f);

            Assert.AreEqual(Vector3.zero, sway);
            Assert.AreEqual(Vector3.zero, velocity);
        }
    }
}

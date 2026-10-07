using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ScrapYardKing.Progression;
using UnityEditor;

namespace ScrapYardKing.Tests
{
    /// <summary>Missions and the daily calendar: sane targets, single ids, and free diamonds that stay modest.</summary>
    public sealed class MissionTests
    {
        MissionConfig missions;
        DailyRewardConfig daily;

        [SetUp]
        public void SetUp()
        {
            missions = AssetDatabase.LoadAssetAtPath<MissionConfig>("Assets/_Project/Data/Progression/MissionConfig.asset");
            daily = AssetDatabase.LoadAssetAtPath<DailyRewardConfig>("Assets/_Project/Data/Progression/DailyRewardConfig.asset");
            Assert.IsNotNull(missions, "run U4_Build.Assets");
            Assert.IsNotNull(daily, "run U4_Build.Assets");
        }

        [Test]
        public void Title_FillsTargetAndPlural()
        {
            Assert.AreEqual("HIRE 1 WORKER", MissionManager.Title("HIRE {0} WORKER{s}", 1, "1"));
            Assert.AreEqual("HIRE 5 WORKERS", MissionManager.Title("HIRE {0} WORKER{s}", 5, "5"));
        }

        [Test]
        public void DailyTargets_GrowWithLevel()
        {
            foreach (var m in missions.DailyPool)
            {
                Assert.GreaterOrEqual(m.Target(10), m.Target(1), m.id);
                Assert.Greater(m.Target(1), 0, m.id);
            }
        }

        [Test]
        public void Ids_AreUnique()
        {
            var ids = new HashSet<string>();
            foreach (var m in missions.DailyPool) Assert.IsTrue(ids.Add(m.id), m.id);
            foreach (var a in missions.Achievements) Assert.IsTrue(ids.Add(a.id), a.id);
        }

        [Test]
        public void Achievements_TiersRiseAndEachPays()
        {
            foreach (var a in missions.Achievements)
            {
                Assert.AreEqual(a.targets.Length, a.diamonds.Length, a.id);
                for (int i = 1; i < a.targets.Length; i++) Assert.Greater(a.targets[i], a.targets[i - 1], a.id);
                Assert.IsTrue(a.diamonds.All(d => d > 0), a.id);
            }
        }

        [Test]
        public void Calendar_HasSevenDays_AndDaySevenIsTheBiggest()
        {
            Assert.AreEqual(7, daily.Days.Length);
            int seventh = daily.Days[6].reward.diamonds;
            for (int i = 0; i < 6; i++) Assert.Less(daily.Days[i].reward.diamonds, seventh);
        }

        [Test]
        public void FreeDiamondsPerDay_StayModest()
        {
            // the three best dailies plus a seventh of the week's calendar: well under an hour of skips a day
            int dailies = missions.DailyPool.Select(m => m.reward.diamonds).OrderByDescending(d => d).Take(missions.DailyCount).Sum();
            float calendar = daily.Days.Sum(d => d.reward.diamonds) / 7f;
            Assert.LessOrEqual(dailies + calendar, 30f);
        }
    }
}

using ScrapYardKing.Feedback;
using ScrapYardKing.Harvest;
using UnityEngine;

namespace ScrapYardKing.World
{
    /// <summary>
    /// Tuning for the Giant Scrap event (blueprint boss tier: "Giant object: 120+ pieces plus rare material chance").
    /// The player earns the giant by dismantling heavy scrap, so there is never a timer to wait on.
    /// </summary>
    [CreateAssetMenu(fileName = "GiantEvent_", menuName = "Scrap Yard King/World/Giant Event Config")]
    public sealed class GiantEventConfig : ScriptableObject
    {
        [Header("Rules")]
        [SerializeField] ScrapDefinition giant;
        [Tooltip("Scrap of this blueprint tier or higher charges the event when it is broken.")]
        [SerializeField, Range(1, 5)] int feederMinTier = 3;
        [Tooltip("Heavy objects to break before the first giant arrives.")]
        [SerializeField, Min(1)] int firstCharge = 3;
        [Tooltip("Heavy objects to break for every giant after the first.")]
        [SerializeField, Min(1)] int repeatCharge = 8;
        [SerializeField, Min(0)] long cashBonus = 5000;

        [Header("Arrival")]
        [SerializeField, Min(0f)] float dropHeight = 14f;
        [SerializeField, Min(0.05f)] float dropDuration = 0.7f;
        [Tooltip("Loose items inside this radius are hopped out of the landing zone.")]
        [SerializeField, Min(0.5f)] float clearRadius = 6.5f;
        [SerializeField, Min(0f)] float focusHold = 3.2f;
        [SerializeField] ParticleSystem landVfx;
        [SerializeField, Min(0.1f)] float landVfxScale = 3f;
        [SerializeField, Range(0f, 1f)] float landShake = 0.9f;
        [SerializeField] SfxDefinition arriveSfx;
        [SerializeField] SfxDefinition landSfx;

        [Header("Fight camera")]
        [Tooltip("Extra camera distance while the player is close to the giant, so the whole machine stays in frame.")]
        [SerializeField, Min(0f)] float cameraPullBack = 7f;
        [SerializeField, Min(1f)] float fightRadius = 9f;

        [Header("Defeat")]
        [SerializeField] SfxDefinition defeatSfx;
        [SerializeField, Range(0f, 1f)] float defeatPunch = 0.8f;

        [Header("Text")]
        [SerializeField] string arriveTitle = "GIANT SCRAP!";
        [SerializeField] string arriveLine = "CUT IT UP FOR A BIG BONUS";
        [SerializeField] string defeatTitle = "DEMOLISHED!";
        [Tooltip("World sign while charging. {0} = broken so far, {1} = needed.")]
        [SerializeField] string signCharging = "GIANT SCRAP\n<size=62%>CUT HEAVY SCRAP</size>  {0}/{1}";
        [SerializeField] string signIncoming = "INCOMING!\n<size=62%>STAND CLEAR</size>";
        [Tooltip("World sign while the giant stands. {0} = its name.")]
        [SerializeField] string signActive = "{0}\n<size=62%>CUT IT UP!</size>";

        public ScrapDefinition Giant => giant;
        public int FeederMinTier => feederMinTier;
        public long CashBonus => cashBonus;
        public float DropHeight => dropHeight;
        public float DropDuration => dropDuration;
        public float ClearRadius => clearRadius;
        public float FocusHold => focusHold;
        public ParticleSystem LandVfx => landVfx;
        public float LandVfxScale => landVfxScale;
        public float LandShake => landShake;
        public SfxDefinition ArriveSfx => arriveSfx;
        public SfxDefinition LandSfx => landSfx;
        public float CameraPullBack => cameraPullBack;
        public float FightRadius => fightRadius;
        public SfxDefinition DefeatSfx => defeatSfx;
        public float DefeatPunch => defeatPunch;
        public string ArriveTitle => arriveTitle;
        public string ArriveLine => arriveLine;
        public string DefeatTitle => defeatTitle;
        public string SignCharging => signCharging;
        public string SignIncoming => signIncoming;
        public string SignActive => signActive;

        /// <summary>Heavy objects to break before the next giant, given how many giants are already down.</summary>
        public int ChargeNeeded(int giantsDefeated) => GiantEventMath.ChargeNeeded(giantsDefeated, firstCharge, repeatCharge);

        /// <summary>True for scrap that charges the event (heavy enough, and not the giant itself).</summary>
        public bool IsFeeder(ScrapDefinition scrap) => scrap != null && scrap != giant && scrap.Tier >= feederMinTier;
    }

    /// <summary>Pure rules of the Giant Scrap event.</summary>
    public static class GiantEventMath
    {
        public static int ChargeNeeded(int giantsDefeated, int firstCharge, int repeatCharge) =>
            Mathf.Max(1, giantsDefeated <= 0 ? firstCharge : repeatCharge);

        /// <summary>Charge after one more feeder broke: never above what is needed.</summary>
        public static int AddCharge(int charge, int needed) => Mathf.Min(Mathf.Max(0, charge) + 1, Mathf.Max(1, needed));
    }
}

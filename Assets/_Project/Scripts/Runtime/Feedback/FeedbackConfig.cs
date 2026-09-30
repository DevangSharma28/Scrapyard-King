using UnityEngine;

namespace ScrapYardKing.Feedback
{
    /// <summary>Game-feel tuning shared by every harvestable object and the pickup loop.</summary>
    [CreateAssetMenu(fileName = "FeedbackConfig", menuName = "Scrap Yard King/Feedback/Feedback Config")]
    public sealed class FeedbackConfig : ScriptableObject
    {
        [Header("Cut Hit")]
        [SerializeField, Min(0)] float hitStopDuration = 0.025f;
        [SerializeField, Range(0f, 1f)] float hitStopTimeScale = 0.05f;
        [SerializeField, Min(0)] float hitShakeDuration = 0.12f;
        [SerializeField, Min(0)] float hitShakeAmplitude = 0.06f;
        [SerializeField, Min(0)] float hitPunchScale = 0.05f;
        [SerializeField, Range(0f, 1f)] float hitCameraShake = 0.04f;

        [Header("Break")]
        [SerializeField, Min(0)] float breakHitStopDuration = 0.07f;
        [SerializeField, Min(0)] float breakCameraPunch = 1.2f;
        [SerializeField, Min(0)] float breakPopupScale = 1.3f;
        [SerializeField, Min(0)] float partFlySpeed = 5f;

        [Header("Pickup")]
        [SerializeField, Min(0.05f)] float pickupFlyDuration = 0.3f;
        [SerializeField, Min(0)] float pickupArcHeight = 1.4f;
        [Tooltip("Pitch rises by this much per consecutive pickup, creating a satisfying combo ramp.")]
        [SerializeField, Min(0)] float pickupPitchStep = 0.04f;
        [SerializeField, Min(0)] int pickupPitchMaxSteps = 12;
        [SerializeField, Min(0)] float pickupComboWindow = 0.6f;

        [Header("Default Effects")]
        [SerializeField] ParticleSystem defaultHitVfx;
        [SerializeField] ParticleSystem defaultBreakVfx;
        [SerializeField] ParticleSystem spawnVfx;
        [SerializeField] SfxDefinition defaultHitSfx;
        [SerializeField] SfxDefinition defaultBreakSfx;
        [SerializeField] SfxDefinition partDetachSfx;
        [SerializeField] SfxDefinition pickupSfx;
        [SerializeField] SfxDefinition stackFullSfx;
        [SerializeField] SfxDefinition spawnSfx;

        [Header("Popups")]
        [SerializeField] Color positivePopupColor = new(1f, 0.85f, 0.2f);
        [SerializeField] Color warningPopupColor = new(1f, 0.35f, 0.25f);

        public float HitStopDuration => hitStopDuration;
        public float HitStopTimeScale => hitStopTimeScale;
        public float HitShakeDuration => hitShakeDuration;
        public float HitShakeAmplitude => hitShakeAmplitude;
        public float HitPunchScale => hitPunchScale;
        public float HitCameraShake => hitCameraShake;
        public float BreakHitStopDuration => breakHitStopDuration;
        public float BreakCameraPunch => breakCameraPunch;
        public float BreakPopupScale => breakPopupScale;
        public float PartFlySpeed => partFlySpeed;
        public float PickupFlyDuration => pickupFlyDuration;
        public float PickupArcHeight => pickupArcHeight;
        public float PickupPitchStep => pickupPitchStep;
        public int PickupPitchMaxSteps => pickupPitchMaxSteps;
        public float PickupComboWindow => pickupComboWindow;
        public ParticleSystem DefaultHitVfx => defaultHitVfx;
        public ParticleSystem DefaultBreakVfx => defaultBreakVfx;
        public ParticleSystem SpawnVfx => spawnVfx;
        public SfxDefinition DefaultHitSfx => defaultHitSfx;
        public SfxDefinition DefaultBreakSfx => defaultBreakSfx;
        public SfxDefinition PartDetachSfx => partDetachSfx;
        public SfxDefinition PickupSfx => pickupSfx;
        public SfxDefinition StackFullSfx => stackFullSfx;
        public SfxDefinition SpawnSfx => spawnSfx;
        public Color PositivePopupColor => positivePopupColor;
        public Color WarningPopupColor => warningPopupColor;
    }
}

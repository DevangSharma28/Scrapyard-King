using ScrapYardKing.Feedback;
using UnityEngine;

namespace ScrapYardKing.Factory
{
    /// <summary>
    /// Active Overdrive (blueprint p.5: the player stands at a machine and it runs at 2x for a while). Standing on the
    /// machine's boost pad charges a gauge; when full the machine gets a Speed multiplier for the duration, then cools down.
    /// </summary>
    [CreateAssetMenu(fileName = "OverdriveConfig", menuName = "Scrap Yard King/Factory/Overdrive Config")]
    public sealed class OverdriveConfig : ScriptableObject
    {
        [SerializeField, Min(1f)] float speedMultiplier = 2f;
        [SerializeField, Min(0.5f)] float duration = 10f;
        [Tooltip("Seconds of standing on the pad to fill the gauge.")]
        [SerializeField, Min(0.1f)] float chargeTime = 1.5f;
        [SerializeField, Min(0f)] float cooldown = 5f;
        [Header("Video extension")]
        [Tooltip("After the free burst the pad offers this video: the same Overdrive for videoSeconds. Empty = no offer.")]
        [SerializeField] Boosts.AdOfferDefinition videoOffer;
        [SerializeField, Min(1f)] float videoSeconds = 120f;
        [Tooltip("A pad offers the video at most this often (seconds), watched or not, so standing on it is never nagged.")]
        [SerializeField, Min(0f)] float offerEvery = 90f;
        [SerializeField] SfxDefinition chargeSfx;
        [SerializeField] SfxDefinition startSfx;
        [SerializeField] SfxDefinition endSfx;

        public float SpeedMultiplier => speedMultiplier;
        public float Duration => duration;
        public float ChargeTime => chargeTime;
        public float Cooldown => cooldown;
        public Boosts.AdOfferDefinition VideoOffer => videoOffer;
        public float VideoSeconds => videoSeconds;
        public float OfferEvery => offerEvery;
        public SfxDefinition ChargeSfx => chargeSfx;
        public SfxDefinition StartSfx => startSfx;
        public SfxDefinition EndSfx => endSfx;
    }
}

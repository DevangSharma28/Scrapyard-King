using System;
using DG.Tweening;
using ScrapYardKing.CameraSystem;
using ScrapYardKing.Core;
using ScrapYardKing.Economy;
using ScrapYardKing.Feedback;
using ScrapYardKing.Harvest;
using ScrapYardKing.Player;
using TMPro;
using UnityEngine;

namespace ScrapYardKing.World
{
    /// <summary>
    /// The Giant Scrap event. Breaking heavy scrap charges it (a sign at the landing zone counts up); when it is full a
    /// giant drops onto the zone, the camera pans over and pulls back for the fight, and dismantling it pays a cash bonus
    /// on top of its huge drop. The giant itself is ordinary <see cref="ScrapObject"/> data: parts, damage stages and the
    /// final destruction sequence all come from its <see cref="ScrapDefinition"/> and prefab.
    /// </summary>
    [DefaultExecutionOrder(-450)]
    public sealed class GiantScrapEvent : ServiceBehaviour<GiantScrapEvent>, ISaveable
    {
        [Serializable]
        sealed class SavedState
        {
            public int charge, defeated;
        }

        public enum EventState
        {
            /// <summary>Counting broken heavy scrap.</summary>
            Charging,
            /// <summary>Charged; waiting for a clear landing zone.</summary>
            Incoming,
            /// <summary>The giant stands in the yard.</summary>
            Active
        }

        const float RetryInterval = 0.5f;

        [SerializeField] GiantEventConfig config;
        [Tooltip("Where the giant lands (position and facing).")]
        [SerializeField] Transform arena;
        [Tooltip("Layers that hold the drop back while they overlap the giant's footprint (characters).")]
        [SerializeField] LayerMask blockingLayers;
        [SerializeField, Min(0f)] float footprintMargin = 0.4f;
        [SerializeField] TMP_Text sign;
        [Tooltip("Unit-scale object punched when the count changes (never the 0.01-scale canvas).")]
        [SerializeField] Transform signPunch;
        [Tooltip("Shown while no giant stands on the zone (floor markings).")]
        [SerializeField] GameObject zoneMarkings;

        ScrapManager scrapManager;
        HarvestManager harvest;
        EconomyManager economy;
        CameraController cameraController;
        PlayerCharacter player;
        int defeated;
        float nextTry;

        /// <summary>The giant arrived / was dismantled (cash bonus).</summary>
        public event Action<ScrapObject> Arrived;
        public event Action<long> Defeated;

        public GiantEventConfig Config => config;
        public EventState State { get; private set; } = EventState.Charging;
        public int Charge { get; private set; }
        public int ChargeNeeded => config != null ? config.ChargeNeeded(defeated) : 1;
        public int GiantsDefeated => defeated;
        /// <summary>The live giant, or null.</summary>
        public ScrapObject Giant { get; private set; }
        public float Health01 => Giant != null ? Giant.Health01 : 0f;

        void OnEnable() => GameEvents.ScrapBroken += OnScrapBroken;

        void OnDisable() => GameEvents.ScrapBroken -= OnScrapBroken;

        void Start()
        {
            Services.TryGet(out scrapManager);
            Services.TryGet(out harvest);
            Services.TryGet(out economy);
            Services.TryGet(out cameraController);
            Services.TryGet(out player);
            RefreshSign(false);
            SaveRegistry.Register(this);
        }

        string ISaveable.SaveKey => "giant_event";

        // A giant standing in the yard is saved as "fully charged": it drops in again next session.
        string ISaveable.CaptureState() =>
            JsonUtility.ToJson(new SavedState { charge = State == EventState.Charging ? Charge : ChargeNeeded, defeated = defeated });

        void ISaveable.RestoreState(string json)
        {
            if (State == EventState.Active) return;
            var saved = JsonUtility.FromJson<SavedState>(json);
            defeated = Mathf.Max(0, saved.defeated);
            Charge = Mathf.Clamp(saved.charge, 0, ChargeNeeded);
            State = Charge >= ChargeNeeded ? EventState.Incoming : EventState.Charging;
            RefreshSign(false);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            SaveRegistry.Unregister(this);
            if (Giant != null) Giant.Broken -= OnGiantBroken;
            if (signPunch != null) signPunch.DOKill();
        }

        public bool IsFeeder(ScrapDefinition scrap) => config != null && config.IsFeeder(scrap);

        void OnScrapBroken(ScrapBrokenEvent e)
        {
            if (State != EventState.Charging || !IsFeeder(e.Definition)) return;
            Charge = GiantEventMath.AddCharge(Charge, ChargeNeeded);
            if (Charge >= ChargeNeeded) State = EventState.Incoming;
            RefreshSign(true);
        }

        void Update()
        {
            switch (State)
            {
                case EventState.Incoming:
                    if (Time.time < nextTry) return;
                    nextTry = Time.time + RetryInterval;
                    if (!IsBlocked()) Arrive();
                    break;

                case EventState.Active:
                    UpdateFightCamera();
                    break;
            }
        }

        void Arrive()
        {
            if (config == null || config.Giant == null || arena == null) return;
            if (scrapManager == null && !Services.TryGet(out scrapManager)) return;

            Vector3 ground = arena.position;
            if (harvest != null) harvest.PushLooseItemsOut(ground, config.ClearRadius);
            var giant = scrapManager.Spawn(config.Giant, config.Giant.PickPrefab(), ground + Vector3.up * config.DropHeight, arena.rotation, false);
            if (giant == null) return;

            Giant = giant;
            giant.Broken += OnGiantBroken;
            State = EventState.Active;
            if (zoneMarkings != null) zoneMarkings.SetActive(false);

            // The crane lets go: a straight fall, then one heavy landing.
            var t = giant.transform;
            t.DOKill();
            t.DOMove(ground, config.DropDuration).SetEase(Ease.InQuad).OnComplete(() => Land(t, ground));

            if (cameraController != null || Services.TryGet(out cameraController)) cameraController.Focus(ground, config.FocusHold);
            GameFeedback.Sfx(config.ArriveSfx);
            RefreshSign(true);
            Arrived?.Invoke(giant);
            GameEvents.RaiseGiantScrapArrived(config.Giant.Id);
        }

        void Land(Transform giant, Vector3 ground)
        {
            GameFeedback.Vfx(config.LandVfx, ground, Quaternion.identity, config.LandVfxScale);
            GameFeedback.Sfx(config.LandSfx);
            GameFeedback.CameraShake(config.LandShake);
            if (harvest != null) harvest.PushLooseItemsOut(ground, config.ClearRadius);
            if (giant != null) giant.DOPunchScale(new Vector3(0.06f, -0.1f, 0.06f), 0.4f, 5, 0.6f);
        }

        void OnGiantBroken(ScrapObject scrap)
        {
            scrap.Broken -= OnGiantBroken;
            if (scrap != Giant) return;

            Vector3 center = scrap.Center;
            Giant = null;
            defeated++;
            Charge = 0;
            State = EventState.Charging;
            if (cameraController != null) cameraController.SetPullBack(0f);
            if (zoneMarkings != null) zoneMarkings.SetActive(true);

            long bonus = config.CashBonus;
            if (bonus > 0)
            {
                if (economy != null || Services.TryGet(out economy)) economy.AddCash(bonus, center);
                var feedback = GameFeedback.Config;
                GameFeedback.Popup($"+${CurrencyFormat.Short(bonus)}", center + Vector3.up * 3.2f,
                    feedback != null ? feedback.PositivePopupColor : Color.yellow, 2f);
            }

            GameFeedback.Sfx(config.DefeatSfx);
            GameFeedback.CameraPunch(config.DefeatPunch);
            RefreshSign(true);
            Defeated?.Invoke(bonus);
            GameEvents.RaiseGiantScrapDefeated(config.Giant.Id, bonus);
        }

        /// <summary>Pulls the camera back while the player is at the giant, so the fight frames the whole machine.</summary>
        void UpdateFightCamera()
        {
            if (cameraController == null || Giant == null) return;
            if (player == null && !Services.TryGet(out player)) return;
            bool fighting = Giant.SurfaceDistance(player.transform.position) <= config.FightRadius;
            cameraController.SetPullBack(fighting ? config.CameraPullBack : 0f);
        }

        bool IsBlocked()
        {
            if (blockingLayers.value == 0 || config == null || config.Giant == null || arena == null) return false;
            var prefab = config.Giant.Prefab;
            if (prefab == null || !prefab.TryGetComponent(out BoxCollider box)) return false;

            Vector3 scale = prefab.transform.localScale;
            Vector3 center = arena.position + arena.rotation * Vector3.Scale(box.center, scale);
            Vector3 halfExtents = Vector3.Scale(box.size, scale) * 0.5f + Vector3.one * footprintMargin;
            return Physics.CheckBox(center, halfExtents, arena.rotation, blockingLayers, QueryTriggerInteraction.Ignore);
        }

        void RefreshSign(bool punch)
        {
            if (sign != null && config != null)
                sign.text = State switch
                {
                    EventState.Incoming => config.SignIncoming,
                    EventState.Active => string.Format(config.SignActive, config.Giant != null ? config.Giant.DisplayName.ToUpperInvariant() : string.Empty),
                    _ => string.Format(config.SignCharging, Charge, ChargeNeeded)
                };

            if (!punch || signPunch == null) return;
            signPunch.DOKill(true);
            signPunch.DOPunchScale(Vector3.one * 0.25f, 0.3f, 6, 0.6f);
        }

        void OnDrawGizmosSelected()
        {
            if (arena == null) return;
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.8f);
            Gizmos.DrawWireSphere(arena.position, config != null ? config.ClearRadius : 6f);
        }
    }
}

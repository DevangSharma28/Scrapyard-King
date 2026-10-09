using System;
using System.Collections.Generic;
using DG.Tweening;
using ScrapYardKing.Core;
using ScrapYardKing.Feedback;
using ScrapYardKing.Harvest;
using ScrapYardKing.Items;
using ScrapYardKing.Persistence;
using ScrapYardKing.Progression;
using UnityEngine;

namespace ScrapYardKing.Factory
{
    /// <summary>
    /// A crane that feeds a machine without the player. With <c>liftWholeScrap</c> (the Claw Crane) it swings over the
    /// nearest untouched light scrap (tyre stack, drum, car), lifts the whole object, carries it over the hopper and feeds
    /// its pieces in as the hopper makes room; the object shrinks and is gone with the last piece. Without it (the Heavy
    /// Yard crane) it lifts a handful of loose pieces. Bought and upgraded like any station
    /// (<see cref="IUpgradeable"/>; level 0 = not built, only its foundation stands). It uses the same two doors as the
    /// player and the porters: loose items come from <see cref="HarvestManager"/>, the hopper is an
    /// <see cref="IItemReceiver"/>. A full hopper leaves the claw waiting above it, so the bottleneck stays visible.
    /// The motion is scripted (yaw, trolley, hoist), not physics.
    /// </summary>
    public sealed class ClawCrane : MonoBehaviour, IUpgradeable
    {
        enum Phase { Idle, ToItem, Lower, Lift, ToHopper, Release, Waiting }

        [SerializeField] ClawCraneDefinition definition;
        [SerializeField, Min(0)] int level;
        [Tooltip("The machine (or any receiver) the crane feeds.")]
        [SerializeField] MonoBehaviour target;
        [Tooltip("Point above the hopper where the claw opens.")]
        [SerializeField] Transform dropPoint;
        [Tooltip("Items the crane handles. Empty = whatever the target accepts. Needed when the target is a belt, which takes anything.")]
        [SerializeField] ItemDefinition[] takes;
        [Tooltip("Lift whole scrap objects (ScrapDefinition.CraneLiftable) instead of loose pieces.")]
        [SerializeField] bool liftWholeScrap;
        [Tooltip("Pieces fed into the hopper per second while a whole object hangs over it.")]
        [SerializeField, Min(1f)] float feedRate = 18f;

        [Header("Rig")]
        [Tooltip("Everything that only exists once the crane is built.")]
        [SerializeField] GameObject builtRoot;
        [Tooltip("Rotates around Y: cab, jib, trolley.")]
        [SerializeField] Transform pivot;
        [Tooltip("Unit-length beam along +Z; scaled to the level's reach. Not used when the jib is built in sections.")]
        [SerializeField] Transform jib;
        [Tooltip("Jib built in sections, one per level: section n shows from level n + 1, so an upgrade visibly adds a length of truss.")]
        [SerializeField] GameObject[] jibSections;
        [Tooltip("Slides along the jib (local Z of the pivot).")]
        [SerializeField] Transform trolley;
        [Tooltip("Unit-length cable hanging from the trolley; scaled to the hoist length.")]
        [SerializeField] Transform cable;
        [SerializeField] Transform claw;
        [SerializeField] Transform hold;
        [Tooltip("Claw fingers: closed at 0 degrees, opened by rotating around their local X.")]
        [SerializeField] Transform[] fingers;

        [Header("Motion (presentation)")]
        [SerializeField, Min(1f)] float yawSpeed = 150f;
        [SerializeField, Min(0.1f)] float trolleySpeed = 9f;
        [SerializeField, Min(0.1f)] float hoistSpeed = 9f;
        [SerializeField] float travelHeight = 4.2f;
        [SerializeField] float grabHeight = 0.55f;
        [SerializeField, Min(0f)] float minTrolley = 1.6f;
        [SerializeField] float fingerOpenAngle = 34f;
        [SerializeField, Min(0f)] float retryDelay = 0.4f;

        readonly List<WorldItem> held = new();
        IItemReceiver receiver;
        HarvestManager harvest;
        ScrapManager scrapManager;
        ScrapObject targetScrap, carried;
        float carriedHeight, feedBudget;
        Phase phase;
        Vector3 goal;
        float yaw, reachNow, hoist, open = 1f, nextLook;

        public event Action<IUpgradeable> UpgradeChanged;

        public string UpgradeId => definition != null ? definition.Id : name;
        public string DisplayName => definition != null ? definition.DisplayName : name;
        public Sprite Icon => definition != null ? definition.Icon : null;
        public int Level => level;
        public int MaxLevel => definition != null ? definition.MaxLevel : 1;
        public bool IsMaxed => level >= MaxLevel;
        public long NextCost => IsMaxed || definition == null ? 0 : definition.GetLevel(level + 1).cost;
        public string LevelLabel => level == 0 ? "BUILD" : $"Lv.{level}";
        public string NextEffect
        {
            get
            {
                if (IsMaxed || definition == null) return string.Empty;
                var next = definition.GetLevel(level + 1);
                return level == 0 ? "FEEDS THE CRUSHER" : $"{Stats.reach:0} → {next.reach:0} m reach";
            }
        }

        public Vector3? FeedbackPosition => transform.position + Vector3.up * 2f;
        ClawCraneLevel Stats => definition.GetLevel(Mathf.Max(1, level));

        void Awake()
        {
            receiver = target as IItemReceiver;
            if (pivot != null) yaw = pivot.localEulerAngles.y;
            reachNow = minTrolley;
            hoist = travelHeight;
            ApplyVisuals(false);
        }

        void Start()
        {
            Services.TryGet(out harvest);
            if (Services.TryGet(out UpgradeManager upgrades)) upgrades.Register(this);
        }

        void OnDestroy()
        {
            if (Services.TryGet(out UpgradeManager upgrades)) upgrades.Unregister(this);
            DOTween.Kill(this);
        }

        void IUpgradeable.ApplyLevel(int newLevel)
        {
            bool wasBuilt = level > 0;
            level = Mathf.Clamp(newLevel, 0, MaxLevel);
            ApplyVisuals(!wasBuilt && level > 0 && !SaveRegistry.IsRestoring);
            UpgradeChanged?.Invoke(this);
        }

        void ApplyVisuals(bool fanfare)
        {
            if (builtRoot != null) builtRoot.SetActive(level > 0);
            if (level == 0 || definition == null) return;
            if (jibSections != null && jibSections.Length > 0)
            {
                for (int i = 0; i < jibSections.Length; i++)
                    if (jibSections[i] != null) jibSections[i].SetActive(level > i);
            }
            else if (jib != null) jib.localScale = new Vector3(1f, 1f, Stats.reach + 0.6f);
            if (fanfare && builtRoot != null)
            {
                // Presentation only: the crane pops up out of its foundation.
                var t = builtRoot.transform;
                t.localScale = new Vector3(1f, 0.05f, 1f);
                t.DOScale(Vector3.one, 0.55f).SetEase(Ease.OutBack).SetTarget(this);
            }

            Pose();
        }

        void Update()
        {
            if (level == 0 || definition == null || receiver == null || pivot == null) return;
            float speed = Stats.speed;
            switch (phase)
            {
                case Phase.Idle:
                    open = Mathf.MoveTowards(open, 1f, Time.deltaTime * 6f);
                    if (Time.time >= nextLook) Look();
                    break;
                case Phase.ToItem:
                    if (Travel(goal, travelHeight, speed)) phase = Phase.Lower;
                    break;
                case Phase.Lower:
                    if (liftWholeScrap && (targetScrap == null || !targetScrap.CanBeLifted))
                    {
                        targetScrap = null;
                        phase = Phase.Lift;   // somebody started cutting it: go back up empty
                        break;
                    }

                    if (Hoist(liftWholeScrap ? targetScrap.TopHeight + 0.1f : grabHeight, speed)) Grab();
                    break;
                case Phase.Lift:
                    open = Mathf.MoveTowards(open, 0f, Time.deltaTime * 8f);
                    if (Hoist(CarryHeight, speed)) phase = carried != null || held.Count > 0 ? Phase.ToHopper : Phase.Idle;
                    break;
                case Phase.ToHopper:
                    if (Travel(dropPoint.position, CarryHeight, speed)) phase = Phase.Release;
                    break;
                case Phase.Release:
                    float over = carried != null ? dropPoint.position.y + carriedHeight * 0.6f : dropPoint.position.y;
                    if (Hoist(Mathf.Min(CarryHeight, over), speed)) Release();
                    break;
                case Phase.Waiting:
                    // The hopper is full: hang above it with the load until there is room. A whole object feeds in
                    // piece by piece at feedRate, shrinking as it goes.
                    if (carried != null) Feed();
                    else if (Time.time >= nextLook) Release();
                    break;
            }

            Pose();
        }

        /// <summary>High enough that what hangs from the claw clears the hopper.</summary>
        float CarryHeight => carried != null ? Mathf.Max(travelHeight, dropPoint.position.y + carriedHeight + 0.4f) : travelHeight;

        /// <summary>Picks the next job: an untouched light scrap object in reach (whole mode) or a loose piece.</summary>
        void Look()
        {
            nextLook = Time.time + retryDelay;
            Vector3 origin = transform.position;
            if (liftWholeScrap)
            {
                if (scrapManager == null && !Services.TryGet(out scrapManager)) return;
                targetScrap = NearestLiftable(origin);
                if (targetScrap == null) return;
                goal = targetScrap.transform.position;
                phase = Phase.ToItem;
                return;
            }

            if (harvest == null && !Services.TryGet(out harvest)) return;
            if (!harvest.TryFindNearestCollectable(dropPoint.position, Stats.reach * 2f, Wanted, p => Flat(p - origin).sqrMagnitude <= Stats.reach * Stats.reach &&
                    Flat(p - origin).sqrMagnitude >= minTrolley * minTrolley, out var position)) return;
            goal = position;
            phase = Phase.ToItem;
        }

        ScrapObject NearestLiftable(Vector3 origin)
        {
            ScrapObject best = null;
            float bestDistance = float.MaxValue, reach2 = Stats.reach * Stats.reach, min2 = minTrolley * minTrolley;
            foreach (var s in scrapManager.Registered)
            {
                if (s == null || !s.CanBeLifted || !Wanted(s.Definition.DropItem)) continue;
                float d = Flat(s.transform.position - origin).sqrMagnitude;
                if (d > reach2 || d < min2) continue;
                float toHopper = Flat(s.transform.position - dropPoint.position).sqrMagnitude;
                if (toHopper >= bestDistance) continue;
                bestDistance = toHopper;
                best = s;
            }

            return best;
        }

        void Grab()
        {
            if (liftWholeScrap)
            {
                var s = targetScrap;
                targetScrap = null;
                if (s == null || !s.Lift(hold != null ? hold : claw))
                {
                    phase = Phase.Lift;   // go back up empty and look again
                    return;
                }

                carried = s;
                carriedHeight = s.Height;
                GameFeedback.Sfx(definition.GrabSfx);
                claw.DOPunchScale(Vector3.one * 0.18f, 0.2f, 6, 0.6f).SetTarget(this);
                phase = Phase.Lift;
                return;
            }

            Vector3 at = claw.position;
            at.y = 0f;
            for (int i = 0; i < Stats.grabSize; i++)
            {
                var item = harvest.TakeNearestCollectable(at, definition.GrabRadius, Wanted);
                if (item == null) break;
                item.SetHeld();
                item.MoveTo(hold, UnityEngine.Random.insideUnitSphere * 0.28f, UnityEngine.Random.rotation, 0.16f, 0.4f, null);
                held.Add(item);
            }

            if (held.Count == 0)
            {
                // Somebody else got there first.
                phase = Phase.Idle;
                hoist = Mathf.Max(hoist, grabHeight);
                return;
            }

            GameFeedback.Sfx(definition.GrabSfx);
            claw.DOPunchScale(Vector3.one * 0.18f, 0.2f, 6, 0.6f).SetTarget(this);
            phase = Phase.Lift;
        }

        void Release()
        {
            if (carried != null)
            {
                GameFeedback.Sfx(definition.DropSfx);
                feedBudget = 0f;
                phase = Phase.Waiting;
                return;
            }

            for (int i = held.Count - 1; i >= 0; i--)
            {
                var item = held[i];
                if (item == null)
                {
                    held.RemoveAt(i);
                    continue;
                }

                if (!receiver.CanAccept(item.Definition)) continue;
                receiver.Accept(item);
                held.RemoveAt(i);
            }

            if (held.Count > 0)
            {
                phase = Phase.Waiting;
                nextLook = Time.time + retryDelay;
                return;
            }

            GameFeedback.Sfx(definition.DropSfx);
            open = 1f;
            phase = Phase.Idle;
            nextLook = Time.time + 0.1f;
        }

        void Feed()
        {
            open = Mathf.MoveTowards(open, 0.5f, Time.deltaTime * 3f);
            feedBudget = Mathf.Min(feedBudget + feedRate * Time.deltaTime, 3f);
            int now = Mathf.FloorToInt(feedBudget);
            if (now <= 0) return;
            feedBudget -= now;
            if (!carried.FeedInto(receiver, dropPoint.position, now)) return;
            carried = null;
            open = 1f;
            claw.DOPunchScale(Vector3.one * 0.12f, 0.2f, 6, 0.6f).SetTarget(this);
            phase = Phase.Idle;
            nextLook = Time.time + 0.1f;
        }

        bool Wanted(ItemDefinition item) => (takes == null || takes.Length == 0 || Array.IndexOf(takes, item) >= 0) && receiver.CanAccept(item);

        /// <summary>Swings and trolleys toward a ground point at the travel height. True once the claw is over it.</summary>
        bool Travel(Vector3 point, float height, float speed)
        {
            Vector3 flat = Flat(point - transform.position);
            float wantYaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            float wantReach = Mathf.Clamp(flat.magnitude, minTrolley, Stats.reach);
            yaw = Mathf.MoveTowardsAngle(yaw, wantYaw, yawSpeed * speed * Time.deltaTime);
            reachNow = Mathf.MoveTowards(reachNow, wantReach, trolleySpeed * speed * Time.deltaTime);
            Hoist(height, speed);
            return Mathf.Abs(Mathf.DeltaAngle(yaw, wantYaw)) < 0.5f && Mathf.Abs(reachNow - wantReach) < 0.03f;
        }

        bool Hoist(float height, float speed)
        {
            hoist = Mathf.MoveTowards(hoist, height, hoistSpeed * speed * Time.deltaTime);
            return Mathf.Abs(hoist - height) < 0.02f;
        }

        /// <summary>Writes yaw, trolley and hoist to the rig. The crane's own rotation is ignored: the pivot turns in world space.</summary>
        void Pose()
        {
            pivot.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (trolley != null) trolley.localPosition = new Vector3(0f, trolley.localPosition.y, reachNow);
            float top = trolley != null ? trolley.position.y : pivot.position.y;
            float length = Mathf.Max(0.1f, top - hoist);
            if (cable != null) cable.localScale = new Vector3(1f, length, 1f);
            if (claw != null && trolley != null) claw.position = new Vector3(trolley.position.x, hoist, trolley.position.z);
            if (fingers == null) return;
            for (int i = 0; i < fingers.Length; i++)
                if (fingers[i] != null) fingers[i].localRotation = Quaternion.Euler(0f, i * 360f / fingers.Length, 0f) * Quaternion.Euler(-open * fingerOpenAngle, 0f, 0f);
        }

        static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);
    }
}

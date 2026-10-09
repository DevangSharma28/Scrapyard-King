using System;
using System.Collections.Generic;
using DG.Tweening;
using ScrapYardKing.Core;
using ScrapYardKing.Economy;
using ScrapYardKing.Feedback;
using ScrapYardKing.Items;
using ScrapYardKing.Progression;
using ScrapYardKing.UI;
using UnityEngine;

namespace ScrapYardKing.Factory
{
    /// <summary>
    /// Loading dock with a truck that backs in, takes goods through a deposit pad (it is an <see cref="IItemReceiver"/>
    /// like every other station, so the player and the Loader use the same pad), drives off when its bed is full and pays
    /// for the whole load onto the dock's <see cref="CashPile"/>. While the truck is on the road the pad takes nothing:
    /// carriers wait on it with their load, which is the dock's visible bottleneck.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TruckBay : MonoBehaviour, IItemReceiver, IStation, IUpgradeable, ISaveable
    {
        public enum BayState
        {
            Away,
            Arriving,
            Loading,
            Departing
        }

        [SerializeField] TruckBayDefinition definition;
        [SerializeField, Min(1)] int level = 1;

        [Header("Truck")]
        [Tooltip("Moves between the two points; its rotation stays that of the park point (it backs in, drives out).")]
        [SerializeField] Transform truck;
        [Tooltip("Cargo grid on the truck bed.")]
        [SerializeField] ItemPile cargo;
        [SerializeField] Transform parkPoint;
        [SerializeField] Transform awayPoint;
        [SerializeField] Transform[] wheels;
        [SerializeField, Min(0.05f)] float wheelRadius = 0.5f;
        [Tooltip("Unit-scale visual punched when the truck docks and when crates land.")]
        [SerializeField] Transform truckBody;
        [Tooltip("Cash pops out of here when the truck pays (cab window).")]
        [SerializeField] Transform payPoint;
        [SerializeField] ParticleSystem exhaust;

        [Header("Dock")]
        [SerializeField] CashPile cashPile;
        [SerializeField] StationLabel label;
        [SerializeField] LevelVisuals levelVisuals;

        [Header("Feel")]
        [Tooltip("Seconds the full truck waits (last crates landing, tailgate thump) before it drives off.")]
        [SerializeField, Min(0f)] float departDelay = 0.9f;
        [Tooltip("The truck slows to a crawl within this many metres of the dock.")]
        [SerializeField, Min(0f)] float easeDistance = 7f;
        [SerializeField, Min(0.1f)] float crawlSpeed = 1.4f;
        [Tooltip("The carrier is already at the dock when the bay starts (a ship moored at the quay before the port opens), instead of driving in.")]
        [SerializeField] bool startDocked;

        [Header("Show (optional parts of the truck model)")]
        [Tooltip("Drop-side on the dock side: hinged at the bed edge, swings down while loading.")]
        [SerializeField] Transform sideGate;
        [SerializeField] Vector3 gateOpenEuler = new(0f, 0f, -100f);
        [Tooltip("Roof beacon: spins while the truck moves or is about to leave.")]
        [SerializeField] Transform beacon;
        [Tooltip("White lights at the back: blink while reversing in.")]
        [SerializeField] GameObject reverseLights;
        [SerializeField] SfxDefinition reverseSfx;
        [SerializeField] SfxDefinition gateSfx;
        [SerializeField] SfxDefinition hornSfx;
        [Tooltip("How far the body settles on its springs with a full bed (metres).")]
        [SerializeField, Min(0f)] float fullSink = 0.12f;

        readonly Dictionary<ItemDefinition, int> manifest = new();
        // The current order: units asked for per product. What is loaded is always the manifest.
        readonly Dictionary<ItemDefinition, int> required = new();
        TruckContractDefinition contract;
        bool contractAnnounced;
        ItemPool pool;
        float along, pathLength, returnAt, dockedAt, departAt = -1f, nextBeep;
        bool leaving;
        Quaternion gateClosed = Quaternion.identity;
        Vector3 bodyRest;
        long cargoValue;
        Vector3 bodyScale = Vector3.one;

        public event Action<IUpgradeable> UpgradeChanged;
        /// <summary>A truck left and paid (cash for the load).</summary>
        public event Action<TruckBay, long> Shipped;

        public TruckBayDefinition Definition => definition;
        public BayState State { get; private set; } = BayState.Away;
        public int Level => level;
        public TruckBayLevel Stats => definition.GetLevel(level);
        public int Loaded => cargo.Count;
        public int Capacity => cargo.Capacity;
        public CashPile CashPile => cashPile;
        /// <summary>The truck is docked and has room.</summary>
        public bool IsLoading => State == BayState.Loading && !cargo.IsFull;
        /// <summary>The order of the truck at the dock, or null (no truck, or a dock without contracts).</summary>
        public TruckContractDefinition Contract => contract;
        /// <summary>Seconds the truck has stood at the dock (0 when it is not loading).</summary>
        public float DockedFor => State == BayState.Loading ? Time.time - dockedAt : 0f;
        /// <summary>Seconds until the next truck starts its approach (0 unless it is away).</summary>
        public float AwayLeft => State == BayState.Away ? Mathf.Max(0f, returnAt - Time.time) : 0f;

        /// <summary>Pieces the current order still lacks (0 without an order or once it is met).</summary>
        public int MissingCount
        {
            get
            {
                if (State != BayState.Loading || contract == null) return 0;
                int missing = 0;
                foreach (var line in required) missing += Mathf.Max(0, line.Value - LoadedOf(line.Key));
                return missing;
            }
        }

        public int RequiredCount
        {
            get
            {
                int n = 0;
                foreach (var line in required) n += line.Value;
                return n;
            }
        }

        /// <summary>What the missing part of the order would pay (order bonus included).</summary>
        public long MissingValue
        {
            get
            {
                if (State != BayState.Loading || contract == null) return 0;
                long value = 0;
                foreach (var line in required) value += (long)Mathf.Max(0, line.Value - LoadedOf(line.Key)) * line.Key.BaseValue;
                return TruckBayMath.Payout(value, Stats.payoutMultiplier * contract.RewardMultiplier);
            }
        }

        /// <summary>
        /// Completes the order now (a rewarded video or diamonds): the missing goods are booked as loaded, the order is
        /// announced complete and the truck leaves and pays as if the bars had been carried. Nothing physical is added.
        /// </summary>
        public bool FinishOrder()
        {
            if (MissingCount <= 0) return false;
            foreach (var line in new List<KeyValuePair<ItemDefinition, int>>(required))
            {
                int missing = line.Value - LoadedOf(line.Key);
                if (missing <= 0) continue;
                manifest[line.Key] = LoadedOf(line.Key) + missing;
                cargoValue += (long)missing * line.Key.BaseValue;
            }

            ReadyToLeave();
            AnnounceComplete();
            RefreshLabel();
            return true;
        }

        /// <summary>Starts the next truck's approach now (diamonds skip the wait).</summary>
        public bool CallNow()
        {
            if (State != BayState.Away) return false;
            returnAt = Time.time;
            return true;
        }

        public string StationId => definition != null ? definition.Id : name;
        public string UpgradeId => StationId;
        public string DisplayName => definition.DisplayName;
        public Sprite Icon => definition.Icon;
        public int MaxLevel => definition.MaxLevel;
        public bool IsMaxed => level >= MaxLevel;
        public long NextCost => IsMaxed ? 0 : definition.GetLevel(level + 1).upgradeCost;
        public string LevelLabel => $"Lv.{level}";
        public string NextEffect => IsMaxed
            ? string.Empty
            : $"{definition.GetLevel(level).capacity} → {definition.GetLevel(level + 1).capacity} · x{definition.GetLevel(level + 1).payoutMultiplier:0.0#}";
        public Vector3? FeedbackPosition => transform.position + Vector3.up * 2.5f;

        void Awake()
        {
            if (truckBody != null)
            {
                bodyScale = truckBody.localScale;
                bodyRest = truckBody.localPosition;
            }

            if (sideGate != null) gateClosed = sideGate.localRotation;
            if (reverseLights != null) reverseLights.SetActive(false);
            ApplyLevel();
        }

        void OnEnable() => cargo.Changed += OnCargoChanged;

        void OnDisable() => cargo.Changed -= OnCargoChanged;

        void Start()
        {
            Services.TryGet(out pool);
            pathLength = Vector3.Distance(parkPoint.position, awayPoint.position);
            along = pathLength;
            StationRegistry.Register(this);
            if (startDocked)
            {
                truck.gameObject.SetActive(true);
                Dock();
            }
            else
            {
                PlaceTruck();
                truck.gameObject.SetActive(false);
                returnAt = Time.time + definition.FirstArrivalDelay;
                RefreshLabel();
            }

            if (Services.TryGet(out UpgradeManager upgrades)) upgrades.Register(this);
            SaveRegistry.Register(this);
        }

        string ISaveable.SaveKey => "stock/" + StationId;

        string ISaveable.CaptureState()
        {
            // A truck that already left has been paid for; only cargo still waiting to leave is saved.
            var state = State == BayState.Departing ? new StockState() : cargo.CaptureStock();
            state.cash = cashPile != null ? cashPile.StoredCash : 0;
            return JsonUtility.ToJson(state);
        }

        void ISaveable.RestoreState(string json)
        {
            var state = JsonUtility.FromJson<StockState>(json);
            if (pool != null || Services.TryGet(out pool)) cargo.RestoreStock(state, pool, Count);
            if (cashPile != null && state.cash > 0) cashPile.Add(state.cash, cashPile.transform.position + Vector3.up * 0.5f);
        }

        void OnDestroy()
        {
            SaveRegistry.Unregister(this);
            StationRegistry.Unregister(this);
            if (Services.TryGet(out UpgradeManager upgrades)) upgrades.Unregister(this);
            if (truckBody != null) truckBody.DOKill();
            if (sideGate != null) sideGate.DOKill();
        }

        public void SetLevel(int newLevel)
        {
            level = Mathf.Clamp(newLevel, 1, definition.MaxLevel);
            ApplyLevel(true);
            UpgradeChanged?.Invoke(this);
        }

        void IUpgradeable.ApplyLevel(int newLevel) => SetLevel(newLevel);

        /// <summary>True for goods the truck buys, docked or not (what a carrier should bring here).</summary>
        public bool Takes(ItemDefinition item) => definition != null && definition.Accepts(item);

        /// <summary>The order still needs this product. The truck takes other goods too; these pay the order's bonus.</summary>
        public bool Wants(ItemDefinition item) => item != null && required.TryGetValue(item, out int need) && LoadedOf(item) < need;

        int LoadedOf(ItemDefinition item) => manifest.TryGetValue(item, out int n) ? n : 0;

        bool ContractMet()
        {
            if (contract == null || required.Count == 0) return false;
            foreach (var line in required)
                if (LoadedOf(line.Key) < line.Value) return false;
            return true;
        }

        /// <summary>Base value of the loaded goods that the order asked for.</summary>
        long MatchedValue()
        {
            long value = 0;
            foreach (var line in required) value += (long)Mathf.Min(LoadedOf(line.Key), line.Value) * line.Key.BaseValue;
            return value;
        }

        /// <summary>
        /// Picks the arriving truck's order among those the yard can serve right now (dock level, and every product
        /// obtainable), by weight. A dock with no eligible order gets a plain truck that buys a full load.
        /// </summary>
        void RollContract()
        {
            contract = null;
            contractAnnounced = false;
            required.Clear();
            var options = definition.Contracts;
            float total = 0f;
            foreach (var c in options)
                if (Eligible(c)) total += c.Weight;
            if (total <= 0f) return;
            float roll = UnityEngine.Random.value * total;
            foreach (var c in options)
            {
                if (!Eligible(c)) continue;
                roll -= c.Weight;
                if (roll > 0f) continue;
                contract = c;
                break;
            }

            if (contract == null) return;
            float shares = 0f;
            foreach (var line in contract.Lines) shares += line.share;
            int budget = Mathf.Max(1, Mathf.RoundToInt(cargo.Capacity * definition.ContractFill));
            foreach (var line in contract.Lines)
                required[line.item] = Mathf.Max(1, Mathf.RoundToInt(budget * line.share / Mathf.Max(0.01f, shares)));
        }

        bool Eligible(TruckContractDefinition c)
        {
            if (c == null || c.Weight <= 0f || level < c.MinLevel || c.Lines == null || c.Lines.Length == 0) return false;
            foreach (var line in c.Lines)
                if (line.item == null || !Takes(line.item) || !Obtainable(line.item, 2)) return false;
            return true;
        }

        /// <summary>
        /// Some stock of <paramref name="item"/> exists, or a built machine makes it from something that is itself
        /// obtainable (followed <paramref name="depth"/> machines back). Keeps a steel order away until there is steel.
        /// </summary>
        static bool Obtainable(ItemDefinition item, int depth)
        {
            foreach (var s in StationRegistry.All)
                if (s is Storage storage && storage.CountOf(i => i == item) > 0) return true;
            foreach (var s in StationRegistry.All)
            {
                if (s is not Machine m || m.Definition == null || !m.Definition.Produces(item)) continue;
                if (depth <= 0) return true;
                var d = m.Definition;
                if (!d.HasRecipes) return d.Input == null || Obtainable(d.Input, depth - 1);
                foreach (var recipe in d.Recipes)
                    if (recipe.output == item && Obtainable(recipe.input, depth - 1)) return true;
            }

            return false;
        }

        void RaiseContract(bool completed = false)
        {
            var status = new ContractStatus { StationId = StationId };
            if (contract != null)
            {
                long full = 0;
                foreach (var line in required)
                {
                    status.Required += line.Value;
                    status.Loaded += Mathf.Min(LoadedOf(line.Key), line.Value);
                    full += (long)line.Value * line.Key.BaseValue;
                }

                status.Title = contract.DisplayName;
                status.Icon = contract.Icon;
                status.Reward = TruckBayMath.Payout(full, Stats.payoutMultiplier * contract.RewardMultiplier);
                status.Active = State == BayState.Loading;
                status.Completed = completed;
            }

            GameEvents.RaiseContractChanged(status);
        }

        public bool CanAccept(ItemDefinition item) => State == BayState.Loading && Takes(item) && cargo.CanAccept(item);

        public void Accept(WorldItem item)
        {
            var kind = item.Definition;
            cargo.Accept(item);
            Count(item);
            GameEvents.RaiseItemsDelivered(StationId, kind, 1);
            Punch(0.035f);
            bool met = ContractMet();
            if (cargo.IsFull || met) ReadyToLeave();
            if (met && !contractAnnounced) AnnounceComplete();
            else if (!contractAnnounced) RaiseContract();
            RefreshLabel();
        }

        /// <summary>The order is complete: horn, a thump on the camera, and the HUD card says so.</summary>
        void AnnounceComplete()
        {
            if (contractAnnounced) return;
            contractAnnounced = true;
            GameFeedback.Sfx(definition.ContractSfx);
            GameFeedback.CameraPunch(0.8f);
            GameFeedback.CameraShake(0.3f);
            var config = GameFeedback.Config;
            GameFeedback.Popup("ORDER COMPLETE!", truck.position + Vector3.up * 3.6f, config != null ? config.PositivePopupColor : Color.yellow, 1.5f);
            Punch(0.1f);
            RaiseContract(true);
        }

        /// <summary>Books a crate on the truck into the load's value and manifest.</summary>
        void Count(WorldItem item)
        {
            var kind = item.Definition;
            cargoValue += kind.BaseValue;
            manifest[kind] = manifest.TryGetValue(kind, out int n) ? n + 1 : 1;
        }

        /// <summary>
        /// The load is complete (or the bed full): the side swings up, the horn sounds and the beacon turns; the truck
        /// pulls out departDelay later. Once per visit.
        /// </summary>
        void ReadyToLeave()
        {
            if (leaving)
            {
                departAt = Time.time + departDelay;
                return;
            }

            leaving = true;
            departAt = Time.time + departDelay;
            Gate(false);
            GameFeedback.Sfx(hornSfx);
        }

        void Gate(bool open)
        {
            if (sideGate == null) return;
            sideGate.DOKill();
            sideGate.DOLocalRotateQuaternion(open ? gateClosed * Quaternion.Euler(gateOpenEuler) : gateClosed, open ? 0.45f : 0.3f)
                .SetEase(open ? Ease.OutBounce : Ease.InOutQuad);
            GameFeedback.Sfx(gateSfx);
        }

        /// <summary>Beacon, reverse lights and beeps while reversing in; the body settles as the bed fills.</summary>
        void Show()
        {
            bool moving = State is BayState.Arriving or BayState.Departing || leaving;
            if (beacon != null && moving) beacon.Rotate(0f, 540f * Time.deltaTime, 0f, Space.Self);
            if (State == BayState.Arriving)
            {
                bool on = Mathf.Repeat(Time.time, 0.5f) < 0.25f;
                if (reverseLights != null && reverseLights.activeSelf != on) reverseLights.SetActive(on);
                if (Time.time >= nextBeep)
                {
                    nextBeep = Time.time + 0.5f;
                    GameFeedback.Sfx(reverseSfx);
                }
            }

            if (truckBody != null && fullSink > 0f)
            {
                float fill = cargo.Capacity > 0 ? (float)cargo.Count / cargo.Capacity : 0f;
                var target = bodyRest + Vector3.down * (fullSink * fill);
                truckBody.localPosition = Vector3.MoveTowards(truckBody.localPosition, target, Time.deltaTime * 0.6f);
            }
        }

        void Update()
        {
            Show();
            switch (State)
            {
                case BayState.Away:
                    if (Time.time >= returnAt) BeginArrival();
                    break;

                case BayState.Arriving:
                    Drive(0f);
                    if (along <= 0.001f) Dock();
                    break;

                case BayState.Loading:
                    if (departAt >= 0f && Time.time >= departAt) Depart();
                    break;

                case BayState.Departing:
                    Drive(pathLength);
                    if (along >= pathLength - 0.001f) Leave();
                    break;
            }
        }

        void BeginArrival()
        {
            State = BayState.Arriving;
            along = pathLength;
            PlaceTruck();
            truck.gameObject.SetActive(true);
            if (exhaust != null) exhaust.Play();
            RefreshLabel();
        }

        void Dock()
        {
            State = BayState.Loading;
            dockedAt = Time.time;
            along = 0f;
            PlaceTruck();
            RollContract();
            departAt = -1f;
            leaving = false;
            if (cargo.IsFull || ContractMet()) ReadyToLeave();
            if (exhaust != null) exhaust.Stop();
            if (reverseLights != null) reverseLights.SetActive(false);
            Gate(true);
            GameFeedback.Sfx(definition.ArriveSfx);
            Punch(0.07f);
            RefreshLabel();
            RaiseContract();
        }

        void Depart()
        {
            State = BayState.Departing;
            departAt = -1f;

            // Goods the order asked for earn its bonus on top of the dock's multiplier; anything else on the bed is paid
            // at the dock's plain rate.
            long matched = MatchedValue();
            float bonus = contract != null ? contract.RewardMultiplier : 1f;
            long total = TruckBayMath.Payout(matched, Stats.payoutMultiplier * bonus) + TruckBayMath.Payout(cargoValue - matched, Stats.payoutMultiplier);
            Vector3 from = payPoint != null ? payPoint.position : truck.position + Vector3.up * 2f;
            if (cashPile != null) cashPile.Add(total, from);
            GameFeedback.Sfx(definition.PayoutSfx);
            GameFeedback.Sfx(definition.DepartSfx);
            var config = GameFeedback.Config;
            GameFeedback.Popup($"+${total}", from + Vector3.up * 1.2f, config != null ? config.PositivePopupColor : Color.yellow, 1.4f);

            foreach (var line in manifest)
                GameEvents.RaiseItemsSold(line.Key, line.Value, TruckBayMath.Share(total, (long)line.Key.BaseValue * line.Value, cargoValue));
            manifest.Clear();
            cargoValue = 0;
            contract = null;
            required.Clear();
            RaiseContract();

            if (exhaust != null) exhaust.Play();
            Punch(0.07f);
            RefreshLabel();
            Shipped?.Invoke(this, total);
        }

        void Leave()
        {
            State = BayState.Away;
            leaving = false;
            if (truckBody != null) truckBody.localPosition = bodyRest;
            if (exhaust != null) exhaust.Stop();
            truck.gameObject.SetActive(false);

            WorldItem item;
            while ((item = cargo.Take(null)) != null)
            {
                if (pool != null) pool.Release(item);
                else item.Deactivate();
            }

            float tempo = Services.TryGet(out ScrapYardKing.Boosts.BoostManager boosts) ? boosts.Multiplier(ScrapYardKing.Boosts.BoostKind.TruckTempo) : 1f;
            returnAt = Time.time + Stats.returnDelay / tempo;
            RefreshLabel();
        }

        void Drive(float target)
        {
            float speed = TruckBayMath.SpeedAt(along, definition.TruckSpeed, crawlSpeed, easeDistance);
            float next = Mathf.MoveTowards(along, target, speed * Time.deltaTime);
            float moved = next - along;
            along = next;
            PlaceTruck();
            SpinWheels(moved);
        }

        void PlaceTruck()
        {
            Vector3 park = parkPoint.position;
            Vector3 dir = pathLength > 0.001f ? (awayPoint.position - park) / pathLength : Vector3.zero;
            truck.SetPositionAndRotation(park + dir * along, parkPoint.rotation);
        }

        void SpinWheels(float moved)
        {
            if (wheels == null || Mathf.Approximately(moved, 0f)) return;
            // Positive = rolling forward (away from the dock when the truck faces the road it leaves by).
            Vector3 dir = awayPoint.position - parkPoint.position;
            float sign = Vector3.Dot(parkPoint.forward, dir) >= 0f ? 1f : -1f;
            float degrees = sign * moved / (2f * Mathf.PI * wheelRadius) * 360f;
            foreach (var w in wheels)
                if (w != null) w.Rotate(degrees, 0f, 0f, Space.Self);
        }

        void Punch(float amount)
        {
            if (truckBody == null) return;
            truckBody.DOKill();
            truckBody.localScale = bodyScale;
            truckBody.DOPunchScale(new Vector3(amount, -amount * 1.4f, amount) * bodyScale.x, 0.28f, 6, 0.6f);
        }

        void ApplyLevel(bool animate = false)
        {
            if (definition == null || cargo == null) return;
            cargo.Capacity = Stats.capacity;
            if (levelVisuals != null) levelVisuals.Apply(level, animate);
            // A bigger bed can turn a truck that was "full" into one with room again.
            if (!cargo.IsFull && !ContractMet() && State == BayState.Loading && leaving)
            {
                departAt = -1f;
                leaving = false;
                Gate(true);
            }
            else if (!cargo.IsFull && !ContractMet()) departAt = -1f;
            RefreshLabel();
        }

        void OnCargoChanged(ItemPile _) => RefreshLabel();

        void RefreshLabel()
        {
            if (label == null || definition == null) return;
            label.Set(definition.DisplayName, level, cargo.Count, cargo.Capacity);
            switch (State)
            {
                case BayState.Loading:
                    if (contract == null) label.SetStatus(StationStatus.None);
                    else
                    {
                        int need = 0, got = 0;
                        foreach (var line in required)
                        {
                            need += line.Value;
                            got += Mathf.Min(LoadedOf(line.Key), line.Value);
                        }

                        label.SetStatus(StationStatus.Boosted, $"{contract.DisplayName} {got}/{need}");
                    }

                    break;
                case BayState.Arriving:
                    label.SetStatus(StationStatus.Boosted, definition.ArrivingText);
                    break;
                default:
                    label.SetStatus(StationStatus.Full, definition.AwayText);
                    break;
            }
        }
    }
}

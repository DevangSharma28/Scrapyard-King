using System;
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
    /// Counter where finished goods are sold. Carriers stock the counter through a deposit pad; each sale takes a few
    /// units, sends them to the buyer and pops cash bundles onto the <see cref="CashPile"/>.
    /// Sales come from a walk-in timer, or from the customer queue (which turns <see cref="WalkInDemand"/> off and
    /// uses <see cref="TryHandOver"/> + <see cref="CompleteSale"/>).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SellDesk : MonoBehaviour, IItemReceiver, IStation, IUpgradeable
    {
        [SerializeField] SellDeskDefinition definition;
        [SerializeField, Min(1)] int level = 1;
        [SerializeField] ItemPile counter;
        [SerializeField] CashPile cashPile;
        [Tooltip("Where sold goods fly to (the buyer side of the counter).")]
        [SerializeField] Transform buyerPoint;
        [Tooltip("Punched on every sale; cash pops out of here.")]
        [SerializeField] Transform register;
        [SerializeField] StationLabel label;
        [Tooltip("When on, the desk sells on its own timer. The customer queue turns this off and calls ServeCustomer.")]
        [SerializeField] bool walkInDemand = true;
        [SerializeField] LevelVisuals levelVisuals;

        ItemPool pool;
        float nextSaleTime = -1f;
        bool customersWaiting;

        public event Action<SellDesk, long> Sold;
        public event Action<IUpgradeable> UpgradeChanged;

        public SellDeskDefinition Definition => definition;
        public int Level => level;
        public SellDeskLevel Stats => definition.GetLevel(level);
        public int Stock => counter.Count;
        public bool CanTakeStock => counter.FreeSpace > 0;
        public CashPile CashPile => cashPile;

        public string StationId => definition != null ? definition.Id : name;
        public string UpgradeId => StationId;
        public string DisplayName => definition.DisplayName;
        public Sprite Icon => definition.Icon;
        public int MaxLevel => definition.MaxLevel;
        public bool IsMaxed => level >= MaxLevel;
        public long NextCost => IsMaxed ? 0 : definition.GetLevel(level + 1).upgradeCost;
        public string LevelLabel => $"Lv.{level}";
        public string NextEffect => IsMaxed ? string.Empty : $"{UnitsPerMinute(level):0} → {UnitsPerMinute(level + 1):0}/min";
        public Vector3? FeedbackPosition => transform.position + Vector3.up * 2.5f;

        /// <summary>Average units sold per minute at a level, assuming the counter never runs dry.</summary>
        public float UnitsPerMinute(int atLevel)
        {
            var l = definition.GetLevel(atLevel);
            return 60f / l.saleInterval * (l.unitsPerSale.x + l.unitsPerSale.y) * 0.5f;
        }

        /// <summary>Set by the customer line while its front customer waits for stock that isn't on the counter.</summary>
        /// <summary>The front customer is waiting at an empty counter (shows NEEDS STOCK).</summary>
        public bool CustomersWaiting => customersWaiting;

        /// <summary>What the front customer asked for (null = no order yet). The guide stocks this first.</summary>
        public ItemDefinition WaitingFor { get; private set; }

        /// <summary>Set by the customer line: the front order and whether the counter has none of it.</summary>
        public void SetWaitingFor(ItemDefinition item, bool outOfStock)
        {
            WaitingFor = item;
            if (customersWaiting == outOfStock) return;
            customersWaiting = outOfStock;
            RefreshLabel();
        }

        public bool WalkInDemand
        {
            get => walkInDemand;
            set => walkInDemand = value;
        }

        void Awake() => ApplyLevel();

        void OnEnable() => counter.Changed += OnCounterChanged;

        void OnDisable() => counter.Changed -= OnCounterChanged;

        void Start()
        {
            Services.TryGet(out pool);
            RefreshLabel();
            StationRegistry.Register(this);
            if (Services.TryGet(out UpgradeManager upgrades)) upgrades.Register(this);
        }

        void OnDestroy()
        {
            StationRegistry.Unregister(this);
            if (Services.TryGet(out UpgradeManager upgrades)) upgrades.Unregister(this);
        }

        public void SetLevel(int newLevel)
        {
            level = Mathf.Clamp(newLevel, 1, definition.MaxLevel);
            ApplyLevel(true);
            UpgradeChanged?.Invoke(this);
        }

        void IUpgradeable.ApplyLevel(int newLevel) => SetLevel(newLevel);

        /// <summary>Takes what the definition sells while the counter has room.</summary>
        public bool CanAccept(ItemDefinition item) => definition != null && definition.Sells(item) && counter.CanAccept(item);

        public bool Sells(ItemDefinition item) => definition != null && definition.Sells(item);

        /// <summary>Units on the counter matching <paramref name="filter"/> (null = all).</summary>
        public int StockOf(Func<ItemDefinition, bool> filter) => counter.CountOf(filter);

        public void Accept(WorldItem item)
        {
            counter.Accept(item);
            if (nextSaleTime < 0f) nextSaleTime = Time.time + definition.FirstSaleDelay;
            GameEvents.RaiseItemsDelivered(StationId, item.Definition, 1);
        }

        void Update()
        {
            if (!walkInDemand) return;
            if (counter.Count == 0)
            {
                nextSaleTime = -1f;
                return;
            }

            if (nextSaleTime < 0f) nextSaleTime = Time.time + definition.FirstSaleDelay;
            if (Time.time < nextSaleTime) return;

            ServeCustomer(RollUnitsWanted(), buyerPoint);
            nextSaleTime = Time.time + Stats.saleInterval;
        }

        /// <summary>Units one customer wants at the current level (random in the level's range).</summary>
        public int RollUnitsWanted()
        {
            var range = Stats.unitsPerSale;
            return UnityEngine.Random.Range(range.x, range.y + 1);
        }

        /// <summary>Sells up to <paramref name="units"/> from the counter to a buyer at <paramref name="buyer"/>. Returns cash earned.</summary>
        public long ServeCustomer(int units, Transform buyer)
        {
            long total = 0;
            int sold = 0;
            ItemDefinition soldItem = null;
            for (int i = 0; i < units; i++)
            {
                if (!TryHandOver(buyer, i * 0.06f, out var item, out long value)) break;
                soldItem = item;
                total += value;
                sold++;
            }

            CompleteSale(soldItem, sold, total);
            return total;
        }

        /// <summary>
        /// Takes one unit off the counter and throws it to <paramref name="buyer"/>. No cash yet: the buyer pays for the
        /// whole order with <see cref="CompleteSale"/>. False when the counter is empty.
        /// </summary>
        public bool TryHandOver(Transform buyer, float delay, out ItemDefinition item, out long value) =>
            TryHandOver(buyer, delay, null, out item, out value);

        /// <summary>As above, for a unit matching <paramref name="filter"/> (a customer who wants one material).</summary>
        public bool TryHandOver(Transform buyer, float delay, Func<ItemDefinition, bool> filter, out ItemDefinition item, out long value)
        {
            item = null;
            value = 0;
            var unit = counter.Take(filter);
            if (unit == null) return false;

            item = unit.Definition;
            value = Mathf.Max(1, Mathf.RoundToInt(item.BaseValue * Stats.priceMultiplier));
            SendToBuyer(unit, buyer != null ? buyer : buyerPoint, delay);
            return true;
        }

        /// <summary>Pays for a finished order: cash bundles pop onto the pile, a popup shows the total, sale events fire.</summary>
        public void CompleteSale(ItemDefinition soldItem, int sold, long total)
        {
            if (sold <= 0) return;

            if (register != null)
            {
                register.DOKill(true);
                register.DOPunchScale(Vector3.one * 0.2f, 0.3f, 6, 0.6f);
            }

            var from = register != null ? register.position + Vector3.up * 0.8f : transform.position + Vector3.up;
            if (cashPile != null) cashPile.Add(total, from);
            GameFeedback.Sfx(definition.SaleSfx);

            var config = GameFeedback.Config;
            GameFeedback.Popup($"+${total}", from + Vector3.up * 0.8f, config != null ? config.PositivePopupColor : Color.yellow, 1.1f);

            GameEvents.RaiseItemsSold(soldItem, sold, total);
            Sold?.Invoke(this, total);
        }

        void SendToBuyer(WorldItem item, Transform destination, float delay)
        {
            var t = item.transform;
            t.SetParent(null, true);
            t.DOKill(true);
            DOTween.Sequence()
                .AppendInterval(delay)
                .Append(t.DOJump(destination.position, 1.2f, 1, 0.35f).SetEase(Ease.OutQuad))
                .Join(t.DOScale(0f, 0.35f).SetEase(Ease.InBack))
                .OnComplete(() =>
                {
                    if (pool != null) pool.Release(item);
                    else item.Deactivate();
                })
                .SetTarget(t);
        }

        void ApplyLevel(bool animate = false)
        {
            if (definition == null || counter == null) return;
            counter.Capacity = Stats.counterCapacity;
            if (levelVisuals != null) levelVisuals.Apply(level, animate);
            RefreshLabel();
        }

        void OnCounterChanged(ItemPile _) => RefreshLabel();

        void RefreshLabel()
        {
            if (label == null || definition == null) return;
            label.Set(definition.DisplayName, level, counter.Count, counter.Capacity);
            label.SetStatus(customersWaiting ? StationStatus.NeedsStock : StationStatus.None);
        }
    }
}

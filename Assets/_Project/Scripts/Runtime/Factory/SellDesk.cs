using System;
using DG.Tweening;
using ScrapYardKing.Core;
using ScrapYardKing.Economy;
using ScrapYardKing.Feedback;
using ScrapYardKing.Items;
using ScrapYardKing.UI;
using UnityEngine;

namespace ScrapYardKing.Factory
{
    /// <summary>
    /// Counter where finished goods are sold. Carriers stock the counter through a deposit pad; each sale takes a few
    /// units, sends them to the buyer and pops cash bundles onto the <see cref="CashPile"/>.
    /// Sales are driven by a walk-in timer until the customer queue takes over via <see cref="ServeCustomer"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SellDesk : MonoBehaviour, IItemReceiver
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

        ItemPool pool;
        float nextSaleTime = -1f;

        public event Action<SellDesk, long> Sold;

        public SellDeskDefinition Definition => definition;
        public int Level => level;
        public SellDeskLevel Stats => definition.GetLevel(level);
        public int Stock => counter.Count;

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
        }

        public void SetLevel(int newLevel)
        {
            level = Mathf.Clamp(newLevel, 1, definition.MaxLevel);
            ApplyLevel();
        }

        /// <summary>Sells anything with a price.</summary>
        public bool CanAccept(ItemDefinition item) => item != null && item.BaseValue > 0 && counter.CanAccept(item);

        public void Accept(WorldItem item)
        {
            counter.Accept(item);
            if (nextSaleTime < 0f) nextSaleTime = Time.time + definition.FirstSaleDelay;
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

            var stats = Stats;
            ServeCustomer(UnityEngine.Random.Range(stats.unitsPerSale.x, stats.unitsPerSale.y + 1), buyerPoint);
            nextSaleTime = Time.time + stats.saleInterval;
        }

        /// <summary>Sells up to <paramref name="units"/> from the counter to a buyer at <paramref name="buyer"/>. Returns cash earned.</summary>
        public long ServeCustomer(int units, Transform buyer)
        {
            var stats = Stats;
            long total = 0;
            int sold = 0;
            ItemDefinition soldItem = null;
            var destination = buyer != null ? buyer : buyerPoint;

            for (int i = 0; i < units; i++)
            {
                var item = counter.Take(null);
                if (item == null) break;

                soldItem = item.Definition;
                total += Mathf.Max(1, Mathf.RoundToInt(item.Definition.BaseValue * stats.priceMultiplier));
                sold++;
                SendToBuyer(item, destination, i * 0.06f);
            }

            if (sold == 0) return 0;

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
            return total;
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

        void ApplyLevel()
        {
            if (definition == null || counter == null) return;
            counter.Capacity = Stats.counterCapacity;
            RefreshLabel();
        }

        void OnCounterChanged(ItemPile _) => RefreshLabel();

        void RefreshLabel()
        {
            if (label != null && definition != null) label.Set(definition.DisplayName, level, counter.Count, counter.Capacity);
        }
    }
}

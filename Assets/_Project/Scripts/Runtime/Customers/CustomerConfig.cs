using System;
using ScrapYardKing.Feedback;
using ScrapYardKing.Items;
using UnityEngine;
using Random = UnityEngine.Random;

namespace ScrapYardKing.Customers
{
    /// <summary>
    /// Who shows up at a sell desk and how they behave. How fast the desk serves them, how much each one buys and how
    /// long the line may get come from the desk's level (<see cref="Factory.SellDeskLevel"/>).
    /// </summary>
    [CreateAssetMenu(fileName = "CustomerConfig", menuName = "Scrap Yard King/Customers/Customer Config")]
    public sealed class CustomerConfig : ScriptableObject
    {
        /// <summary>One material customers ask for and how often.</summary>
        [Serializable]
        public struct OrderOption
        {
            public ItemDefinition item;
            [Min(0f)] public float weight;
        }

        [SerializeField] Customer[] prefabs;
        [SerializeField, Min(0.1f)] float walkSpeed = 2.8f;
        [Tooltip("Seconds between arrivals while the line has room (random in range).")]
        [SerializeField] Vector2 arrivalInterval = new(1.2f, 3f);
        [Tooltip("Seconds between units handed over at the counter.")]
        [SerializeField, Min(0.02f)] float handOverInterval = 0.16f;
        [Tooltip("What every customer buys when Order Options is empty (also the bubble icon).")]
        [SerializeField] ItemDefinition orderItem;
        [Tooltip("Materials customers ask for (blueprint: customers request material). Empty = Order Item.")]
        [SerializeField] OrderOption[] orderOptions;
        [Tooltip("Chance an order picks a material that is already on the counter, so the line rarely stalls on luck.")]
        [SerializeField, Range(0f, 1f)] float preferInStock = 0.8f;
        [SerializeField] SfxDefinition arriveSfx;
        [SerializeField] SfxDefinition happySfx;

        public int PrefabCount => prefabs != null ? prefabs.Length : 0;
        public float WalkSpeed => walkSpeed;
        public float HandOverInterval => handOverInterval;
        public ItemDefinition OrderItem => orderItem;
        public SfxDefinition ArriveSfx => arriveSfx;
        public SfxDefinition HappySfx => happySfx;

        /// <summary>Random look, re-rolled once if it matches <paramref name="avoid"/> so two in a row rarely match.</summary>
        /// <summary>
        /// Picks what the next customer wants. <paramref name="stockOf"/> returns counter stock per item; with
        /// probability <see cref="preferInStock"/> the pick is limited to items in stock (when any are).
        /// <paramref name="obtainable"/> rules out goods the yard cannot make yet (ingots before the Furnace is built),
        /// so nobody waits at the counter for something that will never come.
        /// </summary>
        public ItemDefinition RollOrder(Func<ItemDefinition, int> stockOf, Func<ItemDefinition, bool> obtainable = null)
        {
            if (orderOptions == null || orderOptions.Length == 0) return orderItem;

            bool inStockOnly = false;
            if (stockOf != null && Random.value < preferInStock)
                foreach (var o in orderOptions)
                    if (o.item != null && o.weight > 0f && stockOf(o.item) > 0)
                    {
                        inStockOnly = true;
                        break;
                    }

            float total = 0f;
            foreach (var o in orderOptions)
                if (Eligible(o, inStockOnly, stockOf, obtainable)) total += o.weight;
            if (total <= 0f) return orderItem;

            float roll = Random.value * total;
            foreach (var o in orderOptions)
            {
                if (!Eligible(o, inStockOnly, stockOf, obtainable)) continue;
                roll -= o.weight;
                if (roll <= 0f) return o.item;
            }

            return orderOptions[^1].item;
        }

        static bool Eligible(OrderOption o, bool inStockOnly, Func<ItemDefinition, int> stockOf, Func<ItemDefinition, bool> obtainable) =>
            o.item != null && o.weight > 0f && (!inStockOnly || stockOf(o.item) > 0) &&
            (obtainable == null || obtainable(o.item) || (stockOf != null && stockOf(o.item) > 0));

        public Customer PickPrefab(Customer avoid = null)
        {
            if (PrefabCount == 0) return null;
            var pick = prefabs[Random.Range(0, prefabs.Length)];
            if (pick == avoid && prefabs.Length > 1) pick = prefabs[(System.Array.IndexOf(prefabs, pick) + Random.Range(1, prefabs.Length)) % prefabs.Length];
            return pick;
        }

        public float NextArrivalDelay() => Random.Range(Mathf.Min(arrivalInterval.x, arrivalInterval.y), Mathf.Max(arrivalInterval.x, arrivalInterval.y));
    }
}

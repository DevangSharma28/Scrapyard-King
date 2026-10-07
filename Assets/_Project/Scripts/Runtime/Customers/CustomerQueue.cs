using System;
using System.Collections.Generic;
using ScrapYardKing.Core;
using ScrapYardKing.Factory;
using ScrapYardKing.Feedback;
using ScrapYardKing.Items;
using UnityEngine;

namespace ScrapYardKing.Customers
{
    /// <summary>
    /// The line in front of a <see cref="SellDesk"/> (blueprint: customer queue, cap 3 in the starter yard). Customers
    /// walk in from the road while the line has room, the front one gets its order handed over unit by unit, pays for
    /// it in one go and walks off. The desk level sets serve speed (one customer per sale interval), order size and
    /// line length. An empty counter stalls the line visibly; nothing is sold off-screen.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CustomerQueue : MonoBehaviour
    {
        [SerializeField] SellDesk desk;
        [SerializeField] CustomerConfig config;
        [Tooltip("Line positions, front (at the counter) first. Each slot's forward is the way customers face.")]
        [SerializeField] Transform[] slots;
        [Tooltip("Spawn point first, then the walk to the back of the line.")]
        [SerializeField] Transform[] entryPath;
        [Tooltip("Walk from the counter out of view; customers despawn at the last point.")]
        [SerializeField] Transform[] exitPath;
        [SerializeField] Transform customersRoot;
        [Tooltip("Seconds the happy customer lingers at the counter before walking off.")]
        [SerializeField, Min(0f)] float cheerDuration = 0.45f;

        readonly List<Customer> line = new();
        readonly Dictionary<Customer, Stack<Customer>> pools = new();
        readonly List<Vector3> scratch = new();
        float nextArrival, nextServiceAt, nextHandOver, waitingSince = -1f;
        bool serving;
        ItemDefinition orderSold;
        Customer lastPrefab;

        public int Count => line.Count;
        public int Capacity => desk != null ? Mathf.Clamp(desk.Stats.queueCapacity, 1, slots.Length) : 0;
        /// <summary>The customer at the counter, or null.</summary>
        public Customer Front => line.Count > 0 ? line[0] : null;
        public int CustomersServed { get; private set; }

        void Start()
        {
            if (desk == null || config == null || slots == null || slots.Length == 0 || entryPath == null || entryPath.Length == 0)
            {
                Debug.LogError("[CustomerQueue] Desk, config, slots and entry path are required.", this);
                enabled = false;
                return;
            }

            // The line replaces the walk-in timer.
            desk.WalkInDemand = false;
            nextArrival = Time.time + 0.5f;
        }

        void Update()
        {
            if (line.Count < Capacity && Time.time >= nextArrival) Arrive();
            ServeFront();
        }

        // ---------- arrivals ----------

        void Arrive()
        {
            nextArrival = Time.time + config.NextArrivalDelay();
            var prefab = config.PickPrefab(lastPrefab);
            if (prefab == null) return;
            lastPrefab = prefab;

            var customer = Rent(prefab, entryPath[0].position, entryPath[0].rotation);
            int slot = line.Count;
            line.Add(customer);

            scratch.Clear();
            // The entry path ends behind the last slot, so the final leg only crosses empty places.
            for (int i = 1; i < entryPath.Length; i++) scratch.Add(entryPath[i].position);
            scratch.Add(slots[slot].position);
            customer.Walk(scratch, config.WalkSpeed, slots[slot].rotation);
        }

        // ---------- serving ----------

        void ServeFront()
        {
            var front = Front;
            if (front == null || front.IsWalking)
            {
                desk.SetWaitingFor(null, false);
                return;
            }

            if (!front.HasOrder)
            {
                front.OrderItem = config.RollOrder(item => desk.StockOf(i => i == item), Obtainable);
                front.Wanted = desk.RollUnitsWanted();
                front.Received = 0;
                front.Owed = 0;
                orderSold = null;
                var shown = front.OrderItem != null ? front.OrderItem : config.OrderItem;
                front.Bubble.ShowOrder(shown != null ? shown.Icon : null, front.Wanted);
                GameFeedback.Sfx(config.ArriveSfx);
            }

            if (SettleForStock(front)) return;
            var wants = front.OrderItem;
            Func<ItemDefinition, bool> matches = wants != null ? i => i == wants : null;
            desk.SetWaitingFor(wants, desk.StockOf(matches) == 0);
            if (!serving)
            {
                float interval = desk.SaleInterval;
                float remaining = nextServiceAt - Time.time;
                front.Bubble.SetWait(remaining > 0f ? 1f - remaining / interval : -1f);
                if (remaining > 0f || desk.StockOf(matches) == 0) return;

                serving = true;
                nextServiceAt = Time.time + interval;
                nextHandOver = Time.time;
                front.Bubble.SetWait(-1f);
            }

            if (Time.time < nextHandOver) return;
            if (!desk.TryHandOver(front.HandPoint, 0f, matches, out var sold, out long value)) return;

            nextHandOver = Time.time + config.HandOverInterval;
            orderSold = sold;
            front.Received++;
            front.Owed += value;
            front.Bubble.SetRemaining(front.Wanted - front.Received);
            if (front.Received >= front.Wanted) Finish(front);
        }

        /// <summary>
        /// A customer whose material has run out does not wait forever. After a short wait (or at once when the counter
        /// is full of other goods, so theirs could never be stocked) they pay for what they already got and leave, or,
        /// if they got nothing yet, take something that is on the counter. Without this one unlucky order locks the line,
        /// the counter and every machine behind it for good. An empty counter still stalls the line: that bottleneck stays.
        /// Returns true when the front customer left.
        /// </summary>
        bool SettleForStock(Customer front)
        {
            var wants = front.OrderItem;
            if (wants == null || desk.StockOf(i => i == wants) > 0)
            {
                waitingSince = -1f;
                return false;
            }

            if (waitingSince < 0f) waitingSince = Time.time;
            bool patienceOver = config.SettleAfter > 0f && Time.time - waitingSince >= config.SettleAfter;
            if (desk.CanTakeStock && !patienceOver) return false;

            if (front.Received > 0)
            {
                waitingSince = -1f;
                Finish(front);
                return true;
            }

            if (desk.Stock == 0) return false;
            var settled = config.RollOrder(item => desk.StockOf(i => i == item), Obtainable, true);
            if (settled == null || settled == wants) return false;
            front.OrderItem = settled;
            front.Bubble.ShowOrder(settled.Icon, front.Wanted);
            waitingSince = -1f;
            return false;
        }

        /// <summary>Something in the yard can make <paramref name="item"/> now, or a bin already holds some.</summary>
        static bool Obtainable(ItemDefinition item)
        {
            foreach (var s in StationRegistry.All)
            {
                if (s is Machine m && m.Definition != null && m.Definition.Produces(item)) return true;
                if (s is Storage storage && storage.CountOf(i => i == item) > 0) return true;
            }

            return false;
        }

        void Finish(Customer customer)
        {
            serving = false;
            desk.CompleteSale(orderSold, customer.Received, customer.Owed);
            CustomersServed++;
            GameEvents.RaiseCustomerServed(desk.StationId, customer.Received);

            customer.Cheer();
            customer.Bubble.ShowHappy(cheerDuration + 0.4f);
            GameFeedback.Sfx(config.HappySfx);
            customer.ResetOrder();
            line.RemoveAt(0);

            scratch.Clear();
            if (exitPath != null)
                foreach (var p in exitPath)
                    if (p != null) scratch.Add(p.position);
            customer.Walk(scratch, config.WalkSpeed, null, cheerDuration, () => Return(customer));

            // Everyone steps up one place.
            for (int i = 0; i < line.Count; i++)
            {
                if (line[i].RetargetFinal(slots[i].position, slots[i].rotation)) continue;
                scratch.Clear();
                scratch.Add(slots[i].position);
                line[i].Walk(scratch, config.WalkSpeed, slots[i].rotation, 0.1f + i * 0.08f);
            }
        }

        // ---------- pooling ----------

        Customer Rent(Customer prefab, Vector3 position, Quaternion rotation)
        {
            if (!pools.TryGetValue(prefab, out var pool))
            {
                pool = new Stack<Customer>();
                pools[prefab] = pool;
            }

            var customer = pool.Count > 0 ? pool.Pop() : Instantiate(prefab, customersRoot != null ? customersRoot : transform);
            customer.Source = prefab;
            customer.transform.SetPositionAndRotation(position, rotation);
            customer.ResetOrder();
            customer.Bubble.HideImmediate();
            customer.gameObject.SetActive(true);
            return customer;
        }

        void Return(Customer customer)
        {
            customer.gameObject.SetActive(false);
            if (customer.Source != null && pools.TryGetValue(customer.Source, out var pool)) pool.Push(customer);
            else Destroy(customer.gameObject);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.8f);
            if (slots != null)
                foreach (var s in slots)
                    if (s != null) Gizmos.DrawWireSphere(s.position, 0.35f);
            DrawPath(entryPath);
            DrawPath(exitPath);
        }

        static void DrawPath(Transform[] points)
        {
            if (points == null) return;
            for (int i = 1; i < points.Length; i++)
                if (points[i - 1] != null && points[i] != null) Gizmos.DrawLine(points[i - 1].position, points[i].position);
        }
    }
}

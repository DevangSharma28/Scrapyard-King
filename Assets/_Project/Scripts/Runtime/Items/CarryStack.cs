using System;
using System.Collections.Generic;
using ScrapYardKing.Core;
using UnityEngine;

namespace ScrapYardKing.Items
{
    /// <summary>
    /// A physical stack of <see cref="WorldItem"/>s carried on a character's back. Shared by the player and
    /// carrier workers (Porter, Loader). Items fly into their slot, then sway with the carrier's movement.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CarryStack : MonoBehaviour, IItemReceiver, IItemSource
    {
        static readonly List<CarryStack> ActiveStacks = new();

        /// <summary>Every enabled stack in the scene. Transfer pads poll this instead of relying on physics triggers.</summary>
        public static IReadOnlyList<CarryStack> Active => ActiveStacks;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => ActiveStacks.Clear();

        sealed class Entry
        {
            public WorldItem Item;
            public float BaseY;
            public float Yaw;
            public Vector3 Current;
            public bool Settled;
        }

        [SerializeField] Transform stackRoot;
        [SerializeField, Min(0)] int capacity = 8;
        [Tooltip("Fly time and arc used when items are handed to this stack by pads and machines.")]
        [SerializeField, Min(0.05f)] float receiveDuration = 0.3f;
        [SerializeField, Min(0f)] float receiveArc = 1.2f;

        [Header("Sway")]
        [Tooltip("Top-of-stack lean per unit of carrier speed.")]
        [SerializeField, Min(0f)] float swayPerSpeed = 0.07f;
        [SerializeField, Min(0f)] float maxSway = 0.5f;
        [SerializeField, Min(0f)] float swayStiffness = 90f;
        [SerializeField, Min(0f)] float swayDamping = 11f;
        [SerializeField, Min(0f)] float swayTiltDegrees = 25f;
        [SerializeField, Min(0f)] float settleSharpness = 18f;

        readonly List<Entry> entries = new();
        Vector3 lastRootPosition, sway, swayVelocity;

        public event Action<CarryStack> Changed;

        public Transform StackRoot => stackRoot != null ? stackRoot : transform;
        public int Count => entries.Count;
        public bool IsFull => entries.Count >= capacity;
        public int FreeSpace => Mathf.Max(0, capacity - entries.Count);
        public float TopHeight => entries.Count == 0 ? 0f : entries[^1].BaseY + entries[^1].Item.Definition.StackHeight;

        public int Capacity
        {
            get => capacity;
            set
            {
                value = Mathf.Max(0, value);
                if (value == capacity) return;
                capacity = value;
                Changed?.Invoke(this);
            }
        }

        /// <summary>Whether this stack will take an item of <paramref name="item"/> type right now.</summary>
        public bool CanAccept(ItemDefinition item) => item != null && !IsFull;

        public int CountOf(ItemDefinition item)
        {
            int count = 0;
            foreach (var e in entries)
                if (e.Item.Definition == item) count++;
            return count;
        }

        /// <summary>Claims a slot and flies the item into it.</summary>
        public bool TryAdd(WorldItem item, float flyDuration, float arcHeight)
        {
            if (item == null || !CanAccept(item.Definition)) return false;

            var entry = new Entry
            {
                Item = item,
                BaseY = TopHeight,
                Yaw = UnityEngine.Random.Range(-12f, 12f)
            };
            entries.Add(entry);
            item.MoveTo(StackRoot, new Vector3(0f, entry.BaseY, 0f), Quaternion.Euler(0f, entry.Yaw, 0f), flyDuration, arcHeight, null);
            Changed?.Invoke(this);
            return true;
        }

        /// <summary>
        /// Removes the top-most item (optionally of one type) and hands it to the caller, who is responsible for moving it
        /// (e.g. into a machine hopper). Returns null when no matching item exists.
        /// </summary>
        public WorldItem TakeTop(ItemDefinition filter = null) => Take(filter == null ? null : item => item == filter);

        /// <summary>Removes the top-most item whose definition passes <paramref name="filter"/> (null = any).</summary>
        public WorldItem Take(Func<ItemDefinition, bool> filter)
        {
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var item = entries[i].Item;
                if (filter != null && !filter(item.Definition)) continue;

                entries.RemoveAt(i);
                RecalculateHeights();
                Changed?.Invoke(this);
                return item;
            }

            return null;
        }

        void IItemReceiver.Accept(WorldItem item) => TryAdd(item, receiveDuration, receiveArc);

        void OnEnable()
        {
            if (!ActiveStacks.Contains(this)) ActiveStacks.Add(this);
        }

        void OnDisable() => ActiveStacks.Remove(this);

        void Awake() => lastRootPosition = StackRoot.position;

        void RecalculateHeights()
        {
            float y = 0f;
            foreach (var e in entries)
            {
                e.BaseY = y;
                y += e.Item.Definition.StackHeight;
                e.Item.RetargetMove(new Vector3(0f, e.BaseY, 0f));
            }
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            var root = StackRoot;
            if (dt <= 0f)
            {
                lastRootPosition = root.position;
                return;
            }

            Vector3 worldVelocity = (root.position - lastRootPosition) / dt;
            lastRootPosition = root.position;
            Vector3 localVelocity = root.InverseTransformDirection(worldVelocity);
            localVelocity.y = 0f;

            Vector3 targetSway = Vector3.ClampMagnitude(-localVelocity * swayPerSpeed, maxSway);
            swayVelocity += ((targetSway - sway) * swayStiffness - swayVelocity * swayDamping) * dt;
            sway += swayVelocity * dt;

            float top = Mathf.Max(TopHeight, 0.001f);
            float blend = Easing.Damp(settleSharpness, dt);
            foreach (var e in entries)
            {
                var item = e.Item;
                if (item.CurrentState != WorldItem.State.Held) continue;

                var tr = item.transform;
                if (!e.Settled)
                {
                    e.Current = tr.localPosition;
                    e.Settled = true;
                }

                float k = e.BaseY / top;
                k *= k;
                var target = new Vector3(sway.x * k, e.BaseY, sway.z * k);
                e.Current = Vector3.Lerp(e.Current, target, blend);
                tr.localPosition = e.Current;
                tr.localRotation = Quaternion.Euler(sway.z * k * swayTiltDegrees, e.Yaw, -sway.x * k * swayTiltDegrees);
            }
        }
    }
}

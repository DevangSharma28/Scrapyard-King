using System;
using ScrapYardKing.Core;
using ScrapYardKing.Feedback;
using ScrapYardKing.Harvest;
using UnityEngine;

namespace ScrapYardKing.Items
{
    /// <summary>
    /// Vacuums nearby loose items into a <see cref="CarryStack"/>. Role-agnostic: the player uses it with feedback on,
    /// Porter workers reuse it silently.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ItemCollector : MonoBehaviour
    {
        [SerializeField] CarryStack stack;
        [SerializeField, Min(0f)] float radius = 2.2f;
        [Tooltip("Seconds between two pickups. Small staggering makes a pile read as a satisfying stream.")]
        [SerializeField, Min(0f)] float pickupInterval = 0.03f;
        [SerializeField, Min(1)] int maxPickupsPerFrame = 3;
        [SerializeField] bool playFeedback = true;
        [Tooltip("Only pick up these item types. Empty = anything the stack accepts.")]
        [SerializeField] ItemDefinition[] onlyItems;

        HarvestManager harvest;
        Func<ItemDefinition, bool> acceptFilter;
        float nextPickupTime, comboExpiresAt;
        int combo;
        bool wasFull;

        public event Action<WorldItem> Collected;

        public CarryStack Stack => stack;

        public float Radius
        {
            get => radius;
            set => radius = Mathf.Max(0f, value);
        }

        public float PickupInterval
        {
            get => pickupInterval;
            set => pickupInterval = Mathf.Max(0f, value);
        }

        /// <summary>Whether this collector would ever pick up <paramref name="item"/> (ignores stack space).</summary>
        public bool Wants(ItemDefinition item) => item != null && (onlyItems == null || onlyItems.Length == 0 || Array.IndexOf(onlyItems, item) >= 0);

        void Awake() => acceptFilter = item => stack != null && Wants(item) && stack.CanAccept(item);

        void Start() => Services.TryGet(out harvest);

        void Update()
        {
            if (stack == null) return;
            if (harvest == null && !Services.TryGet(out harvest)) return;

            HandleFullState();
            if (stack.IsFull || Time.time < nextPickupTime) return;

            var config = playFeedback ? GameFeedback.Config : null;
            float flyDuration = config != null ? config.PickupFlyDuration : 0.3f;
            float arc = config != null ? config.PickupArcHeight : 1.2f;

            for (int i = 0; i < maxPickupsPerFrame && !stack.IsFull; i++)
            {
                var item = harvest.TakeNearestCollectable(transform.position, radius, acceptFilter);
                if (item == null) break;

                if (!stack.TryAdd(item, flyDuration, arc))
                {
                    harvest.ReturnToGround(item);
                    break;
                }

                OnCollected(item, config);
                nextPickupTime = Time.time + pickupInterval;
                if (pickupInterval > 0f) break;
            }
        }

        void OnCollected(WorldItem item, FeedbackConfig config)
        {
            GameEvents.RaiseItemsCollected(item.Definition, 1);
            Collected?.Invoke(item);
            if (config == null) return;

            combo = Time.time <= comboExpiresAt ? combo + 1 : 0;
            comboExpiresAt = Time.time + config.PickupComboWindow;
            float pitch = 1f + Mathf.Min(combo, config.PickupPitchMaxSteps) * config.PickupPitchStep;
            GameFeedback.Sfx(config.PickupSfx, pitch);
        }

        void HandleFullState()
        {
            bool full = stack.IsFull && stack.Capacity > 0;
            if (full && !wasFull && playFeedback)
            {
                var config = GameFeedback.Config;
                if (config != null) GameFeedback.Sfx(config.StackFullSfx);
            }

            wasFull = full;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}

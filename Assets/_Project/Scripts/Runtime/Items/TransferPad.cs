using DG.Tweening;
using ScrapYardKing.Feedback;
using UnityEngine;

namespace ScrapYardKing.Items
{
    public enum TransferMode
    {
        /// <summary>Carrier's stack → target (machine hopper, counter).</summary>
        Deposit,
        /// <summary>Target (storage, tray) → carrier's stack.</summary>
        Withdraw
    }

    /// <summary>
    /// A floor pad that moves items between whoever stands on it and a target. Works for the player and for workers,
    /// because it polls <see cref="CarryStack.Active"/> instead of relying on physics triggers.
    /// A deposit pad can spill: when its target stays full while a carrier that allows spilling waits on it with a full
    /// stack, matching items are thrown onto the ground beside the pad. This breaks the one deadlock the physical chain
    /// has (storage full → machine jammed → full stack of raw scrap → can't pick up goods to sell).
    /// </summary>
    public sealed class TransferPad : MonoBehaviour
    {
        [SerializeField] TransferMode mode;
        [Tooltip("IItemReceiver for Deposit, IItemSource for Withdraw.")]
        [SerializeField] MonoBehaviour target;
        [SerializeField] Vector2 size = new(2f, 2f);
        [Tooltip("Seconds between items. Short enough to read as a stream, long enough to see each item.")]
        [SerializeField, Min(0.01f)] float interval = 0.07f;
        [SerializeField, Min(0f)] float engageDelay = 0.15f;

        [Header("Spill (deposit pads)")]
        [Tooltip("Seconds a full carrier waits on a jammed pad before items spill. 0 = never spill.")]
        [SerializeField, Min(0f)] float spillAfter;
        [Tooltip("Only these items spill (e.g. raw scrap at the crusher). Empty = any item the target takes.")]
        [SerializeField] ItemDefinition[] spillItems;
        [SerializeField] Transform spillDirection;
        [SerializeField, Min(0.02f)] float spillInterval = 0.08f;
        [Tooltip("Items thrown per jam: enough to make room for a few goods without emptying the whole stack.")]
        [SerializeField, Min(1)] int spillCount = 4;
        [SerializeField, Min(0f)] float spillCollectDelay = 6f;
        [SerializeField] SfxDefinition spillSfx;

        [Header("Visual")]
        [SerializeField] Transform visual;
        [SerializeField] SfxDefinition transferSfx;
        [SerializeField, Min(0f)] float pitchStep = 0.025f;
        [SerializeField, Min(0)] int maxPitchSteps = 16;

        IItemReceiver receiver;
        IItemSource source;
        CarryStack occupant;
        Vector3 visualScale;
        float occupiedSince, nextTransfer, jammedSince = -1f, nextSpill;
        int streak, spilled;
        Harvest.HarvestManager harvest;

        public CarryStack Occupant => occupant;
        /// <summary>Withdraw pads: what the pad takes items from.</summary>
        public IItemSource Source => source;
        /// <summary>Deposit pads: what the pad puts items into.</summary>
        public IItemReceiver Receiver => receiver;
        public TransferMode Mode => mode;

        public float Interval
        {
            get => interval;
            set => interval = Mathf.Max(0.01f, value);
        }

        void Awake()
        {
            receiver = target as IItemReceiver;
            source = target as IItemSource;
            if (mode == TransferMode.Deposit && receiver == null) Debug.LogError($"[TransferPad] {name}: target is not an IItemReceiver.", this);
            if (mode == TransferMode.Withdraw && source == null) Debug.LogError($"[TransferPad] {name}: target is not an IItemSource.", this);
            if (visual != null) visualScale = visual.localScale;
        }

        void Update()
        {
            var current = FindOccupant();
            if (current != occupant) SetOccupant(current);
            if (occupant == null) return;

            float now = Time.time;
            if (now - occupiedSince < engageDelay || now < nextTransfer) return;
            if (!TryTransfer())
            {
                TrySpill(now);
                return;
            }

            jammedSince = -1f;

            nextTransfer = now + interval;
            streak++;
            if (visual != null) visual.DOPunchScale(visualScale * 0.06f, 0.12f, 4, 0.5f);

            var sfx = transferSfx;
            if (sfx == null)
            {
                var config = GameFeedback.Config;
                if (config != null) sfx = config.PickupSfx;
            }

            GameFeedback.Sfx(sfx, 1f + Mathf.Min(streak, maxPitchSteps) * pitchStep);
        }

        bool TryTransfer()
        {
            if (mode == TransferMode.Deposit)
            {
                if (receiver == null) return false;
                var item = occupant.Take(receiver.CanAccept);
                if (item == null) return false;
                receiver.Accept(item);
                return true;
            }

            if (source == null || source.Count == 0 || occupant.IsFull) return false;
            var taken = source.Take(occupant.CanAccept);
            if (taken == null) return false;
            ((IItemReceiver)occupant).Accept(taken);
            return true;
        }

        void TrySpill(float now)
        {
            if (spillAfter <= 0f || mode != TransferMode.Deposit || occupant == null || !occupant.CanSpill) return;
            // A jam only matters for a full stack; once spilling starts, finish the batch.
            if ((spilled == 0 && !occupant.IsFull) || !occupant.Contains(ShouldSpill)) return;
            if (jammedSince < 0f)
            {
                jammedSince = now;
                spilled = 0;
                return;
            }

            if (now - jammedSince < spillAfter || now < nextSpill || spilled >= spillCount) return;
            if (harvest == null && !Core.Services.TryGet(out harvest)) return;
            var item = occupant.Take(ShouldSpill);
            if (item == null) return;

            nextSpill = now + spillInterval;
            if (spilled++ == 0)
            {
                var config = GameFeedback.Config;
                GameFeedback.Popup("FULL!", transform.position + Vector3.up * 2.2f, config != null ? config.WarningPopupColor : Color.red, 1f);
            }

            Vector3 away = spillDirection != null ? spillDirection.forward : -transform.forward;
            away.y = 0f;
            away = Quaternion.Euler(0f, Random.Range(-35f, 35f), 0f) * (away.sqrMagnitude > 0.001f ? away.normalized : Vector3.back);
            harvest.Drop(item, away * Random.Range(2.5f, 3.5f) + Vector3.up * 4.5f, spillCollectDelay);
            GameFeedback.Sfx(spillSfx, 1f + spilled * 0.05f);
        }

        bool ShouldSpill(ItemDefinition item) =>
            spillItems == null || spillItems.Length == 0 ? receiver != null && item != null : System.Array.IndexOf(spillItems, item) >= 0;

        void SetOccupant(CarryStack stack)
        {
            occupant = stack;
            occupiedSince = Time.time;
            jammedSince = -1f;
            streak = 0;
            if (visual == null) return;
            visual.DOKill();
            visual.DOScale(stack != null ? visualScale * 0.92f : visualScale, 0.15f).SetEase(Ease.OutBack);
        }

        CarryStack FindOccupant()
        {
            var stacks = CarryStack.Active;
            for (int i = 0; i < stacks.Count; i++)
            {
                Vector3 local = transform.InverseTransformPoint(stacks[i].transform.position);
                if (Mathf.Abs(local.x) <= size.x * 0.5f && Mathf.Abs(local.z) <= size.y * 0.5f) return stacks[i];
            }

            return null;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = mode == TransferMode.Deposit ? new Color(1f, 0.8f, 0.2f, 0.8f) : new Color(0.3f, 1f, 0.4f, 0.8f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.up * 0.05f, new Vector3(size.x, 0.1f, size.y));
        }
    }
}

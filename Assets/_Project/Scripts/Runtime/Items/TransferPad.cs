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

        [Header("Visual")]
        [SerializeField] Transform visual;
        [SerializeField] SfxDefinition transferSfx;
        [SerializeField, Min(0f)] float pitchStep = 0.025f;
        [SerializeField, Min(0)] int maxPitchSteps = 16;

        IItemReceiver receiver;
        IItemSource source;
        CarryStack occupant;
        Vector3 visualScale;
        float occupiedSince, nextTransfer;
        int streak;

        public CarryStack Occupant => occupant;
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
            if (!TryTransfer()) return;

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

        void SetOccupant(CarryStack stack)
        {
            occupant = stack;
            occupiedSince = Time.time;
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

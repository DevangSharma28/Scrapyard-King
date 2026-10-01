using DG.Tweening;
using ScrapYardKing.Core;
using ScrapYardKing.Feedback;
using ScrapYardKing.Player;
using UnityEngine;

namespace ScrapYardKing.Tiles
{
    /// <summary>
    /// Floor tile the player activates by standing on it. Polls the player's position against the tile footprint
    /// (like <see cref="Items.TransferPad"/>), so it needs no physics. Only the player engages tiles; workers walk over them.
    /// Subclasses react to <see cref="OnEngage"/>, <see cref="OnStay"/> and <see cref="OnDisengage"/>.
    /// </summary>
    public abstract class Tile : MonoBehaviour
    {
        [SerializeField] Vector2 size = new(2.4f, 2.4f);
        [Tooltip("Seconds the player must stand on the tile before it engages, so walking across it does nothing.")]
        [SerializeField, Min(0f)] float engageDelay = 0.2f;
        [Tooltip("Pressed down while the player stands on it.")]
        [SerializeField] Transform visual;
        [SerializeField] SfxDefinition engageSfx;

        PlayerCharacter player;
        Vector3 visualScale = Vector3.one;
        float occupiedSince = -1f;

        /// <summary>The player is inside the footprint.</summary>
        public bool IsOccupied => occupiedSince >= 0f;
        /// <summary>The player has stood here long enough for the tile to act.</summary>
        public bool IsEngaged { get; private set; }
        public Vector2 Size => size;
        protected Transform Visual => visual;
        protected Vector3 VisualScale => visualScale;
        protected PlayerCharacter Player => player;

        protected virtual void Awake()
        {
            if (visual != null) visualScale = visual.localScale;
        }

        protected virtual void Start() => Services.TryGet(out player);

        protected virtual void OnDisable()
        {
            if (IsEngaged) Disengage();
            occupiedSince = -1f;
        }

        protected virtual void Update()
        {
            if (player == null && !Services.TryGet(out player)) return;

            bool inside = Contains(player.transform.position);
            if (inside && !IsOccupied) SetOccupied(true);
            else if (!inside && IsOccupied) SetOccupied(false);
            if (!IsOccupied) return;

            if (!IsEngaged && Time.time - occupiedSince >= engageDelay)
            {
                IsEngaged = true;
                GameFeedback.Sfx(engageSfx);
                OnEngage();
            }

            if (IsEngaged) OnStay(Time.deltaTime);
        }

        public bool Contains(Vector3 worldPosition)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            return Mathf.Abs(local.x) <= size.x * 0.5f && Mathf.Abs(local.z) <= size.y * 0.5f;
        }

        void SetOccupied(bool on)
        {
            occupiedSince = on ? Time.time : -1f;
            if (!on && IsEngaged) Disengage();

            if (visual == null) return;
            visual.DOKill();
            visual.DOScale(on ? visualScale * 0.94f : visualScale, 0.18f).SetEase(Ease.OutBack);
        }

        void Disengage()
        {
            IsEngaged = false;
            OnDisengage();
        }

        protected virtual void OnEngage() { }

        protected virtual void OnStay(float deltaTime) { }

        protected virtual void OnDisengage() { }

        protected virtual void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.8f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.up * 0.05f, new Vector3(size.x, 0.1f, size.y));
        }
    }
}

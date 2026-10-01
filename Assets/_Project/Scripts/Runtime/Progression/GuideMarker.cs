using DG.Tweening;
using UnityEngine;

namespace ScrapYardKing.Progression
{
    /// <summary>World-space "go here" marker: a bouncing arrow over the target and a pulsing ring on the ground.</summary>
    public sealed class GuideMarker : MonoBehaviour
    {
        [Tooltip("Scaled to show/hide. Parent of arrow and ring.")]
        [SerializeField] Transform visual;
        [Tooltip("Raised per target so the arrow clears tall objects while the ring stays on the ground.")]
        [SerializeField] Transform arrowPivot;
        [SerializeField] Transform arrow;
        [SerializeField] Transform ring;
        [SerializeField] float arrowHeight = 2.4f;
        [SerializeField] float bobHeight = 0.45f;
        [SerializeField, Min(0.05f)] float bobDuration = 0.45f;
        [SerializeField, Min(0.01f)] float moveDuration = 0.25f;

        Vector3? target;

        public bool Visible { get; private set; }

        void Awake()
        {
            if (arrow != null)
                arrow.DOLocalMoveY(arrowHeight + bobHeight, bobDuration).From(arrowHeight).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo);
            if (ring != null)
            {
                var s = ring.localScale;
                ring.DOScale(s * 1.25f, 0.6f).From(s * 0.85f).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo);
            }

            if (visual != null) visual.localScale = Vector3.zero;
        }

        void OnDestroy()
        {
            if (arrow != null) arrow.DOKill();
            if (ring != null) ring.DOKill();
            if (visual != null) visual.DOKill();
            transform.DOKill();
        }

        /// <summary>Points at <paramref name="worldPosition"/>; arrow floats above it, ring sits on the ground.</summary>
        public void Show(Vector3 worldPosition, float extraHeight = 0f)
        {
            var ground = new Vector3(worldPosition.x, 0.06f, worldPosition.z);
            bool moved = !target.HasValue || (target.Value - ground).sqrMagnitude > 0.04f;
            target = ground;
            if (arrowPivot != null) arrowPivot.localPosition = Vector3.up * extraHeight;

            if (!Visible)
            {
                transform.DOKill();
                transform.position = ground;
                SetVisible(true);
            }
            else if (moved)
            {
                transform.DOKill();
                transform.DOMove(ground, moveDuration).SetEase(Ease.OutQuad);
            }
        }

        public void Hide()
        {
            target = null;
            if (Visible) SetVisible(false);
        }

        void SetVisible(bool show)
        {
            Visible = show;
            if (visual == null) return;
            visual.DOKill();
            visual.DOScale(show ? Vector3.one : Vector3.zero, show ? 0.3f : 0.15f).SetEase(show ? Ease.OutBack : Ease.InQuad);
        }
    }
}

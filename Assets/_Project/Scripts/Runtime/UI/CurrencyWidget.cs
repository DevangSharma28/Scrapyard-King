using DG.Tweening;
using ScrapYardKing.Economy;
using TMPro;
using UnityEngine;

namespace ScrapYardKing.UI
{
    /// <summary>HUD currency pill: counts up smoothly and punches its icon when the balance changes.</summary>
    public sealed class CurrencyWidget : MonoBehaviour
    {
        [SerializeField] TMP_Text label;
        [SerializeField] RectTransform icon;
        [SerializeField, Min(0.05f)] float countDuration = 0.45f;

        double shown;
        Tween countTween;
        Vector3 iconScale = Vector3.one;

        public RectTransform Icon => icon;

        void Awake()
        {
            if (icon != null) iconScale = icon.localScale;
        }

        void OnDestroy() => countTween?.Kill();

        public void SetInstant(long value)
        {
            countTween?.Kill();
            shown = value;
            label.text = CurrencyFormat.Short(value);
        }

        public void AnimateTo(long value)
        {
            countTween?.Kill();
            countTween = DOTween.To(() => shown, x =>
            {
                shown = x;
                label.text = CurrencyFormat.Short(x);
            }, value, countDuration).SetEase(Ease.OutCubic);

            if (icon == null) return;
            icon.DOKill(true);
            icon.localScale = iconScale;
            icon.DOPunchScale(iconScale * 0.3f, 0.25f, 8, 0.6f);
        }
    }
}

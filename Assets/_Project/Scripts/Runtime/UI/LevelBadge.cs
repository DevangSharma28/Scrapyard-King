using DG.Tweening;
using ScrapYardKing.Core;
using ScrapYardKing.Progression;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ScrapYardKing.UI
{
    /// <summary>Top-left yard level + XP bar (blueprint HUD). XP earned in the world flies in as stars first.</summary>
    public sealed class LevelBadge : MonoBehaviour
    {
        [SerializeField] RectTransform badge;
        [SerializeField] TMP_Text levelText;
        [SerializeField] Image fill;
        [SerializeField] TMP_Text xpText;
        [SerializeField] UIFlyer flyer;
        [SerializeField] Sprite xpIcon;
        [SerializeField, Min(0.05f)] float fillDuration = 0.35f;

        ProgressionManager progression;
        Tween fillTween;
        int shownLevel;

        public RectTransform Badge => badge;

        void Start()
        {
            if (!Services.TryGet(out progression)) return;
            shownLevel = progression.Level;
            levelText.text = shownLevel.ToString();
            fill.fillAmount = progression.Progress01;
            UpdateXpText();
            progression.XpGained += OnXpGained;
            progression.LevelChanged += OnLevelChanged;
        }

        void OnDestroy()
        {
            fillTween?.Kill();
            if (progression == null) return;
            progression.XpGained -= OnXpGained;
            progression.LevelChanged -= OnLevelChanged;
        }

        void OnXpGained(int amount, Vector3? source)
        {
            if (source.HasValue && flyer != null) flyer.Fly(source.Value, badge, xpIcon, 1, Refresh);
            else Refresh();
        }

        void OnLevelChanged(int level) => Refresh();

        void Refresh()
        {
            UpdateXpText();
            // Complete (not just kill) a running level-up: its callback writes the new level number, and XP often arrives
            // again within the fill time (the next sale), which used to leave the badge showing an old level.
            fillTween?.Kill(true);
            if (progression.Level != shownLevel)
            {
                shownLevel = progression.Level;
                fillTween = DOTween.Sequence()
                    .Append(fill.DOFillAmount(1f, fillDuration * 0.6f))
                    .AppendCallback(() =>
                    {
                        levelText.text = shownLevel.ToString();
                        fill.fillAmount = 0f;
                        badge.DOKill(true);
                        badge.DOPunchScale(Vector3.one * 0.45f, 0.5f, 7, 0.6f);
                    })
                    .Append(fill.DOFillAmount(progression.Progress01, fillDuration));
                return;
            }

            fillTween = fill.DOFillAmount(progression.Progress01, fillDuration).SetEase(Ease.OutCubic);
            badge.DOKill(true);
            badge.DOPunchScale(Vector3.one * 0.12f, 0.2f, 5, 0.5f);
        }

        void UpdateXpText()
        {
            if (xpText != null) xpText.text = $"{progression.Xp}/{progression.XpToNext}";
        }
    }
}

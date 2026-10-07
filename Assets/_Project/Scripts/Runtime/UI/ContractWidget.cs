using DG.Tweening;
using ScrapYardKing.Core;
using ScrapYardKing.Economy;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ScrapYardKing.UI
{
    /// <summary>
    /// HUD card for the truck's current order: what it is, how far it is loaded and what it pays. It listens to
    /// <see cref="GameEvents.ContractChanged"/> and knows nothing about docks or trucks. Hidden while no truck is waiting.
    /// </summary>
    public sealed class ContractWidget : MonoBehaviour
    {
        [SerializeField] RectTransform root;
        [SerializeField] CanvasGroup group;
        [SerializeField] Image icon;
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text progress;
        [SerializeField] Image fill;
        [SerializeField] TMP_Text reward;
        [Tooltip("Unit-scale child punched when the order moves on.")]
        [SerializeField] RectTransform punchRoot;
        [SerializeField, Min(0.05f)] float fadeDuration = 0.25f;
        [SerializeField, Min(0f)] float completeHold = 1.6f;
        [SerializeField] Color fillColor = new(0.35f, 0.85f, 0.4f);
        [SerializeField] Color completeColor = new(1f, 0.82f, 0.2f);

        int lastLoaded = -1;
        bool shown, celebrating;

        void Awake()
        {
            if (group != null) group.alpha = 0f;
            if (root != null) root.gameObject.SetActive(false);
        }

        void OnEnable() => GameEvents.ContractChanged += OnChanged;

        void OnDisable()
        {
            GameEvents.ContractChanged -= OnChanged;
            DOTween.Kill(this);
        }

        void OnChanged(ContractStatus status)
        {
            if (root == null) return;
            if (status.Completed)
            {
                Fill(status);
                if (fill != null) fill.color = completeColor;
                if (title != null) title.SetText("ORDER COMPLETE!");
                Show(true);
                celebrating = true;
                // the card grows, the pay rolls up from zero, then it settles and leaves
                DOTween.Kill(this);
                root.localScale = Vector3.one;
                DOTween.Sequence().SetTarget(this)
                    .Append(root.DOScale(1.12f, 0.25f).SetEase(Ease.OutBack))
                    .AppendInterval(Mathf.Max(0.2f, completeHold - 0.45f))
                    .Append(root.DOScale(1f, 0.2f))
                    .AppendCallback(() =>
                    {
                        celebrating = false;
                        Show(false);
                    });
                if (reward != null) UIAnim.CountTo(reward, 0, status.Reward, 0.6f, x => CurrencyFormat.Short(x));
                Punch(0.22f);
                return;
            }

            if (!status.Active)
            {
                if (celebrating) return;   // the truck leaving must not cut the celebration short
                Show(false);
                lastLoaded = -1;
                return;
            }

            if (celebrating)
            {
                DOTween.Kill(this);   // the next order is here: end the celebration
                root.localScale = Vector3.one;
                celebrating = false;
            }

            Fill(status);
            if (fill != null) fill.color = fillColor;
            Show(true);
            if (lastLoaded >= 0 && status.Loaded > lastLoaded) Punch(0.08f);
            lastLoaded = status.Loaded;
        }

        void Fill(ContractStatus status)
        {
            if (icon != null)
            {
                icon.sprite = status.Icon;
                icon.enabled = status.Icon != null;
            }

            if (title != null) title.SetText(status.Title);
            if (progress != null) progress.SetText("{0} / {1}", status.Loaded, status.Required);
            if (fill != null) fill.fillAmount = status.Required > 0 ? Mathf.Clamp01(status.Loaded / (float)status.Required) : 0f;
            if (reward != null) reward.SetText(CurrencyFormat.Short(status.Reward));
        }

        void Show(bool show)
        {
            if (show == shown) return;
            shown = show;
            if (group != null) group.DOKill();
            if (show) root.gameObject.SetActive(true);
            if (group == null)
            {
                root.gameObject.SetActive(show);
                return;
            }

            var tween = group.DOFade(show ? 1f : 0f, fadeDuration);
            if (!show) tween.OnComplete(() => root.gameObject.SetActive(false));
        }

        void Punch(float amount)
        {
            if (punchRoot == null) return;
            punchRoot.DOKill(true);
            punchRoot.DOPunchScale(Vector3.one * amount, 0.3f, 6, 0.6f);
        }
    }
}

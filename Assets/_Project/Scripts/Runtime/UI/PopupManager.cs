using System.Collections.Generic;
using DG.Tweening;
using ScrapYardKing.Core;
using ScrapYardKing.Economy;
using ScrapYardKing.Feedback;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ScrapYardKing.UI
{
    /// <summary>
    /// The one popup card every reward, question and message uses. Requests queue up and show one at a time, each
    /// going through the same beats: <b>entry</b> (dim, card pops in, title plate drops), <b>reveal</b> (icon pops,
    /// rays turn, sparkles burst), <b>reward</b> (the amount rolls up), <b>CTA</b> (buttons rise; the primary breathes),
    /// <b>exit</b> (card shrinks away while the reward flies to the HUD). Taps in the first moments are ignored so a
    /// tap meant for the world cannot claim or spend by accident.
    /// </summary>
    [DefaultExecutionOrder(-520)]
    public sealed class PopupManager : ServiceBehaviour<PopupManager>
    {
        [Header("Card")]
        [SerializeField] GameObject root;
        [SerializeField] Image dim;
        [SerializeField] RectTransform card;
        [SerializeField] CanvasGroup cardGroup;
        [SerializeField] Image titlePlate;
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text subtitle;
        [SerializeField] TMP_Text body;
        [SerializeField] RectTransform rays;
        [SerializeField] Image icon;
        [SerializeField] TMP_Text amount;
        [SerializeField] RectTransform sparkleRoot;

        [Header("Buttons")]
        [SerializeField] Button primary;
        [SerializeField] Image primaryBack;
        [SerializeField] TMP_Text primaryLabel;
        [SerializeField] TMP_Text primarySub;
        [SerializeField] Image primaryIcon;
        [SerializeField] HoldButton hold;
        [SerializeField] TMP_Text holdLabel;
        [SerializeField] Button secondary;
        [SerializeField] Image secondaryBack;
        [SerializeField] TMP_Text secondaryLabel;
        [SerializeField] TMP_Text secondarySub;
        [SerializeField] Image secondaryIcon;
        [SerializeField] Button close;

        [Header("Kit")]
        [SerializeField] Sprite plateYellow;
        [SerializeField] Sprite plateBlue;
        [SerializeField] Sprite plateRed;
        [SerializeField] Sprite buttonGreen;
        [SerializeField] Sprite buttonDark;
        [SerializeField] Sprite buttonYellow;

        [Header("Feel")]
        [SerializeField] SfxDefinition openSfx;
        [SerializeField] SfxDefinition rewardSfx;
        [SerializeField] SfxDefinition closeSfx;
        [Tooltip("Seconds after the game starts before the first popup (the loading cover goes first).")]
        [SerializeField, Min(0f)] float firstPopupAfter = 2.6f;
        [Tooltip("Taps are ignored this long after a card appears.")]
        [SerializeField, Min(0f)] float inputGuard = 0.45f;
        [SerializeField, Range(0f, 1f)] float dimAlpha = 0.72f;

        [Header("Layout")]
        [Tooltip("Primary button alone: centred at this size.")]
        [SerializeField] Vector2 singleButton = new(520f, 150f);
        [Tooltip("Two buttons: each this size, this far apart (centre to centre).")]
        [SerializeField] Vector2 pairButton = new(360f, 150f);
        [SerializeField] float pairSpacing = 390f;
        [Tooltip("Height the card loses when there is no icon / no amount / no body text.")]
        [SerializeField] float iconBlock = 300f;
        [SerializeField] float amountBlock = 110f;
        [SerializeField] float bodyBlock = 170f;

        readonly Queue<(PopupRequest request, float readyAt)> queue = new();
        PopupRequest current;
        float acceptInputAt;
        Image[] sparkles;
        Vector2 platePosition;
        float cardHeight, amountY, subtitleY;

        /// <summary>A popup is on screen.</summary>
        public bool IsShowing => current != null;
        public int Queued => queue.Count;

        protected override void Awake()
        {
            base.Awake();
            if (root != null) root.SetActive(false);
            sparkles = sparkleRoot != null ? sparkleRoot.GetComponentsInChildren<Image>(true) : new Image[0];
            if (titlePlate != null) platePosition = titlePlate.rectTransform.anchoredPosition;
            if (card != null) cardHeight = card.sizeDelta.y;
            if (amount != null) amountY = amount.rectTransform.anchoredPosition.y;
            if (subtitle != null) subtitleY = subtitle.rectTransform.anchoredPosition.y;
            if (primary != null) primary.onClick.AddListener(() => Press(true));
            if (secondary != null) secondary.onClick.AddListener(() => Press(false));
            if (close != null) close.onClick.AddListener(Dismiss);
            if (hold != null) hold.Completed += () => Press(true);
        }

        /// <summary>Queues a popup. It appears when nothing else is showing and its delay has passed.</summary>
        public void Show(PopupRequest request)
        {
            if (request == null) return;
            queue.Enqueue((request, Time.unscaledTime + request.Delay));
        }

        /// <summary>A one-button message.</summary>
        public void Message(string heading, string text, Sprite picture = null) =>
            Show(new PopupRequest { Style = PopupStyle.Info, Title = heading, Body = text, Icon = picture, PrimaryLabel = "OK" });

        void Update()
        {
            if (current != null || queue.Count == 0 || root == null) return;
            while (queue.Count > 0)
            {
                var (next, readyAt) = queue.Peek();
                if (next.MaxWait <= 0f || Time.unscaledTime <= readyAt + next.MaxWait) break;
                queue.Dequeue();   // stale offer
            }

            if (queue.Count == 0 || Time.unscaledTime < firstPopupAfter || Time.unscaledTime < queue.Peek().readyAt) return;
            Present(queue.Dequeue().request);
        }

        void Present(PopupRequest r)
        {
            current = r;
            bool reward = r.Style == PopupStyle.Reward;
            bool danger = r.Style == PopupStyle.Danger;
            root.SetActive(true);
            acceptInputAt = Time.unscaledTime + inputGuard;

            if (titlePlate != null)
                titlePlate.sprite = danger ? plateRed : r.Style == PopupStyle.Confirm ? plateBlue : plateYellow;
            Set(title, r.Title);
            Set(subtitle, r.Subtitle);
            Set(body, r.Body);
            Set(amount, r.Amount);
            if (amount != null) amount.color = r.AmountColor;
            if (icon != null)
            {
                icon.gameObject.SetActive(r.Icon != null);
                icon.sprite = r.Icon;
                icon.rectTransform.localRotation = Quaternion.identity;
            }

            // size the card around what it shows
            float noIcon = r.Icon == null ? iconBlock : 0f;
            float noAmount = string.IsNullOrEmpty(r.Amount) ? amountBlock : 0f;
            float noBody = string.IsNullOrEmpty(r.Body) ? bodyBlock : 0f;
            if (card != null) card.sizeDelta = new Vector2(card.sizeDelta.x, cardHeight - noIcon - noAmount - noBody);
            if (amount != null) amount.rectTransform.anchoredPosition = new Vector2(0f, amountY + noIcon);
            if (subtitle != null) subtitle.rectTransform.anchoredPosition = new Vector2(0f, subtitleY + noIcon + noAmount);
            if (sparkleRoot != null) sparkleRoot.gameObject.SetActive(r.Icon != null);

            // buttons
            bool hasSecondary = !string.IsNullOrEmpty(r.SecondaryLabel);
            if (primary != null) primary.gameObject.SetActive(!danger);
            if (hold != null) hold.gameObject.SetActive(danger);
            Set(holdLabel, r.PrimaryLabel);
            Set(primaryLabel, r.PrimaryLabel);
            Set(primarySub, r.PrimarySub);
            SetIcon(primaryIcon, r.PrimaryIcon);
            if (secondary != null) secondary.gameObject.SetActive(hasSecondary);
            Set(secondaryLabel, r.SecondaryLabel);
            Set(secondarySub, r.SecondarySub);
            SetIcon(secondaryIcon, r.SecondaryIcon);
            FitLabel(primaryLabel, r.PrimarySub, r.PrimaryIcon);
            FitLabel(secondaryLabel, r.SecondarySub, r.SecondaryIcon);
            if (primaryBack != null) primaryBack.sprite = hasSecondary && r.SecondaryIsBetter ? buttonYellow : buttonGreen;
            if (secondaryBack != null) secondaryBack.sprite = r.SecondaryIsBetter ? buttonGreen : buttonDark;
            if (close != null) close.gameObject.SetActive(r.Closable);
            Layout(primary != null ? (RectTransform)primary.transform : null, hasSecondary ? -pairSpacing * 0.5f : 0f, hasSecondary ? pairButton : singleButton);
            Layout(secondary != null ? (RectTransform)secondary.transform : null, pairSpacing * 0.5f, pairButton);

            // entry
            if (dim != null)
            {
                var c = dim.color;
                c.a = 0f;
                dim.color = c;
                UIAnim.Dim(dim, dimAlpha);
            }

            UIAnim.Show(card, cardGroup);
            if (titlePlate != null)
            {
                var plate = titlePlate.rectTransform;
                plate.DOKill();
                plate.anchoredPosition = platePosition + Vector2.up * 70f;
                plate.DOAnchorPos(platePosition, 0.35f).SetEase(Ease.OutBack, 2f).SetDelay(0.08f).SetUpdate(true).SetLink(plate.gameObject);
            }

            // reveal
            if (icon != null && r.Icon != null)
            {
                UIAnim.RewardPop(icon.rectTransform, 0.12f);
                if (r.SpinIcon) UIAnim.Spin(icon.rectTransform, 0.8f).SetDelay(0.4f);
            }

            if (rays != null)
            {
                rays.gameObject.SetActive(reward);
                rays.DOKill();
                if (reward)
                {
                    rays.localScale = Vector3.zero;
                    rays.DOScale(1f, 0.5f).SetEase(Ease.OutBack).SetDelay(0.1f).SetUpdate(true).SetLink(rays.gameObject);
                    rays.DOLocalRotate(new Vector3(0f, 0f, -360f), 14f, RotateMode.FastBeyond360)
                        .SetEase(Ease.Linear).SetLoops(-1).SetUpdate(true).SetLink(rays.gameObject);
                }
            }

            if (reward) Burst(0.25f);

            // reward
            if (amount != null && r.CountTo > 0 && r.AmountFormat != null)
                UIAnim.CountTo(amount, 0, r.CountTo, 0.7f, r.AmountFormat).SetDelay(0.25f);
            if (amount != null && !string.IsNullOrEmpty(r.Amount)) UIAnim.Punch(amount.transform, 0.25f, 0.3f);

            // CTA
            var cta = danger ? (hold != null ? hold.transform : null) : primary != null ? primary.transform : null;
            if (cta != null)
            {
                cta.DOKill();
                cta.localScale = Vector3.zero;
                var rise = cta.DOScale(1f, 0.3f).SetEase(Ease.OutBack, 2f).SetDelay(0.35f).SetUpdate(true).SetLink(cta.gameObject);
                if (reward && !hasSecondary) rise.OnComplete(() => UIAnim.Breathe(cta, 1.04f));
            }

            if (hasSecondary && secondary != null)
            {
                var t = secondary.transform;
                t.DOKill();
                t.localScale = Vector3.zero;
                var rise = t.DOScale(1f, 0.3f).SetEase(Ease.OutBack, 2f).SetDelay(0.45f).SetUpdate(true).SetLink(t.gameObject);
                if (r.SecondaryIsBetter) rise.OnComplete(() => UIAnim.Breathe(t, 1.04f));
            }

            GameFeedback.Sfx(r.Sound != null ? r.Sound : reward ? rewardSfx : openSfx);
            if (reward) Haptics.Heavy();
        }

        void Burst(float delay)
        {
            if (sparkles == null) return;
            for (int i = 0; i < sparkles.Length; i++)
            {
                var s = sparkles[i].rectTransform;
                s.DOKill();
                s.anchoredPosition = Vector2.zero;
                s.localScale = Vector3.zero;
                float angle = (i / (float)sparkles.Length) * Mathf.PI * 2f + Random.Range(-0.3f, 0.3f);
                var to = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Random.Range(170f, 300f);
                float size = Random.Range(0.6f, 1.15f);
                DOTween.Sequence().SetUpdate(true).SetLink(s.gameObject).SetDelay(delay + Random.Range(0f, 0.08f))
                    .Append(s.DOAnchorPos(to, 0.55f).SetEase(Ease.OutCubic))
                    .Join(s.DOScale(size, 0.18f).SetEase(Ease.OutBack))
                    .Join(s.DOLocalRotate(new Vector3(0f, 0f, Random.Range(-180f, 180f)), 0.55f))
                    .Insert(0.3f, s.DOScale(0f, 0.25f).SetEase(Ease.InQuad));
            }
        }

        void Press(bool isPrimary)
        {
            var r = current;
            if (r == null || Time.unscaledTime < acceptInputAt) return;
            var origin = icon != null && icon.gameObject.activeInHierarchy ? CurrencyOrigin.FromUI(icon.rectTransform) : default;
            var button = isPrimary ? (r.Style == PopupStyle.Danger && hold != null ? hold.transform : primary != null ? primary.transform : null)
                                   : secondary != null ? secondary.transform : null;
            UIAnim.Claim(button);
            Analytics.Log(AnalyticsEvents.RewardClaimed, ("popup", r.Title), ("button", isPrimary ? "primary" : "secondary"));
            Close(() =>
            {
                if (isPrimary) r.OnPrimary?.Invoke(origin);
                else r.OnSecondary?.Invoke(origin);
            });
        }

        void Dismiss()
        {
            var r = current;
            if (r == null || Time.unscaledTime < acceptInputAt) return;
            Close(r.OnClose);
        }

        /// <summary>Closes the card. <paramref name="then"/> runs at once (so a reward flies while the card leaves).</summary>
        void Close(System.Action then)
        {
            acceptInputAt = float.MaxValue;
            then?.Invoke();
            GameFeedback.Sfx(closeSfx);
            UIAnim.Dim(dim, 0f, 0.2f);
            UIAnim.Hide(card, cardGroup, () =>
            {
                if (rays != null) rays.DOKill();
                root.SetActive(false);
                current = null;
            });
        }

        /// <summary>The label fills the button, leaving room for a second line and an icon only when they are there.</summary>
        static void FitLabel(TMP_Text label, string sub, Sprite sideIcon)
        {
            if (label == null) return;
            var rt = label.rectTransform;
            rt.offsetMin = new Vector2(sideIcon != null ? 100f : 30f, string.IsNullOrEmpty(sub) ? 12f : 52f);
            rt.offsetMax = new Vector2(-30f, -10f);
        }

        static void Layout(RectTransform button, float x, Vector2 size)
        {
            if (button == null) return;
            button.anchoredPosition = new Vector2(x, button.anchoredPosition.y);
            button.sizeDelta = size;
        }

        static void Set(TMP_Text label, string text)
        {
            if (label == null) return;
            bool has = !string.IsNullOrEmpty(text);
            label.gameObject.SetActive(has);
            if (has) label.SetText(text);
        }

        static void SetIcon(Image image, Sprite sprite)
        {
            if (image == null) return;
            image.gameObject.SetActive(sprite != null);
            image.sprite = sprite;
        }
    }
}

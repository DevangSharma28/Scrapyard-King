using DG.Tweening;
using ScrapYardKing.Boosts;
using ScrapYardKing.Core;
using ScrapYardKing.Economy;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ScrapYardKing.UI
{
    /// <summary>
    /// The HUD's one rewarded-video button. It shows whatever <see cref="OfferDirector"/> currently offers, with the time
    /// it has left, and is hidden when there is no offer or a video is playing. A tap accepts the offer.
    /// </summary>
    public sealed class OfferButton : MonoBehaviour
    {
        [SerializeField] RectTransform root;
        [SerializeField] Button button;
        [SerializeField] Image icon;
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text detail;
        [SerializeField] Image timer;
        [Tooltip("Unit-scale child that breathes while the offer is up.")]
        [SerializeField] RectTransform pulseRoot;

        OfferDirector director;
        AdOfferDefinition shown;

        void Awake()
        {
            if (root != null) root.gameObject.SetActive(false);
            if (button != null) button.onClick.AddListener(Accept);
        }

        void OnDestroy()
        {
            if (button != null) button.onClick.RemoveListener(Accept);
            if (pulseRoot != null) pulseRoot.DOKill();
        }

        void Accept()
        {
            if (director != null) director.Accept();
        }

        void Update()
        {
            if (root == null || (director == null && !Services.TryGet(out director))) return;
            var offer = director.Current;
            bool busy = (Services.TryGet(out AdService ads) && ads.Busy) || (Services.TryGet(out PopupManager popups) && popups.IsShowing);
            if (busy) offer = null;
            if (offer != shown)
            {
                shown = offer;
                root.gameObject.SetActive(offer != null);
                if (pulseRoot != null)
                {
                    pulseRoot.DOKill();
                    pulseRoot.localScale = Vector3.one;
                    if (offer != null) pulseRoot.DOScale(1.06f, 0.5f).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetUpdate(true);
                }

                if (offer != null)
                {
                    if (icon != null) icon.sprite = offer.Icon;
                    if (title != null) title.SetText(offer.Title);
                    long cash = director.CurrentCash;
                    if (detail != null) detail.SetText(cash > 0 ? "+" + CurrencyFormat.Short(cash) : "WATCH");
                }
            }

            if (offer != null && timer != null) timer.fillAmount = director.TimeLeft01;
        }
    }
}

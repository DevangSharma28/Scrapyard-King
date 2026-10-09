using DG.Tweening;
using ScrapYardKing.Boosts;
using ScrapYardKing.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ScrapYardKing.UI
{
    /// <summary>
    /// The one fixed video button: 2X CASH for two minutes, always in the same place on the left edge, so a player who
    /// wants it never waits for the rotating offer. Shown while its offer is ready (cooldown and daily cap from the
    /// <see cref="AdOfferDefinition"/>) and the boost is not already running; hidden, never greyed, otherwise. The
    /// rotating HUD offer no longer carries 2X CASH (its weight is 0).
    /// </summary>
    public sealed class CashBoostButton : MonoBehaviour
    {
        [SerializeField] AdOfferDefinition offer;
        [SerializeField] RectTransform root;
        [SerializeField] Button button;
        [SerializeField] Image icon;
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text detail;
        [Tooltip("Unit-scale child that breathes while the button is up.")]
        [SerializeField] RectTransform pulseRoot;
        [Tooltip("Yard level from which the button appears (the first minutes stay clean).")]
        [SerializeField, Min(1)] int fromLevel = 3;

        OfferDirector director;
        BoostManager boosts;
        bool shown;
        float nextCheck;

        void Awake()
        {
            if (root != null) root.gameObject.SetActive(false);
            if (button != null) button.onClick.AddListener(Watch);
        }

        void OnDestroy()
        {
            if (button != null) button.onClick.RemoveListener(Watch);
            if (pulseRoot != null) pulseRoot.DOKill();
        }

        void Watch()
        {
            if (director == null || offer == null) return;
            director.Watch(offer, () =>
            {
                if (boosts != null && offer.Boost != null) boosts.Activate(offer.Boost);
            });
            Show(false);
        }

        void Update()
        {
            if (Time.unscaledTime < nextCheck || root == null || offer == null) return;
            nextCheck = Time.unscaledTime + 0.25f;
            if (director == null && !Services.TryGet(out director)) return;
            if (boosts == null) Services.TryGet(out boosts);

            int level = Services.TryGet(out Progression.ProgressionManager progression) ? progression.Level : 1;
            bool busy = (Services.TryGet(out AdService ads) && ads.Busy) || (Services.TryGet(out PopupManager popups) && popups.IsShowing);
            bool running = boosts != null && offer.Boost != null && boosts.IsActive(offer.Boost.Kind);
            Show(level >= fromLevel && !busy && !running && director.Ready(offer));
        }

        void Show(bool on)
        {
            if (on == shown) return;
            shown = on;
            root.gameObject.SetActive(on);
            if (pulseRoot != null)
            {
                pulseRoot.DOKill();
                pulseRoot.localScale = Vector3.one;
                if (on) pulseRoot.DOScale(1.05f, 0.6f).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetUpdate(true);
            }

            if (!on) return;
            UIAnim.UnlockReveal(root);
            if (icon != null) icon.sprite = offer.Icon;
            if (title != null) title.SetText(offer.Title);
            int minutes = offer.Boost != null ? Mathf.RoundToInt(offer.Boost.Duration / 60f) : 2;
            if (detail != null) detail.SetText($"{minutes} MIN");
        }
    }
}

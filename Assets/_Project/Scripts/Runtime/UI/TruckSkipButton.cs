using DG.Tweening;
using ScrapYardKing.Boosts;
using ScrapYardKing.Core;
using ScrapYardKing.Economy;
using ScrapYardKing.Factory;
using ScrapYardKing.Progression;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ScrapYardKing.UI
{
    /// <summary>
    /// One small button under the order card that turns waiting into a choice, shown only when it is worth something:
    /// - <b>FINISH ORDER</b> while a truck has waited a while and a good part of its order is still missing: a video
    ///   (when the instant-truck offer is ready and no other video button is up) or diamonds priced at the cash value
    ///   of what is missing;
    /// - <b>NEXT TRUCK 0:42 · BRING NOW</b> while the next truck is still far off: diamonds priced by the time saved.
    /// Rules and thresholds come from <see cref="PremiumEconomyConfig"/>; spending goes through <see cref="DiamondSpend"/>.
    /// </summary>
    public sealed class TruckSkipButton : MonoBehaviour
    {
        enum Mode
        {
            None,
            Finish,
            Call
        }

        [SerializeField] RectTransform root;
        [SerializeField] Button button;
        [SerializeField] Image back;
        [SerializeField] Image icon;
        [SerializeField] TMP_Text label;
        [SerializeField] TMP_Text sub;
        [SerializeField] Sprite videoBack;
        [SerializeField] Sprite diamondBack;
        [SerializeField] Sprite diamondIcon;
        [SerializeField] Sprite truckIcon;

        Mode mode;
        TruckBay bay;
        bool video;
        int cost;
        float nextCheck;

        void Awake()
        {
            if (root != null) root.gameObject.SetActive(false);
            if (button != null) button.onClick.AddListener(Tap);
        }

        void OnDisable() => SetContextual(false);

        void Update()
        {
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.25f;
            if (!Services.TryGet(out DiamondRewards rewards) || rewards.Config == null) return;
            var config = rewards.Config;
            Services.TryGet(out OfferDirector director);

            // pick the most useful thing to offer across the docks
            Mode best = Mode.None;
            TruckBay pick = null;
            foreach (var station in StationRegistry.All)
            {
                if (station is not TruckBay b || !b.isActiveAndEnabled) continue;
                int missing = b.MissingCount;
                int loaded = b.RequiredCount - missing;
                if (missing > 0 && missing >= Mathf.CeilToInt(b.RequiredCount * config.FinishMinMissing) && b.DockedFor >= config.FinishMinDocked &&
                    loaded >= Mathf.CeilToInt(b.RequiredCount * config.FinishMinLoaded))
                {
                    best = Mode.Finish;
                    pick = b;
                    break;
                }

                if (best == Mode.None && b.AwayLeft >= config.CallMinSeconds)
                {
                    best = Mode.Call;
                    pick = b;
                }
            }

            Apply(best, pick, config, director);
        }

        void Apply(Mode next, TruckBay b, PremiumEconomyConfig config, OfferDirector director)
        {
            bool wasShown = mode != Mode.None;
            mode = next;
            bay = b;
            if (mode == Mode.None)
            {
                SetContextual(false);
                if (wasShown) Hide();
                return;
            }

            int level = Services.TryGet(out ProgressionManager progression) ? progression.Level : 1;
            string noun = b.StationId.Contains("ship") ? "SHIP" : "TRUCK";
            if (mode == Mode.Finish)
            {
                video = director != null && director.Current == null && director.Ready(director.InstantTruck);
                cost = config.CashCost(b.MissingValue, level);
                label.SetText("FINISH ORDER");
                sub.SetText(video ? "WATCH" : cost.ToString());
                icon.sprite = video ? director.VideoIcon : diamondIcon;
                back.sprite = video ? videoBack : diamondBack;
            }
            else
            {
                video = false;
                cost = config.SkipCost(b.AwayLeft);
                label.SetText($"NEXT {noun} {DiamondSpend.Duration(b.AwayLeft)}");
                sub.SetText("NOW · " + cost);
                icon.sprite = diamondIcon;
                back.sprite = diamondBack;
            }

            SetContextual(video);
            if (!wasShown) Show();
        }

        void SetContextual(bool on)
        {
            if (Services.TryGet(out OfferDirector director)) director.ContextualVisible = on;
        }

        void Show()
        {
            root.gameObject.SetActive(true);
            UIAnim.UnlockReveal(root);
        }

        void Hide()
        {
            root.DOKill();
            root.DOScale(0f, 0.18f).SetEase(Ease.InBack).SetUpdate(true).SetLink(root.gameObject)
                .OnComplete(() => root.gameObject.SetActive(false));
        }

        void Tap()
        {
            var b = bay;
            if (b == null) return;
            var picture = b.Contract != null && b.Contract.Icon != null ? b.Contract.Icon : truckIcon;
            if (mode == Mode.Finish)
            {
                if (video && Services.TryGet(out OfferDirector director))
                {
                    director.Watch(director.InstantTruck, () => b.FinishOrder());
                    return;
                }

                DiamondSpend.Request(cost, "FINISH ORDER?", $"{b.MissingCount} MORE BARS · THE TRUCK LEAVES NOW",
                    "+$" + CurrencyFormat.Short(b.MissingValue), "instant_truck", picture, () => b.FinishOrder());
            }
            else if (mode == Mode.Call)
            {
                float left = b.AwayLeft;
                DiamondSpend.Request(cost, "SPEED UP?", "BRING THE NEXT TRUCK NOW", "SAVE " + DiamondSpend.Duration(left),
                    "truck_call", truckIcon, () => b.CallNow());
            }

            nextCheck = 0f;
        }
    }
}

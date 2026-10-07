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
    /// Top-of-screen currency HUD. Cash earned in the world (or claimed in a popup) flies to the counter first, and the
    /// counter only rolls up when the coins land, so the eye follows the money. Under each capsule a ticker sums what
    /// just came in ("+$1.2K", growing while sales keep landing) or went out ("-$600"). A big gain (a truck, a reward)
    /// gets a glow, a bigger punch and its own sound. The diamond capsule stays hidden until the first diamond: the first
    /// minutes show only cash, level and the task.
    /// </summary>
    public sealed class HudController : MonoBehaviour
    {
        [SerializeField] CurrencyWidget cash;
        [SerializeField] CurrencyWidget premium;
        [SerializeField] UIFlyer flyer;
        [SerializeField] Sprite cashIcon;
        [SerializeField] Sprite premiumIcon;
        [SerializeField, Min(1)] int maxCoinsPerGain = 3;

        [Header("Tickers and glow")]
        [SerializeField] TMP_Text cashTicker;
        [SerializeField] TMP_Text premiumTicker;
        [SerializeField] Image cashGlow;
        [Tooltip("Seconds a ticker keeps adding up gains before it fades.")]
        [SerializeField, Min(0.2f)] float tickerWindow = 0.9f;
        [Tooltip("A gain at least this big, or this share of the balance, counts as big.")]
        [SerializeField, Min(1)] long bigGain = 2500;
        [SerializeField, Range(0f, 1f)] float bigGainShare = 0.2f;
        [SerializeField] SfxDefinition bigGainSfx;
        [SerializeField] SfxDefinition diamondSfx;

        static readonly Color GainColor = new(0.62f, 1f, 0.5f);
        static readonly Color SpendColor = new(1f, 0.62f, 0.35f);
        static readonly Color DiamondColor = new(0.75f, 0.9f, 1f);

        EconomyManager economy;
        long pendingCashDisplay, pendingPremiumDisplay;
        Ticker cashTick, premiumTick;
        bool premiumShown;

        sealed class Ticker
        {
            public TMP_Text label;
            public long sum;
            public int sign;
            public float until;
            public Vector2 rest;
        }

        void Start()
        {
            cashTick = MakeTicker(cashTicker);
            premiumTick = MakeTicker(premiumTicker);
            if (cashGlow != null) cashGlow.color = new Color(1f, 0.9f, 0.4f, 0f);
            if (!Services.TryGet(out economy)) return;
            cash.SetInstant(economy.Cash);
            pendingCashDisplay = economy.Cash;
            pendingPremiumDisplay = economy.Premium;
            if (premium != null)
            {
                premium.SetInstant(economy.Premium);
                premiumShown = DiamondsVisible();
                premium.gameObject.SetActive(premiumShown);
            }

            economy.CashChanged += OnCashChanged;
            economy.PremiumChanged += OnPremiumChanged;
        }

        void OnDestroy()
        {
            if (economy == null) return;
            economy.CashChanged -= OnCashChanged;
            economy.PremiumChanged -= OnPremiumChanged;
        }

        static Ticker MakeTicker(TMP_Text label)
        {
            if (label == null) return null;
            label.alpha = 0f;
            return new Ticker { label = label, rest = label.rectTransform.anchoredPosition };
        }

        bool DiamondsVisible() =>
            economy.Premium > 0 || (Services.TryGet(out DiamondRewards rewards) && rewards.DiamondsRevealed);

        void OnCashChanged(long balance, long delta, CurrencyOrigin origin)
        {
            pendingCashDisplay = balance;
            if (delta == 0)
            {
                cash.SetInstant(balance);
                return;
            }

            bool big = delta >= bigGain || (delta > 0 && delta >= (balance - delta) * bigGainShare && delta >= 200);
            if (delta < 0 || !origin.HasValue || flyer == null)
            {
                cash.AnimateTo(balance);
                Tick(cashTick, delta, delta < 0 ? SpendColor : GainColor, "$");
                if (big) BigGain();
                return;
            }

            int coins = Mathf.Clamp((int)(delta / 5), 1, big ? maxCoinsPerGain + 5 : maxCoinsPerGain);
            void Land()
            {
                cash.AnimateTo(pendingCashDisplay);
                Tick(cashTick, delta, GainColor, "$");
                if (big) BigGain();
            }

            if (origin.Screen.HasValue) flyer.FlyFromScreen(origin.Screen.Value, cash.Icon, cashIcon, coins, Land);
            else flyer.Fly(origin.World.Value, cash.Icon, cashIcon, coins, Land);
        }

        void OnPremiumChanged(long balance, long delta, CurrencyOrigin origin)
        {
            pendingPremiumDisplay = balance;
            if (premium == null) return;
            if (!premiumShown && DiamondsVisible())
            {
                premiumShown = true;
                premium.gameObject.SetActive(true);
                premium.SetInstant(0);
                UIAnim.UnlockReveal(premium.transform);
            }

            if (delta == 0)
            {
                premium.SetInstant(balance);
                return;
            }

            void Land()
            {
                premium.AnimateTo(pendingPremiumDisplay);
                Tick(premiumTick, delta, delta < 0 ? SpendColor : DiamondColor, "");
                if (delta > 0) GameFeedback.Sfx(diamondSfx);
            }

            if (delta > 0 && origin.HasValue && flyer != null)
            {
                int gems = Mathf.Clamp((int)delta / 5, 3, 8);
                if (origin.Screen.HasValue) flyer.FlyFromScreen(origin.Screen.Value, premium.Icon, premiumIcon, gems, Land);
                else flyer.Fly(origin.World.Value, premium.Icon, premiumIcon, gems, Land);
            }
            else Land();
        }

        void BigGain()
        {
            UIAnim.Punch(cash.transform, 0.12f, 0.4f);
            GameFeedback.Sfx(bigGainSfx);
            if (cashGlow == null) return;
            cashGlow.DOKill();
            DOTween.Sequence().SetUpdate(true).SetLink(cashGlow.gameObject)
                .Append(cashGlow.DOFade(0.9f, 0.1f))
                .Append(cashGlow.DOFade(0f, 0.6f).SetEase(Ease.InQuad));
        }

        void Tick(Ticker t, long delta, Color color, string unit)
        {
            if (t == null || delta == 0) return;
            int sign = delta > 0 ? 1 : -1;
            if (Time.unscaledTime > t.until || sign != t.sign) t.sum = 0;
            t.sum += delta;
            t.sign = sign;
            t.until = Time.unscaledTime + tickerWindow;

            var label = t.label;
            var rt = label.rectTransform;
            label.SetText((sign > 0 ? "+" : "-") + unit + CurrencyFormat.Short(System.Math.Abs(t.sum)));
            label.color = color;
            label.DOKill();
            rt.DOKill();
            rt.anchoredPosition = t.rest;
            rt.localScale = Vector3.one;
            label.alpha = 1f;
            DOTween.Sequence().SetUpdate(true).SetLink(label.gameObject)
                .Append(rt.DOPunchScale(Vector3.one * 0.25f, 0.2f, 6, 0.5f))
                .AppendInterval(tickerWindow * 0.6f)
                .Append(rt.DOAnchorPos(t.rest + Vector2.down * 18f * sign * -1f, 0.35f))
                .Join(label.DOFade(0f, 0.35f));
        }
    }
}

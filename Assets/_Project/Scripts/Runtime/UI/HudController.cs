using ScrapYardKing.Core;
using ScrapYardKing.Economy;
using UnityEngine;

namespace ScrapYardKing.UI
{
    /// <summary>
    /// Top-of-screen HUD (blueprint: cash + premium top right). Cash earned in the world flies to the counter first,
    /// and the counter only rolls up when the coins land, so the eye follows the money.
    /// </summary>
    public sealed class HudController : MonoBehaviour
    {
        [SerializeField] CurrencyWidget cash;
        [SerializeField] CurrencyWidget premium;
        [SerializeField] UIFlyer flyer;
        [SerializeField] Sprite cashIcon;
        [SerializeField, Min(1)] int maxCoinsPerGain = 3;

        EconomyManager economy;
        long pendingCashDisplay;

        void Start()
        {
            if (!Services.TryGet(out economy)) return;
            cash.SetInstant(economy.Cash);
            if (premium != null) premium.SetInstant(economy.Premium);
            pendingCashDisplay = economy.Cash;
            economy.CashChanged += OnCashChanged;
            economy.PremiumChanged += OnPremiumChanged;
        }

        void OnDestroy()
        {
            if (economy == null) return;
            economy.CashChanged -= OnCashChanged;
            economy.PremiumChanged -= OnPremiumChanged;
        }

        void OnCashChanged(long balance, long delta, Vector3? source)
        {
            pendingCashDisplay = balance;
            if (delta <= 0 || source == null || flyer == null)
            {
                cash.AnimateTo(balance);
                return;
            }

            flyer.Fly(source.Value, cash.Icon, cashIcon, Mathf.Clamp((int)(delta / 5), 1, maxCoinsPerGain),
                () => cash.AnimateTo(pendingCashDisplay));
        }

        void OnPremiumChanged(long balance, long delta)
        {
            if (premium != null) premium.AnimateTo(balance);
        }
    }
}

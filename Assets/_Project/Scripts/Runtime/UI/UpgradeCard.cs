using System.Collections.Generic;
using DG.Tweening;
using ScrapYardKing.Core;
using ScrapYardKing.Economy;
using ScrapYardKing.Progression;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ScrapYardKing.UI
{
    /// <summary>
    /// One card in the upgrade panel (reference: icon, name, level, green cost button). Shows locked,
    /// affordable, unaffordable and maxed states and a pip per level. While unaffordable, the button fills up with your
    /// cash ("almost there"); once affordable it breathes, and the whole card pulses when the guide says it is next.
    /// </summary>
    public sealed class UpgradeCard : MonoBehaviour
    {
        [SerializeField] Image icon;
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text level;
        [SerializeField] TMP_Text effect;
        [SerializeField] TMP_Text cost;
        [SerializeField] Button buyButton;
        [SerializeField] Image buttonImage;
        [SerializeField] GameObject coinIcon;
        [SerializeField] GameObject lockOverlay;
        [SerializeField] TMP_Text lockText;
        [SerializeField] RectTransform highlight;
        [SerializeField] Color affordableColor = Color.white;
        [SerializeField] Color unaffordableColor = new(0.55f, 0.55f, 0.58f);

        [Header("Savings (optional)")]
        [Tooltip("Fill inside the buy button that grows with your cash until the upgrade is affordable.")]
        [SerializeField] Image savingsFill;
        [SerializeField, Range(1f, 1.2f)] float affordablePulse = 1.05f;

        [Header("Level pips (optional)")]
        [SerializeField] RectTransform pipRoot;
        [SerializeField] Image pipTemplate;
        [SerializeField] Color pipOnColor = new(1f, 0.78f, 0.2f);
        [SerializeField] Color pipOffColor = new(0f, 0f, 0f, 0.3f);

        readonly List<Image> pips = new();
        int shownLevel = -1;

        UpgradeManager manager;
        EconomyManager economy;
        IUpgradeable upgrade;
        RectTransform rect;
        Tween highlightTween, pulseTween;
        Vector3 buttonScale = Vector3.one;
        bool pulsing;
        bool wasLocked = true;

        public string UpgradeId { get; private set; }
        public IUpgradeable Upgrade => upgrade;
        public RectTransform Rect => rect != null ? rect : rect = (RectTransform)transform;

        public void Setup(string upgradeId, UpgradeManager upgradeManager)
        {
            UpgradeId = upgradeId;
            manager = upgradeManager;
            Services.TryGet(out economy);
            buttonScale = buyButton.transform.localScale;
            buyButton.onClick.AddListener(OnBuy);
            SetHighlighted(false);
            gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (upgrade != null) upgrade.UpgradeChanged -= OnUpgradeChanged;
            highlightTween?.Kill();
            pulseTween?.Kill();
        }

        public void Bind(IUpgradeable target)
        {
            if (upgrade != null) upgrade.UpgradeChanged -= OnUpgradeChanged;
            upgrade = target;
            upgrade.UpgradeChanged += OnUpgradeChanged;
            if (icon != null)
            {
                icon.sprite = upgrade.Icon;
                icon.enabled = upgrade.Icon != null;
            }

            title.text = upgrade.DisplayName;
            gameObject.SetActive(true);
            wasLocked = !manager.IsUnlocked(upgrade);
            Refresh();
        }

        void OnUpgradeChanged(IUpgradeable _) => Refresh();

        public void Refresh()
        {
            if (upgrade == null || manager == null) return;

            bool unlocked = manager.IsUnlocked(upgrade);
            if (lockOverlay != null) lockOverlay.SetActive(!unlocked);
            if (lockText != null) lockText.text = $"LV {manager.UnlockLevel(upgrade.UpgradeId)}";
            if (unlocked && wasLocked)
            {
                Rect.DOKill(true);
                Rect.DOPunchScale(Vector3.one * 0.15f, 0.4f, 6, 0.6f);
            }

            wasLocked = !unlocked;
            level.text = upgrade.LevelLabel;
            RefreshPips();
            if (effect != null) effect.text = upgrade.NextEffect;

            if (!unlocked)
            {
                // The lock overlay shows the required level where the price normally sits.
                cost.text = string.Empty;
                coinIcon.SetActive(false);
                buyButton.interactable = false;
                buttonImage.color = unaffordableColor;
                ShowSavings(false, 0f);
                SetPulse(false);
                return;
            }

            if (upgrade.IsMaxed)
            {
                cost.text = "MAX";
                coinIcon.SetActive(false);
                buyButton.interactable = false;
                buttonImage.color = unaffordableColor;
                ShowSavings(false, 0f);
                SetPulse(false);
                return;
            }

            cost.text = CurrencyFormat.Short(upgrade.NextCost);
            coinIcon.SetActive(true);
            bool canBuy = unlocked && manager.CanAfford(upgrade);
            buyButton.interactable = unlocked;
            buttonImage.color = canBuy ? affordableColor : unaffordableColor;
            long cash = economy != null ? economy.Cash : 0;
            ShowSavings(!canBuy, upgrade.NextCost > 0 ? Mathf.Clamp01(cash / (float)upgrade.NextCost) : 1f);
            SetPulse(canBuy);
        }

        void ShowSavings(bool on, float ratio)
        {
            if (savingsFill == null) return;
            if (savingsFill.gameObject.activeSelf != on) savingsFill.gameObject.SetActive(on);
            if (on) savingsFill.fillAmount = ratio;
        }

        void SetPulse(bool on)
        {
            if (on == pulsing) return;
            pulsing = on;
            pulseTween?.Kill();
            var t = buyButton.transform;
            t.localScale = buttonScale;
            if (on) pulseTween = t.DOScale(buttonScale * affordablePulse, 0.55f).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo);
        }

        void RefreshPips()
        {
            if (pipRoot == null || pipTemplate == null) return;
            pipTemplate.gameObject.SetActive(false);
            int max = upgrade.MaxLevel;
            while (pips.Count < max)
            {
                var pip = Instantiate(pipTemplate, pipRoot);
                pip.name = "Pip" + pips.Count;
                pips.Add(pip);
            }

            for (int i = 0; i < pips.Count; i++)
            {
                pips[i].gameObject.SetActive(i < max);
                pips[i].color = i < upgrade.Level ? pipOnColor : pipOffColor;
            }

            int levelNow = upgrade.Level;
            if (shownLevel >= 0 && levelNow > shownLevel && levelNow - 1 < pips.Count)
            {
                var t = pips[levelNow - 1].rectTransform;
                t.DOKill(true);
                t.DOPunchScale(Vector3.one * 0.8f, 0.35f, 6, 0.5f);
            }

            shownLevel = levelNow;
        }

        public void SetHighlighted(bool on)
        {
            if (highlight == null) return;
            highlightTween?.Kill();
            highlight.gameObject.SetActive(on);
            if (!on) return;
            highlight.localScale = Vector3.one;
            highlightTween = highlight.DOScale(1.06f, 0.45f).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo);
        }

        void OnBuy()
        {
            if (upgrade == null) return;
            Rect.DOKill(true);
            SetPulse(false);
            if (manager.TryPurchase(upgrade))
            {
                Rect.DOPunchScale(Vector3.one * 0.18f, 0.35f, 7, 0.6f);
                return;
            }

            Rect.DOShakeAnchorPos(0.35f, new Vector2(14f, 0f), 18, 0f);
        }
    }
}

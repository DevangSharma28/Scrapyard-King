using System;
using System.Collections.Generic;
using DG.Tweening;
using ScrapYardKing.CameraSystem;
using ScrapYardKing.Core;
using ScrapYardKing.Economy;
using ScrapYardKing.Progression;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ScrapYardKing.UI
{
    /// <summary>
    /// Bottom sheet opened by the upgrade tile. Lists the catalog entries flagged for the panel, grouped under section
    /// headers (YOU, OLD YARD, RECYCLING PLANT) in catalog order. Cards bind when their upgradeable registers (stations
    /// in a locked area register when it opens, so their section appears then), refresh on cash and level changes, and
    /// pulse when the guide says that upgrade is next.
    /// </summary>
    public sealed class UpgradePanel : ServiceBehaviour<UpgradePanel>
    {
        [SerializeField] RectTransform sheet;
        [SerializeField] CanvasGroup group;
        [SerializeField] Button closeButton;
        [SerializeField] RectTransform content;
        [SerializeField] ScrollRect scroll;
        [SerializeField] TMP_Text headerTemplate;
        [SerializeField] RectTransform gridTemplate;
        [SerializeField] UpgradeCard cardTemplate;

        [Header("Feel")]
        [SerializeField, Min(0.05f)] float slideDuration = 0.32f;
        [Tooltip("Camera focus shift while open, so the player stays visible above the sheet.")]
        [SerializeField] Vector3 cameraShift = new(0f, 0f, -3.2f);

        readonly List<UpgradeCard> cards = new();
        readonly List<(GameObject header, RectTransform grid)> sections = new();
        UpgradeManager upgrades;
        EconomyManager economy;
        ProgressionManager progression;
        GuideDirector guide;
        CameraController cameraController;
        float hiddenY;

        public bool IsOpen { get; private set; }
        public event Action<bool> OpenChanged;

        /// <summary>True when at least one listed upgrade is unlocked, not maxed and affordable.</summary>
        public bool HasPurchasable
        {
            get
            {
                if (upgrades == null) return false;
                foreach (var card in cards)
                    if (card.Upgrade != null && upgrades.CanPurchase(card.Upgrade)) return true;
                return false;
            }
        }

        void Start()
        {
            Services.TryGet(out economy);
            Services.TryGet(out progression);
            Services.TryGet(out guide);
            Services.TryGet(out cameraController);

            hiddenY = -sheet.rect.height - 40f;
            sheet.anchoredPosition = new Vector2(sheet.anchoredPosition.x, hiddenY);
            SetInteractive(false);
            sheet.gameObject.SetActive(false);
            if (closeButton != null) closeButton.onClick.AddListener(Close);

            headerTemplate.gameObject.SetActive(false);
            gridTemplate.gameObject.SetActive(false);
            cardTemplate.gameObject.SetActive(false);
            if (!Services.TryGet(out upgrades) || upgrades.Catalog == null) return;

            Build();
            upgrades.Registered += OnRegistered;
            upgrades.Purchased += OnPurchased;
            if (economy != null) economy.CashChanged += OnCashChanged;
            if (progression != null) progression.LevelChanged += OnLevelChanged;
            if (guide != null) guide.HighlightChanged += OnHighlight;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (upgrades != null)
            {
                upgrades.Registered -= OnRegistered;
                upgrades.Purchased -= OnPurchased;
            }

            if (economy != null) economy.CashChanged -= OnCashChanged;
            if (progression != null) progression.LevelChanged -= OnLevelChanged;
            if (guide != null) guide.HighlightChanged -= OnHighlight;
            if (sheet != null) sheet.DOKill();
        }

        void Build()
        {
            string currentGroup = null;
            RectTransform grid = null;
            GameObject headerObject = null;
            foreach (var entry in upgrades.Catalog.Entries)
            {
                if (!entry.inPanel) continue;
                if (grid == null || entry.group != currentGroup)
                {
                    currentGroup = entry.group;
                    headerObject = null;
                    if (!string.IsNullOrEmpty(currentGroup))
                    {
                        var header = Instantiate(headerTemplate, content);
                        header.name = "Header_" + currentGroup;
                        header.text = currentGroup;
                        headerObject = header.gameObject;
                    }

                    grid = Instantiate(gridTemplate, content);
                    grid.name = "Grid_" + (currentGroup ?? "Misc");
                    sections.Add((headerObject, grid));
                }

                var card = Instantiate(cardTemplate, grid);
                card.name = "Card_" + entry.upgradeId;
                card.Setup(entry.upgradeId, upgrades);
                if (upgrades.TryGet(entry.upgradeId, out var u)) card.Bind(u);
                cards.Add(card);
            }

            RefreshSections();
        }

        /// <summary>A section shows only once at least one of its upgrades exists in the world.</summary>
        void RefreshSections()
        {
            foreach (var (header, grid) in sections)
            {
                bool any = false;
                foreach (Transform card in grid)
                    any |= card.gameObject.activeSelf;
                if (header != null) header.SetActive(any);
                grid.gameObject.SetActive(any);
            }
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            sheet.gameObject.SetActive(true);
            sheet.DOKill();
            sheet.DOAnchorPosY(0f, slideDuration).SetEase(Ease.OutBack, 0.8f);
            SetInteractive(true);
            RefreshAll();
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;
            if (guide != null) OnHighlight(guide.HighlightedUpgrade);
            if (cameraController != null) cameraController.SetFocusShift(cameraShift);
            OpenChanged?.Invoke(true);
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            sheet.DOKill();
            sheet.DOAnchorPosY(hiddenY, slideDuration * 0.8f).SetEase(Ease.InCubic).OnComplete(() => sheet.gameObject.SetActive(false));
            SetInteractive(false);
            if (cameraController != null) cameraController.SetFocusShift(Vector3.zero);
            OpenChanged?.Invoke(false);
        }

        void SetInteractive(bool on)
        {
            if (group == null) return;
            group.interactable = on;
            group.blocksRaycasts = on;
        }

        void OnRegistered(IUpgradeable u)
        {
            foreach (var card in cards)
                if (card.UpgradeId == u.UpgradeId) card.Bind(u);
            RefreshSections();
        }

        void OnPurchased(IUpgradeable _) => RefreshAll();
        void OnCashChanged(long balance, long delta, CurrencyOrigin source) => RefreshAll();
        void OnLevelChanged(int level) => RefreshAll();

        void RefreshAll()
        {
            if (!IsOpen) return;
            foreach (var card in cards) card.Refresh();
        }

        void OnHighlight(string id)
        {
            UpgradeCard target = null;
            foreach (var card in cards)
            {
                bool on = !string.IsNullOrEmpty(id) && card.UpgradeId == id;
                card.SetHighlighted(on);
                if (on) target = card;
            }

            if (target != null && IsOpen) ScrollTo(target);
        }

        void ScrollTo(UpgradeCard card)
        {
            if (scroll == null || content == null) return;
            Canvas.ForceUpdateCanvases();
            float contentHeight = content.rect.height;
            float viewHeight = scroll.viewport != null ? scroll.viewport.rect.height : ((RectTransform)scroll.transform).rect.height;
            if (contentHeight <= viewHeight) return;

            Vector3 local = content.InverseTransformPoint(card.Rect.TransformPoint(card.Rect.rect.center));
            float fromTop = -local.y;
            float normalized = 1f - Mathf.Clamp01((fromTop - viewHeight * 0.5f) / (contentHeight - viewHeight));
            DOTween.To(() => scroll.verticalNormalizedPosition, y => scroll.verticalNormalizedPosition = y, normalized, 0.35f)
                .SetEase(Ease.OutCubic).SetTarget(scroll);
        }
    }
}

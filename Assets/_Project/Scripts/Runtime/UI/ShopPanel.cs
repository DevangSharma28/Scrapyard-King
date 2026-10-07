using System.Collections.Generic;
using ScrapYardKing.Boosts;
using ScrapYardKing.Core;
using ScrapYardKing.Economy;
using ScrapYardKing.Feedback;
using ScrapYardKing.Progression;
using ScrapYardKing.Shop;
using UnityEngine;
using UnityEngine.UI;

namespace ScrapYardKing.UI
{
    public enum ShopTab
    {
        Diamonds = 0,
        Boosts = 1,
        Special = 2,
        Free = 3
    }

    /// <summary>
    /// The shop: four tabs (DIAMONDS, BOOSTS, SPECIAL, FREE) built from <see cref="ShopCatalog"/>. Real-money cards show
    /// the store's price and what the pack holds (base, bonus, total, a value ribbon); diamond cards show the diamond
    /// cost and go through <see cref="DiamondSpend"/>; the free card plays a rewarded video. Every button shows its state:
    /// price, diamond cost, WATCH, a countdown, OWNED or a busy "..." while the store works. Opened from the "+" on the
    /// cash and diamond capsules, which appear with the first diamond (nothing about a shop in the first minutes).
    /// Machines and workers are never sold here.
    /// </summary>
    public sealed class ShopPanel : MonoBehaviour
    {
        [Header("Frame")]
        [SerializeField] GameObject root;
        [SerializeField] Image dim;
        [SerializeField] RectTransform card;
        [SerializeField] CanvasGroup cardGroup;
        [SerializeField] Button close;
        [SerializeField] ScrollRect scroll;
        [SerializeField] Button[] tabs;
        [SerializeField] Image[] tabBacks;
        [SerializeField] Sprite tabOn;
        [SerializeField] Sprite tabOff;
        [SerializeField] RectTransform[] pages;
        [SerializeField] RectTransform bundleGridTemplate;

        [Header("Cards")]
        [SerializeField] ShopCard heroTemplate;
        [SerializeField] ShopCard bundleTemplate;
        [SerializeField] ShopCard rowTemplate;

        [Header("Entry")]
        [SerializeField] Button cashEntry;
        [SerializeField] Button diamondEntry;

        [Header("Feel")]
        [SerializeField] Sprite cashIcon;
        [SerializeField] Sprite diamondIcon;
        [SerializeField] SfxDefinition openSfx;
        [SerializeField] SfxDefinition closeSfx;
        [SerializeField] SfxDefinition buySfx;

        readonly List<(ShopCard card, IAPProductConfig product)> productCards = new();
        readonly List<(ShopCard card, ShopCatalog.BoostItem item)> boostCards = new();
        readonly List<(ShopCard card, ShopCatalog.CashCrate crate)> crateCards = new();
        ShopCard freeCard;
        StoreService store;
        EconomyManager economy;
        bool built, unlocked;
        float nextRefresh;
        ShopTab current;

        public bool IsOpen { get; private set; }

        void Awake()
        {
            if (root != null) root.SetActive(false);
            if (close != null) close.onClick.AddListener(Close);
            for (int i = 0; tabs != null && i < tabs.Length; i++)
            {
                var tab = (ShopTab)i;
                tabs[i].onClick.AddListener(() => Show(tab));
            }

            if (cashEntry != null) cashEntry.onClick.AddListener(() => Open(ShopTab.Boosts));
            if (diamondEntry != null) diamondEntry.onClick.AddListener(() => Open(ShopTab.Diamonds));
            foreach (var t in new Component[] { heroTemplate, bundleTemplate, rowTemplate, bundleGridTemplate })
                if (t != null) t.gameObject.SetActive(false);
        }

        void Start()
        {
            Services.TryGet(out store);
            Services.TryGet(out economy);
            DiamondSpend.OpenShop = _ => Open(ShopTab.Diamonds);
            SetEntries(Unlocked());
        }

        void OnDestroy()
        {
            DiamondSpend.OpenShop = null;
        }

        bool Unlocked()
        {
            if (store == null || store.Catalog == null) return false;
            if (!store.Catalog.UnlockWithFirstDiamond) return true;
            return (economy != null && economy.Premium > 0) || (Services.TryGet(out DiamondRewards rewards) && rewards.DiamondsRevealed);
        }

        void SetEntries(bool show)
        {
            if (unlocked == show && built) return;
            bool reveal = show && !unlocked;
            unlocked = show;
            foreach (var b in new[] { cashEntry, diamondEntry })
            {
                if (b == null) continue;
                b.gameObject.SetActive(show);
                if (reveal) UIAnim.UnlockReveal(b.transform, 0.6f);
            }
        }

        void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.5f;
            if (!unlocked) SetEntries(Unlocked());
            if (IsOpen) Refresh();
        }

        // ------------------------------------------------------------------ open / close / tabs

        public void Open(ShopTab tab)
        {
            if (root == null || store == null || store.Catalog == null) return;
            if (!built) Build();
            if (!IsOpen)
            {
                IsOpen = true;
                root.SetActive(true);
                if (dim != null)
                {
                    var c = dim.color;
                    c.a = 0f;
                    dim.color = c;
                    UIAnim.Dim(dim, 0.75f);
                }

                UIAnim.Show(card, cardGroup);
                GameFeedback.Sfx(openSfx);
            }

            Show(tab);
            Analytics.Log(AnalyticsEvents.ShopOpened, ("tab", tab.ToString()));
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            GameFeedback.Sfx(closeSfx);
            UIAnim.Dim(dim, 0f);
            UIAnim.Hide(card, cardGroup, () => root.SetActive(false));
        }

        void Show(ShopTab tab)
        {
            current = tab;
            for (int i = 0; i < pages.Length; i++)
            {
                pages[i].gameObject.SetActive(i == (int)tab);
                if (i < tabBacks.Length) tabBacks[i].sprite = i == (int)tab ? tabOn : tabOff;
            }

            if (scroll != null)
            {
                scroll.content = pages[(int)tab];
                scroll.verticalNormalizedPosition = 1f;
            }

            if ((int)tab < tabs.Length) UIAnim.Punch(tabs[(int)tab].transform, 0.12f, 0.2f);
            if (tab is ShopTab.Diamonds or ShopTab.Special) Analytics.Log(AnalyticsEvents.IapOpened, ("tab", tab.ToString()));
            Refresh();
        }

        // ------------------------------------------------------------------ cards

        void Build()
        {
            built = true;
            var catalog = store.Catalog;

            // DIAMONDS: the hero pack on top, then the bundles two by two
            var diamonds = pages[(int)ShopTab.Diamonds];
            if (catalog.Hero != null && catalog.Hero.Enabled) AddProduct(Instantiate(heroTemplate, diamonds), catalog.Hero, true);
            var grid = Instantiate(bundleGridTemplate, diamonds);
            grid.gameObject.SetActive(true);
            foreach (var p in catalog.Bundles)
                if (p != null && p.Enabled) AddProduct(Instantiate(bundleTemplate, grid), p, false);

            // BOOSTS: boosts and cash crates for diamonds
            var boostPage = pages[(int)ShopTab.Boosts];
            foreach (var item in catalog.Boosts)
            {
                if (item?.boost == null) continue;
                var c = Instantiate(rowTemplate, boostPage);
                c.gameObject.SetActive(true);
                c.Set(item.boost.Icon, item.boost.DisplayName, $"{item.minutes:0} MINUTES").Ribbon(null).Bonus(null, null);
                var it = item;
                c.OnTap(() => BuyBoost(c, it));
                boostCards.Add((c, item));
            }

            foreach (var crate in catalog.Crates)
            {
                if (crate == null) continue;
                var c = Instantiate(rowTemplate, boostPage);
                c.gameObject.SetActive(true);
                var cr = crate;
                c.Ribbon(crate.bonus > 1f ? $"+{Mathf.RoundToInt((crate.bonus - 1f) * 100f)}% MORE" : null).Bonus(null, null);
                c.OnTap(() => BuyCrate(c, cr));
                crateCards.Add((c, crate));
            }

            // SPECIAL: the hero again plus packs
            var special = pages[(int)ShopTab.Special];
            if (catalog.Hero != null && catalog.Hero.Enabled) AddProduct(Instantiate(heroTemplate, special), catalog.Hero, true);
            foreach (var p in catalog.Specials)
                if (p != null && p.Enabled) AddProduct(Instantiate(rowTemplate, special), p, true);

            // FREE
            freeCard = Instantiate(rowTemplate, pages[(int)ShopTab.Free]);
            freeCard.gameObject.SetActive(true);
            freeCard.Set(diamondIcon, "FREE DIAMONDS", $"+{catalog.FreeDiamonds} FOR A SHORT VIDEO").Ribbon(null).Bonus(null, null);
            freeCard.OnTap(() => store.WatchForFreeDiamonds(CurrencyOrigin.FromUI(freeCard.Icon)));
        }

        void AddProduct(ShopCard c, IAPProductConfig product, bool contents)
        {
            c.gameObject.SetActive(true);
            if (contents)
                c.Set(product.Icon, product.Title, store.ContentsLine(product, true), product.Type == StoreProductType.NonConsumable ? "ONE TIME OFFER" : null)
                    .Bonus(null, null);
            else
            {
                c.Set(product.Icon, product.Diamonds.ToString("N0"), "DIAMONDS");
                c.Bonus(product.BonusDiamonds > 0 ? $"+{product.BonusDiamonds:N0} BONUS" : null,
                    product.BonusDiamonds > 0 ? $"{product.TotalDiamonds:N0} TOTAL" : null);
            }

            c.Ribbon(product.Badge);
            c.OnTap(() => BuyProduct(c, product));
            productCards.Add((c, product));
        }

        void Refresh()
        {
            if (!built || store == null) return;
            var premium = store.Premium;
            int level = Services.TryGet(out ProgressionManager progression) ? progression.Level : 1;

            foreach (var (c, product) in productCards)
            {
                if (store.Owns(product) && product.Type == StoreProductType.NonConsumable) c.Face(ShopCard.ButtonLook.Owned, "OWNED");
                else if (store.Busy) c.Face(ShopCard.ButtonLook.Loading, "...");
                else if (!store.Available) c.Face(ShopCard.ButtonLook.Waiting, "OFFLINE");
                else c.Face(ShopCard.ButtonLook.Buy, store.PriceOf(product));
            }

            foreach (var (c, item) in boostCards)
                c.Face(ShopCard.ButtonLook.Diamonds, ShopCatalog.BoostCost(premium, item).ToString());

            foreach (var (c, crate) in crateCards)
            {
                c.Set(crate.icon != null ? crate.icon : cashIcon, crate.title, "$" + CurrencyFormat.Short(ShopCatalog.CrateCash(premium, crate, level)) + " CASH");
                c.Face(ShopCard.ButtonLook.Diamonds, crate.diamonds.ToString());
            }

            if (freeCard != null)
            {
                long wait = store.FreeWait;
                if (wait < 0) freeCard.Face(ShopCard.ButtonLook.Waiting, "TOMORROW");
                else if (wait > 0) freeCard.Face(ShopCard.ButtonLook.Waiting, DiamondSpend.Duration(wait));
                else freeCard.Face(store.FreeReady ? ShopCard.ButtonLook.Watch : ShopCard.ButtonLook.Loading, store.FreeReady ? "WATCH" : "...");
            }
        }

        // ------------------------------------------------------------------ buying

        void BuyProduct(ShopCard c, IAPProductConfig product)
        {
            if (!store.CanBuy(product)) return;
            c.Face(ShopCard.ButtonLook.Loading, "...");
            store.Buy(product, result =>
            {
                if (result == PurchaseResult.Success) GameFeedback.Sfx(buySfx);
                else if (result != PurchaseResult.Cancelled && Services.TryGet(out PopupManager popups))
                    popups.Message("PURCHASE FAILED", "Nothing was charged. Please try again.");
                Refresh();
            });
            Refresh();
        }

        void BuyBoost(ShopCard c, ShopCatalog.BoostItem item)
        {
            int cost = ShopCatalog.BoostCost(store.Premium, item);
            DiamondSpend.Request(cost, "BUY BOOST?", $"{item.boost.DisplayName} FOR {item.minutes:0} MIN", $"{item.boost.Multiplier:0.#}X · {item.minutes:0} MIN",
                "shop:" + item.boost.Id, item.boost.Icon, () =>
                {
                    if (Services.TryGet(out BoostManager boosts)) boosts.Activate(item.boost, item.minutes * 60f);
                    UIAnim.Upgrade(c.transform);
                    GameFeedback.Sfx(buySfx);
                });
        }

        void BuyCrate(ShopCard c, ShopCatalog.CashCrate crate)
        {
            int level = Services.TryGet(out ProgressionManager progression) ? progression.Level : 1;
            long cash = ShopCatalog.CrateCash(store.Premium, crate, level);
            DiamondSpend.Request(crate.diamonds, "BUY CASH?", crate.title, "$" + CurrencyFormat.Short(cash), "shop:crate_" + crate.diamonds,
                crate.icon != null ? crate.icon : cashIcon, () =>
                {
                    if (economy != null) economy.AddCash(cash, CurrencyOrigin.FromUI(c.Icon));
                    UIAnim.Upgrade(c.transform);
                });
        }
    }
}

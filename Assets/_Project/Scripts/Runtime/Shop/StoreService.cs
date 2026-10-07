using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using ScrapYardKing.Boosts;
using ScrapYardKing.Core;
using ScrapYardKing.Economy;
using ScrapYardKing.Persistence;
using ScrapYardKing.Progression;
using ScrapYardKing.UI;
using UnityEngine;

namespace ScrapYardKing.Shop
{
    public enum PurchaseResult
    {
        Success,
        Cancelled,
        Failed,
        Unavailable
    }

    /// <summary>What a real store (Unity IAP, a native plugin) has to give the game.</summary>
    public interface IStoreProvider
    {
        bool IsReady { get; }

        /// <summary>Localised price string, or null if the store does not know the product.</summary>
        string PriceOf(string productId);

        /// <summary>Starts a purchase; calls back once with the result and the store's transaction id.</summary>
        void Purchase(string productId, Action<PurchaseResult, string> done);

        /// <summary>Asks the store which non-consumables this account owns.</summary>
        void Restore(Action<IReadOnlyList<string>> owned);
    }

    /// <summary>
    /// The one door to real-money purchases. The shop asks for a price and to buy a product; the provider talks to the
    /// store. Until a real store is plugged in (<see cref="SetProvider"/>), a stand-in sells everything for free after
    /// a short pause in the Editor and development builds; a release build without a provider sells nothing.
    ///
    /// Safety rules, all enforced here:
    /// - a transaction id is granted once (ledger in the save), so a store that reports twice cannot pay twice;
    /// - a paid purchase is saved as pending before anything is shown, and paid into the wallet on CLAIM; a pending
    ///   purchase survives a crash and is offered again on the next start;
    /// - non-consumables are remembered as owned and can be restored, but never granted twice on one save.
    /// It also runs the Free tab's rewarded-video diamonds, whose cooldown is wall-clock time in the save.
    /// </summary>
    [DefaultExecutionOrder(-510)]
    public sealed class StoreService : ServiceBehaviour<StoreService>, ISaveable
    {
        [Serializable]
        sealed class Pending
        {
            public string transaction, product;
        }

        [Serializable]
        sealed class State
        {
            public List<string> transactions = new();
            public List<string> owned = new();
            public List<Pending> pending = new();
            public long freeReadyAt;
            public int freeDay, freeToday;
        }

        [SerializeField] ShopCatalog catalog;
        [SerializeField] PremiumEconomyConfig premium;
        [SerializeField] Sprite diamondIcon;
        [SerializeField] Sprite cashIcon;
        [Tooltip("Stand-in store: seconds a purchase takes (unscaled).")]
        [SerializeField, Min(0f)] float simulatedSeconds = 0.9f;
        [Tooltip("Stand-in store: every purchase fails (to test the failure path).")]
        [SerializeField] bool simulateFailure;

        IStoreProvider provider;
        State state = new();

        public ShopCatalog Catalog => catalog;
        public PremiumEconomyConfig Premium => premium;
        /// <summary>A purchase or restore is in progress.</summary>
        public bool Busy { get; private set; }
        public bool Simulated => provider == null;
        /// <summary>Real-money products can be bought (a provider is ready, or the stand-in runs in a dev build).</summary>
        public bool Available => provider != null ? provider.IsReady : Application.isEditor || Debug.isDebugBuild;
        public bool AdsRemoved { get; private set; }

        public event Action Changed;

        /// <summary>Plugs in a real store. Call once at startup from the store's bootstrap.</summary>
        public void SetProvider(IStoreProvider store)
        {
            provider = store;
            Changed?.Invoke();
        }

        void Start()
        {
            SaveRegistry.Register(this);
            RefreshOwned();
            SettingsPanel.RestorePurchases = Restore;
            foreach (var p in state.pending.ToArray()) Offer(p, 1.5f);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            SaveRegistry.Unregister(this);
            if (SettingsPanel.RestorePurchases == Restore) SettingsPanel.RestorePurchases = null;
        }

        string ISaveable.SaveKey => "store";
        string ISaveable.CaptureState() => JsonUtility.ToJson(state);

        void ISaveable.RestoreState(string json)
        {
            state = JsonUtility.FromJson<State>(json) ?? new State();
            state.transactions ??= new List<string>();
            state.owned ??= new List<string>();
            state.pending ??= new List<Pending>();
            RefreshOwned();
        }

        void RefreshOwned()
        {
            AdsRemoved = false;
            if (catalog == null) return;
            foreach (string id in state.owned)
            {
                var p = catalog.Find(id);
                if (p != null && p.RemovesAds) AdsRemoved = true;
            }
        }

        public bool Owns(IAPProductConfig product) => product != null && state.owned.Contains(product.ProductId);

        public bool CanBuy(IAPProductConfig product) =>
            product != null && product.Enabled && Available && !Busy &&
            !(product.Type == StoreProductType.NonConsumable && Owns(product));

        public string PriceOf(IAPProductConfig product)
        {
            if (product == null) return "";
            if (provider != null) return provider.PriceOf(product.ProductId) ?? "—";
            return Available ? product.FallbackPrice : "—";
        }

        // ------------------------------------------------------------------ buying

        public void Buy(IAPProductConfig product, Action<PurchaseResult> done = null)
        {
            if (!CanBuy(product))
            {
                done?.Invoke(PurchaseResult.Unavailable);
                return;
            }

            Busy = true;
            Changed?.Invoke();
            Analytics.Log(AnalyticsEvents.IapInitiated, ("product", product.ProductId), ("usd", product.ReferencePriceUsd));
            void Finish(PurchaseResult result, string transaction)
            {
                Busy = false;
                if (result == PurchaseResult.Success) Deliver(product, transaction);
                else Analytics.Log(AnalyticsEvents.IapFailed, ("product", product.ProductId), ("result", result.ToString()));
                Changed?.Invoke();
                done?.Invoke(result);
            }

            if (provider != null) provider.Purchase(product.ProductId, Finish);
            else StartCoroutine(Simulate(product, Finish));
        }

        IEnumerator Simulate(IAPProductConfig product, Action<PurchaseResult, string> done)
        {
            yield return new WaitForSecondsRealtime(simulatedSeconds);
            done(simulateFailure ? PurchaseResult.Failed : PurchaseResult.Success, "sim-" + Guid.NewGuid().ToString("N"));
        }

        /// <summary>The store says paid: record it, then offer the contents. Runs once per transaction id.</summary>
        void Deliver(IAPProductConfig product, string transaction)
        {
            if (string.IsNullOrEmpty(transaction) || state.transactions.Contains(transaction)) return;
            state.transactions.Add(transaction);
            if (product.Type == StoreProductType.NonConsumable && !state.owned.Contains(product.ProductId)) state.owned.Add(product.ProductId);
            RefreshOwned();
            var pending = new Pending { transaction = transaction, product = product.ProductId };
            state.pending.Add(pending);
            if (Services.TryGet(out SaveManager save)) save.Save();
            Analytics.Log(AnalyticsEvents.IapCompleted, ("product", product.ProductId), ("usd", product.ReferencePriceUsd));
            Offer(pending, 0f);
        }

        void Offer(Pending pending, float delay)
        {
            var product = catalog != null ? catalog.Find(pending.product) : null;
            if (product == null)
            {
                state.pending.Remove(pending);
                return;
            }

            if (!Services.TryGet(out PopupManager popups))
            {
                Grant(pending, default);
                return;
            }

            popups.Show(new PopupRequest
            {
                Style = PopupStyle.Reward,
                Title = "THANK YOU!",
                Subtitle = product.Title,
                Icon = product.Icon != null ? product.Icon : diamondIcon,
                SpinIcon = product.TotalDiamonds > 0,
                Amount = product.TotalDiamonds > 0 ? "+" + product.TotalDiamonds.ToString("N0") : null,
                AmountColor = new Color(0.75f, 0.9f, 1f),
                CountTo = product.TotalDiamonds,
                AmountFormat = x => "+" + ((long)Math.Round(x)).ToString("N0"),
                Body = ContentsLine(product, false),
                PrimaryLabel = "CLAIM",
                Delay = delay,
                OnPrimary = origin => Grant(pending, origin)
            });
        }

        void Grant(Pending pending, CurrencyOrigin origin)
        {
            if (!state.pending.Remove(pending)) return;
            var product = catalog.Find(pending.product);
            if (product == null) return;
            if (Services.TryGet(out EconomyManager economy))
            {
                if (product.TotalDiamonds > 0) economy.AddPremium(product.TotalDiamonds, "iap:" + product.ProductId, origin);
                long cash = CashOf(product);
                if (cash > 0) economy.AddCash(cash, origin);
            }

            if (Services.TryGet(out BoostManager boosts))
            {
                if (product.Boost != null && product.BoostMinutes > 0f) boosts.Activate(product.Boost, product.BoostMinutes * 60f);
                if (product.SecondBoost != null && product.SecondBoostMinutes > 0f) boosts.Activate(product.SecondBoost, product.SecondBoostMinutes * 60f);
            }

            if (Services.TryGet(out SaveManager save)) save.Save();
        }

        /// <summary>Cash a product pays at the current yard level.</summary>
        public long CashOf(IAPProductConfig product)
        {
            if (product == null || product.CashAsDiamonds <= 0 || premium == null) return 0;
            int level = Services.TryGet(out ProgressionManager progression) ? progression.Level : 1;
            return product.CashAsDiamonds * premium.CashPerDiamond(level);
        }

        /// <summary>"Cash $25K · 2X FACTORY 30 MIN" (what is in a product besides its diamonds).</summary>
        public string ContentsLine(IAPProductConfig product, bool includeDiamonds)
        {
            var sb = new StringBuilder();
            void Add(string part)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(part);
            }

            if (includeDiamonds && product.TotalDiamonds > 0) Add(product.TotalDiamonds.ToString("N0") + " DIAMONDS");
            long cash = CashOf(product);
            if (cash > 0) Add("$" + CurrencyFormat.Short(cash) + " CASH");
            if (product.Boost != null) Add($"{product.Boost.DisplayName} · {product.BoostMinutes:0} MIN");
            if (product.SecondBoost != null) Add($"{product.SecondBoost.DisplayName} · {product.SecondBoostMinutes:0} MIN");
            if (product.RemovesAds) Add("NO ADS, FOREVER");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ restore

        /// <summary>Asks the store for owned non-consumables and grants the ones this save does not have yet.</summary>
        public void Restore()
        {
            if (Busy) return;
            Services.TryGet(out PopupManager popups);
            if (!Available)
            {
                popups?.Message("STORE OFFLINE", "The store can't be reached right now. Try again later.");
                return;
            }

            Busy = true;
            void Done(IReadOnlyList<string> owned)
            {
                Busy = false;
                int restored = 0;
                foreach (string id in owned ?? Array.Empty<string>())
                {
                    var product = catalog != null ? catalog.Find(id) : null;
                    if (product == null || product.Type != StoreProductType.NonConsumable || state.owned.Contains(id)) continue;
                    Deliver(product, "restore-" + id);
                    restored++;
                }

                Changed?.Invoke();
                if (restored == 0) popups?.Message("RESTORE PURCHASES", "Everything you bought is already on this device.");
            }

            if (provider != null) provider.Restore(Done);
            else Done(state.owned);
        }

        // ------------------------------------------------------------------ free diamonds (rewarded video)

        static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        static int Today => (int)(Now / 86400);

        int FreeLeftToday => catalog == null ? 0 : (state.freeDay == Today ? catalog.FreeDailyCap - state.freeToday : catalog.FreeDailyCap);

        /// <summary>Seconds until the free diamonds can be watched again (0 = now); -1 = none left today.</summary>
        public long FreeWait
        {
            get
            {
                if (FreeLeftToday <= 0) return -1;
                return Math.Max(0, state.freeReadyAt - Now);
            }
        }

        public bool FreeReady => FreeWait == 0 && Services.TryGet(out AdService ads) && ads.Ready;

        public void WatchForFreeDiamonds(CurrencyOrigin origin)
        {
            if (!FreeReady || !Services.TryGet(out AdService ads)) return;
            Analytics.Log(AnalyticsEvents.RvOffered, ("placement", "shop_free_diamonds"));
            ads.ShowRewarded("shop_free_diamonds", () =>
            {
                if (state.freeDay != Today)
                {
                    state.freeDay = Today;
                    state.freeToday = 0;
                }

                state.freeToday++;
                state.freeReadyAt = Now + (long)catalog.FreeCooldownSeconds;
                Analytics.Log(AnalyticsEvents.RvRewardGranted, ("placement", "shop_free_diamonds"), ("diamonds", catalog.FreeDiamonds));
                if (Services.TryGet(out EconomyManager economy)) economy.AddPremium(catalog.FreeDiamonds, "rv:shop_free", origin);
                Changed?.Invoke();
            });
        }
    }
}

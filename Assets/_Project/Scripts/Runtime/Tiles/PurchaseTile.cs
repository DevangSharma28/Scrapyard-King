using System.Collections.Generic;
using DG.Tweening;
using ScrapYardKing.Core;
using ScrapYardKing.Economy;
using ScrapYardKing.Feedback;
using ScrapYardKing.Progression;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ScrapYardKing.Tiles
{
    /// <summary>
    /// Pay-by-standing tile for one <see cref="IUpgradeable"/> (worker hires, area expansions). While the player stands
    /// on it, cash drains from the wallet into the tile as flying bills and a radial fill grows; when the price is
    /// covered the upgrade completes through <see cref="UpgradeManager.CompletePrepaid"/>. Partial payments are kept
    /// when the player walks off. Shows a lock with the required yard level until it unlocks.
    /// </summary>
    public sealed class PurchaseTile : Tile, ISaveable
    {
        [System.Serializable]
        sealed class State
        {
            public long paid;
        }

        static readonly Dictionary<string, PurchaseTile> Tiles = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Tiles.Clear();

        /// <summary>The active tile that sells <paramref name="upgradeId"/>, if any.</summary>
        public static bool TryGet(string upgradeId, out PurchaseTile tile) => Tiles.TryGetValue(upgradeId ?? string.Empty, out tile);

        [SerializeField] string upgradeId;

        [Header("Payment feel")]
        [Tooltip("Seconds to pay a full price while standing on the tile.")]
        [SerializeField, Min(0.1f)] float payDuration = 1.5f;
        [Tooltip("Floor on the drain speed so cheap prices still take a beat.")]
        [SerializeField, Min(1f)] float minCashPerSecond = 60f;
        [SerializeField] Transform billPrefab;
        [SerializeField, Min(0.01f)] float billInterval = 0.045f;
        [SerializeField, Min(0.05f)] float billFlyDuration = 0.32f;
        [SerializeField] SfxDefinition paySfx;
        [SerializeField, Min(0f)] float pitchStep = 0.015f;
        [SerializeField] SfxDefinition completeSfx;
        [SerializeField] ParticleSystem completeVfx;
        [Tooltip("The tile sinks away once the upgrade is maxed (one-off purchases, last hire).")]
        [SerializeField] bool hideWhenMaxed = true;

        [Header("Display")]
        [Tooltip("Unit-scale content under the tile's world canvas; punched, shaken and shrunk for feedback.")]
        [SerializeField] RectTransform displayRoot;
        [SerializeField] Image icon;
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text price;
        [SerializeField] TMP_Text countText;
        [SerializeField] GameObject coinIcon;
        [SerializeField] Image fill;
        [SerializeField] Image frame;
        [SerializeField] GameObject lockRoot;
        [SerializeField] TMP_Text lockText;
        [SerializeField] Color readyColor = new(0.35f, 0.95f, 0.4f);
        [SerializeField] Color idleColor = Color.white;
        [SerializeField] Color lockedColor = new(0.55f, 0.55f, 0.6f);

        readonly Stack<Transform> billPool = new();
        UpgradeManager upgrades;
        EconomyManager economy;
        ProgressionManager progression;
        CashCollector playerCash;
        IUpgradeable upgrade;
        long paid;
        float payCarry, nextBill, shownFill;
        int streak, billsInFlight;
        bool completing, wasLocked = true, hidden, deniedThisVisit, awaitingExit;

        public string UpgradeId => upgradeId;
        public IUpgradeable Upgrade => upgrade;
        public long Paid => paid;
        /// <summary>Cash still needed for the next level (0 when maxed or unbound).</summary>
        public long Remaining => upgrade == null || upgrade.IsMaxed ? 0 : System.Math.Max(0, upgrade.NextCost - paid);
        public bool IsUnlocked => upgrade != null && upgrades != null && upgrades.IsUnlocked(upgrade);
        /// <summary>Unlocked, not maxed and the wallet covers what is left.</summary>
        public bool IsReady => IsUnlocked && !upgrade.IsMaxed && economy != null && economy.Cash >= Remaining;

        protected override void OnEnable()
        {
            base.OnEnable();
            if (!string.IsNullOrEmpty(upgradeId)) Tiles[upgradeId] = this;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (Tiles.TryGetValue(upgradeId ?? string.Empty, out var t) && t == this) Tiles.Remove(upgradeId);
        }

        protected override void Start()
        {
            base.Start();
            Services.TryGet(out economy);
            Services.TryGet(out progression);
            if (Services.TryGet(out upgrades))
            {
                upgrades.Registered += OnRegistered;
                if (upgrades.TryGet(upgradeId, out var u)) Bind(u);
            }

            if (economy != null) economy.CashChanged += OnCashChanged;
            if (progression != null) progression.LevelChanged += OnLevelChanged;
            if (fill != null) fill.fillAmount = 0f;
            Refresh();
            SaveRegistry.Register(this);
        }

        string ISaveable.SaveKey => string.IsNullOrEmpty(upgradeId) ? null : "tile/" + upgradeId;

        string ISaveable.CaptureState() => JsonUtility.ToJson(new State { paid = paid });

        void ISaveable.RestoreState(string state)
        {
            paid = System.Math.Max(0, JsonUtility.FromJson<State>(state).paid);
            Refresh();
        }

        void OnDestroy()
        {
            SaveRegistry.Unregister(this);
            if (upgrades != null) upgrades.Registered -= OnRegistered;
            if (economy != null) economy.CashChanged -= OnCashChanged;
            if (progression != null) progression.LevelChanged -= OnLevelChanged;
            if (upgrade != null) upgrade.UpgradeChanged -= OnUpgradeChanged;
            if (displayRoot != null) displayRoot.DOKill();
        }

        void OnRegistered(IUpgradeable u)
        {
            if (u.UpgradeId == upgradeId) Bind(u);
        }

        void Bind(IUpgradeable u)
        {
            if (upgrade != null) upgrade.UpgradeChanged -= OnUpgradeChanged;
            upgrade = u;
            upgrade.UpgradeChanged += OnUpgradeChanged;
            if (icon != null)
            {
                icon.sprite = u.Icon;
                icon.enabled = u.Icon != null;
            }

            if (title != null) title.text = u.DisplayName.ToUpperInvariant();
            wasLocked = !IsUnlocked;
            Refresh();
        }

        void OnUpgradeChanged(IUpgradeable _) => Refresh();
        void OnCashChanged(long balance, long delta, CurrencyOrigin source) => RefreshColors();
        void OnLevelChanged(int level) => Refresh();

        protected override void OnEngage()
        {
            streak = 0;
            deniedThisVisit = false;
            if (Player != null) Player.TryGetComponent(out playerCash);
        }

        protected override void OnStay(float deltaTime)
        {
            // One purchase per visit: step off and back on to buy the next level.
            if (upgrade == null || completing || hidden || awaitingExit || upgrades == null || economy == null) return;
            if (!IsUnlocked || upgrade.IsMaxed)
            {
                Deny();
                return;
            }

            long cost = upgrade.NextCost;
            long remaining = Remaining;
            if (remaining <= 0)
            {
                BeginComplete();
                return;
            }

            if (economy.Cash <= 0)
            {
                Deny();
                return;
            }

            float rate = Mathf.Max(minCashPerSecond, cost / payDuration);
            payCarry += rate * deltaTime;
            long chunk = System.Math.Min(System.Math.Min((long)payCarry, remaining), economy.Cash);
            if (chunk <= 0) return;

            payCarry -= chunk;
            if (!economy.TrySpendCash(chunk)) return;
            paid += chunk;
            if (Time.time >= nextBill)
            {
                nextBill = Time.time + billInterval;
                FlyBill();
            }

            RefreshPrice();
            if (Remaining <= 0) BeginComplete();
        }

        protected override void OnDisengage()
        {
            payCarry = 0f;
            awaitingExit = false;
        }

        protected override void Update()
        {
            base.Update();
            if (fill == null || upgrade == null) return;
            float target = upgrade.IsMaxed || upgrade.NextCost <= 0 ? 0f : Mathf.Clamp01(paid / (float)upgrade.NextCost);
            shownFill = Mathf.MoveTowards(shownFill, target, Time.deltaTime * 3f);
            if (completing) shownFill = 1f;
            fill.fillAmount = shownFill;
        }

        void Deny()
        {
            if (deniedThisVisit) return;
            deniedThisVisit = true;
            if (displayRoot != null)
            {
                displayRoot.DOKill(true);
                displayRoot.DOShakeAnchorPos(0.3f, new Vector2(14f, 0f), 18, 0f);
            }

            var config = GameFeedback.Config;
            string message = !IsUnlocked ? $"REACH LV {upgrades.UnlockLevel(upgradeId)}" : $"NEED ${CurrencyFormat.Short(Remaining)}";
            if (!upgrade.IsMaxed) GameFeedback.Popup(message, transform.position + Vector3.up * 1.6f, config != null ? config.WarningPopupColor : Color.red, 1f);
        }

        void FlyBill()
        {
            if (billPrefab == null) return;
            var from = playerCash != null ? playerCash.Target.position : Player != null ? Player.transform.position + Vector3.up * 1.2f : transform.position;
            var bill = billPool.Count > 0 ? billPool.Pop() : Instantiate(billPrefab, transform);
            bill.gameObject.SetActive(true);
            bill.SetParent(transform, true);
            bill.position = from;
            bill.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            bill.localScale = billPrefab.localScale;
            billsInFlight++;

            var to = transform.position + new Vector3(Random.Range(-0.3f, 0.3f), 0.1f, Random.Range(-0.3f, 0.3f));
            float pitch = 1f + Mathf.Min(streak++, 30) * pitchStep;
            DOTween.Sequence()
                .Append(bill.DOJump(to, 1.1f, 1, billFlyDuration).SetEase(Ease.InQuad))
                .Join(bill.DOScale(billPrefab.localScale * 0.5f, billFlyDuration).SetEase(Ease.InQuad))
                .OnComplete(() =>
                {
                    billsInFlight--;
                    bill.gameObject.SetActive(false);
                    billPool.Push(bill);
                    GameFeedback.Sfx(paySfx, pitch);
                    if (displayRoot != null)
                    {
                        displayRoot.DOKill(true);
                        displayRoot.DOPunchScale(Vector3.one * 0.05f, 0.1f, 4, 0.5f);
                    }
                })
                .SetTarget(bill);
        }

        void BeginComplete()
        {
            completing = true;
            DOVirtual.DelayedCall(billFlyDuration + 0.05f, Complete, false).SetTarget(this);
        }

        void Complete()
        {
            completing = false;
            if (upgrade == null || upgrade.IsMaxed) return;
            paid = 0;
            payCarry = 0f;
            shownFill = 0f;
            awaitingExit = IsEngaged;
            upgrades.CompletePrepaid(upgrade);

            GameFeedback.Sfx(completeSfx);
            GameFeedback.Vfx(completeVfx, transform.position + Vector3.up * 0.2f, Quaternion.identity);
            GameFeedback.CameraPunch(0.5f);
            if (displayRoot != null)
            {
                displayRoot.DOKill(true);
                displayRoot.DOPunchScale(Vector3.one * 0.25f, 0.4f, 6, 0.6f);
            }

            Refresh();
        }

        void Refresh()
        {
            if (upgrade == null) return;

            if (hideWhenMaxed && upgrade.IsMaxed)
            {
                Hide();
                return;
            }

            bool unlocked = IsUnlocked;
            if (lockRoot != null) lockRoot.SetActive(!unlocked);
            if (lockText != null) lockText.text = $"LV {upgrades.UnlockLevel(upgradeId)}";
            if (countText != null) countText.text = upgrade.MaxLevel > 1 ? upgrade.LevelLabel : upgrade.NextEffect;
            if (unlocked && wasLocked && displayRoot != null)
            {
                displayRoot.DOKill(true);
                displayRoot.DOPunchScale(Vector3.one * 0.2f, 0.45f, 6, 0.6f);
            }

            wasLocked = !unlocked;
            RefreshPrice();
            RefreshColors();
        }

        void RefreshPrice()
        {
            if (price == null || upgrade == null) return;
            bool showPrice = IsUnlocked && !upgrade.IsMaxed;
            price.text = upgrade.IsMaxed ? "MAX" : showPrice ? CurrencyFormat.Short(Remaining) : string.Empty;
            if (coinIcon != null) coinIcon.SetActive(showPrice);
        }

        void RefreshColors()
        {
            if (frame == null || upgrade == null) return;
            frame.color = !IsUnlocked ? lockedColor : IsReady ? readyColor : idleColor;
        }

        void Hide()
        {
            if (hidden) return;
            hidden = true;
            Transform root = displayRoot != null ? displayRoot : transform;
            root.DOKill();
            DOTween.Sequence()
                .AppendInterval(0.35f)
                .Append(root.DOScale(0f, 0.35f).SetEase(Ease.InBack))
                .OnComplete(() => gameObject.SetActive(false))
                .SetTarget(this);
        }
    }
}

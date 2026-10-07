using System;
using DG.Tweening;
using ScrapYardKing.Boosts;
using ScrapYardKing.Core;
using ScrapYardKing.Economy;
using ScrapYardKing.Feedback;
using ScrapYardKing.Progression;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ScrapYardKing.UI
{
    /// <summary>
    /// DAILY REWARDS: seven tiles (days 1–6 in a grid, day 7 wide and golden). Taken days carry a check, today's tile
    /// glows and breathes, CLAIM pays it and flies it to the HUD. Opens by itself once per session when today's reward
    /// is waiting (after any welcome-back card), and from the bottom bar.
    /// </summary>
    public sealed class DailyPanel : MonoBehaviour
    {
        [Serializable]
        public sealed class Tile
        {
            public RectTransform root;
            public Image back;
            public TMP_Text day;
            public Image icon;
            public TMP_Text amount;
            public GameObject check;
        }

        [SerializeField] GameObject root;
        [SerializeField] Image dim;
        [SerializeField] RectTransform card;
        [SerializeField] CanvasGroup cardGroup;
        [SerializeField] Button close;
        [SerializeField] Tile[] tiles;
        [SerializeField] Button claim;
        [SerializeField] Image claimBack;
        [SerializeField] TMP_Text claimLabel;
        [SerializeField] Sprite claimOn;
        [SerializeField] Sprite claimOff;
        [SerializeField] Sprite tileToday;
        [SerializeField] Sprite tileFuture;
        [SerializeField] Sprite tileTaken;
        [SerializeField] Sprite cashIcon;
        [SerializeField] Sprite diamondIcon;
        [SerializeField] SfxDefinition openSfx;
        [SerializeField] SfxDefinition closeSfx;
        [SerializeField] SfxDefinition claimSfx;
        [Tooltip("Seconds after start before the panel may open by itself.")]
        [SerializeField, Min(0f)] float autoOpenAfter = 4.5f;

        DailyRewardManager daily;
        bool autoTried;
        float nextRefresh;
        int breathing = -1;

        public bool IsOpen { get; private set; }

        void Awake()
        {
            if (root != null) root.SetActive(false);
            if (close != null) close.onClick.AddListener(Close);
            if (claim != null) claim.onClick.AddListener(Claim);
        }

        void Update()
        {
            if (!autoTried && Time.unscaledTime >= autoOpenAfter) TryAutoOpen();
            if (!IsOpen || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.5f;
            Refresh();
        }

        void TryAutoOpen()
        {
            if (!Services.TryGet(out daily)) return;
            // after welcome back: its cash still waiting means its card is up or about to be (both can pass their start
            // delay in the same frame, so the popup queue alone is not enough)
            if (Services.TryGet(out IdleIncomeManager idle) && idle.Pending > 0) return;
            if (Services.TryGet(out PopupManager popups) && (popups.IsShowing || popups.Queued > 0)) return;
            autoTried = true;
            // only at the start of a session: in the session of the first diamond the badge on DAILY invites instead
            // (opening it mid-play stacked the calendar on top of the first-diamond gift)
            if (!Badges.ExtrasUnlocked) return;
            if (daily.CanClaim) Open();
        }

        public void Open()
        {
            if (root == null || (daily == null && !Services.TryGet(out daily))) return;
            IsOpen = true;
            root.SetActive(true);
            var c = dim.color;
            c.a = 0f;
            dim.color = c;
            UIAnim.Dim(dim, 0.75f);
            UIAnim.Show(card, cardGroup);
            GameFeedback.Sfx(openSfx);
            breathing = -1;
            Refresh();
            for (int i = 0; i < tiles.Length; i++) UIAnim.UnlockReveal(tiles[i].root, 0.05f * i);
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            GameFeedback.Sfx(closeSfx);
            UIAnim.Dim(dim, 0f);
            UIAnim.Hide(card, cardGroup, () => root.SetActive(false));
        }

        void Refresh()
        {
            var config = daily.Config;
            int next = daily.NextDay;
            int taken = daily.ClaimedToday && next == 0 ? tiles.Length : next;   // a finished week shows all seven taken until tomorrow
            int today = daily.CanClaim ? next : -1;
            for (int i = 0; i < tiles.Length && i < config.Days.Length; i++)
            {
                var t = tiles[i];
                var r = config.Days[i].reward;
                bool isTaken = i < taken;
                t.back.sprite = i == today ? tileToday : isTaken ? tileTaken : tileFuture;
                t.day.SetText(i == tiles.Length - 1 ? "DAY 7 · BIG PRIZE" : $"DAY {i + 1}");
                t.icon.sprite = config.Days[i].icon != null ? config.Days[i].icon : r.diamonds > 0 ? diamondIcon : cashIcon;
                t.amount.SetText(MissionManager.Label(r));
                if (t.check != null) t.check.SetActive(isTaken);
                t.icon.color = isTaken ? new Color(1f, 1f, 1f, 0.45f) : Color.white;
            }

            if (today != breathing)
            {
                if (breathing >= 0 && breathing < tiles.Length) { DG.Tweening.DOTween.Kill(tiles[breathing].root); tiles[breathing].root.localScale = Vector3.one; }
                breathing = today;
                if (today >= 0) UIAnim.Breathe(tiles[today].root, 1.05f).SetDelay(0.4f);
            }

            bool can = daily.CanClaim;
            claim.interactable = can;
            claimBack.sprite = can ? claimOn : claimOff;
            claimLabel.SetText(can ? "CLAIM" : "NEXT IN " + DiamondSpend.Duration(MissionManager.SecondsToNewDay).ToUpperInvariant());
        }

        void Claim()
        {
            if (daily == null || !daily.CanClaim) return;
            int day = daily.NextDay;
            var origin = CurrencyOrigin.FromUI(tiles[day].icon.rectTransform);
            if (!daily.Claim(origin)) return;
            GameFeedback.Sfx(claimSfx);
            UIAnim.Upgrade(tiles[day].root);
            Haptics.Heavy();
            Refresh();
        }
    }
}

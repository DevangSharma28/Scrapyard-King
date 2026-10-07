using System.Collections.Generic;
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
    /// MISSIONS: three tabs. MAIN shows the current step of the main chain (it completes by itself); DAILY the day's
    /// three missions and when new ones come; ACHIEVEMENTS the lifetime goals tier by tier. Every row shows its reward
    /// before the player starts; CLAIM pays it and flies it to the HUD.
    /// </summary>
    public sealed class MissionsPanel : MonoBehaviour
    {
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
        [SerializeField] MissionRow rowTemplate;
        [SerializeField] TMP_Text noteTemplate;

        [Header("Icons")]
        [SerializeField] Sprite cashIcon;
        [SerializeField] Sprite diamondIcon;
        [SerializeField] Sprite xpIcon;
        [SerializeField] Sprite mainIcon;
        [SerializeField] Sprite boostIcon;
        [SerializeField] SfxDefinition openSfx;
        [SerializeField] SfxDefinition closeSfx;
        [SerializeField] SfxDefinition claimSfx;

        readonly List<MissionRow> daily = new();
        readonly List<MissionRow> achievements = new();
        MissionRow mainRow;
        TMP_Text mainNote, dailyNote;
        MissionManager missions;
        bool built;
        float nextRefresh;
        int tab;

        public bool IsOpen { get; private set; }

        void Awake()
        {
            if (root != null) root.SetActive(false);
            if (close != null) close.onClick.AddListener(Close);
            for (int i = 0; tabs != null && i < tabs.Length; i++)
            {
                int t = i;
                tabs[i].onClick.AddListener(() => Show(t));
            }

            if (rowTemplate != null) rowTemplate.gameObject.SetActive(false);
            if (noteTemplate != null) noteTemplate.gameObject.SetActive(false);
        }

        public void Open()
        {
            if (root == null || !Services.TryGet(out missions)) return;
            if (!built) Build();
            if (!IsOpen)
            {
                IsOpen = true;
                root.SetActive(true);
                var c = dim.color;
                c.a = 0f;
                dim.color = c;
                UIAnim.Dim(dim, 0.75f);
                UIAnim.Show(card, cardGroup);
                GameFeedback.Sfx(openSfx);
            }

            // open on DAILY, or on ACHIEVEMENTS when only an achievement waits to be claimed
            bool dailyClaim = false;
            for (int i = 0; i < missions.DailyCount; i++) dailyClaim |= missions.DailyClaimable(i);
            Show(!dailyClaim && missions.Claimable > 0 ? 2 : 1);
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            GameFeedback.Sfx(closeSfx);
            UIAnim.Dim(dim, 0f);
            UIAnim.Hide(card, cardGroup, () => root.SetActive(false));
        }

        void Show(int t)
        {
            tab = t;
            for (int i = 0; i < pages.Length; i++)
            {
                pages[i].gameObject.SetActive(i == t);
                if (i < tabBacks.Length) tabBacks[i].sprite = i == t ? tabOn : tabOff;
            }

            scroll.content = pages[t];
            scroll.verticalNormalizedPosition = 1f;
            UIAnim.Punch(tabs[t].transform, 0.12f, 0.2f);
            Refresh();
        }

        void Build()
        {
            built = true;
            mainRow = Row(pages[0]);
            mainNote = Note(pages[0], "THE MAIN GOAL COMPLETES BY ITSELF. NEW GOALS FOLLOW AS THE YARD GROWS.");
            for (int i = 0; i < missions.Config.DailyCount; i++) daily.Add(Row(pages[1]));
            dailyNote = Note(pages[1], "");
            for (int i = 0; i < missions.Config.Achievements.Length; i++) achievements.Add(Row(pages[2]));
        }

        MissionRow Row(RectTransform page)
        {
            var r = Instantiate(rowTemplate, page);
            r.gameObject.SetActive(true);
            return r;
        }

        TMP_Text Note(RectTransform page, string text)
        {
            var n = Instantiate(noteTemplate, page);
            n.gameObject.SetActive(true);
            n.SetText(text);
            return n;
        }

        void Update()
        {
            if (!IsOpen || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.4f;
            Refresh();
        }

        void Refresh()
        {
            if (!built || missions == null) return;

            // MAIN
            if (Services.TryGet(out TaskManager tasks) && tasks.Main != null)
            {
                var d = tasks.Main.Definition;
                string reward = d.RewardCash > 0 ? "$" + CurrencyFormat.Short(d.RewardCash) : d.RewardXp > 0 ? d.RewardXp + " XP" : "";
                mainRow.gameObject.SetActive(true);
                mainRow.Bind(mainIcon, d.Title, "MAIN GOAL", tasks.Main.Progress01, $"{tasks.Main.Progress} / {d.Amount}",
                    d.RewardCash > 0 ? cashIcon : xpIcon, reward, MissionRow.Look.Working, null);
            }
            else mainRow.gameObject.SetActive(false);

            // DAILY
            for (int i = 0; i < daily.Count; i++)
            {
                bool has = i < missions.DailyCount && missions.Daily(i) != null;
                daily[i].gameObject.SetActive(has);
                if (!has) continue;
                var m = missions.Daily(i);
                long target = missions.DailyTarget(i), progress = missions.DailyProgress(i);
                var look = missions.DailyClaimed(i) ? MissionRow.Look.Done : missions.DailyClaimable(i) ? MissionRow.Look.Claim : MissionRow.Look.Working;
                int index = i;
                var row = daily[i];
                daily[i].Bind(m.icon, MissionManager.Title(m.title, target, Fmt(target)), "DAILY", progress / (float)Mathf.Max(1, target),
                    $"{Fmt(progress)} / {Fmt(target)}", RewardIcon(m.reward), MissionManager.Label(m.reward), look,
                    () => Claimed(missions.ClaimDaily(index, CurrencyOrigin.FromUI(row.Icon)), row));
            }

            dailyNote.SetText("NEW MISSIONS IN " + DiamondSpend.Duration(MissionManager.SecondsToNewDay).ToUpperInvariant());

            // ACHIEVEMENTS
            var list = missions.Config.Achievements;
            for (int i = 0; i < achievements.Count; i++)
            {
                var a = list[i];
                long target = missions.AchievementTarget(i), progress = missions.AchievementProgress(i);
                int tiers = a.targets.Length, tier = missions.AchievementTier(i);
                var look = missions.AchievementDone(i) ? MissionRow.Look.Done : missions.AchievementClaimable(i) ? MissionRow.Look.Claim : MissionRow.Look.Working;
                int index = i;
                var row = achievements[i];
                achievements[i].Bind(a.icon, MissionManager.Title(a.title, target, Fmt(target)), $"TIER {Mathf.Min(tier + 1, tiers)} OF {tiers}",
                    progress / (float)Mathf.Max(1, target), $"{Fmt(progress)} / {Fmt(target)}", diamondIcon,
                    missions.AchievementReward(i).ToString(), look,
                    () => Claimed(missions.ClaimAchievement(index, CurrencyOrigin.FromUI(row.Icon)), row));
            }
        }

        void Claimed(bool ok, MissionRow row)
        {
            if (!ok) return;
            GameFeedback.Sfx(claimSfx);
            UIAnim.Upgrade(row.transform);
            Refresh();
        }

        Sprite RewardIcon(MissionReward r) => r.diamonds > 0 ? diamondIcon : r.cashPerLevel > 0 ? cashIcon : r.boost != null ? r.boost.Icon : boostIcon;

        static string Fmt(long n) => n >= 10000 ? CurrencyFormat.Short(n) : n.ToString("N0");
    }
}

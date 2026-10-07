using System.Collections.Generic;
using DG.Tweening;
using ScrapYardKing.Core;
using ScrapYardKing.Progression;
using ScrapYardKing.Workers;
using ScrapYardKing.World;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ScrapYardKing.UI
{
    /// <summary>Big centre-screen moments: "LEVEL UP!", "PORTER HIRED!". Queued so two never overlap.</summary>
    public sealed class Announcer : MonoBehaviour
    {
        [SerializeField] RectTransform root;
        [SerializeField] CanvasGroup group;
        [SerializeField] TMP_Text headline;
        [SerializeField] TMP_Text subline;
        [Tooltip("Picture of what just arrived (area, machine, worker); hidden when there is none.")]
        [SerializeField] Image icon;
        [SerializeField] Sprite levelIcon;
        [SerializeField, Min(0.2f)] float holdDuration = 1.1f;

        readonly Queue<(string, string, Sprite)> queue = new();
        ProgressionManager progression;
        WorkerManager workers;
        TaskManager tasks;
        bool playing;

        void Start()
        {
            group.alpha = 0f;
            root.localScale = Vector3.zero;
            if (Services.TryGet(out progression)) progression.LevelChanged += OnLevelChanged;
            Services.TryGet(out workers);
            if (Services.TryGet(out tasks)) tasks.ChainCompleted += OnChainCompleted;
            GameEvents.WorkerHired += OnWorkerHired;
            GameEvents.ExpansionOpened += OnExpansionOpened;
            GameEvents.GiantScrapArrived += OnGiantArrived;
            GameEvents.GiantScrapDefeated += OnGiantDefeated;
        }

        void OnDestroy()
        {
            root.DOKill();
            group.DOKill();
            if (progression != null) progression.LevelChanged -= OnLevelChanged;
            if (tasks != null) tasks.ChainCompleted -= OnChainCompleted;
            GameEvents.WorkerHired -= OnWorkerHired;
            GameEvents.ExpansionOpened -= OnExpansionOpened;
            GameEvents.GiantScrapArrived -= OnGiantArrived;
            GameEvents.GiantScrapDefeated -= OnGiantDefeated;
        }

        void OnLevelChanged(int level) => Show("LEVEL UP!", $"YARD LEVEL {level}", levelIcon);

        void OnWorkerHired(string id, int total)
        {
            string line = workers != null && workers.TryGetDefinition(id, out var d) && !string.IsNullOrEmpty(d.Tagline)
                ? d.Tagline
                : "A NEW PAIR OF HANDS";
            Show("NEW WORKER!", line, workers != null && workers.TryGetDefinition(id, out var def) ? def.Icon : null);
        }

        void OnExpansionOpened(string id)
        {
            IUpgradeable u = null;
            bool known = Services.TryGet(out UpgradeManager upgrades) && upgrades.TryGet(id, out u);
            string name = known ? u.DisplayName.ToUpperInvariant() : "NEW AREA";
            // a build inside an area (a furnace, the Press) is a new machine, not a new area
            if (id != null && id.StartsWith("build_")) Show("NEW MACHINE!", $"{name} IS READY", known ? u.Icon : null);
            else Show("NEW AREA!", $"{name} IS OPEN", known ? u.Icon : null);
        }

        void OnChainCompleted() => Show("YARD KING!", "FIRST SHIFT COMPLETE");

        void OnGiantArrived(string scrapId)
        {
            if (Services.TryGet(out GiantScrapEvent giant) && giant.Config != null) Show(giant.Config.ArriveTitle, giant.Config.ArriveLine);
        }

        void OnGiantDefeated(string scrapId, long bonus)
        {
            string title = Services.TryGet(out GiantScrapEvent giant) && giant.Config != null ? giant.Config.DefeatTitle : "DEMOLISHED!";
            Show(title, bonus > 0 ? $"+${Economy.CurrencyFormat.Short(bonus)} BONUS" : string.Empty);
        }

        public void Show(string big, string small) => Show(big, small, null);

        public void Show(string big, string small, Sprite picture)
        {
            queue.Enqueue((big, small, picture));
            if (!playing) PlayNext();
        }

        void PlayNext()
        {
            if (queue.Count == 0)
            {
                playing = false;
                return;
            }

            playing = true;
            var (big, small, picture) = queue.Dequeue();
            headline.text = big;
            subline.text = small;
            if (icon != null)
            {
                icon.gameObject.SetActive(picture != null);
                icon.sprite = picture;
                if (picture != null) UIAnim.UnlockReveal(icon.transform, 0.12f);
            }
            root.DOKill();
            group.DOKill();
            root.localScale = Vector3.one * 0.3f;
            group.alpha = 0f;
            DOTween.Sequence()
                .Append(root.DOScale(1f, 0.35f).SetEase(Ease.OutBack))
                .Join(group.DOFade(1f, 0.2f))
                .AppendInterval(holdDuration)
                .Append(group.DOFade(0f, 0.25f))
                .Join(root.DOScale(1.15f, 0.25f))
                .OnComplete(PlayNext)
                .SetTarget(root);
        }
    }
}

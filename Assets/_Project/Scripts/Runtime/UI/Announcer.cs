using System.Collections.Generic;
using DG.Tweening;
using ScrapYardKing.Core;
using ScrapYardKing.Progression;
using ScrapYardKing.Workers;
using TMPro;
using UnityEngine;

namespace ScrapYardKing.UI
{
    /// <summary>Big centre-screen moments: "LEVEL UP!", "PORTER HIRED!". Queued so two never overlap.</summary>
    public sealed class Announcer : MonoBehaviour
    {
        [SerializeField] RectTransform root;
        [SerializeField] CanvasGroup group;
        [SerializeField] TMP_Text headline;
        [SerializeField] TMP_Text subline;
        [SerializeField, Min(0.2f)] float holdDuration = 1.1f;

        readonly Queue<(string, string)> queue = new();
        ProgressionManager progression;
        WorkerManager workers;
        bool playing;

        void Start()
        {
            group.alpha = 0f;
            root.localScale = Vector3.zero;
            if (Services.TryGet(out progression)) progression.LevelChanged += OnLevelChanged;
            Services.TryGet(out workers);
            GameEvents.WorkerHired += OnWorkerHired;
            GameEvents.ExpansionOpened += OnExpansionOpened;
        }

        void OnDestroy()
        {
            root.DOKill();
            group.DOKill();
            if (progression != null) progression.LevelChanged -= OnLevelChanged;
            GameEvents.WorkerHired -= OnWorkerHired;
            GameEvents.ExpansionOpened -= OnExpansionOpened;
        }

        void OnLevelChanged(int level) => Show("LEVEL UP!", $"YARD LEVEL {level}");

        void OnWorkerHired(string id, int total)
        {
            string line = workers != null && workers.TryGetDefinition(id, out var d) && !string.IsNullOrEmpty(d.Tagline)
                ? d.Tagline
                : "A NEW PAIR OF HANDS";
            Show("NEW WORKER!", line);
        }

        void OnExpansionOpened(string id)
        {
            string area = Services.TryGet(out UpgradeManager upgrades) && upgrades.TryGet(id, out var u) ? u.DisplayName.ToUpperInvariant() : "NEW AREA";
            Show("NEW AREA!", $"{area} IS OPEN");
        }

        public void Show(string big, string small)
        {
            queue.Enqueue((big, small));
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
            var (big, small) = queue.Dequeue();
            headline.text = big;
            subline.text = small;
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

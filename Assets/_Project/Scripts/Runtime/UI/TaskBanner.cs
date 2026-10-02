using System.Text;
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
    /// Current main task under the HUD: title, progress bar and count. Slides in, punches on progress, shows a
    /// check and the reward on completion, then slides out for the next one. A second line shows the active side task.
    /// </summary>
    public sealed class TaskBanner : MonoBehaviour
    {
        [SerializeField] RectTransform panel;
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text progressText;
        [SerializeField] Image fill;
        [SerializeField] RectTransform check;
        [SerializeField] TMP_Text reward;
        [SerializeField] TMP_Text sideLine;
        [SerializeField] float hiddenOffset = 320f;

        TaskManager tasks;
        ActiveTask shown;
        Vector2 restPosition;

        void Start()
        {
            restPosition = panel.anchoredPosition;
            panel.anchoredPosition = restPosition + Vector2.up * hiddenOffset;
            if (check != null) check.localScale = Vector3.zero;
            if (reward != null) reward.alpha = 0f;
            if (sideLine != null) sideLine.text = string.Empty;
            if (!Services.TryGet(out tasks)) return;

            tasks.TaskStarted += OnStarted;
            tasks.TaskProgressed += OnProgressed;
            tasks.TaskCompleted += OnCompleted;
            if (tasks.Main != null) Show(tasks.Main);
            RefreshSide();
        }

        void OnDestroy()
        {
            panel.DOKill();
            if (tasks == null) return;
            tasks.TaskStarted -= OnStarted;
            tasks.TaskProgressed -= OnProgressed;
            tasks.TaskCompleted -= OnCompleted;
        }

        void OnStarted(ActiveTask task)
        {
            if (task.Definition.Category == TaskCategory.Main) Show(task);
            RefreshSide();
        }

        void OnProgressed(ActiveTask task)
        {
            RefreshSide();
            if (task != shown) return;
            UpdateProgress(true);
        }

        void OnCompleted(ActiveTask task)
        {
            RefreshSide();
            if (task != shown) return;

            UpdateProgress(true);
            if (check != null)
            {
                check.DOKill();
                check.localScale = Vector3.zero;
                check.DOScale(1f, 0.35f).SetEase(Ease.OutBack);
            }

            if (reward != null)
            {
                reward.text = RewardText(task.Definition);
                reward.DOKill();
                reward.alpha = 0f;
                reward.DOFade(1f, 0.2f);
            }

            panel.DOKill(true);
            panel.DOPunchScale(Vector3.one * 0.08f, 0.35f, 6, 0.6f);
            panel.DOAnchorPos(restPosition + Vector2.up * hiddenOffset, 0.3f).SetDelay(1.25f).SetEase(Ease.InBack);
        }

        void Show(ActiveTask task)
        {
            shown = task;
            title.text = task.Definition.Title.ToUpperInvariant();
            if (check != null) check.localScale = Vector3.zero;
            // Show what the task pays up front: the reward is part of the pull.
            if (reward != null)
            {
                reward.text = RewardText(task.Definition);
                reward.alpha = 0.9f;
            }
            fill.fillAmount = task.Progress01;
            UpdateProgress(false);
            panel.DOKill();
            panel.anchoredPosition = restPosition + Vector2.up * hiddenOffset;
            panel.DOAnchorPos(restPosition, 0.4f).SetEase(Ease.OutBack);
        }

        void UpdateProgress(bool animate)
        {
            if (shown == null) return;
            progressText.text = ProgressText(shown);
            fill.DOKill();
            if (animate)
            {
                fill.DOFillAmount(shown.Progress01, 0.25f).SetEase(Ease.OutCubic);
                progressText.transform.DOKill(true);
                progressText.transform.DOPunchScale(Vector3.one * 0.2f, 0.2f, 5, 0.5f);
            }
            else fill.fillAmount = shown.Progress01;
        }

        void RefreshSide()
        {
            if (sideLine == null || tasks == null) return;
            var sb = new StringBuilder();
            foreach (var t in tasks.SideTasks)
            {
                if (t.IsComplete) continue;
                sb.Append("BONUS: ").Append(t.Definition.Title.ToUpperInvariant()).Append("  ").Append(ProgressText(t));
                if (t.Definition.RewardCash > 0) sb.Append("  <color=#FFD54A>+$").Append(CurrencyFormat.Short(t.Definition.RewardCash)).Append("</color>");
                break;
            }

            sideLine.text = sb.ToString();
        }

        static string ProgressText(ActiveTask task)
        {
            var d = task.Definition;
            return d.Type == TaskType.EarnCash
                ? $"${CurrencyFormat.Short(task.Progress)}/${CurrencyFormat.Short(d.Amount)}"
                : $"{task.Progress}/{d.Amount}";
        }

        static string RewardText(TaskDefinition d)
        {
            var sb = new StringBuilder();
            if (d.RewardCash > 0) sb.Append("+$").Append(CurrencyFormat.Short(d.RewardCash)).Append("  ");
            if (d.RewardXp > 0) sb.Append("+").Append(d.RewardXp).Append(" XP  ");
            if (d.RewardPremium > 0) sb.Append("+").Append(d.RewardPremium).Append(" GEMS");
            return sb.ToString().Trim();
        }
    }
}

using DG.Tweening;
using ScrapYardKing.Boosts;
using ScrapYardKing.Core;
using ScrapYardKing.Economy;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ScrapYardKing.UI
{
    /// <summary>
    /// "While you were away": shown once after loading when <see cref="IdleIncomeManager"/> has cash waiting. Two
    /// buttons: claim, or watch a rewarded video and claim double. Nothing else in the game waits for it.
    /// </summary>
    public sealed class OfflinePanel : MonoBehaviour
    {
        [SerializeField] GameObject root;
        [SerializeField] RectTransform card;
        [SerializeField] TMP_Text away;
        [SerializeField] TMP_Text amount;
        [SerializeField] TMP_Text doubleAmount;
        [SerializeField] Button claim;
        [SerializeField] Button claimDouble;
        [Tooltip("Seconds after the game starts before the panel may appear (the loading card goes first).")]
        [SerializeField, Min(0f)] float delay = 2.8f;

        IdleIncomeManager idle;
        bool done;

        void Awake()
        {
            if (root != null) root.SetActive(false);
            if (claim != null) claim.onClick.AddListener(() => Claim(false));
            if (claimDouble != null) claimDouble.onClick.AddListener(ClaimDouble);
        }

        void Update()
        {
            if (done || Time.unscaledTime < delay || root == null) return;
            if (idle == null && !Services.TryGet(out idle)) return;
            done = true;
            if (idle.Pending <= 0) return;
            int minutes = Mathf.Max(1, (int)(idle.AwaySeconds / 60.0));
            if (away != null) away.SetText(minutes >= 60 ? $"{minutes / 60} H {minutes % 60} MIN" : $"{minutes} MIN");
            if (amount != null) amount.SetText("+" + CurrencyFormat.Short(idle.Pending));
            if (doubleAmount != null) doubleAmount.SetText("+" + CurrencyFormat.Short(idle.Pending * 2));
            root.SetActive(true);
            if (card != null)
            {
                card.localScale = Vector3.one * 0.7f;
                card.DOScale(1f, 0.35f).SetEase(Ease.OutBack).SetUpdate(true);
            }
        }

        void ClaimDouble()
        {
            if (!Services.TryGet(out AdService ads) || !ads.Ready) return;
            ads.ShowRewarded("offline_double", () => Claim(true));
        }

        void Claim(bool doubled)
        {
            if (idle != null) idle.Claim(doubled);
            if (root != null) root.SetActive(false);
        }
    }
}

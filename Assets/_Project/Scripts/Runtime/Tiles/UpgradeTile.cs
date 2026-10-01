using DG.Tweening;
using ScrapYardKing.Core;
using ScrapYardKing.UI;
using UnityEngine;

namespace ScrapYardKing.Tiles
{
    /// <summary>
    /// The yard's upgrade station: standing on it opens the <see cref="UpgradePanel"/>, walking off closes it.
    /// A bobbing "!" badge shows when something in the panel can be bought right now.
    /// </summary>
    public sealed class UpgradeTile : Tile
    {
        [SerializeField] Transform alertBadge;
        [SerializeField, Min(0.05f)] float alertCheckInterval = 0.25f;
        [SerializeField, Min(0f)] float alertBobHeight = 0.25f;

        UpgradePanel panel;
        Vector3 alertRest, alertScale = Vector3.one;
        float nextAlertCheck;
        bool alertOn;

        protected override void Awake()
        {
            base.Awake();
            if (alertBadge == null) return;
            alertRest = alertBadge.localPosition;
            alertScale = alertBadge.localScale;
            alertBadge.gameObject.SetActive(false);
        }

        protected override void Start()
        {
            base.Start();
            Services.TryGet(out panel);
        }

        void OnDestroy()
        {
            if (alertBadge != null) alertBadge.DOKill();
        }

        protected override void Update()
        {
            base.Update();
            if (Time.time < nextAlertCheck) return;
            nextAlertCheck = Time.time + alertCheckInterval;
            if (panel == null && !Services.TryGet(out panel)) return;
            SetAlert(panel.HasPurchasable && !IsEngaged);
        }

        protected override void OnEngage()
        {
            if (panel != null || Services.TryGet(out panel)) panel.Open();
        }

        protected override void OnDisengage()
        {
            if (panel != null) panel.Close();
        }

        void SetAlert(bool on)
        {
            if (alertBadge == null || on == alertOn) return;
            alertOn = on;
            alertBadge.DOKill();
            if (!on)
            {
                alertBadge.DOScale(0f, 0.15f).SetEase(Ease.InBack).OnComplete(() => alertBadge.gameObject.SetActive(false));
                return;
            }

            alertBadge.gameObject.SetActive(true);
            alertBadge.localPosition = alertRest;
            alertBadge.localScale = Vector3.zero;
            alertBadge.DOScale(alertScale, 0.3f).SetEase(Ease.OutBack);
            alertBadge.DOLocalMoveY(alertRest.y + alertBobHeight, 0.45f).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo);
        }
    }
}

using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ScrapYardKing.UI
{
    /// <summary>What a station needs from the player right now, shown as a small tag on its label.</summary>
    public enum StationStatus
    {
        None,
        /// <summary>Output can't leave (downstream full): move the goods on.</summary>
        Jammed,
        /// <summary>Stock is at capacity.</summary>
        Full,
        /// <summary>Active Overdrive or another speed boost.</summary>
        Boosted,
        /// <summary>Customers are waiting at an empty counter.</summary>
        NeedsStock
    }

    /// <summary>
    /// Floating world label over a station: name, level, a stock counter ("7/10") that turns red when full and punches
    /// on change, and a status tag (JAMMED / FULL / x2 / NEEDS STOCK) so a bottleneck reads without any menu.
    /// Billboards to the camera.
    /// </summary>
    public sealed class StationLabel : MonoBehaviour
    {
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text level;
        [SerializeField] TMP_Text counter;
        [SerializeField] Transform counterRoot;
        [SerializeField] Color counterColor = Color.white;
        [SerializeField] Color fullColor = new(1f, 0.35f, 0.3f);
        [SerializeField] bool billboard = true;

        [Header("Status tag (optional)")]
        [SerializeField] RectTransform statusRoot;
        [SerializeField] Image statusBackground;
        [SerializeField] TMP_Text statusText;
        [SerializeField] Color jammedColor = new(0.9f, 0.22f, 0.18f);
        [SerializeField] Color fullColor2 = new(0.95f, 0.5f, 0.1f);
        [SerializeField] Color boostedColor = new(0.2f, 0.75f, 0.95f);
        [SerializeField] Color needsStockColor = new(0.9f, 0.22f, 0.18f);

        StationStatus status;
        string statusDetail;

        Transform cameraTransform;
        int lastCount = int.MinValue;
        Vector3 counterScale = Vector3.one;

        void Awake()
        {
            if (counterRoot != null) counterScale = counterRoot.localScale;
        }

        public void Set(string displayName, int levelValue, int count, int capacity)
        {
            if (title != null) title.text = displayName.ToUpperInvariant();
            if (level != null) level.text = $"Lv.{levelValue}";
            if (counter == null) return;

            counter.text = $"{count}/{capacity}";
            counter.color = count >= capacity ? fullColor : counterColor;
            if (count != lastCount && lastCount != int.MinValue && counterRoot != null)
            {
                counterRoot.DOKill(true);
                counterRoot.localScale = counterScale;
                counterRoot.DOPunchScale(counterScale * 0.15f, 0.18f, 5, 0.5f);
            }

            lastCount = count;
        }

        /// <summary>Shows or hides the status tag. <paramref name="detail"/> overrides the default text (e.g. "x2").</summary>
        public void SetStatus(StationStatus newStatus, string detail = null)
        {
            if (newStatus == status && detail == statusDetail) return;
            bool appearing = status == StationStatus.None && newStatus != StationStatus.None;
            status = newStatus;
            statusDetail = detail;
            if (statusRoot == null) return;

            statusRoot.gameObject.SetActive(newStatus != StationStatus.None);
            if (newStatus == StationStatus.None) return;
            if (statusText != null)
                statusText.text = detail ?? newStatus switch
                {
                    StationStatus.Jammed => "JAMMED",
                    StationStatus.Full => "FULL",
                    StationStatus.Boosted => "BOOST",
                    StationStatus.NeedsStock => "NEEDS STOCK",
                    _ => string.Empty
                };
            if (statusBackground != null)
                statusBackground.color = newStatus switch
                {
                    StationStatus.Jammed => jammedColor,
                    StationStatus.Full => fullColor2,
                    StationStatus.Boosted => boostedColor,
                    _ => needsStockColor
                };
            if (!appearing) return;
            statusRoot.DOKill(true);
            statusRoot.localScale = Vector3.zero;
            statusRoot.DOScale(1f, 0.25f).SetEase(Ease.OutBack);
        }

        void LateUpdate()
        {
            if (!billboard) return;
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
            if (cameraTransform != null) transform.rotation = cameraTransform.rotation;
        }
    }
}

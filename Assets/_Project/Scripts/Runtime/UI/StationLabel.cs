using DG.Tweening;
using ScrapYardKing.Tiles;
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
    /// Billboards to the camera. Floating 3–6 m up, a label lands on screen over the ground north of its station; while
    /// it covers a buy tile there it fades back so the tile stays readable ("nothing covers what the player works with").
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

        [Header("Stepping back from tiles")]
        [Tooltip("Alpha while the label covers a buy tile on screen.")]
        [SerializeField, Range(0f, 1f)] float coveringAlpha = 0.3f;
        [Tooltip("Share of a tile's screen area the label must cover before it fades.")]
        [SerializeField, Range(0f, 1f)] float coverShare = 0.12f;

        static readonly Vector3[] Corners = new Vector3[4];
        CanvasGroup group;
        Graphic[] graphics;
        bool covering;
        float nextCoverCheck;

        StationStatus status;
        string statusDetail;

        Transform cameraTransform;
        int lastCount = int.MinValue;
        Vector3 counterScale = Vector3.one;

        void Awake()
        {
            if (counterRoot != null) counterScale = counterRoot.localScale;
            if (!TryGetComponent(out group)) group = gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            graphics = GetComponentsInChildren<Graphic>(true);
            nextCoverCheck = Random.value * 0.2f;   // spread the checks over frames
            if (statusText != null) statusText.textWrappingMode = TextWrappingModes.NoWrap;
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
            FitStatus();
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

        /// <summary>The tag grows with its text on one line ("BUILDER'S ORDER 0/12" wrapped onto the title in a fixed box).</summary>
        void FitStatus()
        {
            if (statusText == null || statusRoot == null) return;
            float size = statusText.enableAutoSizing ? statusText.fontSizeMax : statusText.fontSize;
            float width = statusText.GetPreferredValues(statusText.text, 1000f, 100f).x * size / Mathf.Max(1f, statusText.fontSize);
            var margins = statusText.rectTransform.sizeDelta;   // the text is stretched with negative margins
            statusRoot.sizeDelta = new Vector2(Mathf.Max(statusRoot.sizeDelta.y * 1.6f, width - margins.x + 8f), statusRoot.sizeDelta.y);
        }

        void LateUpdate()
        {
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
            if (billboard && cameraTransform != null) transform.rotation = cameraTransform.rotation;

            if (Time.unscaledTime >= nextCoverCheck)
            {
                nextCoverCheck = Time.unscaledTime + 0.2f;
                covering = CoversTile();
            }

            float target = covering ? coveringAlpha : 1f;
            if (!Mathf.Approximately(group.alpha, target)) group.alpha = Mathf.MoveTowards(group.alpha, target, Time.unscaledDeltaTime * 4f);
        }

        bool CoversTile()
        {
            var cam = Camera.main;
            if (cam == null || Tile.Visible.Count == 0) return false;
            if (!ScreenRect(cam, out var label)) return false;
            foreach (var tile in Tile.Visible)
            {
                if (!tile.ScreenRect(cam, out var r)) continue;
                float w = Mathf.Min(label.xMax, r.xMax) - Mathf.Max(label.xMin, r.xMin);
                float h = Mathf.Min(label.yMax, r.yMax) - Mathf.Max(label.yMin, r.yMin);
                if (w > 0f && h > 0f && w * h >= r.width * r.height * coverShare) return true;
            }

            return false;
        }

        /// <summary>Screen rect of the parts of the label that are drawn.</summary>
        bool ScreenRect(Camera cam, out Rect rect)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var g in graphics)
            {
                if (g == null || !g.isActiveAndEnabled || g is TMP_SubMeshUI) continue;
                g.rectTransform.GetWorldCorners(Corners);
                foreach (var c in Corners)
                {
                    var p = cam.WorldToScreenPoint(c);
                    if (p.z <= 0f) continue;
                    x0 = Mathf.Min(x0, p.x); y0 = Mathf.Min(y0, p.y); x1 = Mathf.Max(x1, p.x); y1 = Mathf.Max(y1, p.y);
                }
            }

            rect = Rect.MinMaxRect(x0, y0, x1, y1);
            return x1 > x0 && y1 > y0;
        }
    }
}

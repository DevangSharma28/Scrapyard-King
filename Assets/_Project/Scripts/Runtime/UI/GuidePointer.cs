using ScrapYardKing.Core;
using ScrapYardKing.Progression;
using UnityEngine;

namespace ScrapYardKing.UI
{
    /// <summary>Screen-edge arrow that points toward the guide target whenever it is off screen.</summary>
    public sealed class GuidePointer : MonoBehaviour
    {
        [SerializeField] RectTransform arrow;
        [SerializeField] RectTransform area;
        [Tooltip("Inset from the screen edge, in canvas units.")]
        [SerializeField] float margin = 90f;
        [Tooltip("Extra inset at the bottom so the arrow stays above the upgrade rail.")]
        [SerializeField] float bottomMargin = 330f;
        [SerializeField] float topMargin = 300f;

        GuideDirector guide;
        Camera cam;

        void Start()
        {
            Services.TryGet(out guide);
            arrow.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (cam == null) cam = Camera.main;
            if (guide == null || cam == null || !guide.Target.HasValue)
            {
                Hide();
                return;
            }

            Vector3 viewport = cam.WorldToViewportPoint(guide.Target.Value);
            bool behind = viewport.z < 0f;
            if (!behind && viewport.x > 0.04f && viewport.x < 0.96f && viewport.y > 0.18f && viewport.y < 0.86f)
            {
                Hide();
                return;
            }

            if (behind) viewport = new Vector3(1f - viewport.x, 1f - viewport.y, 0f);
            Vector2 size = area.rect.size;
            Vector2 centre = Vector2.zero;
            Vector2 local = new Vector2((viewport.x - 0.5f) * size.x, (viewport.y - 0.5f) * size.y);
            Vector2 dir = (local - centre).normalized;

            float halfW = size.x * 0.5f - margin;
            float top = size.y * 0.5f - topMargin;
            float bottom = -(size.y * 0.5f - bottomMargin);
            float t = float.MaxValue;
            if (Mathf.Abs(dir.x) > 0.0001f) t = Mathf.Min(t, halfW / Mathf.Abs(dir.x));
            if (dir.y > 0.0001f) t = Mathf.Min(t, top / dir.y);
            if (dir.y < -0.0001f) t = Mathf.Min(t, bottom / dir.y);

            arrow.gameObject.SetActive(true);
            arrow.anchoredPosition = dir * t;
            arrow.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
        }

        void Hide()
        {
            if (arrow.gameObject.activeSelf) arrow.gameObject.SetActive(false);
        }
    }
}

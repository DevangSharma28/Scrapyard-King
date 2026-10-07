using DG.Tweening;
using ScrapYardKing.Core;
using ScrapYardKing.World;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ScrapYardKing.UI
{
    /// <summary>
    /// Boss bar for the Giant Scrap event: drops in under the HUD when the giant arrives, drains with every hit (with a
    /// pale trail that catches up, so each chunk of damage reads) and leaves when the giant is dismantled.
    /// </summary>
    public sealed class GiantScrapBar : MonoBehaviour
    {
        [SerializeField] RectTransform root;
        [SerializeField] CanvasGroup group;
        [SerializeField] TMP_Text title;
        [SerializeField] Image fill;
        [SerializeField] Image trail;
        [Tooltip("Punched on every hit.")]
        [SerializeField] RectTransform punchRoot;
        [SerializeField] Gradient colorByHealth;
        [SerializeField, Min(0f)] float trailHold = 0.25f;
        [SerializeField, Min(0.1f)] float trailDrain = 0.9f;
        [Tooltip("Where the bar waits off screen, from its shown position: positive above, negative below.")]
        [SerializeField] float hiddenOffset = 220f;

        GiantScrapEvent giant;
        Vector2 shownPosition;
        float shown = 1f, trailValue = 1f, trailReleaseAt, lastTarget = 1f;
        bool visible;

        void Awake()
        {
            shownPosition = root.anchoredPosition;
            group.alpha = 0f;
            root.anchoredPosition = shownPosition + Vector2.up * hiddenOffset;
        }

        void OnEnable()
        {
            GameEvents.GiantScrapArrived += OnArrived;
            GameEvents.GiantScrapDefeated += OnDefeated;
        }

        void OnDisable()
        {
            GameEvents.GiantScrapArrived -= OnArrived;
            GameEvents.GiantScrapDefeated -= OnDefeated;
        }

        void OnDestroy()
        {
            root.DOKill();
            group.DOKill();
            if (punchRoot != null) punchRoot.DOKill();
        }

        void OnArrived(string scrapId)
        {
            if (!Services.TryGet(out giant)) return;
            if (title != null && giant.Config != null && giant.Config.Giant != null) title.text = giant.Config.Giant.DisplayName.ToUpperInvariant();
            shown = trailValue = lastTarget = 1f;
            Apply();
            Show(true);
        }

        void OnDefeated(string scrapId, long bonus)
        {
            shown = trailValue = 0f;
            Apply();
            Show(false);
        }

        void Show(bool on)
        {
            visible = on;
            root.DOKill();
            group.DOKill();
            if (on)
            {
                root.DOAnchorPos(shownPosition, 0.45f).SetEase(Ease.OutBack);
                group.DOFade(1f, 0.25f);
            }
            else
            {
                root.DOAnchorPos(shownPosition + Vector2.up * hiddenOffset, 0.35f).SetEase(Ease.InBack).SetDelay(0.6f);
                group.DOFade(0f, 0.3f).SetDelay(0.6f);
            }
        }

        void Update()
        {
            if (!visible || giant == null || giant.Giant == null) return;

            float target = giant.Health01;
            if (target < lastTarget - 0.0005f)
            {
                trailReleaseAt = Time.unscaledTime + trailHold;
                if (punchRoot != null)
                {
                    punchRoot.DOKill(true);
                    punchRoot.DOPunchScale(new Vector3(0.03f, 0.14f, 0f), 0.14f, 4, 0.5f);
                }
            }

            lastTarget = target;
            float dt = Time.unscaledDeltaTime;
            shown = Mathf.Lerp(shown, target, Easing.Damp(16f, dt));
            if (trailValue < shown) trailValue = shown;
            else if (Time.unscaledTime >= trailReleaseAt) trailValue = Mathf.MoveTowards(trailValue, shown, trailDrain * dt);
            Apply();
        }

        void Apply()
        {
            if (fill != null)
            {
                fill.fillAmount = shown;
                if (colorByHealth != null) fill.color = colorByHealth.Evaluate(shown);
            }

            if (trail != null) trail.fillAmount = trailValue;
        }
    }
}

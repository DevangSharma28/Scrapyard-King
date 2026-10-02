using ScrapYardKing.Core;
using UnityEngine;

namespace ScrapYardKing.Harvest
{
    /// <summary>
    /// Billboarded health bar. Appears on hit, pops a little with each hit, shows a white "damage trail" that holds for
    /// a beat before draining to the new value (so every chunk of damage is readable), hides after a short idle delay.
    /// </summary>
    public sealed class WorldHealthBar : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] Transform fill;
        [SerializeField] Renderer fillRenderer;
        [SerializeField] Gradient colorByHealth = DefaultGradient();
        [SerializeField, Min(0f)] float hideDelay = 2.5f;
        [SerializeField, Min(0f)] float smoothing = 14f;
        [SerializeField] Color trailColor = new(1f, 0.97f, 0.9f);
        [SerializeField, Min(0f)] float trailHold = 0.22f;
        [SerializeField, Min(0.1f)] float trailDrain = 1.6f;
        [SerializeField, Min(0f)] float hitPop = 0.18f;

        MaterialPropertyBlock block, trailBlock;
        Transform cameraTransform, trail;
        Renderer trailRenderer;
        Vector3 baseScale;
        float target = 1f, shown = 1f, trailValue = 1f, trailReleaseAt, pop, hideAt;

        void Awake()
        {
            baseScale = transform.localScale;
            if (fill == null || fillRenderer == null) return;
            // Trail: a copy of the fill drawn just behind it.
            var copy = Instantiate(fill.gameObject, fill.parent);
            copy.name = "Trail";
            trail = copy.transform;
            trail.SetSiblingIndex(fill.GetSiblingIndex());
            trail.localPosition = fill.localPosition + new Vector3(0f, 0f, 0.004f);
            trailRenderer = copy.GetComponentInChildren<Renderer>();
            trailBlock = new MaterialPropertyBlock();
            trailBlock.SetColor(BaseColorId, trailColor);
            if (trailRenderer != null) trailRenderer.SetPropertyBlock(trailBlock);
        }

        /// <summary>Re-sizes the bar (width in metres) and remembers it as the rest scale.</summary>
        public void SetSize(float width, float height)
        {
            baseScale = new Vector3(width, height, 1f);
            transform.localScale = baseScale;
        }

        public void Show(float health01)
        {
            float next = Mathf.Clamp01(health01);
            if (next < target) pop = 1f;
            target = next;
            trailReleaseAt = Time.time + trailHold;
            hideAt = Time.time + hideDelay;
            if (!gameObject.activeSelf) gameObject.SetActive(true);
        }

        public void Hide()
        {
            target = shown = trailValue = 1f;
            pop = 0f;
            Apply();
            transform.localScale = baseScale;
            gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (Time.time >= hideAt)
            {
                Hide();
                return;
            }

            float dt = Time.unscaledDeltaTime;
            shown = Mathf.Lerp(shown, target, Easing.Damp(smoothing, dt));
            if (trailValue < shown) trailValue = shown;
            else if (Time.time >= trailReleaseAt) trailValue = Mathf.MoveTowards(trailValue, shown, trailDrain * dt);
            pop = Mathf.MoveTowards(pop, 0f, dt * 7f);
            transform.localScale = new Vector3(baseScale.x * (1f + hitPop * 0.4f * pop), baseScale.y * (1f + hitPop * pop), baseScale.z);
            Apply();

            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
            if (cameraTransform != null) transform.rotation = cameraTransform.rotation;
        }

        void Apply()
        {
            if (fill == null) return;
            fill.localScale = new Vector3(shown, 1f, 1f);
            fill.localPosition = new Vector3(-(1f - shown) * 0.5f, 0f, fill.localPosition.z);
            if (trail != null)
            {
                trail.localScale = new Vector3(trailValue, 1f, 1f);
                trail.localPosition = new Vector3(-(1f - trailValue) * 0.5f, 0f, trail.localPosition.z);
            }

            if (fillRenderer == null) return;
            block ??= new MaterialPropertyBlock();
            fillRenderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, colorByHealth.Evaluate(shown));
            fillRenderer.SetPropertyBlock(block);
        }

        static Gradient DefaultGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.95f, 0.25f, 0.2f), 0f),
                    new GradientColorKey(new Color(1f, 0.8f, 0.2f), 0.5f),
                    new GradientColorKey(new Color(0.35f, 0.9f, 0.35f), 1f)
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }
    }
}

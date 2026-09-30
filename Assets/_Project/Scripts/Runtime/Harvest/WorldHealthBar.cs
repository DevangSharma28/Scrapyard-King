using ScrapYardKing.Core;
using UnityEngine;

namespace ScrapYardKing.Harvest
{
    /// <summary>Billboarded two-quad health bar. Appears on hit, hides after a short idle delay.</summary>
    public sealed class WorldHealthBar : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] Transform fill;
        [SerializeField] Renderer fillRenderer;
        [SerializeField] Gradient colorByHealth = DefaultGradient();
        [SerializeField, Min(0f)] float hideDelay = 2.5f;
        [SerializeField, Min(0f)] float smoothing = 14f;

        MaterialPropertyBlock block;
        Transform cameraTransform;
        float target = 1f, shown = 1f, hideAt;

        public void Show(float health01)
        {
            target = Mathf.Clamp01(health01);
            hideAt = Time.time + hideDelay;
            if (!gameObject.activeSelf) gameObject.SetActive(true);
        }

        public void Hide()
        {
            target = shown = 1f;
            Apply();
            gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (Time.time >= hideAt)
            {
                Hide();
                return;
            }

            shown = Mathf.Lerp(shown, target, Easing.Damp(smoothing, Time.unscaledDeltaTime));
            Apply();

            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
            if (cameraTransform != null) transform.rotation = cameraTransform.rotation;
        }

        void Apply()
        {
            if (fill == null) return;
            fill.localScale = new Vector3(shown, 1f, 1f);
            fill.localPosition = new Vector3(-(1f - shown) * 0.5f, 0f, fill.localPosition.z);

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

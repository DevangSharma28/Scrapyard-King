using System.Collections.Generic;
using ScrapYardKing.Core;
using TMPro;
using UnityEngine;

namespace ScrapYardKing.Feedback
{
    /// <summary>
    /// World-space rising popups ("+20", "MAX"). Concurrent popups are capped; the oldest is recycled first,
    /// following the blueprint rule of no more than three floating prompts at once.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public sealed class FloatingTextManager : ServiceBehaviour<FloatingTextManager>
    {
        sealed class Popup
        {
            public TextMeshPro Text;
            public Vector3 Origin;
            public Color Color;
            public float StartTime;
            public float Scale;
        }

        [SerializeField] TextMeshPro prefab;
        [SerializeField, Min(1)] int maxActive = 3;
        [SerializeField, Min(0.1f)] float lifetime = 0.9f;
        [SerializeField] float riseDistance = 1.4f;
        [SerializeField, Range(0f, 1f)] float fadeStart = 0.6f;

        readonly Stack<TextMeshPro> pool = new();
        readonly List<Popup> active = new();
        Transform cameraTransform;

        public void Show(string text, Vector3 worldPosition, Color color, float scale = 1f)
        {
            if (prefab == null) return;
            if (active.Count >= maxActive) Release(0);

            var label = pool.Count > 0 ? pool.Pop() : Instantiate(prefab, transform);
            label.gameObject.SetActive(true);
            label.text = text;
            label.color = color;
            label.transform.position = worldPosition;
            active.Add(new Popup { Text = label, Origin = worldPosition, Color = color, StartTime = Time.unscaledTime, Scale = scale });
        }

        void LateUpdate()
        {
            if (active.Count == 0) return;
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;

            float now = Time.unscaledTime;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var popup = active[i];
                float t = (now - popup.StartTime) / lifetime;
                if (t >= 1f)
                {
                    Release(i);
                    continue;
                }

                var tr = popup.Text.transform;
                tr.position = popup.Origin + Vector3.up * (riseDistance * Easing.OutCubic(t));
                if (cameraTransform != null) tr.rotation = cameraTransform.rotation;

                float pop = t < 0.18f ? Easing.OutBack(t / 0.18f, 3f) : 1f;
                tr.localScale = Vector3.one * (popup.Scale * pop);

                var c = popup.Color;
                c.a = 1f - Mathf.InverseLerp(fadeStart, 1f, t);
                popup.Text.color = c;
            }
        }

        void Release(int index)
        {
            var popup = active[index];
            active.RemoveAt(index);
            if (popup.Text == null) return;
            popup.Text.gameObject.SetActive(false);
            pool.Push(popup.Text);
        }
    }
}

using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace ScrapYardKing.UI
{
    /// <summary>Pooled icons that burst out of a world position and fly into a HUD element.</summary>
    public sealed class UIFlyer : MonoBehaviour
    {
        [SerializeField] RectTransform layer;
        [SerializeField] Image iconPrefab;
        [SerializeField, Min(0.1f)] float duration = 0.55f;
        [SerializeField, Min(0f)] float burstRadius = 60f;

        readonly Stack<Image> pool = new();
        Camera worldCamera;

        /// <summary>Flies <paramref name="count"/> icons from a world position to <paramref name="target"/>. <paramref name="onFirstArrive"/> fires once.</summary>
        public void Fly(Vector3 worldPosition, RectTransform target, Sprite sprite, int count, Action onFirstArrive)
        {
            if (worldCamera == null) worldCamera = Camera.main;
            if (worldCamera == null || target == null || layer == null)
            {
                onFirstArrive?.Invoke();
                return;
            }

            Vector2 screen = worldCamera.WorldToScreenPoint(worldPosition);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(layer, screen, null, out var start);
            Vector2 end = layer.InverseTransformPoint(target.position);
            bool fired = false;

            for (int i = 0; i < count; i++)
            {
                var img = pool.Count > 0 ? pool.Pop() : Instantiate(iconPrefab, layer);
                if (sprite != null) img.sprite = sprite;
                var rt = img.rectTransform;
                rt.gameObject.SetActive(true);
                rt.anchoredPosition = start;
                rt.localScale = Vector3.zero;

                Vector2 burst = start + UnityEngine.Random.insideUnitCircle * burstRadius;
                float delay = i * 0.04f;
                DOTween.Sequence()
                    .AppendInterval(delay)
                    .Append(rt.DOScale(1f, 0.12f).SetEase(Ease.OutBack))
                    .Join(rt.DOAnchorPos(burst, 0.18f).SetEase(Ease.OutQuad))
                    .Append(rt.DOAnchorPos(end, duration).SetEase(Ease.InCubic))
                    .Join(rt.DOScale(0.7f, duration).SetEase(Ease.InQuad))
                    .OnComplete(() =>
                    {
                        rt.gameObject.SetActive(false);
                        pool.Push(img);
                        if (fired) return;
                        fired = true;
                        onFirstArrive?.Invoke();
                    })
                    .SetTarget(rt);
            }
        }
    }
}

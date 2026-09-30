using DG.Tweening;
using TMPro;
using UnityEngine;

namespace ScrapYardKing.UI
{
    /// <summary>
    /// Floating world label over a station: name, level and a stock counter ("7/10") that turns red when full
    /// and punches on change. Billboards to the camera.
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

        void LateUpdate()
        {
            if (!billboard) return;
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
            if (cameraTransform != null) transform.rotation = cameraTransform.rotation;
        }
    }
}

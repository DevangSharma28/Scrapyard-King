using ScrapYardKing.Items;
using TMPro;
using UnityEngine;

namespace ScrapYardKing.UI
{
    /// <summary>Pulsing "MAX" label that floats above a full carry stack.</summary>
    public sealed class CarryStackIndicator : MonoBehaviour
    {
        [SerializeField] CarryStack stack;
        [SerializeField] TMP_Text label;
        [SerializeField] string fullText = "MAX";
        [SerializeField] float heightPadding = 0.55f;
        [SerializeField, Min(0f)] float pulseSpeed = 7f;
        [SerializeField, Min(0f)] float pulseAmount = 0.12f;

        Transform cameraTransform;
        Vector3 baseScale;

        void Awake()
        {
            if (label == null) return;
            baseScale = label.transform.localScale;
            label.text = fullText;
            label.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (stack == null || label == null) return;

            bool show = stack.IsFull && stack.Capacity > 0;
            if (label.gameObject.activeSelf != show) label.gameObject.SetActive(show);
            if (!show) return;

            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
            var tr = label.transform;
            tr.position = stack.StackRoot.position + Vector3.up * (stack.TopHeight + heightPadding);
            if (cameraTransform != null) tr.rotation = cameraTransform.rotation;
            tr.localScale = baseScale * (1f + Mathf.Sin(Time.unscaledTime * pulseSpeed) * pulseAmount);
        }
    }
}

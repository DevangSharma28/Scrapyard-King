using UnityEngine;
using UnityEngine.EventSystems;

namespace ScrapYardKing.UI
{
    /// <summary>
    /// Floating mobile joystick: appears where the thumb lands anywhere inside its rect and follows the thumb when
    /// dragged past its radius. Works with mouse in the editor through the same UI events.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [SerializeField] RectTransform baseRect;
        [SerializeField] RectTransform handle;
        [SerializeField] CanvasGroup visuals;
        [Tooltip("Handle travel in canvas units.")]
        [SerializeField, Min(1f)] float radius = 110f;
        [SerializeField, Range(0f, 0.9f)] float deadZone = 0.12f;
        [Tooltip("Base follows the thumb when it is dragged beyond the radius.")]
        [SerializeField] bool dynamicBase = true;
        [SerializeField, Range(0f, 1f)] float idleAlpha;
        [SerializeField, Range(0f, 1f)] float activeAlpha = 1f;

        RectTransform area;
        Vector2 idlePosition;
        int pointerId = int.MinValue;

        public bool IsHeld => pointerId != int.MinValue;
        public Vector2 Value { get; private set; }

        void Awake()
        {
            area = (RectTransform)transform;
            if (baseRect != null) idlePosition = baseRect.anchoredPosition;
            SetVisible(false);
        }

        void OnDisable() => Release();

        public void OnPointerDown(PointerEventData eventData)
        {
            if (IsHeld) return;
            pointerId = eventData.pointerId;
            if (baseRect != null) baseRect.anchoredPosition = ToLocal(eventData);
            if (handle != null) handle.anchoredPosition = Vector2.zero;
            Value = Vector2.zero;
            SetVisible(true);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId != pointerId || baseRect == null) return;

            Vector2 local = ToLocal(eventData);
            Vector2 delta = local - baseRect.anchoredPosition;
            if (dynamicBase && delta.magnitude > radius)
            {
                baseRect.anchoredPosition = local - delta.normalized * radius;
                delta = delta.normalized * radius;
            }

            delta = Vector2.ClampMagnitude(delta, radius);
            if (handle != null) handle.anchoredPosition = delta;

            Vector2 normalized = delta / radius;
            float magnitude = normalized.magnitude;
            Value = magnitude < deadZone ? Vector2.zero : normalized * Mathf.InverseLerp(deadZone, 1f, magnitude) / magnitude;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId == pointerId) Release();
        }

        void Release()
        {
            pointerId = int.MinValue;
            Value = Vector2.zero;
            if (handle != null) handle.anchoredPosition = Vector2.zero;
            if (baseRect != null) baseRect.anchoredPosition = idlePosition;
            SetVisible(false);
        }

        Vector2 ToLocal(PointerEventData eventData)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(area, eventData.position, eventData.pressEventCamera, out var local);
            return local;
        }

        void SetVisible(bool active)
        {
            if (visuals != null) visuals.alpha = active ? activeAlpha : idleAlpha;
        }
    }
}

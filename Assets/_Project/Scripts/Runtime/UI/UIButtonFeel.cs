using ScrapYardKing.Feedback;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ScrapYardKing.UI
{
    /// <summary>
    /// Tactile press on any button: sinks to 0.94 on touch, springs back with a small overshoot on release, plays the
    /// click and a light haptic. A non-interactable button gives a short "no" shake instead, so nothing ever looks
    /// pressable and stays silent.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIButtonFeel : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IPointerClickHandler
    {
        [Tooltip("What scales; defaults to this object. Use a unit-scale child on world-space canvases.")]
        [SerializeField] RectTransform target;
        [SerializeField] SfxDefinition clickSfx;
        [SerializeField] SfxDefinition deniedSfx;
        [SerializeField] bool haptic = true;

        Selectable selectable;
        bool pressed;

        void Awake()
        {
            if (target == null) target = (RectTransform)transform;
            selectable = GetComponent<Selectable>();
        }

        void OnDisable()
        {
            pressed = false;
            if (target != null) target.localScale = Vector3.one;
        }

        bool Interactable => selectable == null || selectable.IsInteractable();

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!Interactable) return;
            pressed = true;
            UIAnim.ButtonPress(target);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!pressed) return;
            pressed = false;
            UIAnim.ButtonRelease(target);
        }

        public void OnPointerExit(PointerEventData eventData) => OnPointerUp(eventData);

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!Interactable)
            {
                UIAnim.Shake(target, 10f);
                GameFeedback.Sfx(deniedSfx);
                return;
            }

            GameFeedback.Sfx(clickSfx);
            if (haptic) Haptics.Light();
        }
    }
}

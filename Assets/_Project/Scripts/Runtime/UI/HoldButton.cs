using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ScrapYardKing.UI
{
    /// <summary>
    /// A button that fires only after it is held for <see cref="holdSeconds"/>, with a fill showing how far along the
    /// hold is. For actions that cannot be undone (resetting progress): a stray tap does nothing.
    /// </summary>
    public sealed class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [SerializeField] Image fill;
        [SerializeField, Min(0.3f)] float holdSeconds = 2f;

        float heldFor;
        bool holding, fired;

        public event Action Completed;

        void OnEnable() => ResetHold();

        void ResetHold()
        {
            holding = fired = false;
            heldFor = 0f;
            if (fill != null) fill.fillAmount = 0f;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (fired) return;
            holding = true;
            UIAnim.ButtonPress(transform);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!holding) return;
            holding = false;
            UIAnim.ButtonRelease(transform);
        }

        public void OnPointerExit(PointerEventData eventData) => OnPointerUp(eventData);

        void Update()
        {
            if (fired) return;
            heldFor = holding ? heldFor + Time.unscaledDeltaTime : Mathf.Max(0f, heldFor - Time.unscaledDeltaTime * 3f);
            if (fill != null) fill.fillAmount = heldFor / holdSeconds;
            if (heldFor < holdSeconds) return;
            fired = true;
            holding = false;
            Completed?.Invoke();
        }
    }
}

using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ScrapYardKing.UI
{
    /// <summary>
    /// An ON/OFF switch from the kit: the knob slides across, the track swaps between the green and grey sprite and the
    /// word changes, so the state never rests on colour alone. Tap anywhere on the row to flip it.
    /// </summary>
    public sealed class UIToggle : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] Image track;
        [SerializeField] RectTransform knob;
        [SerializeField] TMP_Text stateLabel;
        [SerializeField] Sprite onSprite;
        [SerializeField] Sprite offSprite;
        [SerializeField] float knobTravel = 34f;

        bool value;
        Func<bool> read;
        Action<bool> write;

        public void Bind(Func<bool> getter, Action<bool> setter)
        {
            read = getter;
            write = setter;
            Refresh(false);
        }

        public void Refresh(bool animate)
        {
            if (read != null) value = read();
            if (track != null) track.sprite = value ? onSprite : offSprite;
            if (stateLabel != null) stateLabel.SetText(value ? "ON" : "OFF");
            if (knob == null) return;
            float x = value ? knobTravel : -knobTravel;
            knob.DOKill();
            if (animate) knob.DOAnchorPosX(x, 0.18f).SetEase(Ease.OutBack, 2f).SetUpdate(true).SetLink(knob.gameObject);
            else knob.anchoredPosition = new Vector2(x, knob.anchoredPosition.y);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            value = !value;
            write?.Invoke(value);
            Refresh(true);
            UIAnim.Punch(knob, 0.2f, 0.2f);
        }
    }
}

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ScrapYardKing.UI
{
    /// <summary>
    /// One card in the shop (hero offer, diamond bundle or a row). Only holds references and sets them; the
    /// <see cref="ShopPanel"/> decides what a card says and what its button does. Parts left empty are hidden.
    /// </summary>
    public sealed class ShopCard : MonoBehaviour
    {
        public enum ButtonLook
        {
            Buy,
            Diamonds,
            Watch,
            Waiting,
            Owned,
            Loading
        }

        [SerializeField] Image icon;
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text subtitle;
        [SerializeField] TMP_Text detail;
        [SerializeField] GameObject badge;
        [SerializeField] TMP_Text badgeText;
        [SerializeField] GameObject bonus;
        [SerializeField] TMP_Text bonusText;
        [SerializeField] TMP_Text total;
        [SerializeField] Button button;
        [SerializeField] Image buttonBack;
        [SerializeField] TMP_Text buttonLabel;
        [SerializeField] Image buttonIcon;

        [Header("Kit")]
        [SerializeField] Sprite green;
        [SerializeField] Sprite blue;
        [SerializeField] Sprite grey;
        [SerializeField] Sprite yellow;
        [SerializeField] Sprite gem;
        [SerializeField] Sprite video;

        Action onTap;

        public RectTransform Icon => icon != null ? icon.rectTransform : (RectTransform)transform;
        public RectTransform Button => button != null ? (RectTransform)button.transform : (RectTransform)transform;

        void Awake()
        {
            if (button != null) button.onClick.AddListener(() => onTap?.Invoke());
        }

        public ShopCard Set(Sprite picture, string heading, string sub = null, string more = null)
        {
            if (icon != null)
            {
                icon.gameObject.SetActive(picture != null);
                icon.sprite = picture;
            }

            Text(title, heading);
            Text(subtitle, sub);
            Text(detail, more);
            return this;
        }

        public ShopCard Ribbon(string text)
        {
            if (badge != null) badge.SetActive(!string.IsNullOrEmpty(text));
            Text(badgeText, text);
            return this;
        }

        public ShopCard Bonus(string text, string totalText)
        {
            if (bonus != null) bonus.SetActive(!string.IsNullOrEmpty(text));
            Text(bonusText, text);
            Text(total, totalText);
            return this;
        }

        public ShopCard OnTap(Action action)
        {
            onTap = action;
            return this;
        }

        /// <summary>Sets the button's face: a price, a diamond cost, WATCH, a countdown, OWNED or a busy "...".</summary>
        public void Face(ButtonLook look, string label)
        {
            if (buttonBack != null)
                buttonBack.sprite = look switch
                {
                    ButtonLook.Buy => green,
                    ButtonLook.Diamonds => blue,
                    ButtonLook.Watch => yellow,
                    _ => grey
                };
            if (buttonIcon != null)
            {
                var s = look == ButtonLook.Diamonds ? gem : look == ButtonLook.Watch ? video : null;
                buttonIcon.gameObject.SetActive(s != null);
                buttonIcon.sprite = s;
            }

            if (buttonLabel != null)
            {
                buttonLabel.SetText(label);
                var rt = buttonLabel.rectTransform;
                rt.offsetMin = new Vector2(buttonIcon != null && buttonIcon.gameObject.activeSelf ? 70f : 14f, rt.offsetMin.y);
            }

            if (button != null) button.interactable = look is ButtonLook.Buy or ButtonLook.Diamonds or ButtonLook.Watch;
        }

        static void Text(TMP_Text label, string text)
        {
            if (label == null) return;
            bool has = !string.IsNullOrEmpty(text);
            label.gameObject.SetActive(has);
            if (has) label.SetText(text);
        }
    }
}

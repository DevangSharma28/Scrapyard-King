using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ScrapYardKing.UI
{
    /// <summary>
    /// One mission line: icon, objective, progress bar with "32 / 50", the reward shown up front, and a button that is
    /// a green CLAIM when the goal is met, a grey chip with the reward while it is not, and DONE once taken.
    /// </summary>
    public sealed class MissionRow : MonoBehaviour
    {
        public enum Look
        {
            Working,
            Claim,
            Done,
            Info
        }

        [SerializeField] Image icon;
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text sub;
        [SerializeField] Image fill;
        [SerializeField] TMP_Text progress;
        [SerializeField] Button button;
        [SerializeField] Image buttonBack;
        [SerializeField] TMP_Text buttonLabel;
        [SerializeField] Image rewardIcon;
        [SerializeField] Sprite claimSprite;
        [SerializeField] Sprite waitSprite;
        [SerializeField] Sprite doneIcon;

        Action onClaim;
        Look shown = (Look)(-1);

        public RectTransform Icon => icon != null ? icon.rectTransform : (RectTransform)transform;

        void Awake()
        {
            if (button != null) button.onClick.AddListener(() => onClaim?.Invoke());
        }

        public void Bind(Sprite picture, string heading, string line, float progress01, string progressText,
            Sprite reward, string rewardText, Look look, Action claim)
        {
            onClaim = claim;
            if (icon != null) icon.sprite = picture;
            title.SetText(heading);
            if (sub != null)
            {
                sub.gameObject.SetActive(!string.IsNullOrEmpty(line));
                sub.SetText(line ?? "");
            }

            if (fill != null)
            {
                fill.transform.parent.gameObject.SetActive(look != Look.Info);
                fill.fillAmount = Mathf.Clamp01(progress01);
            }

            if (progress != null) progress.SetText(progressText ?? "");
            if (button != null) button.gameObject.SetActive(look != Look.Info);
            if (buttonBack != null) buttonBack.sprite = look == Look.Claim ? claimSprite : waitSprite;
            if (button != null) button.interactable = look == Look.Claim;
            if (buttonLabel != null) buttonLabel.SetText(look switch { Look.Claim => "CLAIM", Look.Done => "DONE", _ => rewardText });
            if (rewardIcon != null)
            {
                var s = look == Look.Done ? doneIcon : reward;
                rewardIcon.gameObject.SetActive(s != null && look != Look.Info);
                rewardIcon.sprite = s;
            }

            if (look != shown)
            {
                if (look == Look.Claim && button != null) UIAnim.Breathe(button.transform, 1.06f);
                else if (button != null)
                {
                    DG.Tweening.DOTween.Kill(button.transform);
                    button.transform.localScale = Vector3.one;
                }

                shown = look;
            }
        }
    }
}

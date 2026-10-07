using System;
using DG.Tweening;
using ScrapYardKing.Core;
using ScrapYardKing.Feedback;
using ScrapYardKing.Persistence;
using ScrapYardKing.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ScrapYardKing.UI
{
    /// <summary>
    /// Settings, which is also the pause screen: opening it from the gear stops the game and its sound, RESUME (or the
    /// X) carries on. Grouped cards: AUDIO, GAMEPLAY, GRAPHICS, ACCOUNT, SUPPORT, ABOUT and, set apart in red, the
    /// DANGER ZONE with RESET PROGRESS (a warning listing what goes, then a two-second hold). Rows whose target does not
    /// exist yet (no store to restore from, no support address filled in) are hidden instead of doing nothing.
    /// </summary>
    public sealed class SettingsPanel : MonoBehaviour
    {
        /// <summary>Set by the store once it exists: restores non-consumable purchases.</summary>
        public static Action RestorePurchases;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => RestorePurchases = null;

        [Header("Frame")]
        [SerializeField] Button openButton;
        [SerializeField] GameObject root;
        [SerializeField] Image dim;
        [SerializeField] RectTransform card;
        [SerializeField] CanvasGroup cardGroup;
        [SerializeField] Button closeButton;
        [SerializeField] Button resumeButton;
        [SerializeField] ScrollRect scroll;

        [Header("Audio")]
        [SerializeField] UIToggle sound;
        [SerializeField] Slider master;

        [Header("Gameplay")]
        [SerializeField] UIToggle vibration;
        [SerializeField] UIToggle cameraShake;
        [SerializeField] UIToggle popupNumbers;

        [Header("Graphics")]
        [SerializeField] Button qualityLow;
        [SerializeField] Button qualityHigh;
        [SerializeField] Sprite segmentOn;
        [SerializeField] Sprite segmentOff;
        [SerializeField] UIToggle highFrameRate;
        [SerializeField] UIToggle batterySaver;

        [Header("Account")]
        [SerializeField] TMP_Text saveStatus;
        [SerializeField] GameObject restoreRow;
        [SerializeField] Button restoreButton;

        [Header("Support / about")]
        [SerializeField] Button helpButton;
        [SerializeField] Button contactButton;
        [SerializeField] Button privacyButton;
        [SerializeField] Button termsButton;
        [SerializeField] Button creditsButton;
        [SerializeField] TMP_Text version;
        [SerializeField] string supportEmail;
        [SerializeField] string privacyUrl;
        [SerializeField] string termsUrl;
        [SerializeField, TextArea(4, 12)] string helpText;
        [SerializeField, TextArea(4, 12)] string creditsText;
        [SerializeField] Sprite helpIcon;

        [Header("Danger zone")]
        [SerializeField] Button resetButton;
        [SerializeField] Sprite warningIcon;

        [Header("Feel")]
        [SerializeField] SfxDefinition openSfx;
        [SerializeField] SfxDefinition closeSfx;

        float resumeScale = 1f;
        float lastSaveAt = -1f;
        SaveManager save;

        public bool IsOpen { get; private set; }

        void Awake()
        {
            if (root != null) root.SetActive(false);
            Hook(openButton, Open);
            Hook(closeButton, Close);
            Hook(resumeButton, Close);
            Hook(qualityLow, () => SetQuality(GraphicsQuality.Low));
            Hook(qualityHigh, () => SetQuality(GraphicsQuality.High));
            Hook(restoreButton, () => RestorePurchases?.Invoke());
            Hook(helpButton, () => Message("HOW TO PLAY", helpText, helpIcon));
            Hook(creditsButton, () => Message("CREDITS", creditsText, null));
            Hook(contactButton, () => Application.OpenURL("mailto:" + supportEmail));
            Hook(privacyButton, () => Application.OpenURL(privacyUrl));
            Hook(termsButton, () => Application.OpenURL(termsUrl));
            Hook(resetButton, AskReset);

            if (sound != null) sound.Bind(() => GameSettings.Sound, v => GameSettings.Sound = v);
            if (vibration != null) vibration.Bind(() => GameSettings.Vibration, v => GameSettings.Vibration = v);
            if (cameraShake != null) cameraShake.Bind(() => GameSettings.CameraShake, v => GameSettings.CameraShake = v);
            if (popupNumbers != null) popupNumbers.Bind(() => GameSettings.PopupNumbers, v => GameSettings.PopupNumbers = v);
            if (highFrameRate != null) highFrameRate.Bind(() => GameSettings.HighFrameRate, v => GameSettings.HighFrameRate = v);
            if (batterySaver != null) batterySaver.Bind(() => GameSettings.BatterySaver, v => GameSettings.BatterySaver = v);
            if (master != null)
            {
                master.SetValueWithoutNotify(GameSettings.MasterVolume);
                master.onValueChanged.AddListener(v => GameSettings.MasterVolume = v);
            }

            if (version != null) version.SetText("VERSION " + Application.version);
        }

        void Start()
        {
            if (Services.TryGet(out save)) save.Saved += OnSaved;
        }

        void OnDestroy()
        {
            if (save != null) save.Saved -= OnSaved;
            if (IsOpen) Unpause();
        }

        void OnSaved() => lastSaveAt = Time.unscaledTime;

        static void Hook(Button button, Action action)
        {
            if (button != null) button.onClick.AddListener(() => action());
        }

        public void Open()
        {
            if (IsOpen || root == null) return;
            IsOpen = true;
            resumeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            root.SetActive(true);
            Refresh();
            if (scroll != null)
            {
                // rows Refresh shows or hides change the content's height: lay it out first, or "top" is measured
                // against the old height and the panel opened scrolled down to the danger zone
                LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
                scroll.StopMovement();
                scroll.verticalNormalizedPosition = 1f;
            }

            if (dim != null)
            {
                var c = dim.color;
                c.a = 0f;
                dim.color = c;
                UIAnim.Dim(dim, 0.75f);
            }

            UIAnim.Show(card, cardGroup);
            GameFeedback.Sfx(openSfx);
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            Unpause();
            GameFeedback.Sfx(closeSfx);
            UIAnim.Dim(dim, 0f);
            UIAnim.Hide(card, cardGroup, () => root.SetActive(false));
        }

        void Unpause()
        {
            Time.timeScale = resumeScale;
            AudioListener.pause = false;
        }

        void Update()
        {
            if (!IsOpen) return;
            if (Time.timeScale != 0f) Time.timeScale = 0f;   // a hit-stop ending must not unpause the game
            RefreshSaveStatus();
        }

        void Refresh()
        {
            foreach (var t in new[] { sound, vibration, cameraShake, popupNumbers, highFrameRate, batterySaver })
                if (t != null) t.Refresh(false);
            if (master != null) master.SetValueWithoutNotify(GameSettings.MasterVolume);
            RefreshQuality();
            if (restoreRow != null) restoreRow.SetActive(RestorePurchases != null);
            Show(contactButton, !string.IsNullOrWhiteSpace(supportEmail));
            Show(privacyButton, !string.IsNullOrWhiteSpace(privacyUrl));
            Show(termsButton, !string.IsNullOrWhiteSpace(termsUrl));
            Show(helpButton, !string.IsNullOrWhiteSpace(helpText));
            Show(creditsButton, !string.IsNullOrWhiteSpace(creditsText));
            RefreshSaveStatus();
        }

        static void Show(Button button, bool visible)
        {
            if (button != null) button.gameObject.SetActive(visible);
        }

        void RefreshSaveStatus()
        {
            if (saveStatus == null) return;
            if (save == null) saveStatus.SetText("NOT SAVING");
            else if (lastSaveAt < 0f) saveStatus.SetText("SAVED ON THIS DEVICE");
            else
            {
                int ago = Mathf.Max(0, (int)(Time.unscaledTime - lastSaveAt));
                saveStatus.SetText(ago < 5 ? "SAVED JUST NOW" : $"SAVED {DiamondSpend.Duration(ago)} AGO");
            }
        }

        void SetQuality(GraphicsQuality quality)
        {
            GameSettings.Quality = quality;
            if (GameSettings.BatterySaver && quality == GraphicsQuality.High) GameSettings.BatterySaver = false;
            RefreshQuality();
            if (batterySaver != null) batterySaver.Refresh(true);
        }

        void RefreshQuality()
        {
            var q = GameSettings.EffectiveQuality;
            Segment(qualityLow, q == GraphicsQuality.Low);
            Segment(qualityHigh, q == GraphicsQuality.High);
        }

        void Segment(Button button, bool on)
        {
            if (button == null) return;
            if (button.targetGraphic is Image image) image.sprite = on ? segmentOn : segmentOff;
            if (on) UIAnim.Punch(button.transform, 0.1f, 0.2f);
        }

        void Message(string heading, string text, Sprite picture)
        {
            if (Services.TryGet(out PopupManager popups)) popups.Message(heading, text, picture);
        }

        void AskReset()
        {
            if (!Services.TryGet(out PopupManager popups)) return;
            popups.Show(new PopupRequest
            {
                Style = PopupStyle.Danger,
                Title = "RESET PROGRESS?",
                Subtitle = "THIS CANNOT BE UNDONE",
                Icon = warningIcon,
                Body = "This permanently deletes your cash, diamonds, upgrades, workers, opened areas, yard level and tasks. Settings stay.",
                PrimaryLabel = "HOLD TO RESET",
                Closable = true,
                OnPrimary = _ => ResetProgress()
            });
        }

        void ResetProgress()
        {
            Analytics.Log(AnalyticsEvents.ProgressReset);
            if (save != null) save.Wipe();
            Unpause();
            IsOpen = false;
            DOTween.KillAll();
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}

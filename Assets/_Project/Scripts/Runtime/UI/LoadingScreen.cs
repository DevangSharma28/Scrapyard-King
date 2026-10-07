using ScrapYardKing.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ScrapYardKing.UI
{
    /// <summary>
    /// The title card over the first moments of a session: background art, logo and a bar. It hides the frames in
    /// which pools warm up and the save is restored, then fades out by itself (no tap: nothing may wait on it).
    /// The cover sits on its own canvas above the HUD and blocks touches while it is up. It is inactive in the scene,
    /// so the Editor's Game view stays clear outside Play mode; this component switches it on in <c>Awake</c>.
    /// Runs on unscaled time: test time scales and hit-stop do not stretch it.
    /// </summary>
    [DefaultExecutionOrder(-1100)]
    public sealed class LoadingScreen : MonoBehaviour
    {
        [SerializeField] GameObject cover;
        [SerializeField] CanvasGroup group;
        [SerializeField] Image fill;
        [SerializeField] RectTransform logo;
        [SerializeField] TMP_Text percent;

        [Header("Timing (unscaled seconds)")]
        [SerializeField, Min(0f)] float minDuration = 1.8f;
        [SerializeField, Min(0.05f)] float fadeDuration = 0.4f;
        [Tooltip("Frames that must have rendered under the cover before it may leave.")]
        [SerializeField, Min(1)] int minFrames = 5;
        [Tooltip("The bar holds here until the wait is over, then runs to the end.")]
        [SerializeField, Range(0.5f, 1f)] float holdAt = 0.9f;

        [Header("Logo")]
        [SerializeField, Min(0.05f)] float logoPopDuration = 0.5f;
        [SerializeField, Range(0.3f, 1f)] float logoStartScale = 0.8f;

        float elapsed, fade, shown;
        int frames;

        /// <summary>True from <c>Awake</c> until the cover has faded out.</summary>
        public bool IsShowing { get; private set; }

        void Awake()
        {
            if (cover == null || group == null) { enabled = false; return; }
            cover.SetActive(true);
            group.alpha = 1f;
            group.blocksRaycasts = true;
            IsShowing = true;
            SetProgress(0f);
            if (logo != null) logo.localScale = Vector3.one * logoStartScale;
        }

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);   // a first-frame hitch must not eat the whole card
            elapsed += dt;
            frames++;

            if (logo != null)
            {
                float t = Mathf.Clamp01(elapsed / logoPopDuration);
                logo.localScale = Vector3.one * Mathf.LerpUnclamped(logoStartScale, 1f, Easing.OutBack(t));
            }

            bool ready = elapsed >= minDuration && frames >= minFrames;
            float target = ready ? 1f : holdAt * Easing.OutCubic(Mathf.Clamp01(elapsed / Mathf.Max(0.01f, minDuration)));
            shown = Mathf.MoveTowards(shown, target, dt * 2.5f);
            SetProgress(shown);
            if (!ready || shown < 1f) return;

            fade += dt / fadeDuration;
            group.alpha = 1f - Mathf.Clamp01(fade);
            if (fade < 1f) return;

            group.blocksRaycasts = false;
            cover.SetActive(false);
            IsShowing = false;
            enabled = false;
        }

        void SetProgress(float value)
        {
            if (fill != null) fill.fillAmount = value;
            if (percent != null) percent.SetText("{0}%", Mathf.RoundToInt(value * 100f));
        }
    }
}

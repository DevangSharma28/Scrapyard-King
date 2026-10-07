using ScrapYardKing.Core;
using ScrapYardKing.Feedback;
using UnityEngine;

namespace ScrapYardKing.Settings
{
    /// <summary>Pushes <see cref="GameSettings"/> into Unity (volume, quality level, frame rate) at start and on change.</summary>
    [DefaultExecutionOrder(-480)]
    public sealed class SettingsApplier : MonoBehaviour
    {
        [Tooltip("Quality level index per GraphicsQuality (Low, High) in Project Settings > Quality.")]
        [SerializeField] int[] qualityLevels = { 0, 1 };

        void OnEnable()
        {
            GameSettings.Changed += Apply;
            Apply();
        }

        void OnDisable() => GameSettings.Changed -= Apply;

        void Start() => Apply();   // the audio service registers in Awake; apply again once it surely exists

        void Apply()
        {
            AudioListener.volume = GameSettings.Sound ? GameSettings.MasterVolume : 0f;
            if (Services.TryGet(out AudioManager audio)) audio.SfxVolume = 1f;

            int quality = (int)GameSettings.EffectiveQuality;
            if (qualityLevels != null && quality < qualityLevels.Length)
            {
                int level = Mathf.Clamp(qualityLevels[quality], 0, QualitySettings.names.Length - 1);
                if (QualitySettings.GetQualityLevel() != level) QualitySettings.SetQualityLevel(level, true);
            }

            Application.targetFrameRate = GameSettings.TargetFrameRate;
        }
    }
}

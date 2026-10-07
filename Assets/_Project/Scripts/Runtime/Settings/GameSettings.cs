using System;
using ScrapYardKing.Core;
using UnityEngine;

namespace ScrapYardKing.Settings
{
    public enum GraphicsQuality
    {
        Low = 0,
        High = 1
    }

    /// <summary>
    /// The player's device settings: sound, vibration, camera shake, pop-up numbers, graphics and frame rate. Stored in
    /// PlayerPrefs, not in the save: resetting progress keeps them, and they are read before any scene system exists.
    /// <see cref="SettingsApplier"/> pushes them into Unity and the audio service; gameplay code reads the flags
    /// directly (<see cref="CameraShake"/>, <see cref="PopupNumbers"/>, <see cref="Vibration"/>).
    /// </summary>
    public static class GameSettings
    {
        const string Prefix = "syk.settings.";

        /// <summary>Raised after any setting changed.</summary>
        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Changed = null;

        public static bool Sound
        {
            get => GetBool("sound", true);
            set => SetBool("sound", value);
        }

        /// <summary>Overall volume, 0..1.</summary>
        public static float MasterVolume
        {
            get => PlayerPrefs.GetFloat(Prefix + "master", 1f);
            set => SetFloat("master", Mathf.Clamp01(value));
        }

        public static bool Vibration
        {
            get => GetBool("vibration", true);
            set => SetBool("vibration", value);
        }

        public static bool CameraShake
        {
            get => GetBool("shake", true);
            set => SetBool("shake", value);
        }

        /// <summary>Floating numbers in the world (+$, +XP, damage).</summary>
        public static bool PopupNumbers
        {
            get => GetBool("numbers", true);
            set => SetBool("numbers", value);
        }

        public static GraphicsQuality Quality
        {
            get => (GraphicsQuality)Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "quality", DefaultQuality), 0, 1);
            set => SetInt("quality", (int)value);
        }

        /// <summary>60 fps instead of 30.</summary>
        public static bool HighFrameRate
        {
            get => GetBool("fps60", true);
            set => SetBool("fps60", value);
        }

        /// <summary>Low graphics and 30 fps whatever the other two say.</summary>
        public static bool BatterySaver
        {
            get => GetBool("battery", false);
            set => SetBool("battery", value);
        }

        public static GraphicsQuality EffectiveQuality => BatterySaver ? GraphicsQuality.Low : Quality;
        public static int TargetFrameRate => BatterySaver || !HighFrameRate ? 30 : 60;

        static int DefaultQuality => Application.isMobilePlatform ? (int)GraphicsQuality.Low : (int)GraphicsQuality.High;

        static bool GetBool(string key, bool fallback) => PlayerPrefs.GetInt(Prefix + key, fallback ? 1 : 0) != 0;

        static void SetBool(string key, bool value) => SetInt(key, value ? 1 : 0);

        static void SetInt(string key, int value)
        {
            if (PlayerPrefs.HasKey(Prefix + key) && PlayerPrefs.GetInt(Prefix + key) == value) return;
            PlayerPrefs.SetInt(Prefix + key, value);
            Commit(key, value);
        }

        static void SetFloat(string key, float value)
        {
            if (Mathf.Approximately(PlayerPrefs.GetFloat(Prefix + key, -1f), value)) return;
            PlayerPrefs.SetFloat(Prefix + key, value);
            Commit(key, value);
        }

        static void Commit(string key, object value)
        {
            PlayerPrefs.Save();
            Analytics.Log(AnalyticsEvents.SettingsChanged, ("key", key), ("value", value));
            Changed?.Invoke();
        }
    }
}

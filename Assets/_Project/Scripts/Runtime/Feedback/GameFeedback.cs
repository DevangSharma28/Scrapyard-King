using ScrapYardKing.CameraSystem;
using ScrapYardKing.Core;
using UnityEngine;

namespace ScrapYardKing.Feedback
{
    /// <summary>
    /// One-line access to every feedback service. Each call silently no-ops when its service is absent,
    /// so prefabs keep working in test scenes without the full _Systems rig.
    /// </summary>
    public static class GameFeedback
    {
        public static FeedbackConfig Config =>
            Services.TryGet(out GameManager game) && game.Config != null ? game.Config.Feedback : null;

        public static void Sfx(SfxDefinition sfx, float pitchMultiplier = 1f, float volumeMultiplier = 1f)
        {
            if (sfx != null && Services.TryGet(out AudioManager audio)) audio.Play(sfx, pitchMultiplier, volumeMultiplier);
        }

        public static void Vfx(ParticleSystem prefab, Vector3 position, Quaternion rotation, float scale = 1f)
        {
            if (prefab != null && Services.TryGet(out VFXManager vfx)) vfx.Play(prefab, position, rotation, scale);
        }

        public static void HitStop(float duration, float timeScale)
        {
            if (Services.TryGet(out HitStopController hitStop)) hitStop.Trigger(duration, timeScale);
        }

        public static void CameraShake(float trauma)
        {
            if (trauma > 0f && Settings.GameSettings.CameraShake && Services.TryGet(out CameraController cam)) cam.Shake(trauma);
        }

        public static void CameraPunch(float strength)
        {
            if (strength > 0f && Settings.GameSettings.CameraShake && Services.TryGet(out CameraController cam)) cam.Punch(strength);
        }

        public static void Popup(string text, Vector3 position, Color color, float scale = 1f)
        {
            if (Settings.GameSettings.PopupNumbers && Services.TryGet(out FloatingTextManager popups)) popups.Show(text, position, color, scale);
        }

        /// <summary>Returns <paramref name="preferred"/> unless it is null/destroyed, then <paramref name="fallback"/>.</summary>
        public static T Pick<T>(T preferred, T fallback) where T : Object => preferred != null ? preferred : fallback;
    }
}

using System.Collections.Generic;
using ScrapYardKing.Core;
using UnityEngine;
using UnityEngine.Audio;

namespace ScrapYardKing.Feedback
{
    /// <summary>Pooled 2D one-shot player for <see cref="SfxDefinition"/>s. Oldest voice is stolen when all are busy.</summary>
    [DefaultExecutionOrder(-500)]
    public sealed class AudioManager : ServiceBehaviour<AudioManager>
    {
        [SerializeField, Range(4, 32)] int voiceCount = 16;
        [SerializeField, Range(0f, 1f)] float sfxVolume = 1f;
        [SerializeField] AudioMixerGroup sfxGroup;

        readonly Dictionary<SfxDefinition, float> lastPlayed = new();
        AudioSource[] voices;
        int nextVoice;

        public float SfxVolume
        {
            get => sfxVolume;
            set => sfxVolume = Mathf.Clamp01(value);
        }

        protected override void Awake()
        {
            base.Awake();
            voices = new AudioSource[voiceCount];
            for (int i = 0; i < voiceCount; i++)
            {
                var go = new GameObject($"Voice_{i:00}");
                go.transform.SetParent(transform, false);
                var source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.outputAudioMixerGroup = sfxGroup;
                voices[i] = source;
            }
        }

        public void Play(SfxDefinition sfx, float pitchMultiplier = 1f, float volumeMultiplier = 1f)
        {
            if (sfx == null || voices == null) return;

            float now = Time.unscaledTime;
            if (lastPlayed.TryGetValue(sfx, out float last) && now - last < sfx.MinInterval) return;

            var clip = sfx.PickClip();
            if (clip == null) clip = ProceduralSfx.Get(sfx.Fallback);
            if (clip == null) return;

            lastPlayed[sfx] = now;
            var voice = voices[nextVoice];
            nextVoice = (nextVoice + 1) % voices.Length;
            voice.clip = clip;
            voice.pitch = sfx.RandomPitch() * pitchMultiplier;
            voice.volume = sfx.Volume * volumeMultiplier * sfxVolume;
            voice.Play();
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace ScrapYardKing.Feedback
{
    /// <summary>
    /// Synthesises short placeholder sound effects so the harvest loop has audio feedback before real clips exist.
    /// Assigning clips on an <see cref="SfxDefinition"/> bypasses this entirely.
    /// </summary>
    public static class ProceduralSfx
    {
        const int SampleRate = 44100;
        static readonly Dictionary<ProceduralSfxPreset, AudioClip> Cache = new();

        public static AudioClip Get(ProceduralSfxPreset preset)
        {
            if (preset == ProceduralSfxPreset.None) return null;
            if (Cache.TryGetValue(preset, out var clip) && clip != null) return clip;
            clip = Build(preset);
            Cache[preset] = clip;
            return clip;
        }

        static AudioClip Build(ProceduralSfxPreset preset)
        {
            var rng = new System.Random((int)preset * 7919);
            float[] data = preset switch
            {
                ProceduralSfxPreset.MetalHit => MetalHit(rng),
                ProceduralSfxPreset.MetalBreak => MetalBreak(rng),
                ProceduralSfxPreset.Pop => Pop(),
                ProceduralSfxPreset.Thud => Thud(rng),
                ProceduralSfxPreset.Clunk => Clunk(rng),
                ProceduralSfxPreset.Whoosh => Whoosh(rng),
                ProceduralSfxPreset.Coin => Coin(),
                ProceduralSfxPreset.Upgrade => Arpeggio(new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.07f, 0.16f),
                ProceduralSfxPreset.Fanfare => Arpeggio(new[] { 392f, 523.25f, 659.25f, 783.99f, 1046.5f }, 0.09f, 0.35f),
                ProceduralSfxPreset.Denied => Denied(),
                _ => new float[1]
            };

            Normalize(data, 0.9f);
            var clip = AudioClip.Create($"Procedural_{preset}", data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float[] MetalHit(System.Random rng)
        {
            var d = Buffer(0.14f);
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float noise = Noise(rng);
                lp += (noise - lp) * 0.55f;
                float grit = (noise - lp) * Mathf.Exp(-t * 45f);
                float ring = (Mathf.Sin(Tau * 2350f * t) * 0.6f + Mathf.Sin(Tau * 3720f * t) * 0.4f) * Mathf.Exp(-t * 28f);
                d[i] = grit * 0.7f + ring * 0.35f;
            }
            return d;
        }

        static float[] MetalBreak(System.Random rng)
        {
            var d = Buffer(0.55f);
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float thumpFreq = Mathf.Lerp(95f, 45f, t / 0.3f);
                float thump = Mathf.Sin(Tau * thumpFreq * t) * Mathf.Exp(-t * 9f);
                lp += (Noise(rng) - lp) * 0.25f;
                float crash = lp * Mathf.Exp(-t * 7f);
                float partials = (Mathf.Sin(Tau * 870f * t) + Mathf.Sin(Tau * 1430f * t) * 0.7f + Mathf.Sin(Tau * 2290f * t) * 0.5f)
                                 * Mathf.Exp(-t * 11f);
                d[i] = thump * 0.9f + crash * 1.1f + partials * 0.18f;
            }
            return d;
        }

        static float[] Pop()
        {
            var d = Buffer(0.07f);
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float freq = Mathf.Lerp(620f, 1250f, t / 0.07f);
                phase += Tau * freq / SampleRate;
                d[i] = Mathf.Sin(phase) * Mathf.Exp(-t * 38f) * Mathf.Clamp01(t * 900f);
            }
            return d;
        }

        static float[] Thud(System.Random rng)
        {
            var d = Buffer(0.2f);
            float lp = 0f, phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                phase += Tau * Mathf.Lerp(120f, 55f, t / 0.2f) / SampleRate;
                lp += (Noise(rng) - lp) * 0.08f;
                d[i] = (Mathf.Sin(phase) + lp * 1.5f) * Mathf.Exp(-t * 16f);
            }
            return d;
        }

        static float[] Clunk(System.Random rng)
        {
            var d = Buffer(0.22f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float tone = Mathf.Sin(Tau * 310f * t) * 0.7f + Mathf.Sin(Tau * 523f * t) * 0.5f;
                float click = Noise(rng) * Mathf.Exp(-t * 120f);
                d[i] = tone * Mathf.Exp(-t * 18f) + click * 0.6f;
            }
            return d;
        }

        static float[] Whoosh(System.Random rng)
        {
            var d = Buffer(0.3f);
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.3f));
                lp += (Noise(rng) - lp) * Mathf.Lerp(0.05f, 0.3f, env);
                d[i] = lp * env;
            }
            return d;
        }

        static float[] Coin()
        {
            var d = Buffer(0.22f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float first = Mathf.Sin(Tau * 1975f * t) * Mathf.Exp(-t * 22f);
                float second = t > 0.045f ? Mathf.Sin(Tau * 2637f * (t - 0.045f)) * Mathf.Exp(-(t - 0.045f) * 16f) : 0f;
                d[i] = (first * 0.6f + second * 0.8f) * Mathf.Clamp01(t * 2000f);
            }
            return d;
        }

        /// <summary>Bright stepped notes with a short tail each; reads as "reward".</summary>
        static float[] Arpeggio(float[] notes, float step, float tail)
        {
            var d = Buffer(step * notes.Length + tail);
            for (int n = 0; n < notes.Length; n++)
            {
                int start = Mathf.RoundToInt(n * step * SampleRate);
                for (int i = start; i < d.Length; i++)
                {
                    float t = (i - start) / (float)SampleRate;
                    float env = Mathf.Exp(-t * 9f) * Mathf.Clamp01(t * 800f);
                    d[i] += (Mathf.Sin(Tau * notes[n] * t) * 0.7f + Mathf.Sin(Tau * notes[n] * 2f * t) * 0.2f) * env;
                }
            }
            return d;
        }

        static float[] Denied()
        {
            var d = Buffer(0.2f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SampleRate;
                float square = Mathf.Sign(Mathf.Sin(Tau * 140f * t));
                d[i] = square * 0.5f * Mathf.Exp(-t * 10f) * Mathf.Clamp01(t * 600f);
            }
            return d;
        }

        const float Tau = Mathf.PI * 2f;

        static float[] Buffer(float seconds) => new float[Mathf.CeilToInt(seconds * SampleRate)];

        static float Noise(System.Random rng) => (float)(rng.NextDouble() * 2.0 - 1.0);

        static void Normalize(float[] data, float peak)
        {
            float max = 0f;
            foreach (float s in data) max = Mathf.Max(max, Mathf.Abs(s));
            if (max <= 0f) return;
            float gain = peak / max;
            for (int i = 0; i < data.Length; i++) data[i] *= gain;
        }
    }
}

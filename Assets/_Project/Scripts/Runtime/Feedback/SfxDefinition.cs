using UnityEngine;

namespace ScrapYardKing.Feedback
{
    public enum ProceduralSfxPreset
    {
        None,
        MetalHit,
        MetalBreak,
        Pop,
        Thud,
        Clunk,
        Whoosh,
        Coin,
        Upgrade,
        Fanfare,
        Denied,
        /// <summary>Seamless 1 s two-stroke engine + chain whine loop (chainsaw motor).</summary>
        EngineLoop
    }

    /// <summary>A playable sound: clip variations, volume, pitch jitter and a rate limit.</summary>
    [CreateAssetMenu(fileName = "Sfx_", menuName = "Scrap Yard King/Feedback/Sfx Definition")]
    public sealed class SfxDefinition : ScriptableObject
    {
        [SerializeField] AudioClip[] clips;
        [SerializeField, Range(0f, 1f)] float volume = 0.8f;
        [SerializeField] Vector2 pitchRange = new(0.95f, 1.05f);
        [Tooltip("Minimum seconds between two plays of this sound. Prevents stacking when many things fire in one frame.")]
        [SerializeField, Min(0)] float minInterval = 0.03f;
        [Tooltip("Synthesised at runtime when no clips are assigned, so feedback works before final audio lands.")]
        [SerializeField] ProceduralSfxPreset fallback = ProceduralSfxPreset.None;

        public float Volume => volume;
        public float MinInterval => minInterval;
        public ProceduralSfxPreset Fallback => fallback;

        public AudioClip PickClip()
        {
            if (clips == null || clips.Length == 0) return null;
            return clips[Random.Range(0, clips.Length)];
        }

        public float RandomPitch() => Random.Range(pitchRange.x, pitchRange.y);

        void OnValidate()
        {
            pitchRange.x = Mathf.Max(0.1f, pitchRange.x);
            pitchRange.y = Mathf.Max(pitchRange.x, pitchRange.y);
        }
    }
}

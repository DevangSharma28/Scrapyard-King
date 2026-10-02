using ScrapYardKing.Core;
using ScrapYardKing.Feedback;
using ScrapYardKing.Harvest;
using UnityEngine;

namespace ScrapYardKing.Player
{
    /// <summary>
    /// The chainsaw motor: a looping engine sound that revs up while cutting, bogs down briefly on every hit (the chain
    /// biting into metal) and fades out when the saw is idle. Uses unscaled time so hit-stop never stutters the motor.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ToolAudio : MonoBehaviour
    {
        [SerializeField] HarvestTool harvestTool;
        [Tooltip("Looping engine clip; the procedural fallback is used when it has no clips.")]
        [SerializeField] SfxDefinition engine;
        [SerializeField, Range(0f, 1f)] float cuttingVolume = 0.32f;
        [SerializeField, Min(0.1f)] float cuttingPitch = 1.15f;
        [SerializeField, Min(0.1f)] float spinDownPitch = 0.7f;
        [Tooltip("Pitch drop per hit; recovers within a few frames.")]
        [SerializeField, Range(0f, 0.5f)] float hitBog = 0.12f;
        [SerializeField, Min(0.1f)] float revUp = 9f;
        [SerializeField, Min(0.1f)] float spinDown = 5f;

        AudioSource source;
        AudioManager audioManager;
        float volume, pitch = 1f, bog;

        void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.volume = 0f;
            if (engine != null) source.clip = engine.PickClip() ?? ProceduralSfx.Get(engine.Fallback);
        }

        void OnEnable()
        {
            if (harvestTool != null) harvestTool.Hit += OnHit;
        }

        void OnDisable()
        {
            if (harvestTool != null) harvestTool.Hit -= OnHit;
            if (source != null) source.Stop();
        }

        void Start() => Services.TryGet(out audioManager);

        void OnHit(ScrapObject target, ScrapHit hit) => bog = hitBog;

        void Update()
        {
            if (source == null || source.clip == null || harvestTool == null) return;
            float dt = Time.unscaledDeltaTime;
            bool cutting = harvestTool.IsCutting;

            float targetVolume = cutting ? cuttingVolume : 0f;
            volume = Mathf.MoveTowards(volume, targetVolume, (cutting ? revUp : spinDown) * 0.25f * dt);
            pitch = Mathf.Lerp(pitch, cutting ? cuttingPitch : spinDownPitch, Easing.Damp(cutting ? revUp : spinDown, dt));
            bog = Mathf.MoveTowards(bog, 0f, dt * 1.6f);

            float master = audioManager != null ? audioManager.SfxVolume : 1f;
            source.volume = volume * (engine != null ? engine.Volume : 1f) * master;
            source.pitch = Mathf.Max(0.1f, pitch - bog);

            if (volume > 0.001f && !source.isPlaying) source.Play();
            else if (volume <= 0.001f && source.isPlaying) source.Stop();
        }
    }
}

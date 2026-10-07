using DG.Tweening;
using UnityEngine;

namespace ScrapYardKing.Factory
{
    /// <summary>
    /// Makes a machine read as alive: body rumble and spinning cogs while working, piston stomp per cycle,
    /// a pop when output drops, and a status light (green working / amber idle / red blocked). In Active Overdrive
    /// everything runs hotter: faster spin, harder rumble, sparks and a pulsing cyan light. Hot machines (Furnace) also
    /// get a glowing mouth that breathes while working and flares on every output.
    /// </summary>
    public sealed class MachineVisuals : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] Transform body;
        [SerializeField] Transform[] spinners;
        [SerializeField] Vector3 spinAxis = Vector3.forward;
        [SerializeField] float spinSpeed = 420f;
        [Tooltip("Share of spin speed while idle (motor ticking over). A blocked machine stops dead, so a jam reads at a glance.")]
        [SerializeField, Range(0f, 1f)] float idleSpin = 0.06f;
        [SerializeField] Transform[] pistons;
        [SerializeField] float pistonTravel = 0.25f;
        [SerializeField] ParticleSystem workParticles;
        [SerializeField] Renderer statusLight;
        [SerializeField] Color workingColor = new(0.3f, 1f, 0.35f);
        [SerializeField] Color idleColor = new(1f, 0.75f, 0.15f);
        [SerializeField] Color blockedColor = new(1f, 0.25f, 0.2f);

        [Header("Heat glow (optional)")]
        [SerializeField] Renderer glow;
        [SerializeField, ColorUsage(false, true)] Color glowColor = new(1f, 0.45f, 0.08f);
        [SerializeField, Min(0f)] float glowIdle = 0.6f;
        [SerializeField, Min(0f)] float glowWorking = 3.2f;
        [SerializeField, Min(0f)] float glowFlare = 6f;
        [Tooltip("One-shot burst per output (sparks from the furnace mouth).")]
        [SerializeField] ParticleSystem outputBurst;

        [Header("Overdrive")]
        [SerializeField] ParticleSystem overdriveParticles;
        [SerializeField] Color overdriveColor = new(0.3f, 0.9f, 1f);
        [SerializeField, Min(1f)] float overdriveSpin = 2.4f;
        [SerializeField, Min(1f)] float overdriveRumble = 1.8f;

        MaterialPropertyBlock block;
        Vector3 bodyScale, bodyPosition;
        Vector3[] pistonRest;
        Tween rumble;
        float spinRate, heat, flare;
        bool overdrive;
        Machine.MachineState state;

        public bool IsOverdriven => overdrive;

        void Awake()
        {
            if (body != null)
            {
                bodyScale = body.localScale;
                bodyPosition = body.localPosition;
            }

            pistonRest = new Vector3[pistons != null ? pistons.Length : 0];
            for (int i = 0; i < pistonRest.Length; i++) pistonRest[i] = pistons[i].localPosition;
            SetState(Machine.MachineState.Idle);
        }

        void OnDestroy() => rumble?.Kill();

        public void SetState(Machine.MachineState newState)
        {
            state = newState;
            ApplyLight();

            bool working = state == Machine.MachineState.Working;
            if (workParticles != null)
            {
                if (working && !workParticles.isPlaying) workParticles.Play();
                else if (!working && workParticles.isPlaying) workParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            if (body == null) return;
            if (working && (rumble == null || !rumble.IsActive())) StartRumble();
            else if (!working && rumble != null) StopRumble();
        }

        /// <summary>Active Overdrive look on/off.</summary>
        public void SetOverdrive(bool on)
        {
            if (overdrive == on) return;
            overdrive = on;
            if (overdriveParticles != null)
            {
                if (on) overdriveParticles.Play();
                else overdriveParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            if (rumble != null)
            {
                StopRumble();
                if (state == Machine.MachineState.Working) StartRumble();
            }

            if (body != null && on)
            {
                body.DOKill(true);
                body.localScale = bodyScale;
                body.DOPunchScale(Vector3.one * 0.14f, 0.4f, 7, 0.6f);
            }

            ApplyLight();
        }

        void StartRumble()
        {
            float k = overdrive ? overdriveRumble : 1f;
            rumble = body.DOShakePosition(0.4f, new Vector3(0.025f, 0.015f, 0.025f) * k, 25, 90f, false, false).SetLoops(-1, LoopType.Restart);
        }

        void StopRumble()
        {
            rumble?.Kill();
            rumble = null;
            if (body != null) body.localPosition = bodyPosition;
        }

        public void PlayCycle()
        {
            if (pistons == null) return;
            for (int i = 0; i < pistons.Length; i++)
            {
                var p = pistons[i];
                p.DOKill();
                p.localPosition = pistonRest[i];
                DOTween.Sequence()
                    .Append(p.DOLocalMoveY(pistonRest[i].y - pistonTravel, 0.08f).SetEase(Ease.InQuad))
                    .Append(p.DOLocalMoveY(pistonRest[i].y, 0.22f).SetEase(Ease.OutBack))
                    .SetTarget(p);
            }
        }

        public void PlayOutput()
        {
            if (body == null) return;
            body.DOKill(true);
            body.localScale = bodyScale;
            body.DOPunchScale(new Vector3(0.06f, -0.08f, 0.06f), 0.25f, 5, 0.5f);
            flare = 1f;
            if (outputBurst != null) outputBurst.Play(true);
            if (state == Machine.MachineState.Working) SetState(state);
        }

        public void PlayUpgrade()
        {
            if (body == null) return;
            body.DOKill(true);
            body.localScale = bodyScale;
            body.DOPunchScale(Vector3.one * 0.18f, 0.5f, 6, 0.6f);
        }

        void Update()
        {
            if (overdrive) PulseLight();
            UpdateGlow();
            float target = state switch
            {
                Machine.MachineState.Working => spinSpeed * (overdrive ? overdriveSpin : 1f),
                Machine.MachineState.Idle => spinSpeed * idleSpin,
                _ => 0f
            };
            spinRate = Mathf.Lerp(spinRate, target, 1f - Mathf.Exp(-6f * Time.deltaTime));
            if (spinners == null || Mathf.Abs(spinRate) < 0.5f) return;
            foreach (var s in spinners)
                if (s != null) s.Rotate(spinAxis, spinRate * Time.deltaTime, Space.Self);
        }

        void UpdateGlow()
        {
            if (glow == null) return;
            float target = state == Machine.MachineState.Working ? glowWorking * (overdrive ? 1.4f : 1f) : glowIdle;
            heat = Mathf.Lerp(heat, target, 1f - Mathf.Exp(-4f * Time.deltaTime));
            flare = Mathf.MoveTowards(flare, 0f, Time.deltaTime * 2.5f);
            // A slow breath plus a fast flicker reads as fire without a particle per frame.
            float flicker = 1f + Mathf.Sin(Time.time * 2.3f) * 0.12f + Mathf.Sin(Time.time * 17f) * 0.05f;
            float intensity = heat * flicker + flare * flare * glowFlare;
            block ??= new MaterialPropertyBlock();
            glow.GetPropertyBlock(block);
            block.SetColor(EmissionColorId, glowColor * intensity);
            glow.SetPropertyBlock(block);
        }

        void ApplyLight()
        {
            if (statusLight == null) return;
            var color = state switch
            {
                Machine.MachineState.Blocked => blockedColor,
                _ when overdrive => overdriveColor,
                Machine.MachineState.Working => workingColor,
                _ => idleColor
            };

            SetLight(color, 2f);
        }

        void PulseLight()
        {
            if (statusLight == null || state == Machine.MachineState.Blocked) return;
            SetLight(overdriveColor, 1.6f + Mathf.Sin(Time.time * 14f) * 1.2f);
        }

        void SetLight(Color color, float emission)
        {
            block ??= new MaterialPropertyBlock();
            statusLight.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            block.SetColor(EmissionColorId, color * emission);
            statusLight.SetPropertyBlock(block);
        }
    }
}

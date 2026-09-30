using DG.Tweening;
using UnityEngine;

namespace ScrapYardKing.Factory
{
    /// <summary>
    /// Makes a machine read as alive: body rumble and spinning cogs while working, piston stomp per cycle,
    /// a pop when output drops, and a status light (green working / amber idle / red blocked).
    /// </summary>
    public sealed class MachineVisuals : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] Transform body;
        [SerializeField] Transform[] spinners;
        [SerializeField] Vector3 spinAxis = Vector3.forward;
        [SerializeField] float spinSpeed = 420f;
        [SerializeField] Transform[] pistons;
        [SerializeField] float pistonTravel = 0.25f;
        [SerializeField] ParticleSystem workParticles;
        [SerializeField] Renderer statusLight;
        [SerializeField] Color workingColor = new(0.3f, 1f, 0.35f);
        [SerializeField] Color idleColor = new(1f, 0.75f, 0.15f);
        [SerializeField] Color blockedColor = new(1f, 0.25f, 0.2f);

        MaterialPropertyBlock block;
        Vector3 bodyScale, bodyPosition;
        Vector3[] pistonRest;
        Tween rumble;
        float spinRate;
        Machine.MachineState state;

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
            if (working && (rumble == null || !rumble.IsActive()))
            {
                rumble = body.DOShakePosition(0.4f, new Vector3(0.025f, 0.015f, 0.025f), 25, 90f, false, false)
                    .SetLoops(-1, LoopType.Restart);
            }
            else if (!working && rumble != null)
            {
                rumble.Kill();
                rumble = null;
                body.localPosition = bodyPosition;
            }
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
            float target = state == Machine.MachineState.Working ? spinSpeed : 0f;
            spinRate = Mathf.Lerp(spinRate, target, 1f - Mathf.Exp(-6f * Time.deltaTime));
            if (spinners == null || Mathf.Abs(spinRate) < 0.5f) return;
            foreach (var s in spinners)
                if (s != null) s.Rotate(spinAxis, spinRate * Time.deltaTime, Space.Self);
        }

        void ApplyLight()
        {
            if (statusLight == null) return;
            var color = state switch
            {
                Machine.MachineState.Working => workingColor,
                Machine.MachineState.Blocked => blockedColor,
                _ => idleColor
            };

            block ??= new MaterialPropertyBlock();
            statusLight.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            block.SetColor(EmissionColorId, color * 2f);
            statusLight.SetPropertyBlock(block);
        }
    }
}

using DG.Tweening;
using ScrapYardKing.Core;
using ScrapYardKing.Factory;
using ScrapYardKing.Feedback;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ScrapYardKing.Tiles
{
    /// <summary>
    /// Boost pad next to a machine. Standing on it fills a gauge; a full gauge puts the machine into Active Overdrive
    /// (Speed multiplier, hotter visuals) for a while, then the pad cools down. Rewards hands-on play without making
    /// it mandatory.
    /// </summary>
    public sealed class OverdriveTile : Tile
    {
        public enum Phase
        {
            Ready,
            Charging,
            Active,
            Cooldown
        }

        [SerializeField] Machine machine;
        [Tooltip("More machines the same pad boosts (a furnace battery). Machines that are not built yet are skipped.")]
        [SerializeField] Machine[] alsoBoosts;
        [SerializeField] OverdriveConfig config;

        [Header("Display")]
        [SerializeField] RectTransform displayRoot;
        [SerializeField] Image gauge;
        [SerializeField] Image glow;
        [SerializeField] TMP_Text label;
        [SerializeField] Color chargeColor = new(1f, 0.8f, 0.2f);
        [SerializeField] Color activeColor = new(0.3f, 0.9f, 1f);
        [SerializeField] Color cooldownColor = new(0.55f, 0.58f, 0.66f);
        [SerializeField, Min(0.05f)] float chargeTickInterval = 0.18f;

        float charge, phaseEnds, nextTick;
        int ticks;

        public Phase Current { get; private set; }
        public Machine Machine => machine;

        protected override void Start()
        {
            base.Start();
            SetPhase(Phase.Ready);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (Current == Phase.Active) EndOverdrive();
        }

        void OnDestroy()
        {
            if (displayRoot != null) displayRoot.DOKill();
        }

        protected override void OnStay(float deltaTime)
        {
            if (config == null || machine == null) return;
            if (Current != Phase.Ready && Current != Phase.Charging) return;

            if (Current == Phase.Ready) SetPhase(Phase.Charging);
            charge = Mathf.Min(1f, charge + deltaTime / config.ChargeTime);
            if (Time.time >= nextTick)
            {
                nextTick = Time.time + chargeTickInterval;
                GameFeedback.Sfx(config.ChargeSfx, 1f + ticks++ * 0.08f);
            }

            if (charge >= 1f) StartOverdrive();
        }

        protected override void Update()
        {
            base.Update();
            if (config == null || machine == null) return;

            switch (Current)
            {
                case Phase.Charging when !IsEngaged:
                    charge = Mathf.MoveTowards(charge, 0f, Time.deltaTime * 1.5f);
                    if (charge <= 0f) SetPhase(Phase.Ready);
                    break;
                case Phase.Active:
                    if (Time.time >= phaseEnds) EndOverdrive();
                    break;
                case Phase.Cooldown:
                    if (Time.time >= phaseEnds) SetPhase(Phase.Ready);
                    break;
            }

            RefreshDisplay();
        }

        void StartOverdrive()
        {
            charge = 0f;
            machine.Speed.AddModifier(new StatModifier(this, StatModifierType.Multiply, config.SpeedMultiplier));
            if (machine.Visuals != null) machine.Visuals.SetOverdrive(true);
            if (alsoBoosts != null)
                foreach (var m in alsoBoosts)
                {
                    if (m == null || !m.isActiveAndEnabled) continue;
                    m.Speed.AddModifier(new StatModifier(this, StatModifierType.Multiply, config.SpeedMultiplier));
                    if (m.Visuals != null) m.Visuals.SetOverdrive(true);
                }
            phaseEnds = Time.time + config.Duration;
            SetPhase(Phase.Active);

            GameFeedback.Sfx(config.StartSfx);
            GameFeedback.CameraPunch(0.7f);
            GameFeedback.CameraShake(0.25f);
            var feedback = GameFeedback.Config;
            GameFeedback.Popup($"OVERDRIVE x{config.SpeedMultiplier:0.#}!", machine.transform.position + Vector3.up * 3.2f,
                feedback != null ? feedback.PositivePopupColor : Color.cyan, 1.3f);
            if (displayRoot != null)
            {
                displayRoot.DOKill(true);
                displayRoot.DOPunchScale(Vector3.one * 0.3f, 0.45f, 7, 0.6f);
            }

            GameEvents.RaiseMachineOverdrive(machine.StationId);
        }

        void EndOverdrive()
        {
            machine.Speed.RemoveModifiersFrom(this);
            if (machine.Visuals != null) machine.Visuals.SetOverdrive(false);
            if (alsoBoosts != null)
                foreach (var m in alsoBoosts)
                {
                    if (m == null) continue;
                    m.Speed.RemoveModifiersFrom(this);
                    if (m.Visuals != null) m.Visuals.SetOverdrive(false);
                }
            if (config != null) GameFeedback.Sfx(config.EndSfx);
            phaseEnds = Time.time + (config != null ? config.Cooldown : 0f);
            SetPhase(Phase.Cooldown);
        }

        void SetPhase(Phase phase)
        {
            Current = phase;
            ticks = 0;
            RefreshDisplay();
        }

        void RefreshDisplay()
        {
            if (config == null) return;
            float fill;
            Color color;
            string text;
            switch (Current)
            {
                case Phase.Active:
                    float left = Mathf.Max(0f, phaseEnds - Time.time);
                    fill = left / config.Duration;
                    color = activeColor;
                    text = $"x{config.SpeedMultiplier:0.#}  {Mathf.CeilToInt(left)}s";
                    break;
                case Phase.Cooldown:
                    fill = config.Cooldown > 0f ? 1f - Mathf.Max(0f, phaseEnds - Time.time) / config.Cooldown : 1f;
                    color = cooldownColor;
                    text = "COOLING";
                    break;
                default:
                    fill = charge;
                    color = chargeColor;
                    text = Current == Phase.Charging ? "HOLD..." : "BOOST";
                    break;
            }

            if (gauge != null)
            {
                gauge.fillAmount = fill;
                gauge.color = color;
            }

            if (glow != null)
            {
                var c = color;
                c.a = Current == Phase.Active ? 0.45f + Mathf.Sin(Time.time * 10f) * 0.2f : 0.18f;
                glow.color = c;
            }

            if (label != null && label.text != text) label.text = text;
        }
    }
}

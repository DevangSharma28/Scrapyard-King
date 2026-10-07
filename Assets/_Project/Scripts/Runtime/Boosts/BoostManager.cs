using System;
using System.Collections.Generic;
using ScrapYardKing.Core;
using ScrapYardKing.Factory;
using ScrapYardKing.Feedback;
using ScrapYardKing.Player;
using UnityEngine;

namespace ScrapYardKing.Boosts
{
    /// <summary>
    /// Timed boosts. A boost is data (<see cref="BoostDefinition"/>); this service keeps the clock and applies it.
    /// Machine speed and the player's walk are ordinary <see cref="StatModifier"/>s on their stats, re-applied once a
    /// second so machines built during a boost get it too. Cash, scrap respawn and truck tempo have no stat to modify:
    /// their owners ask <see cref="Multiplier"/>. One boost per kind runs at a time; activating again adds time.
    /// Remaining time is saved and continues the next session (it does not run while the game is closed).
    /// </summary>
    [DefaultExecutionOrder(-440)]
    public sealed class BoostManager : ServiceBehaviour<BoostManager>, ISaveable
    {
        public sealed class ActiveBoost
        {
            public BoostDefinition Definition;
            public float Remaining;
        }

        [Serializable]
        sealed class State
        {
            public List<string> ids = new();
            public List<float> remaining = new();
        }

        [Tooltip("Every boost in the game, so saved boosts can be found again by id.")]
        [SerializeField] BoostDefinition[] known;

        readonly List<ActiveBoost> active = new();
        float nextApply;

        /// <summary>Running boosts, oldest first. The HUD reads this every frame.</summary>
        public IReadOnlyList<ActiveBoost> Active => active;

        /// <summary>Raised when a boost starts, gets more time or ends.</summary>
        public event Action<BoostDefinition, bool> Changed;

        void Start() => SaveRegistry.Register(this);

        protected override void OnDestroy()
        {
            base.OnDestroy();
            SaveRegistry.Unregister(this);
            RemoveModifiers();
        }

        /// <summary>The multiplier a running boost of this kind gives, or 1.</summary>
        public float Multiplier(BoostKind kind)
        {
            foreach (var b in active)
                if (b.Definition.Kind == kind) return b.Definition.Multiplier;
            return 1f;
        }

        public bool IsActive(BoostKind kind) => Multiplier(kind) > 1.0001f;

        /// <summary>Starts a boost, or adds its duration to the one of the same kind that is running.</summary>
        public void Activate(BoostDefinition definition, bool fanfare = true) =>
            Activate(definition, definition != null ? definition.Duration : 0f, fanfare);

        /// <summary>
        /// Starts a boost for <paramref name="seconds"/> (a shop boost lasts longer than the definition's own run), or
        /// adds them to the one of the same kind that is running. The stack cap never cuts a bought duration short.
        /// </summary>
        public void Activate(BoostDefinition definition, float seconds, bool fanfare = true)
        {
            if (definition == null || seconds <= 0f) return;
            var running = active.Find(b => b.Definition.Kind == definition.Kind);
            float cap = Mathf.Max(definition.MaxSeconds, seconds);
            if (running != null)
            {
                running.Definition = definition;
                running.Remaining = Mathf.Min(running.Remaining + seconds, Mathf.Max(cap, running.Remaining));
            }
            else active.Add(new ActiveBoost { Definition = definition, Remaining = seconds });

            Analytics.Log(AnalyticsEvents.BoostActivated, ("boost", definition.Id), ("seconds", seconds));

            Apply();
            if (fanfare)
            {
                GameFeedback.Sfx(definition.StartSfx);
                GameFeedback.CameraPunch(0.5f);
                if (Services.TryGet(out PlayerCharacter player))
                    GameFeedback.Popup(definition.DisplayName + "!", player.transform.position + Vector3.up * 2.6f, definition.Color, 1.4f);
            }

            Changed?.Invoke(definition, true);
        }

        void Update()
        {
            if (active.Count == 0) return;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                active[i].Remaining -= Time.deltaTime;
                if (active[i].Remaining > 0f) continue;
                var ended = active[i].Definition;
                active.RemoveAt(i);
                Apply();
                GameFeedback.Sfx(ended.EndSfx);
                Changed?.Invoke(ended, false);
            }

            if (Time.time >= nextApply) Apply();
        }

        /// <summary>Puts the stat modifiers where they belong (and takes them away from where they no longer do).</summary>
        void Apply()
        {
            nextApply = Time.time + 1f;
            RemoveModifiers();
            float production = Multiplier(BoostKind.Production);
            if (production > 1.0001f)
                foreach (var station in StationRegistry.All)
                    if (station is Machine machine) machine.Speed.AddModifier(new StatModifier(this, StatModifierType.Multiply, production));
            float move = Multiplier(BoostKind.MoveSpeed);
            if (move > 1.0001f && Services.TryGet(out PlayerCharacter player))
                player.Stats.Get(PlayerStat.MoveSpeed).AddModifier(new StatModifier(this, StatModifierType.Multiply, move));
        }

        void RemoveModifiers()
        {
            foreach (var station in StationRegistry.All)
                if (station is Machine machine) machine.Speed.RemoveModifiersFrom(this);
            if (Services.TryGet(out PlayerCharacter player)) player.Stats.Get(PlayerStat.MoveSpeed).RemoveModifiersFrom(this);
        }

        string ISaveable.SaveKey => "boosts";

        string ISaveable.CaptureState()
        {
            var state = new State();
            foreach (var b in active)
            {
                state.ids.Add(b.Definition.Id);
                state.remaining.Add(b.Remaining);
            }

            return JsonUtility.ToJson(state);
        }

        void ISaveable.RestoreState(string json)
        {
            var state = JsonUtility.FromJson<State>(json);
            active.Clear();
            if (state?.ids == null || known == null) return;
            for (int i = 0; i < state.ids.Count && i < state.remaining.Count; i++)
            {
                var def = Array.Find(known, d => d != null && d.Id == state.ids[i]);
                if (def != null && state.remaining[i] > 1f) active.Add(new ActiveBoost { Definition = def, Remaining = state.remaining[i] });
            }

            Apply();
        }
    }
}

using System;
using System.Collections.Generic;
using DG.Tweening;
using ScrapYardKing.Core;
using ScrapYardKing.Feedback;
using ScrapYardKing.Items;
using ScrapYardKing.UI;
using UnityEngine;

namespace ScrapYardKing.Factory
{
    /// <summary>
    /// Generic processing station: items land in the hopper, get pulled into the machine one cycle at a time and come
    /// out as the definition's output, which is handed to the next receiver (usually a <see cref="Conveyor"/>).
    /// When the next receiver is full the machine blocks, so the bottleneck is visible in the world.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Machine : MonoBehaviour, IItemReceiver
    {
        public enum MachineState
        {
            Idle,
            Working,
            Blocked
        }

        [SerializeField] MachineDefinition definition;
        [SerializeField, Min(1)] int level = 1;
        [SerializeField] ItemPile hopper;
        [Tooltip("Inputs fly here and disappear when a cycle starts.")]
        [SerializeField] Transform intake;
        [Tooltip("Outputs appear here before moving on.")]
        [SerializeField] Transform outputPoint;
        [Tooltip("IItemReceiver that takes outputs (conveyor, storage, next machine).")]
        [SerializeField] MonoBehaviour outputTarget;
        [SerializeField] MachineVisuals visuals;
        [SerializeField] StationLabel label;

        readonly List<WorldItem> pendingOutputs = new();
        IItemReceiver output;
        ItemPool pool;
        float progress;

        /// <summary>Throughput multiplier. Active Overdrive and Operators add modifiers here.</summary>
        public ModifiableStat Speed { get; } = new(1f);

        public event Action<Machine> Changed;

        public MachineDefinition Definition => definition;
        public int Level => level;
        public MachineLevel Stats => definition.GetLevel(level);
        public MachineState State { get; private set; }
        public float CycleTime => Stats.cycleTime / Mathf.Max(0.01f, Speed.Value);
        public float Progress01 => progress;
        public int QueuedInputs => hopper != null ? hopper.Count : 0;

        void Awake()
        {
            output = outputTarget as IItemReceiver;
            if (outputTarget != null && output == null) Debug.LogError($"[Machine] {name}: output target is not an IItemReceiver.", this);
            ApplyLevel();
        }

        void OnEnable()
        {
            if (hopper != null) hopper.Changed += OnHopperChanged;
        }

        void OnDisable()
        {
            if (hopper != null) hopper.Changed -= OnHopperChanged;
        }

        void Start()
        {
            Services.TryGet(out pool);
            RefreshLabel();
        }

        public void SetLevel(int newLevel)
        {
            level = Mathf.Clamp(newLevel, 1, definition.MaxLevel);
            ApplyLevel();
            if (visuals != null) visuals.PlayUpgrade();
            Changed?.Invoke(this);
        }

        public bool CanAccept(ItemDefinition item) => definition != null && item == definition.Input && hopper.CanAccept(item);

        public void Accept(WorldItem item) => hopper.Accept(item);

        void Update()
        {
            FlushOutputs();
            if (pendingOutputs.Count > 0)
            {
                SetState(MachineState.Blocked);
                return;
            }

            if (State == MachineState.Working)
            {
                progress += Time.deltaTime / CycleTime;
                if (progress >= 1f) CompleteCycle();
                return;
            }

            if (hopper.Count >= Stats.inputsPerCycle) StartCycle();
            else SetState(MachineState.Idle);
        }

        void StartCycle()
        {
            for (int i = 0; i < Stats.inputsPerCycle; i++)
            {
                var item = hopper.Take(null);
                if (item == null) break;
                var target = intake != null ? intake : transform;
                item.MoveTo(target, Vector3.zero, Quaternion.identity, Mathf.Min(0.25f, CycleTime * 0.5f), 0.4f, Consume);
            }

            progress = 0f;
            SetState(MachineState.Working);
            if (visuals != null) visuals.PlayCycle();
            GameFeedback.Sfx(definition.CycleSfx);
        }

        void CompleteCycle()
        {
            progress = 0f;
            var stats = Stats;
            if (pool != null && definition.Output != null)
            {
                var point = outputPoint != null ? outputPoint : transform;
                for (int i = 0; i < stats.outputsPerCycle; i++)
                {
                    var item = pool.Get(definition.Output, point.position, point.rotation);
                    item.Place(point, Vector3.up * (i * 0.1f), Quaternion.identity);
                    var scale = item.transform.localScale;
                    item.transform.localScale = Vector3.zero;
                    item.transform.DOScale(scale, 0.22f).SetEase(Ease.OutBack);
                    pendingOutputs.Add(item);
                }
            }

            GameEvents.RaiseItemsProcessed(new ItemsProcessedEvent(definition.Id, definition.Input, stats.inputsPerCycle, definition.Output,
                stats.outputsPerCycle));
            if (visuals != null) visuals.PlayOutput();
            GameFeedback.Sfx(definition.OutputSfx);
            SetState(MachineState.Idle);
            FlushOutputs();
        }

        void FlushOutputs()
        {
            while (pendingOutputs.Count > 0 && output != null && output.CanAccept(pendingOutputs[0].Definition))
            {
                var item = pendingOutputs[0];
                pendingOutputs.RemoveAt(0);
                output.Accept(item);
            }
        }

        void Consume(WorldItem item)
        {
            if (pool != null) pool.Release(item);
            else item.Deactivate();
        }

        void SetState(MachineState state)
        {
            if (State == state) return;
            State = state;
            if (visuals != null) visuals.SetState(state);
            Changed?.Invoke(this);
        }

        void ApplyLevel()
        {
            if (definition == null || hopper == null) return;
            hopper.Capacity = Stats.inputCapacity;
            RefreshLabel();
        }

        void OnHopperChanged(ItemPile _) => RefreshLabel();

        void RefreshLabel()
        {
            if (label == null || definition == null || hopper == null) return;
            label.Set(definition.DisplayName, level, hopper.Count, hopper.Capacity);
        }
    }
}

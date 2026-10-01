using System;
using System.Collections.Generic;
using DG.Tweening;
using ScrapYardKing.Core;
using ScrapYardKing.Feedback;
using ScrapYardKing.Items;
using ScrapYardKing.Progression;
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
    public sealed class Machine : MonoBehaviour, IItemReceiver, IStation, IUpgradeable
    {
        public enum MachineState
        {
            Idle,
            Working,
            Blocked
        }

        /// <summary>Where one product of a splitting machine appears and which receiver takes it (e.g. iron → iron belt).</summary>
        [Serializable]
        public struct OutputPort
        {
            public ItemDefinition item;
            [Tooltip("IItemReceiver for this item.")]
            public MonoBehaviour target;
            public Transform point;
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
        [Tooltip("Per-item outputs for splitting machines. Items without a port use Output Target / Output Point.")]
        [SerializeField] OutputPort[] outputPorts;
        [SerializeField] MachineVisuals visuals;
        [SerializeField] StationLabel label;
        [SerializeField] LevelVisuals levelVisuals;

        readonly List<WorldItem> pendingOutputs = new();
        IItemReceiver output;
        IItemReceiver[] portReceivers;
        WeightedSpread spread;
        ItemPool pool;
        float progress;

        /// <summary>Throughput multiplier. Active Overdrive and Operators add modifiers here.</summary>
        public ModifiableStat Speed { get; } = new(1f);

        public event Action<Machine> Changed;
        public event Action<IUpgradeable> UpgradeChanged;

        public MachineDefinition Definition => definition;
        public int Level => level;
        public MachineLevel Stats => definition.GetLevel(level);
        public MachineState State { get; private set; }
        public MachineVisuals Visuals => visuals;
        public float CycleTime => Stats.cycleTime / Mathf.Max(0.01f, Speed.Value);
        public float Progress01 => progress;
        public int QueuedInputs => hopper != null ? hopper.Count : 0;

        public string StationId => definition != null ? definition.Id : name;
        public string UpgradeId => StationId;
        public string DisplayName => definition.DisplayName;
        public Sprite Icon => definition.Icon;
        public int MaxLevel => definition.MaxLevel;
        public bool IsMaxed => level >= MaxLevel;
        public long NextCost => IsMaxed ? 0 : definition.GetLevel(level + 1).upgradeCost;
        public string LevelLabel => $"Lv.{level}";
        public string NextEffect => IsMaxed ? string.Empty : $"{definition.InputsPerMinute(level):0} → {definition.InputsPerMinute(level + 1):0}/min";
        public Vector3? FeedbackPosition => transform.position + Vector3.up * 2.5f;

        void Awake()
        {
            output = outputTarget as IItemReceiver;
            if (outputTarget != null && output == null) Debug.LogError($"[Machine] {name}: output target is not an IItemReceiver.", this);
            portReceivers = new IItemReceiver[outputPorts != null ? outputPorts.Length : 0];
            for (int i = 0; i < portReceivers.Length; i++)
            {
                portReceivers[i] = outputPorts[i].target as IItemReceiver;
                if (portReceivers[i] == null) Debug.LogError($"[Machine] {name}: output port {i} target is not an IItemReceiver.", this);
            }

            if (definition != null && definition.HasOutputMix)
            {
                var weights = new float[definition.OutputMix.Length];
                for (int i = 0; i < weights.Length; i++) weights[i] = definition.OutputMix[i].weight;
                spread = new WeightedSpread(weights);
            }

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
            StationRegistry.Register(this);
            if (Services.TryGet(out UpgradeManager upgrades)) upgrades.Register(this);
        }

        void OnDestroy()
        {
            StationRegistry.Unregister(this);
            if (Services.TryGet(out UpgradeManager upgrades)) upgrades.Unregister(this);
        }

        public void SetLevel(int newLevel)
        {
            level = Mathf.Clamp(newLevel, 1, definition.MaxLevel);
            ApplyLevel(true);
            if (visuals != null) visuals.PlayUpgrade();
            Changed?.Invoke(this);
            UpgradeChanged?.Invoke(this);
        }

        void IUpgradeable.ApplyLevel(int newLevel) => SetLevel(newLevel);

        public bool CanAccept(ItemDefinition item) => definition != null && item == definition.Input && hopper.CanAccept(item);

        public void Accept(WorldItem item)
        {
            hopper.Accept(item);
            GameEvents.RaiseItemsDelivered(StationId, item.Definition, 1);
        }

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
            var product = NextProduct();
            if (pool != null && product != null)
            {
                var point = PointFor(product);
                for (int i = 0; i < stats.outputsPerCycle; i++)
                {
                    var item = pool.Get(product, point.position, point.rotation);
                    item.Place(point, Vector3.up * (i * 0.1f), Quaternion.identity);
                    var scale = item.transform.localScale;
                    item.transform.localScale = Vector3.zero;
                    item.transform.DOScale(scale, 0.22f).SetEase(Ease.OutBack);
                    pendingOutputs.Add(item);
                }
            }

            GameEvents.RaiseItemsProcessed(new ItemsProcessedEvent(definition.Id, definition.Input, stats.inputsPerCycle, product,
                stats.outputsPerCycle));
            if (visuals != null) visuals.PlayOutput();
            GameFeedback.Sfx(definition.OutputSfx);
            SetState(MachineState.Idle);
            FlushOutputs();
        }

        ItemDefinition NextProduct()
        {
            if (spread == null) return definition.Output;
            int index = spread.Next();
            return index >= 0 ? definition.OutputMix[index].item : definition.Output;
        }

        int PortIndex(ItemDefinition item)
        {
            if (outputPorts == null) return -1;
            for (int i = 0; i < outputPorts.Length; i++)
                if (outputPorts[i].item == item) return i;
            return -1;
        }

        Transform PointFor(ItemDefinition item)
        {
            int port = PortIndex(item);
            if (port >= 0 && outputPorts[port].point != null) return outputPorts[port].point;
            return outputPoint != null ? outputPoint : transform;
        }

        IItemReceiver ReceiverFor(ItemDefinition item)
        {
            int port = PortIndex(item);
            return port >= 0 ? portReceivers[port] : output;
        }

        // First in, first out: a full belt for one product holds the machine, so the blockage stays visible.
        void FlushOutputs()
        {
            while (pendingOutputs.Count > 0)
            {
                var item = pendingOutputs[0];
                var receiver = ReceiverFor(item.Definition);
                if (receiver == null || !receiver.CanAccept(item.Definition)) return;
                pendingOutputs.RemoveAt(0);
                receiver.Accept(item);
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

        void ApplyLevel(bool animate = false)
        {
            if (definition == null || hopper == null) return;
            hopper.Capacity = Stats.inputCapacity;
            if (levelVisuals != null) levelVisuals.Apply(level, animate);
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

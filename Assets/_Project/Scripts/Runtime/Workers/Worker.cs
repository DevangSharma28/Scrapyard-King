using ScrapYardKing.Core;
using ScrapYardKing.Items;
using UnityEngine;
using UnityEngine.AI;

namespace ScrapYardKing.Workers
{
    /// <summary>
    /// Shared worker body: NavMesh movement, stats, carry stack and collector. Role behaviour lives in a separate
    /// brain component (e.g. <see cref="PorterBrain"/>) that reads its route from a <see cref="WorkerSite"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class Worker : MonoBehaviour
    {
        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int WorkingId = Animator.StringToHash("Working");

        [SerializeField] CarryStack stack;
        [SerializeField] ItemCollector collector;
        [SerializeField] Animator animator;
        [Tooltip("Player pickup interval the efficiency stat scales.")]
        [SerializeField, Min(0.005f)] float basePickupInterval = 0.05f;

        NavMeshAgent agent;
        bool working;

        public WorkerDefinition Definition { get; private set; }
        public WorkerSite Site { get; private set; }
        public CarryStack Stack => stack;
        public ItemCollector Collector => collector;
        public NavMeshAgent Agent => agent;

        public ModifiableStat Speed { get; private set; }
        public ModifiableStat Capacity { get; private set; }
        public ModifiableStat Efficiency { get; private set; }

        void Awake() => agent = GetComponent<NavMeshAgent>();

        public void Initialize(WorkerDefinition definition, WorkerSite site)
        {
            Definition = definition;
            Site = site;
            Speed = new ModifiableStat(definition.MoveSpeed);
            Capacity = new ModifiableStat(definition.CarryCapacity);
            Efficiency = new ModifiableStat(definition.Efficiency);
            Speed.Changed += _ => ApplyStats();
            Capacity.Changed += _ => ApplyStats();
            Efficiency.Changed += _ => ApplyStats();
            if (collector != null)
            {
                collector.Radius = definition.PickupRadius;
                collector.enabled = definition.CollectLooseItems;
            }

            ApplyStats();
        }

        void ApplyStats()
        {
            agent.speed = Speed.Value;
            if (stack != null) stack.Capacity = Capacity.IntValue;
            if (collector != null) collector.PickupInterval = basePickupInterval / Mathf.Max(0.1f, Efficiency.Value);
        }

        public void MoveTo(Vector3 destination)
        {
            if (agent.isOnNavMesh) agent.SetDestination(destination);
        }

        public void Stop()
        {
            if (agent.isOnNavMesh) agent.ResetPath();
        }

        /// <summary>Plays the "at work" gesture (operators at a console, sellers at a counter).</summary>
        public void SetWorking(bool on)
        {
            if (working == on) return;
            working = on;
            if (animator != null) animator.SetBool(WorkingId, on);
        }

        /// <summary>Turns on the spot toward <paramref name="point"/> (used while standing at a post).</summary>
        public void FaceTowards(Vector3 point, float degreesPerSecond = 540f)
        {
            Vector3 d = point - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), degreesPerSecond * Time.deltaTime);
        }

        /// <summary>True when the agent has (nearly) reached its destination.</summary>
        public bool HasArrived(float tolerance = 0.4f) =>
            agent.isOnNavMesh && !agent.pathPending && agent.remainingDistance <= Mathf.Max(tolerance, agent.stoppingDistance);

        void Update()
        {
            if (animator == null || Speed == null) return;
            animator.SetFloat(SpeedId, Mathf.Clamp01(agent.velocity.magnitude / Mathf.Max(0.1f, Speed.Value)), 0.1f, Time.deltaTime);
        }
    }
}

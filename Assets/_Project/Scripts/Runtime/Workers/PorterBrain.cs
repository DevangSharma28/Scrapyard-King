using ScrapYardKing.Core;
using ScrapYardKing.Harvest;
using UnityEngine;

namespace ScrapYardKing.Workers
{
    /// <summary>
    /// Carrier job loop. Zone routes: walk to loose items in a pickup zone and let the collector vacuum them up.
    /// Pad routes: stand on the pickup pad until loaded. Then carry the load to the drop-off pad and wait there until
    /// the pad has unloaded it. Pads do every transfer, exactly as for the player, so a full hopper or counter makes
    /// the worker wait visibly.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Worker))]
    public sealed class PorterBrain : MonoBehaviour
    {
        public enum State
        {
            Idle,
            Collect,
            Load,
            Deliver,
            Unload
        }

        [SerializeField, Min(0.05f)] float thinkInterval = 0.35f;
        [Tooltip("With a partial load and nothing left to pick up, wait this long before delivering anyway.")]
        [SerializeField, Min(0f)] float partialLoadPatience = 1.5f;
        [SerializeField, Min(0.1f)] float arriveDistance = 0.6f;

        Worker worker;
        PorterRoute route;
        HarvestManager harvest;
        float nextThink, stateSince;
        int lastCount;
        Items.TransferPad loadPad;

        public State Current { get; private set; }

        void Awake() => worker = GetComponent<Worker>();

        void Start() => Services.TryGet(out harvest);

        void Update()
        {
            if (route == null) route = worker.Site as PorterRoute;
            if (route == null || route.Dropoff == null) return;
            if (harvest == null && !Services.TryGet(out harvest)) return;
            if (Time.time < nextThink) return;
            nextThink = Time.time + thinkInterval;
            Think();
        }

        void Think()
        {
            if (route.UsesPad) ThinkPad();
            else ThinkZones();
        }

        void ThinkPad()
        {
            var stack = worker.Stack;
            switch (Current)
            {
                case State.Idle:
                    if (stack.Count > 0) Enter(State.Deliver, route.Dropoff.transform.position);
                    else if ((loadPad = route.FullestPad()) != null) Enter(State.Load, loadPad.transform.position);
                    else worker.MoveTo(route.IdlePosition);
                    break;

                case State.Load:
                    var pad = loadPad;
                    if (stack.IsFull)
                    {
                        Enter(State.Deliver, route.Dropoff.transform.position);
                        break;
                    }

                    worker.MoveTo(pad.transform.position);
                    if (stack.Count != lastCount)
                    {
                        lastCount = stack.Count;
                        stateSince = Time.time;
                    }

                    if (HorizontalDistance(pad.transform.position) > arriveDistance || PadHasStock(pad)) break;
                    worker.Stop();
                    if (Time.time - stateSince < partialLoadPatience) break;
                    if (stack.Count > 0) Enter(State.Deliver, route.Dropoff.transform.position);
                    else Enter(State.Idle, route.IdlePosition);
                    break;

                default:
                    ThinkDelivery(stack);
                    break;
            }
        }

        void ThinkZones()
        {
            var stack = worker.Stack;
            switch (Current)
            {
                case State.Idle:
                    if (TryFindItem(out var item)) Enter(State.Collect, item);
                    else if (stack.Count > 0) Enter(State.Deliver, route.Dropoff.transform.position);
                    else worker.MoveTo(route.IdlePosition);
                    break;

                case State.Collect:
                    if (stack.IsFull)
                    {
                        Enter(State.Deliver, route.Dropoff.transform.position);
                        break;
                    }

                    if (TryFindItem(out var next))
                    {
                        worker.MoveTo(next);
                        stateSince = Time.time;
                    }
                    else if (stack.Count > 0 && Time.time - stateSince >= partialLoadPatience) Enter(State.Deliver, route.Dropoff.transform.position);
                    else if (stack.Count == 0) Enter(State.Idle, route.IdlePosition);
                    break;

                default:
                    ThinkDelivery(stack);
                    break;
            }
        }

        void ThinkDelivery(Items.CarryStack stack)
        {
            switch (Current)
            {
                case State.Deliver:
                    if (stack.Count == 0)
                    {
                        Enter(State.Idle, route.IdlePosition);
                        break;
                    }

                    worker.MoveTo(route.Dropoff.transform.position);
                    if (HorizontalDistance(route.Dropoff.transform.position) <= arriveDistance)
                    {
                        worker.Stop();
                        Enter(State.Unload, null);
                    }
                    break;

                case State.Unload:
                    if (stack.Count == 0) Enter(State.Idle, route.IdlePosition);
                    break;
            }
        }

        void Enter(State state, Vector3? destination)
        {
            Current = state;
            stateSince = Time.time;
            lastCount = worker.Stack.Count;
            if (destination.HasValue) worker.MoveTo(destination.Value);
        }

        bool TryFindItem(out Vector3 position)
        {
            var collector = worker.Collector;
            return harvest.TryFindNearestCollectable(transform.position, route.SearchRadius(transform.position), collector.Wants, route.InAnyZone,
                out position);
        }

        static bool PadHasStock(Items.TransferPad pad) => pad.Source != null && pad.Source.Count > 0;

        float HorizontalDistance(Vector3 target)
        {
            Vector3 d = target - transform.position;
            d.y = 0f;
            return d.magnitude;
        }
    }
}

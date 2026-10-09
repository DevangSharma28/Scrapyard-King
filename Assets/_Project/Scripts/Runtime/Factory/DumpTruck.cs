using ScrapYardKing.Core;
using ScrapYardKing.Feedback;
using ScrapYardKing.Harvest;
using ScrapYardKing.Items;
using UnityEngine;

namespace ScrapYardKing.Factory
{
    /// <summary>
    /// A dump truck on a <see cref="DumpRoute"/>: backs into the loading bay, takes what the excavator drops on its bed
    /// (an <see cref="IItemReceiver"/> only while it stands there), drives to the tip spot, raises the body and pours the
    /// load onto the dump pile as loose scrap, then drives the loop back, waiting at the holding point while the other
    /// truck has the bay. Kinematic, like the road trucks; the wheels turn through <see cref="World.VehicleWheels"/>.
    /// </summary>
    public sealed class DumpTruck : MonoBehaviour, IItemReceiver
    {
        enum State { Driving, Loading, Tipping, Holding }

        [SerializeField] DumpRoute route;
        [Tooltip("Index of the route point the truck starts on.")]
        [SerializeField] int startIndex;
        [SerializeField] ItemPile bed;
        [Tooltip("Items the truck carries. Empty = anything the bed takes.")]
        [SerializeField] ItemDefinition[] takes;
        [Tooltip("Hinge of the dump body (rotates around local X, back edge down).")]
        [SerializeField] Transform tipper;
        [SerializeField] float tipAngle = -52f;
        [Tooltip("Where the load pours out (behind the body).")]
        [SerializeField] Transform pourPoint;
        [SerializeField] Transform beacon;

        [Header("Feel")]
        [SerializeField, Min(0.5f)] float speed = 6f;
        [SerializeField, Min(10f)] float turnSpeed = 110f;
        [Tooltip("Leaves the bay with at least this many pieces once loadPatience has passed.")]
        [SerializeField, Min(1)] int minLoad = 6;
        [SerializeField, Min(0f)] float loadPatience = 14f;
        [SerializeField, Min(1f)] float pourRate = 14f;
        [SerializeField] SfxDefinition hornSfx;
        [SerializeField] SfxDefinition tipSfx;

        State state;
        int next;
        float stateSince, pourBudget, tipNow;
        HarvestManager harvest;
        ItemPool pool;

        public bool IsLoading => state == State.Loading && !bed.IsFull;

        void Start()
        {
            Services.TryGet(out harvest);
            Services.TryGet(out pool);
            if (route == null) { enabled = false; return; }
            route.Join(this);
            transform.position = route.Point(startIndex);
            next = startIndex + 1;
            Face(route.Point(next) - transform.position, route.Reverse(next), 9999f);
            if (startIndex == route.HoldIndex) Enter(State.Holding);
            else if (startIndex == route.LoadIndex && route.TryClaimBay(this)) Enter(State.Loading);
        }

        void OnDestroy()
        {
            if (route != null) route.Leave(this);
        }

        public bool CanAccept(ItemDefinition item) =>
            state == State.Loading && (takes == null || takes.Length == 0 || System.Array.IndexOf(takes, item) >= 0) && bed.CanAccept(item);

        public void Accept(WorldItem item) => bed.Accept(item);

        void Update()
        {
            if (route == null) return;
            if (beacon != null && state != State.Loading) beacon.Rotate(0f, 480f * Time.deltaTime, 0f, Space.Self);
            switch (state)
            {
                case State.Driving: Drive(); break;
                case State.Loading:
                    if (bed.IsFull || (bed.Count >= minLoad && Time.time - stateSince >= loadPatience)) Depart();
                    break;
                case State.Tipping: Tip(); break;
                case State.Holding:
                    if (route.TryClaimBay(this)) Go();
                    break;
            }
        }

        void Drive()
        {
            Vector3 target = route.Point(next);
            Vector3 delta = target - transform.position;
            delta.y = 0f;
            float step = speed * Time.deltaTime;
            bool reverse = route.Reverse(next);
            Face(delta, reverse, turnSpeed);
            if (delta.magnitude > step)
            {
                transform.position += delta.normalized * step;
                return;
            }

            transform.position = new Vector3(target.x, transform.position.y, target.z);
            int reached = next % route.Count;
            next = reached + 1;
            if (reached == (route.LoadIndex + 1) % route.Count) route.ReleaseBay(this);
            if (reached == route.LoadIndex) Enter(State.Loading);
            else if (reached == route.TipIndex) Enter(State.Tipping);
            else if (reached == route.HoldIndex) Enter(State.Holding);
        }

        void Face(Vector3 travel, bool reverse, float degreesPerSecond)
        {
            travel.y = 0f;
            if (travel.sqrMagnitude < 0.0001f) return;
            var want = Quaternion.LookRotation(reverse ? -travel : travel);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, degreesPerSecond * Time.deltaTime);
        }

        void Depart()
        {
            GameFeedback.Sfx(hornSfx);
            Go();
        }

        void Go() => Enter(State.Driving);

        void Enter(State s)
        {
            state = s;
            stateSince = Time.time;
            pourBudget = 0f;
        }

        /// <summary>Body up, pour the load out piece by piece as loose scrap on the pile, body down, drive on.</summary>
        void Tip()
        {
            float t = Time.time - stateSince;
            bool pouring = t > 0.7f && bed.Count > 0;
            float want = bed.Count > 0 || t < 0.7f ? 1f : 0f;
            tipNow = Mathf.MoveTowards(tipNow, want, Time.deltaTime * 1.4f);
            if (tipper != null) tipper.localRotation = Quaternion.Euler(tipAngle * Smooth(tipNow), 0f, 0f);
            if (t < 0.05f) GameFeedback.Sfx(tipSfx);

            if (pouring)
            {
                // the pile is loose scrap: a full world (loose item cap) keeps the body up until there is room
                bool room = harvest == null || harvest.LooseCount < 300;
                pourBudget += pourRate * Time.deltaTime;
                while (room && pourBudget >= 1f && bed.Count > 0)
                {
                    pourBudget -= 1f;
                    var item = bed.Take(null);
                    if (item == null) break;
                    var kind = item.Definition;
                    if (pool != null) pool.Release(item);
                    else item.Deactivate();
                    Vector3 at = pourPoint != null ? pourPoint.position : transform.position - transform.forward * 3f;
                    if (harvest != null) harvest.SpawnDrops(kind, 1, at, -transform.forward, 2.5f, 1.2f);
                }

                return;
            }

            if (bed.Count == 0 && tipNow <= 0.001f) Go();
        }

        static float Smooth(float x) => x * x * (3f - 2f * x);
    }
}

using System.Collections.Generic;
using DG.Tweening;
using ScrapYardKing.Core;
using ScrapYardKing.Feedback;
using ScrapYardKing.Harvest;
using ScrapYardKing.Items;
using UnityEngine;

namespace ScrapYardKing.Factory
{
    /// <summary>
    /// The Heavy Yard excavator: scoops loose scrap within its reach and drops it on the dump truck standing in the
    /// loading bay of its <see cref="DumpRoute"/>. Same doors as everyone else: loose items from
    /// <see cref="HarvestManager"/>, the truck is an <see cref="IItemReceiver"/>. With no truck in the bay it waits with
    /// the bucket up. The arm is a two-link IK (boom, stick) toward the bucket's target; the upper body turns on the tracks.
    /// </summary>
    public sealed class Excavator : MonoBehaviour
    {
        enum Phase { Idle, Reach, Scoop, Lift, ToTruck, Dump }

        [SerializeField] DumpRoute route;
        [Tooltip("Turns around Y: cab, boom and everything on it.")]
        [SerializeField] Transform upper;
        [Tooltip("Boom pivot (pitches around local X; the boom points along +Z).")]
        [SerializeField] Transform boom;
        [Tooltip("Stick pivot at the boom's end.")]
        [SerializeField] Transform stick;
        [Tooltip("Bucket pivot at the stick's end.")]
        [SerializeField] Transform bucket;
        [Tooltip("Where scooped pieces ride.")]
        [SerializeField] Transform hold;
        [SerializeField, Min(0.5f)] float boomLength = 3.6f;
        [SerializeField, Min(0.5f)] float stickLength = 2.6f;
        [SerializeField, Min(0f)] float minReach = 3f;
        [SerializeField, Min(1f)] float maxReach = 8.5f;
        [Tooltip("Pieces per scoop.")]
        [SerializeField, Min(1)] int scoopSize = 4;
        [SerializeField, Min(0.2f)] float scoopRadius = 1.3f;

        [Header("Motion (presentation)")]
        [SerializeField, Min(10f)] float yawSpeed = 70f;
        [SerializeField, Min(0.5f)] float armSpeed = 5f;
        [SerializeField] SfxDefinition scoopSfx;
        [SerializeField] SfxDefinition dumpSfx;

        readonly List<WorldItem> held = new();
        HarvestManager harvest;
        Phase phase;
        float yaw, curl, nextLook;
        Vector2 tip = new(4f, 3f);       // bucket target in the arm plane: (horizontal distance from the boom pivot, height)
        Vector3 goal;

        void Start()
        {
            Services.TryGet(out harvest);
            if (upper != null) yaw = upper.eulerAngles.y;
        }

        void OnDestroy() => DOTween.Kill(this);

        void Update()
        {
            if (upper == null || boom == null || stick == null) return;
            var truck = route != null ? route.Loading : null;
            switch (phase)
            {
                case Phase.Idle:
                    Move(new Vector2(4.5f, 3.2f));
                    curl = Mathf.MoveTowards(curl, 0.3f, Time.deltaTime);
                    if (truck != null && Time.time >= nextLook) Look(truck);
                    break;
                case Phase.Reach:
                    // swing over the piece with the bucket up, then down to it
                    bool over = Turn(goal);
                    if (Move(over ? new Vector2(Flat(goal - Pivot).magnitude, 0.35f) : new Vector2(tip.x, 3f)) && over) Scoop();
                    break;
                case Phase.Lift:
                    curl = Mathf.MoveTowards(curl, 1f, Time.deltaTime * 2f);
                    if (Move(new Vector2(tip.x, 3.4f))) phase = Phase.ToTruck;
                    break;
                case Phase.ToTruck:
                    if (truck == null) break;   // wait with the load up until a truck stands in the bay
                    var bed = truck.transform.position;
                    if (Turn(bed) & Move(new Vector2(Mathf.Clamp(Flat(bed - Pivot).magnitude, minReach, maxReach), 3.2f))) Dump(truck);
                    break;
                case Phase.Dump:
                    curl = Mathf.MoveTowards(curl, -0.6f, Time.deltaTime * 3f);
                    if (truck != null)
                        for (int i = held.Count - 1; i >= 0; i--)
                        {
                            if (held[i] == null) { held.RemoveAt(i); continue; }
                            if (!truck.CanAccept(held[i].Definition)) continue;
                            truck.Accept(held[i]);
                            held.RemoveAt(i);
                        }

                    if (held.Count == 0 && curl <= -0.59f)
                    {
                        phase = Phase.Idle;
                        nextLook = Time.time + 0.2f;
                    }
                    break;
            }

            Pose();
        }

        Vector3 Pivot => boom.position;

        void Look(DumpTruck truck)
        {
            nextLook = Time.time + 0.5f;
            if (harvest == null && !Services.TryGet(out harvest)) return;
            Vector3 origin = transform.position;
            if (!harvest.TryFindNearestCollectable(origin, maxReach, truck.CanAccept, p =>
                {
                    float d = Flat(p - origin).magnitude;
                    return d >= minReach && d <= maxReach;
                }, out var position)) return;
            goal = position;
            phase = Phase.Reach;
        }

        void Scoop()
        {
            Vector3 at = Pivot + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * tip.x;
            at.y = 0f;
            var truck = route != null ? route.Loading : null;
            for (int i = 0; i < scoopSize; i++)
            {
                var item = harvest.TakeNearestCollectable(at, scoopRadius, truck != null ? truck.CanAccept : (System.Func<ItemDefinition, bool>)(_ => true));
                if (item == null) break;
                item.SetHeld();
                item.MoveTo(hold, Random.insideUnitSphere * 0.25f, Random.rotation, 0.14f, 0.3f, null);
                held.Add(item);
            }

            phase = Phase.Lift;
            if (held.Count == 0) return;
            GameFeedback.Sfx(scoopSfx);
            bucket.DOPunchScale(Vector3.one * 0.12f, 0.2f, 6, 0.6f).SetTarget(this);
        }

        void Dump(DumpTruck truck)
        {
            phase = Phase.Dump;
            GameFeedback.Sfx(dumpSfx);
        }

        /// <summary>Turns the upper body toward a point. True once it faces it.</summary>
        bool Turn(Vector3 point)
        {
            var d = Flat(point - transform.position);
            if (d.sqrMagnitude < 0.01f) return true;
            float want = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            yaw = Mathf.MoveTowardsAngle(yaw, want, yawSpeed * Time.deltaTime);
            return Mathf.Abs(Mathf.DeltaAngle(yaw, want)) < 1f;
        }

        /// <summary>Moves the bucket target toward <paramref name="to"/>. True once there.</summary>
        bool Move(Vector2 to)
        {
            tip = Vector2.MoveTowards(tip, to, armSpeed * Time.deltaTime);
            return (tip - to).sqrMagnitude < 0.0025f;
        }

        /// <summary>Two-link IK, elbow up: the boom rises, the stick hangs toward the bucket target.</summary>
        void Pose()
        {
            upper.rotation = Quaternion.Euler(0f, yaw, 0f);
            float pivotY = Pivot.y - transform.position.y;
            float d = Mathf.Max(0.3f, tip.x), y = tip.y - pivotY;
            float reach = Mathf.Clamp(Mathf.Sqrt(d * d + y * y), Mathf.Abs(boomLength - stickLength) + 0.05f, boomLength + stickLength - 0.05f);
            float c2 = (reach * reach - boomLength * boomLength - stickLength * stickLength) / (2f * boomLength * stickLength);
            float q2 = Mathf.Acos(Mathf.Clamp(c2, -1f, 1f));
            float q1 = Mathf.Atan2(y, d) + Mathf.Atan2(stickLength * Mathf.Sin(q2), boomLength + stickLength * Mathf.Cos(q2));
            boom.localRotation = Quaternion.Euler(-q1 * Mathf.Rad2Deg, 0f, 0f);
            stick.localRotation = Quaternion.Euler(q2 * Mathf.Rad2Deg, 0f, 0f);
            if (bucket != null)
            {
                // keep the bucket mouth level, curled in while carrying (curl 1) and tipped open to dump (curl -0.6)
                float stickWorld = (q1 - q2) * Mathf.Rad2Deg;
                bucket.localRotation = Quaternion.Euler(stickWorld + 90f - curl * 70f, 0f, 0f);
            }
        }

        static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);
    }
}

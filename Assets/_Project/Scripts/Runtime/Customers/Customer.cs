using System;
using System.Collections.Generic;
using ScrapYardKing.Items;
using UnityEngine;

namespace ScrapYardKing.Customers
{
    /// <summary>
    /// A buyer walking a fixed path (road → line → counter → away). Movement is kinematic along waypoints: customers
    /// live on the road and sidewalk, outside the yard's NavMesh. The <see cref="CustomerQueue"/> owns the order.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Customer : MonoBehaviour
    {
        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int HappyId = Animator.StringToHash("Happy");

        [SerializeField] Animator animator;
        [SerializeField] CustomerBubble bubble;
        [Tooltip("Sold goods fly here.")]
        [SerializeField] Transform handPoint;
        [SerializeField, Min(1f)] float turnSpeed = 600f;

        readonly List<Vector3> path = new();
        int pathIndex;
        float speed, waitUntil;
        Quaternion? finalFacing;
        Action arrived;

        /// <summary>Prefab this instance came from (pool key).</summary>
        public Customer Source { get; set; }
        public CustomerBubble Bubble => bubble;
        public Transform HandPoint => handPoint != null ? handPoint : transform;
        public bool IsWalking => pathIndex < path.Count || Time.time < waitUntil;

        /// <summary>On the last leg and within <paramref name="distance"/> of where it is going (the counter can start).</summary>
        public bool Arriving(float distance) =>
            Time.time >= waitUntil && (pathIndex >= path.Count ||
                                       (pathIndex == path.Count - 1 && (path[^1] - transform.position).sqrMagnitude <= distance * distance));

        // Order state, written by the queue.
        /// <summary>Material this customer buys (null = anything on the counter).</summary>
        public ItemDefinition OrderItem { get; set; }
        public int Wanted { get; set; }
        public int Received { get; set; }
        public long Owed { get; set; }
        public bool HasOrder => Wanted > 0;

        public void ResetOrder()
        {
            OrderItem = null;
            Wanted = Received = 0;
            Owed = 0;
        }

        /// <summary>Walks through <paramref name="points"/> after <paramref name="delay"/> seconds, then faces <paramref name="facing"/>.</summary>
        public void Walk(IEnumerable<Vector3> points, float walkSpeed, Quaternion? facing = null, float delay = 0f, Action onArrived = null)
        {
            path.Clear();
            path.AddRange(points);
            pathIndex = 0;
            speed = walkSpeed;
            finalFacing = facing;
            waitUntil = Time.time + delay;
            arrived = onArrived;
        }

        /// <summary>While still walking in, moves the last waypoint instead of cutting a new path. False when standing.</summary>
        public bool RetargetFinal(Vector3 point, Quaternion facing)
        {
            if (pathIndex >= path.Count) return false;
            path[^1] = point;
            finalFacing = facing;
            return true;
        }

        public void Cheer()
        {
            if (animator != null) animator.SetTrigger(HappyId);
        }

        void Update()
        {
            float moved = 0f;
            if (Time.time >= waitUntil && pathIndex < path.Count)
            {
                Vector3 target = path[pathIndex];
                Vector3 position = transform.position;
                Vector3 delta = target - position;
                delta.y = 0f;
                float step = speed * Time.deltaTime;
                if (delta.magnitude <= step)
                {
                    transform.position = new Vector3(target.x, position.y, target.z);
                    pathIndex++;
                    if (pathIndex >= path.Count) OnArrived();
                }
                else
                {
                    transform.position = position + delta.normalized * step;
                    Turn(Quaternion.LookRotation(delta));
                }

                moved = 1f;
            }
            else if (finalFacing.HasValue && pathIndex >= path.Count) Turn(finalFacing.Value);

            if (animator != null) animator.SetFloat(SpeedId, moved, 0.08f, Time.deltaTime);
        }

        void OnArrived()
        {
            var callback = arrived;
            arrived = null;
            callback?.Invoke();
        }

        void Turn(Quaternion to) => transform.rotation = Quaternion.RotateTowards(transform.rotation, to, turnSpeed * Time.deltaTime);
    }
}

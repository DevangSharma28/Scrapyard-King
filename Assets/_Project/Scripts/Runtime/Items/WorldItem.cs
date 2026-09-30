using System;
using DG.Tweening;
using ScrapYardKing.Core;
using UnityEngine;

namespace ScrapYardKing.Items
{
    /// <summary>
    /// A single resource unit in the world. Lives through: Flying (ballistic drop with bounces) → Grounded →
    /// Moving (arc tween into a stack or a machine port) → Held (positioned by its owner, e.g. <see cref="CarryStack"/>).
    /// Motion is kinematic (no Rigidbody) to keep hundreds of pieces cheap on mobile.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldItem : MonoBehaviour
    {
        public enum State
        {
            Idle,
            Flying,
            Grounded,
            Moving,
            Held
        }

        [SerializeField, Min(0f)] float halfHeight = 0.16f;
        [SerializeField, Min(0f)] float gravity = 30f;
        [SerializeField, Range(0f, 1f)] float bounciness = 0.35f;
        [SerializeField, Range(0f, 1f)] float groundFriction = 0.55f;
        [SerializeField, Min(0f)] float settleSpeed = 2.5f;
        [Tooltip("Optional look variations, picked at random each time the item is taken from the pool.")]
        [SerializeField] Renderer visual;
        [SerializeField] Material[] materialVariants;
        [SerializeField] MeshFilter meshFilter;
        [SerializeField] Mesh[] meshVariants;

        Vector3 velocity, angularVelocity, baseScale;
        float groundY, collectableAt;
        Bounds bounds;

        Transform moveParent;
        Vector3 moveFrom, moveTo;
        Quaternion moveFromRotation, moveToRotation;
        float moveElapsed, moveDuration, moveArc;
        Action<WorldItem> onArrived;

        public ItemDefinition Definition { get; private set; }
        public State CurrentState { get; private set; }
        public float Gravity => gravity;

        /// <summary>Loose (flying or grounded) and past its post-drop delay.</summary>
        public bool IsCollectable =>
            (CurrentState == State.Flying || CurrentState == State.Grounded) && Time.time >= collectableAt;

        void Awake() => baseScale = transform.localScale;

        public void Initialize(ItemDefinition definition)
        {
            Definition = definition;
            CurrentState = State.Idle;
            transform.localScale = baseScale;
            if (visual != null && materialVariants != null && materialVariants.Length > 0)
                visual.sharedMaterial = materialVariants[UnityEngine.Random.Range(0, materialVariants.Length)];
            if (meshFilter != null && meshVariants != null && meshVariants.Length > 0)
                meshFilter.sharedMesh = meshVariants[UnityEngine.Random.Range(0, meshVariants.Length)];
        }

        /// <summary>Throws the item from <paramref name="position"/>. It bounces inside <paramref name="area"/> and settles on the ground.</summary>
        public void Launch(Vector3 position, Vector3 launchVelocity, float collectDelay, float groundHeight, Bounds area)
        {
            transform.SetPositionAndRotation(position, UnityEngine.Random.rotation);
            transform.localScale = baseScale;
            velocity = launchVelocity;
            angularVelocity = UnityEngine.Random.insideUnitSphere * 900f;
            groundY = groundHeight;
            bounds = area;
            collectableAt = Time.time + collectDelay;
            onArrived = null;
            CurrentState = State.Flying;
            gameObject.SetActive(true);
        }

        /// <summary>
        /// Reparents under <paramref name="parent"/> and arcs to a local pose. Following a moving parent is free
        /// because interpolation happens in the parent's local space.
        /// </summary>
        public void MoveTo(Transform parent, Vector3 localPosition, Quaternion localRotation, float duration, float arcHeight,
            Action<WorldItem> arrived)
        {
            transform.SetParent(parent, true);
            moveParent = parent;
            moveFrom = transform.localPosition;
            moveFromRotation = transform.localRotation;
            moveTo = localPosition;
            moveToRotation = localRotation;
            moveElapsed = 0f;
            moveDuration = Mathf.Max(0.01f, duration);
            moveArc = arcHeight;
            onArrived = arrived;
            CurrentState = State.Moving;
        }

        /// <summary>Changes the destination of an in-flight move (e.g. a stack slot shifted).</summary>
        public void RetargetMove(Vector3 localPosition)
        {
            if (CurrentState == State.Moving) moveTo = localPosition;
        }

        /// <summary>Owner takes over positioning.</summary>
        public void SetHeld() => CurrentState = State.Held;

        /// <summary>Snaps under <paramref name="parent"/> at a local pose and marks the item held.</summary>
        public void Place(Transform parent, Vector3 localPosition, Quaternion localRotation)
        {
            transform.SetParent(parent, false);
            transform.SetLocalPositionAndRotation(localPosition, localRotation);
            onArrived = null;
            moveParent = null;
            CurrentState = State.Held;
        }

        /// <summary>Resets transient state before the item goes back to its pool.</summary>
        public void Deactivate()
        {
            transform.DOKill();
            onArrived = null;
            moveParent = null;
            CurrentState = State.Idle;
            transform.localScale = baseScale;
            gameObject.SetActive(false);
        }

        void Update()
        {
            switch (CurrentState)
            {
                case State.Flying:
                    SimulateFlight(Time.deltaTime);
                    break;
                case State.Moving:
                    SimulateMove(Time.deltaTime);
                    break;
            }
        }

        void SimulateFlight(float dt)
        {
            if (dt <= 0f) return;

            velocity.y -= gravity * dt;
            Vector3 p = transform.position + velocity * dt;

            if (p.x < bounds.min.x || p.x > bounds.max.x)
            {
                p.x = Mathf.Clamp(p.x, bounds.min.x, bounds.max.x);
                velocity.x *= -bounciness;
            }

            if (p.z < bounds.min.z || p.z > bounds.max.z)
            {
                p.z = Mathf.Clamp(p.z, bounds.min.z, bounds.max.z);
                velocity.z *= -bounciness;
            }

            float floor = groundY + halfHeight;
            if (p.y <= floor)
            {
                p.y = floor;
                if (-velocity.y > settleSpeed)
                {
                    velocity.y = -velocity.y * bounciness;
                    velocity.x *= groundFriction;
                    velocity.z *= groundFriction;
                    angularVelocity *= 0.5f;
                }
                else
                {
                    velocity = Vector3.zero;
                    angularVelocity = Vector3.zero;
                    CurrentState = State.Grounded;
                }
            }

            transform.position = p;
            transform.Rotate(angularVelocity * dt, Space.World);
        }

        void SimulateMove(float dt)
        {
            if (moveParent == null)
            {
                CurrentState = State.Held;
                return;
            }

            moveElapsed += dt;
            float t = Mathf.Clamp01(moveElapsed / moveDuration);
            float eased = Easing.InQuad(t);
            transform.localPosition = Vector3.LerpUnclamped(moveFrom, moveTo, eased) + Vector3.up * (moveArc * Easing.Arc(t));
            transform.localRotation = Quaternion.Slerp(moveFromRotation, moveToRotation, t);

            if (t < 1f) return;

            CurrentState = State.Held;
            var callback = onArrived;
            onArrived = null;
            callback?.Invoke(this);
        }
    }
}

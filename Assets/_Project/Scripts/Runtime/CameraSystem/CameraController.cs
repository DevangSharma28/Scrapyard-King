using ScrapYardKing.Core;
using UnityEngine;

namespace ScrapYardKing.CameraSystem
{
    /// <summary>
    /// Fixed-angle follow camera for the top-down yard: smoothed follow with look-ahead, trauma-based shake,
    /// a spring "punch" toward the focus, and distance compensation so narrow portrait screens still see
    /// <see cref="minVisibleWidth"/> world units across. Uses unscaled time so effects play through hit-stop.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public sealed class CameraController : ServiceBehaviour<CameraController>
    {
        [SerializeField] Transform target;
        [SerializeField] Camera cam;

        [Header("Framing")]
        [SerializeField, Range(20f, 89f)] float pitch = 52f;
        [SerializeField] float yaw;
        [SerializeField, Min(1f)] float baseDistance = 22f;
        [SerializeField, Range(10f, 90f)] float verticalFov = 40f;
        [Tooltip("World units that must stay visible horizontally at the focus point. Pushes the camera back on portrait screens.")]
        [SerializeField, Min(1f)] float minVisibleWidth = 13f;
        [SerializeField] Vector3 focusOffset = new(0f, 0.5f, 1f);

        [Header("Follow")]
        [SerializeField, Min(0f)] float followSmoothTime = 0.14f;
        [SerializeField, Min(0f)] float lookAhead = 1.4f;
        [SerializeField, Min(0.01f)] float lookAheadSmoothTime = 0.35f;
        [SerializeField, Min(0.1f)] float lookAheadFullSpeed = 5f;

        [Header("Shake")]
        [SerializeField, Min(0f)] float maxShakeOffset = 0.4f;
        [SerializeField, Min(0f)] float maxShakeAngle = 1.2f;
        [SerializeField, Min(0f)] float traumaDecay = 2.4f;
        [SerializeField, Min(0f)] float shakeFrequency = 24f;

        [Header("Punch")]
        [SerializeField, Min(0f)] float punchStiffness = 220f;
        [SerializeField, Min(0f)] float punchDamping = 16f;

        Vector3 focus, focusVelocity, lookAheadOffset, lookAheadVelocity, lastTargetPosition;
        float trauma, punchOffset, punchVelocity;

        public Transform Target => target;
        public Camera Camera => cam;

        public void SetTarget(Transform newTarget, bool snap = true)
        {
            target = newTarget;
            if (snap) SnapToTarget();
        }

        /// <summary>Adds screen shake. Trauma is squared internally so small values stay subtle.</summary>
        public void Shake(float amount) => trauma = Mathf.Clamp01(trauma + amount);

        /// <summary>Kicks the camera toward the focus point; a spring returns it.</summary>
        public void Punch(float strength) => punchVelocity += strength * 10f;

        public void SnapToTarget()
        {
            if (target == null) return;
            lastTargetPosition = target.position;
            focus = target.position + focusOffset;
            focusVelocity = lookAheadOffset = lookAheadVelocity = Vector3.zero;
            Apply(Vector3.zero, 0f);
        }

        protected override void Awake()
        {
            base.Awake();
            if (cam == null) cam = GetComponent<Camera>();
        }

        void Start() => SnapToTarget();

        void LateUpdate()
        {
            if (target == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            if (dt <= 0f) return;

            Vector3 targetPosition = target.position;
            Vector3 velocity = (targetPosition - lastTargetPosition) / Mathf.Max(Time.deltaTime, 0.0001f);
            lastTargetPosition = targetPosition;
            velocity.y = 0f;

            float speed01 = Mathf.Clamp01(velocity.magnitude / lookAheadFullSpeed);
            Vector3 desiredLookAhead = velocity.sqrMagnitude > 0.01f ? velocity.normalized * (lookAhead * speed01) : Vector3.zero;
            lookAheadOffset = Vector3.SmoothDamp(lookAheadOffset, desiredLookAhead, ref lookAheadVelocity, lookAheadSmoothTime, Mathf.Infinity, dt);
            focus = Vector3.SmoothDamp(focus, targetPosition + focusOffset + lookAheadOffset, ref focusVelocity, followSmoothTime, Mathf.Infinity, dt);

            punchVelocity += (-punchStiffness * punchOffset - punchDamping * punchVelocity) * dt;
            punchOffset += punchVelocity * dt;

            float shake = trauma * trauma;
            trauma = Mathf.Max(0f, trauma - traumaDecay * dt);
            float time = Time.unscaledTime * shakeFrequency;
            Vector3 shakeOffset = new Vector3(Perlin(time, 0f), Perlin(time, 1f), 0f) * (maxShakeOffset * shake);
            float shakeRoll = Perlin(time, 2f) * maxShakeAngle * shake;

            Apply(shakeOffset, shakeRoll);
        }

        void Apply(Vector3 localShake, float roll)
        {
            if (cam != null) cam.fieldOfView = verticalFov;

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            float distance = Mathf.Max(baseDistance, DistanceForWidth()) - punchOffset;
            Vector3 position = focus - rotation * Vector3.forward * distance + rotation * localShake;
            transform.SetPositionAndRotation(position, rotation * Quaternion.Euler(0f, 0f, roll));
        }

        float DistanceForWidth()
        {
            float aspect = cam != null ? cam.aspect : 9f / 16f;
            float halfHorizontal = Mathf.Atan(Mathf.Tan(verticalFov * 0.5f * Mathf.Deg2Rad) * aspect);
            return minVisibleWidth / (2f * Mathf.Tan(halfHorizontal));
        }

        static float Perlin(float t, float seed) => Mathf.PerlinNoise(t, seed * 17.3f) * 2f - 1f;
    }
}

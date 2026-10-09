using UnityEngine;

namespace ScrapYardKing.World
{
    /// <summary>
    /// Turns a vehicle's wheels by how far it actually moved along its own forward axis (customer trucks driven by
    /// <see cref="Customers.Customer"/>), and lets the body dip a little when it stops or starts. Presentation only.
    /// </summary>
    public sealed class VehicleWheels : MonoBehaviour
    {
        [SerializeField] Transform[] wheels;
        [SerializeField, Min(0.05f)] float radius = 0.52f;
        [Tooltip("Body that pitches forward when braking and back when pulling away (optional).")]
        [SerializeField] Transform body;
        [SerializeField, Min(0f)] float pitchPerAccel = 0.6f;
        [SerializeField, Min(0f)] float maxPitch = 3f;

        Vector3 last;
        float speed, pitch;
        Quaternion bodyRest = Quaternion.identity;

        void Awake()
        {
            if (body != null) bodyRest = body.localRotation;
        }

        void OnEnable() => last = transform.position;

        void LateUpdate()
        {
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            Vector3 delta = transform.position - last;
            last = transform.position;
            float moved = Vector3.Dot(delta, transform.forward);
            if (wheels != null && Mathf.Abs(moved) > 0.0001f)
            {
                float degrees = moved / (2f * Mathf.PI * radius) * 360f;
                foreach (var w in wheels)
                    if (w != null) w.Rotate(degrees, 0f, 0f, Space.Self);
            }

            if (body == null) return;
            float now = moved / dt;
            float accel = (now - speed) / dt;
            speed = now;
            float target = Mathf.Clamp(-accel * pitchPerAccel, -maxPitch, maxPitch);
            pitch = Mathf.Lerp(pitch, target, 1f - Mathf.Exp(-8f * dt));
            body.localRotation = bodyRest * Quaternion.Euler(pitch, 0f, 0f);
        }
    }
}

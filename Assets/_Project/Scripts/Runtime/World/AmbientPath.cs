using UnityEngine;

namespace ScrapYardKing.World
{
    /// <summary>
    /// Presentation only: moves background traffic (road trucks, a forklift doing rounds) along waypoints, facing its
    /// direction of travel, with optional pauses and wheel spin. Either loops back to the first point or reappears at
    /// the start (one-way traffic).
    /// </summary>
    public sealed class AmbientPath : MonoBehaviour
    {
        [SerializeField] Transform[] points;
        [SerializeField, Min(0.1f)] float speed = 4f;
        [Tooltip("Seconds to wait at each waypoint (forklift loading).")]
        [SerializeField, Min(0f)] float pause;
        [Tooltip("True: drive back to the first point. False: jump to the first point after a delay (traffic).")]
        [SerializeField] bool loop = true;
        [SerializeField, Min(0f)] float respawnDelay = 6f;
        [Tooltip("Rotated around their local X while moving.")]
        [SerializeField] Transform[] wheels;
        [SerializeField, Min(0.05f)] float wheelRadius = 0.4f;
        [SerializeField, Min(0f)] float turnSpeed = 6f;

        int target = 1;
        float wait;

        void Start()
        {
            if (points == null || points.Length < 2) enabled = false;
            else transform.position = points[0].position;
        }

        void Update()
        {
            if (wait > 0f)
            {
                wait -= Time.deltaTime;
                return;
            }

            Vector3 goal = points[target].position;
            Vector3 to = goal - transform.position;
            to.y = 0f;
            float step = speed * Time.deltaTime;
            if (to.magnitude <= step)
            {
                transform.position = new Vector3(goal.x, transform.position.y, goal.z);
                Advance();
                return;
            }

            Vector3 dir = to.normalized;
            transform.position += dir * step;
            var look = Quaternion.LookRotation(dir, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-turnSpeed * Time.deltaTime));
            if (wheels == null) return;
            float degrees = step / (2f * Mathf.PI * wheelRadius) * 360f;
            foreach (var w in wheels)
                if (w != null) w.Rotate(Vector3.right, degrees, Space.Self);
        }

        void Advance()
        {
            wait = pause;
            target++;
            if (target < points.Length) return;
            if (loop)
            {
                target = 0;
                return;
            }

            // One-way traffic: reappear at the start after a while, already facing the second point.
            target = 1;
            transform.position = points[0].position;
            Vector3 d = points[1].position - points[0].position;
            d.y = 0f;
            if (d.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(d.normalized);
            wait = respawnDelay;
        }
    }
}

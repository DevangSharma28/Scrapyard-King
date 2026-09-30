using UnityEngine;

namespace ScrapYardKing.Harvest
{
    /// <summary>
    /// A visual chunk (door, hood, wheel, lid) that flies off as the object loses health, giving the
    /// "dismantling" read. Purely visual; drops are spawned by <see cref="ScrapObject"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScrapPart : MonoBehaviour
    {
        [Tooltip("Lower detaches first.")]
        [SerializeField] int detachOrder;
        [SerializeField, Min(0f)] float lifetime = 0.55f;
        [SerializeField, Min(0f)] float gravity = 24f;

        Transform originalParent;
        Vector3 localPosition, localScale, detachedScale;
        Quaternion localRotation;
        Vector3 velocity, spin;
        float elapsed;
        bool detached, cached;

        public int DetachOrder => detachOrder;
        public bool IsDetached => detached;
        public float Lifetime => lifetime;

        void Awake() => CachePose();

        void CachePose()
        {
            if (cached) return;
            originalParent = transform.parent;
            localPosition = transform.localPosition;
            localRotation = transform.localRotation;
            localScale = transform.localScale;
            cached = true;
        }

        /// <summary>
        /// Flings the part away. It is reparented to <paramref name="flightParent"/> so the owner's shake/collapse
        /// animation on its visual root does not affect the part mid-flight.
        /// </summary>
        public void Detach(Vector3 awayDirection, float speed, Transform flightParent)
        {
            if (detached) return;
            CachePose();
            detached = true;
            elapsed = 0f;
            if (flightParent != null) transform.SetParent(flightParent, true);
            detachedScale = transform.localScale;
            velocity = awayDirection.normalized * speed + Vector3.up * (speed * 1.1f);
            spin = Random.onUnitSphere * 540f;
        }

        public void ResetPart()
        {
            CachePose();
            detached = false;
            if (transform.parent != originalParent) transform.SetParent(originalParent, false);
            transform.localPosition = localPosition;
            transform.localRotation = localRotation;
            transform.localScale = localScale;
            gameObject.SetActive(true);
        }

        void Update()
        {
            if (!detached) return;

            float dt = Time.deltaTime;
            elapsed += dt;
            velocity.y -= gravity * dt;
            transform.position += velocity * dt;
            transform.Rotate(spin * dt, Space.World);

            float shrinkStart = lifetime * 0.55f;
            if (elapsed > shrinkStart)
                transform.localScale = detachedScale * (1f - Mathf.InverseLerp(shrinkStart, lifetime, elapsed));

            if (elapsed >= lifetime) gameObject.SetActive(false);
        }
    }
}

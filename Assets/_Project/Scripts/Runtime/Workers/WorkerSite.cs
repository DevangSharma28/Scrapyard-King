using UnityEngine;

namespace ScrapYardKing.Workers
{
    /// <summary>Scene data describing where workers of one role do their job. Brains read it; it has no behaviour.</summary>
    public abstract class WorkerSite : MonoBehaviour
    {
        [Tooltip("Matched against WorkerDefinition.siteId so two kinds of the same role can work different routes.")]
        [SerializeField] string siteId;
        [SerializeField] Transform idlePoint;

        public string SiteId => siteId;
        public abstract WorkerRole Role { get; }
        public Vector3 IdlePosition => idlePoint != null ? idlePoint.position : transform.position;
    }
}

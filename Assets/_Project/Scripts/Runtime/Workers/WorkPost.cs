using ScrapYardKing.Factory;
using UnityEngine;

namespace ScrapYardKing.Workers
{
    /// <summary>
    /// A place where a specialist stands and works: the control console of a machine (Operator) or the till behind a
    /// counter (Seller). Scene data only; <see cref="PostBrain"/> does the work.
    /// </summary>
    public sealed class WorkPost : WorkerSite
    {
        [SerializeField] WorkerRole role = WorkerRole.Operator;
        [Tooltip("Operator posts: the machine that runs faster while the post is manned.")]
        [SerializeField] Machine machine;
        [Tooltip("More machines the same operator runs (a furnace battery). Machines that are not built yet are skipped.")]
        [SerializeField] Machine[] alsoRuns;
        [Tooltip("Seller posts: the sell desk that serves faster while the post is manned.")]
        [SerializeField] SellDesk desk;
        [Tooltip("What the worker faces while working (console, customers).")]
        [SerializeField] Transform lookAt;

        public override WorkerRole Role => role;
        public Machine Machine => machine;
        public System.Collections.Generic.IReadOnlyList<Machine> AlsoRuns => alsoRuns ?? System.Array.Empty<Machine>();
        public SellDesk Desk => desk;
        public Vector3 LookPoint => lookAt != null ? lookAt.position : transform.position + transform.forward;
    }
}

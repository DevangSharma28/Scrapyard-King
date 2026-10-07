using ScrapYardKing.Core;
using ScrapYardKing.Factory;
using UnityEngine;

namespace ScrapYardKing.Workers
{
    /// <summary>
    /// Specialist job loop (blueprint: Operator "runs one machine faster", Seller "serves customers and reduces queue
    /// time"). The worker walks to its <see cref="WorkPost"/> and, while standing there, multiplies the machine's
    /// <see cref="Machine.Speed"/> or the desk's <see cref="SellDesk.ServiceSpeed"/> by the definition's work boost.
    /// The modifier exists only while the post is manned, so the boost is visible in the world.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Worker))]
    public sealed class PostBrain : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] float arriveDistance = 0.5f;
        [Tooltip("Seconds the work gesture keeps playing after a sale (sellers).")]
        [SerializeField, Min(0f)] float serveGesture = 0.9f;

        Worker worker;
        WorkPost post;
        ModifiableStat boosted;
        readonly System.Collections.Generic.List<ModifiableStat> alsoBoosted = new();
        float gestureUntil;

        /// <summary>True while the worker stands at the post and the boost is applied.</summary>
        public bool AtPost => boosted != null;

        void Awake() => worker = GetComponent<Worker>();

        void OnDisable() => Leave();

        void Update()
        {
            if (post == null)
            {
                post = worker.Site as WorkPost;
                if (post == null) return;
                if (post.Desk != null) post.Desk.Sold += OnSold;
            }

            Vector3 d = post.IdlePosition - transform.position;
            d.y = 0f;
            if (d.magnitude > arriveDistance)
            {
                Leave();
                worker.MoveTo(post.IdlePosition);
                return;
            }

            worker.Stop();
            worker.FaceTowards(post.LookPoint);
            Arrive();
            worker.SetWorking(IsBusy());
        }

        void OnDestroy()
        {
            if (post != null && post.Desk != null) post.Desk.Sold -= OnSold;
        }

        void OnSold(SellDesk desk, long cash) => gestureUntil = Time.time + serveGesture;

        bool IsBusy()
        {
            if (post.Machine != null) return post.Machine.State == Machine.MachineState.Working;
            return Time.time < gestureUntil;
        }

        void Arrive()
        {
            if (boosted != null || worker.Definition == null) return;
            boosted = post.Machine != null ? post.Machine.Speed : post.Desk != null ? post.Desk.ServiceSpeed : null;
            if (boosted == null) return;
            boosted.AddModifier(new StatModifier(this, StatModifierType.Multiply, worker.Definition.WorkBoost));
            foreach (var m in post.AlsoRuns)
            {
                if (m == null || !m.isActiveAndEnabled) continue;
                m.Speed.AddModifier(new StatModifier(this, StatModifierType.Multiply, worker.Definition.WorkBoost));
                alsoBoosted.Add(m.Speed);
            }
        }

        void Leave()
        {
            if (boosted == null) return;
            boosted.RemoveModifiersFrom(this);
            boosted = null;
            foreach (var stat in alsoBoosted) stat.RemoveModifiersFrom(this);
            alsoBoosted.Clear();
            if (worker != null) worker.SetWorking(false);
        }
    }
}

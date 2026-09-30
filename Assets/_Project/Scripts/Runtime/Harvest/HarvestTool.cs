using System;
using ScrapYardKing.Core;
using ScrapYardKing.Feedback;
using UnityEngine;

namespace ScrapYardKing.Harvest
{
    /// <summary>
    /// Auto-cutter: picks the nearest scrap in range and hits it at the owner's cut rate. Stats come from any
    /// <see cref="IHarvesterStats"/> on this object or a parent, so upgrades never touch this class.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HarvestTool : MonoBehaviour
    {
        [Tooltip("Where hits land from (the blade). Defaults to 1 m above this transform.")]
        [SerializeField] Transform toolTip;
        [Tooltip("Distance bonus for the current target so the cutter does not flicker between equidistant objects.")]
        [SerializeField, Min(0f)] float targetStickiness = 0.5f;
        [Tooltip("Wind-up before the first hit on a new target.")]
        [SerializeField, Min(0f)] float firstHitDelay = 0.08f;
        [SerializeField, Min(0f)] float tooToughMessageCooldown = 2.5f;

        IHarvesterStats stats;
        ScrapManager scrapManager;
        ScrapObject target;
        float hitTimer, nextTooToughMessage;

        /// <summary>Raised after every hit (target, hit data).</summary>
        public event Action<ScrapObject, ScrapHit> Hit;

        public ScrapObject CurrentTarget => target;
        public bool IsCutting => target != null;

        void Awake() => stats = GetComponentInParent<IHarvesterStats>();

        void Start() => Services.TryGet(out scrapManager);

        void OnDisable() => target = null;

        void Update()
        {
            if (stats == null) return;
            if (scrapManager == null && !Services.TryGet(out scrapManager)) return;

            Vector3 origin = transform.position;
            var next = scrapManager.FindTarget(origin, stats.CutRange, stats.CutPower, target, targetStickiness, out var tooTough);
            if (next != target)
            {
                target = next;
                hitTimer = firstHitDelay;
            }

            if (target == null)
            {
                if (tooTough != null) WarnTooTough(tooTough);
                return;
            }

            hitTimer -= Time.deltaTime;
            if (hitTimer > 0f) return;
            hitTimer = Mathf.Max(hitTimer + 1f / Mathf.Max(0.01f, stats.CutRate), 0f);

            Vector3 from = toolTip != null ? toolTip.position : origin + Vector3.up;
            Vector3 point = target.ClosestPoint(from);
            Vector3 direction = point - origin;
            direction.y = 0f;
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;

            var struck = target;
            var hit = new ScrapHit(stats.CutPower, point, direction, this);
            bool broke = struck.ApplyHit(hit);
            Hit?.Invoke(struck, hit);
            if (broke) target = null;
        }

        void WarnTooTough(ScrapObject scrap)
        {
            if (Time.time < nextTooToughMessage) return;
            nextTooToughMessage = Time.time + tooToughMessageCooldown;

            var config = GameFeedback.Config;
            var color = config != null ? config.WarningPopupColor : Color.red;
            GameFeedback.Popup($"Need Power {scrap.Definition.MinCutPower:0}", scrap.Center + Vector3.up * 1.5f, color);
        }
    }
}

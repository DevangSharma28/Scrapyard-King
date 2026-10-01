using ScrapYardKing.Core;
using ScrapYardKing.Feedback;
using UnityEngine;

namespace ScrapYardKing.Harvest
{
    /// <summary>
    /// Keeps one piece of scrap alive at this spot: spawns on start, respawns after the definition's delay once broken.
    /// The next object and its rotation are chosen up front, and the respawn waits while a character overlaps that
    /// object's real collider footprint, so nothing pops up inside the player.
    /// </summary>
    public sealed class ScrapSpawnPoint : MonoBehaviour
    {
        const float RetryInterval = 0.5f;

        [SerializeField] ScrapDefinition[] candidates;
        [SerializeField] bool randomYaw = true;
        [SerializeField, Min(0f)] float respawnDelayMultiplier = 1f;
        [Tooltip("Layers that block a respawn when overlapping the object's footprint (characters).")]
        [SerializeField] LayerMask blockingLayers;
        [Tooltip("Extra clearance around the footprint when checking for blockers.")]
        [SerializeField, Min(0f)] float footprintMargin = 0.15f;
        [Tooltip("Loose items inside this radius are hopped out of the way on respawn.")]
        [SerializeField, Min(0.1f)] float clearRadius = 1.8f;
        [Tooltip("Pop the first object in with the spawn animation (e.g. spawn points revealed by an expansion).")]
        [SerializeField] bool animateFirstSpawn;

        ScrapManager manager;
        ScrapObject current;
        ScrapDefinition next;
        ScrapObject nextPrefab;
        Quaternion nextRotation;
        float respawnAt = -1f;

        public ScrapObject Current => current;

        void Start()
        {
            if (!Services.TryGet(out manager)) return;
            PrepareNext();
            SpawnNow(animateFirstSpawn);
        }

        void OnDisable()
        {
            if (current != null) current.Broken -= OnBroken;
        }

        void Update()
        {
            if (current != null || respawnAt < 0f || Time.time < respawnAt) return;

            if (IsBlocked())
            {
                respawnAt = Time.time + RetryInterval;
                return;
            }

            SpawnNow(true);
        }

        void PrepareNext()
        {
            next = candidates != null && candidates.Length > 0 ? candidates[Random.Range(0, candidates.Length)] : null;
            nextPrefab = next != null ? next.PickPrefab() : null;
            nextRotation = randomYaw ? Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * transform.rotation : transform.rotation;
        }

        void SpawnNow(bool animate)
        {
            if (next == null || manager == null) return;

            if (Services.TryGet(out HarvestManager harvest)) harvest.PushLooseItemsOut(transform.position, clearRadius);

            current = manager.Spawn(next, nextPrefab, transform.position, nextRotation, animate);
            if (current == null) return;

            current.Broken += OnBroken;
            respawnAt = -1f;

            if (!animate) return;
            var config = GameFeedback.Config;
            if (config == null) return;
            GameFeedback.Vfx(config.SpawnVfx, transform.position, Quaternion.identity);
            GameFeedback.Sfx(config.SpawnSfx);
        }

        void OnBroken(ScrapObject scrap)
        {
            scrap.Broken -= OnBroken;
            if (scrap != current) return;
            current = null;
            respawnAt = Time.time + scrap.Definition.RespawnDelay * respawnDelayMultiplier;
            PrepareNext();
        }

        bool IsBlocked()
        {
            if (blockingLayers.value == 0 || nextPrefab == null) return false;

            var prefab = nextPrefab.transform;
            if (prefab.TryGetComponent(out BoxCollider box))
            {
                Vector3 scale = prefab.localScale;
                Vector3 center = transform.position + nextRotation * Vector3.Scale(box.center, scale);
                Vector3 halfExtents = Vector3.Scale(box.size, scale) * 0.5f + Vector3.one * footprintMargin;
                return Physics.CheckBox(center, halfExtents, nextRotation, blockingLayers, QueryTriggerInteraction.Ignore);
            }

            return Physics.CheckSphere(transform.position + Vector3.up * clearRadius, clearRadius, blockingLayers, QueryTriggerInteraction.Ignore);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.8f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.05f, clearRadius);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * clearRadius);
        }
    }
}

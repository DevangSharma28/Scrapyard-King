using System;
using ScrapYardKing.Core;
using ScrapYardKing.Feedback;
using UnityEngine;

namespace ScrapYardKing.Harvest
{
    /// <summary>
    /// A harvestable piece of scrap in the yard. Takes hits, sheds <see cref="ScrapPart"/>s at evenly spaced health
    /// thresholds (dropping a share of its pieces each time), then bursts on break. Pooled and respawned by
    /// <see cref="ScrapManager"/> / <see cref="ScrapSpawnPoint"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScrapObject : MonoBehaviour
    {
        enum Phase
        {
            Spawning,
            Alive,
            Breaking,
            Dead
        }

        const float SpawnDuration = 0.35f;
        const float BreakSwellDuration = 0.05f;
        const float BreakCollapseDuration = 0.16f;
        const float MinBreakHoldDuration = 0.3f;

        [SerializeField] ScrapDefinition definition;
        [Tooltip("Shaken and scaled on hits. Keep the collider and health bar outside it.")]
        [SerializeField] Transform visualRoot;
        [SerializeField] Collider hitCollider;
        [SerializeField] WorldHealthBar healthBar;

        ScrapPart[] parts = Array.Empty<ScrapPart>();
        Vector3 visualBasePosition, visualBaseScale;
        Phase phase = Phase.Alive;
        float health, phaseTime, shakeTime, shakeDuration, shakeAmplitude, punchScale, breakHoldDuration;
        int partsDetached, piecesTotal, piecesRemaining;

        public event Action<ScrapObject> Broken;

        public ScrapDefinition Definition => definition;
        /// <summary>Prefab this instance was spawned from; the pool key.</summary>
        public ScrapObject SourcePrefab { get; set; }
        public float Health => health;
        public float Health01 => definition != null ? health / definition.MaxHealth : 0f;
        public bool IsTargetable => phase == Phase.Alive && isActiveAndEnabled;
        public Vector3 Center => hitCollider != null ? hitCollider.bounds.center : transform.position;
        public float TopHeight => hitCollider != null ? hitCollider.bounds.max.y : transform.position.y + 1f;

        void Awake()
        {
            if (visualRoot == null) visualRoot = transform;
            visualBasePosition = visualRoot.localPosition;
            visualBaseScale = visualRoot.localScale;
            parts = GetComponentsInChildren<ScrapPart>(true);
            Array.Sort(parts, (a, b) => a.DetachOrder.CompareTo(b.DetachOrder));
            breakHoldDuration = Mathf.Max(MinBreakHoldDuration, BreakSwellDuration + BreakCollapseDuration);
            foreach (var part in parts) breakHoldDuration = Mathf.Max(breakHoldDuration, part.Lifetime);
            if (definition != null) ResetState();
        }

        void OnEnable()
        {
            if (Services.TryGet(out ScrapManager manager)) manager.Register(this);
        }

        void OnDisable()
        {
            if (Services.TryGet(out ScrapManager manager)) manager.Unregister(this);
        }

        /// <summary>Assigns data and restores full health. Called by the pool on every spawn.</summary>
        public void Initialize(ScrapDefinition scrapDefinition)
        {
            definition = scrapDefinition;
            ResetState();
        }

        public void ResetState()
        {
            health = definition.MaxHealth;
            partsDetached = 0;
            piecesTotal = definition.RollDropAmount();
            piecesRemaining = piecesTotal;
            foreach (var part in parts) part.ResetPart();
            visualRoot.localPosition = visualBasePosition;
            visualRoot.localScale = visualBaseScale;
            shakeTime = 0f;
            if (hitCollider != null) hitCollider.enabled = true;
            if (healthBar != null) healthBar.Hide();
            phase = Phase.Alive;
        }

        /// <summary>Pops the object up from the ground. It becomes targetable when the animation ends.</summary>
        public void PlaySpawnAnimation()
        {
            phase = Phase.Spawning;
            phaseTime = 0f;
            visualRoot.localScale = Vector3.zero;
        }

        /// <summary>Horizontal distance from <paramref name="from"/> to the collider surface (0 when inside).</summary>
        public float SurfaceDistance(Vector3 from)
        {
            if (hitCollider == null) return Vector3.Distance(from, transform.position);
            Vector3 delta = hitCollider.ClosestPoint(from) - from;
            delta.y = 0f;
            return delta.magnitude;
        }

        public Vector3 ClosestPoint(Vector3 from) => hitCollider != null ? hitCollider.ClosestPoint(from) : transform.position;

        /// <summary>Applies one cut. Returns true when this hit broke the object.</summary>
        public bool ApplyHit(in ScrapHit hit)
        {
            if (phase != Phase.Alive || definition == null) return false;

            health = Mathf.Max(0f, health - hit.Damage);
            var config = GameFeedback.Config;
            PlayHitFeedback(hit, config);
            DetachParts(hit, config);

            if (health > 0f) return false;
            Break(config);
            return true;
        }

        void PlayHitFeedback(in ScrapHit hit, FeedbackConfig config)
        {
            Vector3 toCutter = -hit.Direction;
            toCutter.y = 0f;
            if (toCutter.sqrMagnitude < 0.0001f) toCutter = -transform.forward;
            var sparkRotation = Quaternion.LookRotation((toCutter.normalized + Vector3.up * 0.6f).normalized);

            if (healthBar != null) healthBar.Show(Health01);
            if (config == null) return;

            shakeDuration = config.HitShakeDuration;
            shakeTime = shakeDuration;
            shakeAmplitude = config.HitShakeAmplitude;
            punchScale = config.HitPunchScale;

            GameFeedback.Vfx(GameFeedback.Pick(definition.HitVfx, config.DefaultHitVfx), hit.Point, sparkRotation);
            GameFeedback.Sfx(GameFeedback.Pick(definition.HitSfx, config.DefaultHitSfx));
            GameFeedback.HitStop(config.HitStopDuration, config.HitStopTimeScale);
            GameFeedback.CameraShake(config.HitCameraShake);
        }

        void DetachParts(in ScrapHit hit, FeedbackConfig config)
        {
            float health01 = Health01;
            while (partsDetached < parts.Length && health01 <= DropMath.PartThreshold(partsDetached, parts.Length))
            {
                var part = parts[partsDetached];
                int pieces = DropMath.PiecesForPart(piecesTotal, definition.PartDropShare, parts.Length, partsDetached);
                partsDetached++;

                Vector3 away = AwayFromCenter(part.transform.position, hit.Direction);
                part.Detach(away, config != null ? config.PartFlySpeed : 5f, transform);
                DropPieces(pieces, part.transform.position, away);
                if (config != null) GameFeedback.Sfx(config.PartDetachSfx);
            }
        }

        void Break(FeedbackConfig config)
        {
            phase = Phase.Breaking;
            phaseTime = 0f;
            if (hitCollider != null) hitCollider.enabled = false;
            if (healthBar != null) healthBar.Hide();

            Vector3 center = Center;
            float partSpeed = config != null ? config.PartFlySpeed : 5f;
            for (; partsDetached < parts.Length; partsDetached++)
            {
                var part = parts[partsDetached];
                part.Detach(AwayFromCenter(part.transform.position, Vector3.forward), partSpeed * 1.3f, transform);
            }

            DropPieces(piecesRemaining, center, Vector3.zero);
            int rare = definition.RollRareDropAmount();
            if (rare > 0 && Services.TryGet(out HarvestManager harvest))
                harvest.SpawnDrops(definition.RareDropItem, rare, center, Vector3.zero, definition.DropLaunchSpeed * 1.2f, definition.DropSpread);

            if (config != null)
            {
                GameFeedback.Vfx(GameFeedback.Pick(definition.BreakVfx, config.DefaultBreakVfx), center, Quaternion.identity, definition.BreakVfxScale);
                GameFeedback.Sfx(GameFeedback.Pick(definition.BreakSfx, config.DefaultBreakSfx));
                GameFeedback.HitStop(config.BreakHitStopDuration, config.HitStopTimeScale);
                GameFeedback.CameraShake(definition.BreakShake);
                GameFeedback.CameraPunch(config.BreakCameraPunch);

                float top = hitCollider != null ? hitCollider.bounds.max.y : center.y + 1f;
                var popupColor = definition.DropItem != null ? definition.DropItem.Color : config.PositivePopupColor;
                GameFeedback.Popup($"+{piecesTotal + rare}", new Vector3(center.x, top + 0.6f, center.z), popupColor, config.BreakPopupScale);
            }

            GameEvents.RaiseScrapBroken(new ScrapBrokenEvent(definition, center, piecesTotal + rare));
            Broken?.Invoke(this);
        }

        void DropPieces(int count, Vector3 origin, Vector3 bias)
        {
            count = Mathf.Min(count, piecesRemaining);
            if (count <= 0) return;
            piecesRemaining -= count;
            if (Services.TryGet(out HarvestManager harvest))
                harvest.SpawnDrops(definition.DropItem, count, origin, bias, definition.DropLaunchSpeed, definition.DropSpread);
        }

        Vector3 AwayFromCenter(Vector3 point, Vector3 fallback)
        {
            Vector3 away = point - Center;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = fallback;
            away.y = 0f;
            return away.sqrMagnitude > 0.0001f ? away.normalized : Vector3.forward;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            switch (phase)
            {
                case Phase.Spawning:
                    phaseTime += Time.deltaTime;
                    visualRoot.localScale = visualBaseScale * Easing.OutBack(phaseTime / SpawnDuration);
                    if (phaseTime >= SpawnDuration)
                    {
                        visualRoot.localScale = visualBaseScale;
                        phase = Phase.Alive;
                    }
                    break;

                case Phase.Alive:
                    UpdateShake(dt);
                    break;

                case Phase.Breaking:
                    phaseTime += dt;
                    float scale = phaseTime < BreakSwellDuration
                        ? Mathf.Lerp(1f, 1.15f, phaseTime / BreakSwellDuration)
                        : Mathf.Lerp(1.15f, 0f, Easing.InQuad((phaseTime - BreakSwellDuration) / BreakCollapseDuration));
                    visualRoot.localScale = visualBaseScale * Mathf.Max(0f, scale);
                    if (phaseTime >= breakHoldDuration) Die();
                    break;
            }
        }

        void UpdateShake(float dt)
        {
            if (shakeTime <= 0f) return;

            shakeTime = Mathf.Max(0f, shakeTime - dt);
            float k = shakeDuration > 0f ? shakeTime / shakeDuration : 0f;
            Vector3 jitter = UnityEngine.Random.insideUnitSphere * (shakeAmplitude * k);
            jitter.y *= 0.3f;
            visualRoot.localPosition = visualBasePosition + jitter;
            visualRoot.localScale = visualBaseScale * (1f + punchScale * k);
        }

        void Die()
        {
            phase = Phase.Dead;
            visualRoot.localScale = Vector3.zero;
            if (Services.TryGet(out ScrapManager manager)) manager.Release(this);
            else gameObject.SetActive(false);
        }
    }
}

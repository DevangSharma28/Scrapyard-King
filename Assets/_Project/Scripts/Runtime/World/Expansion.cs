using System;
using DG.Tweening;
using ScrapYardKing.CameraSystem;
using ScrapYardKing.Core;
using ScrapYardKing.Feedback;
using ScrapYardKing.Harvest;
using ScrapYardKing.Progression;
using UnityEngine;

namespace ScrapYardKing.World
{
    /// <summary>
    /// A locked area of the yard. While closed, its barriers stand (and carve the NavMesh), its content is hidden and
    /// flying items bounce off its area. Buying it (level 0 → 1, usually on a <see cref="Tiles.PurchaseTile"/>) pans the
    /// camera over, sinks the barriers, pops the content in one piece at a time and grows the drop area.
    /// </summary>
    public sealed class Expansion : MonoBehaviour, IUpgradeable
    {
        /// <summary>A renderer that changes look when the area opens (locked ground → yard floor).</summary>
        [Serializable]
        public struct MaterialSwap
        {
            public Renderer renderer;
            public Material material;
        }

        [SerializeField] ExpansionDefinition definition;
        [Tooltip("Fences/gates that sink into the ground when the area opens.")]
        [SerializeField] Transform[] barriers;
        [Tooltip("Children are revealed one by one (scrap spawn points, props). Inactive while locked.")]
        [SerializeField] Transform contentRoot;
        [Tooltip("Shown only while locked (signs, tape).")]
        [SerializeField] GameObject lockedOnly;
        [Tooltip("Ground footprint of the area (y ignored). Blocks loose items while locked, extends the drop area when open.")]
        [SerializeField] Bounds area = new(new Vector3(29f, 0f, 34f), new Vector3(20f, 4f, 7.5f));
        [SerializeField] Transform focusPoint;
        [SerializeField] MaterialSwap[] materialSwaps;

        [Header("Reveal feel")]
        [SerializeField, Min(0f)] float focusHold = 2.6f;
        [SerializeField, Min(0f)] float revealDelay = 0.6f;
        [SerializeField, Min(0f)] float barrierSinkDepth = 2.2f;
        [SerializeField, Min(0.05f)] float barrierSinkDuration = 0.6f;
        [SerializeField, Min(0f)] float contentStagger = 0.12f;
        [SerializeField] ParticleSystem revealVfx;
        [Tooltip("Dust puff and pop sound for every revealed piece (construction burst).")]
        [SerializeField] bool burstPerPiece = true;
        [SerializeField, Min(0f)] float popPitchStep = 0.04f;

        HarvestManager harvest;
        bool open;

        public event Action<IUpgradeable> UpgradeChanged;

        public ExpansionDefinition Definition => definition;
        public bool IsOpen => open;
        public string UpgradeId => definition != null ? definition.Id : name;
        public string DisplayName => definition != null ? definition.DisplayName : name;
        public Sprite Icon => definition != null ? definition.Icon : null;
        public int Level => open ? 1 : 0;
        public int MaxLevel => 1;
        public bool IsMaxed => open;
        public long NextCost => open || definition == null ? 0 : definition.Cost;
        public string LevelLabel => open ? "OPEN" : "LOCKED";
        public string NextEffect => open ? string.Empty : definition != null && !string.IsNullOrEmpty(definition.Teaser) ? definition.Teaser : "NEW AREA";
        // The reveal plays its own feedback.
        public Vector3? FeedbackPosition => null;

        void Awake()
        {
            if (contentRoot == null) return;
            foreach (Transform child in contentRoot) child.gameObject.SetActive(false);
        }

        void Start()
        {
            if (Services.TryGet(out harvest) && !open) harvest.AddBlockedArea(area);
            if (Services.TryGet(out UpgradeManager upgrades)) upgrades.Register(this);
        }

        void OnDestroy()
        {
            if (Services.TryGet(out UpgradeManager upgrades)) upgrades.Unregister(this);
            DOTween.Kill(this);
        }

        void IUpgradeable.ApplyLevel(int level)
        {
            if (level >= 1 && !open) Open();
        }

        /// <summary>Opens the area with the full reveal.</summary>
        public void Open()
        {
            if (open) return;
            open = true;

            if (harvest != null)
            {
                harvest.RemoveBlockedArea(area);
                harvest.ExpandDropBounds(area);
            }

            Vector3 focus = focusPoint != null ? focusPoint.position : area.center;
            if (Services.TryGet(out CameraController cameraController)) cameraController.Focus(focus, focusHold);
            GameFeedback.Sfx(definition != null ? definition.OpenSfx : null);

            var sequence = DOTween.Sequence().SetTarget(this).AppendInterval(revealDelay);
            if (lockedOnly != null) sequence.AppendCallback(() => lockedOnly.SetActive(false));
            SinkBarriers(sequence);
            SwapMaterials(sequence);
            RevealContent(sequence);
            sequence.AppendCallback(() => GameFeedback.Vfx(revealVfx, focus, Quaternion.identity));

            UpgradeChanged?.Invoke(this);
            GameEvents.RaiseExpansionOpened(UpgradeId);
        }

        void SinkBarriers(Sequence sequence)
        {
            if (barriers == null) return;
            sequence.AppendCallback(() =>
            {
                GameFeedback.CameraShake(0.35f);
                foreach (var b in barriers)
                {
                    if (b == null) continue;
                    // Stop carving right away so workers can path in while it sinks.
                    foreach (var obstacle in b.GetComponentsInChildren<UnityEngine.AI.NavMeshObstacle>()) obstacle.enabled = false;
                    foreach (var c in b.GetComponentsInChildren<Collider>()) c.enabled = false;
                    var target = b;
                    target.DOShakePosition(0.25f, new Vector3(0.12f, 0f, 0.12f), 20, 90f, false, false)
                        .OnComplete(() => target.DOMoveY(target.position.y - barrierSinkDepth, barrierSinkDuration).SetEase(Ease.InBack)
                            .OnComplete(() => target.gameObject.SetActive(false)));
                }
            });
            sequence.AppendInterval(0.25f + barrierSinkDuration * 0.6f);
        }

        void SwapMaterials(Sequence sequence)
        {
            if (materialSwaps == null || materialSwaps.Length == 0) return;
            sequence.AppendCallback(() =>
            {
                foreach (var swap in materialSwaps)
                    if (swap.renderer != null && swap.material != null) swap.renderer.sharedMaterial = swap.material;
            });
        }

        void RevealContent(Sequence sequence)
        {
            if (contentRoot == null) return;
            int index = 0;
            foreach (Transform child in contentRoot)
            {
                var c = child;
                float pitch = 1f + index++ * popPitchStep;
                sequence.AppendCallback(() =>
                {
                    c.gameObject.SetActive(true);
                    // Spawn points pop their own scrap; everything else pops here.
                    if (c.GetComponent<ScrapSpawnPoint>() != null) return;
                    Vector3 scale = c.localScale;
                    c.localScale = Vector3.zero;
                    c.DOScale(scale, 0.4f).SetEase(Ease.OutBack);
                    if (!burstPerPiece) return;
                    var feedback = GameFeedback.Config;
                    if (feedback == null) return;
                    GameFeedback.Vfx(feedback.SpawnVfx, c.position, Quaternion.identity);
                    GameFeedback.Sfx(feedback.SpawnSfx, pitch);
                });
                sequence.AppendInterval(contentStagger);
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.7f);
            Gizmos.DrawWireCube(new Vector3(area.center.x, 0.1f, area.center.z), new Vector3(area.size.x, 0.2f, area.size.z));
        }
    }
}

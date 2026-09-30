using System.Collections.Generic;
using DG.Tweening;
using ScrapYardKing.Core;
using ScrapYardKing.Feedback;
using UnityEngine;

namespace ScrapYardKing.Economy
{
    /// <summary>
    /// Physical cash waiting to be picked up: sales pop green bundles onto a neat stack, and a
    /// <see cref="CashCollector"/> standing on the pad pulls them in one by one.
    /// </summary>
    public sealed class CashPile : MonoBehaviour
    {
        sealed class Bundle
        {
            public Transform Transform;
            public long Value;
        }

        [SerializeField] Transform bundlePrefab;
        [SerializeField] Transform stackRoot;
        [SerializeField, Min(1)] int columns = 3;
        [SerializeField, Min(1)] int rows = 2;
        [SerializeField] Vector3 cellSize = new(0.5f, 0.13f, 0.3f);
        [Tooltip("Beyond this many bundles, extra value is added to existing bundles instead of spawning more.")]
        [SerializeField, Min(1)] int maxBundles = 72;

        [Header("Collection")]
        [SerializeField] Transform pad;
        [SerializeField] Vector2 padSize = new(2.2f, 2.2f);
        [SerializeField, Min(0.005f)] float collectInterval = 0.025f;
        [SerializeField, Min(0.05f)] float flyDuration = 0.28f;
        [SerializeField] SfxDefinition collectSfx;
        [SerializeField, Min(0f)] float pitchStep = 0.02f;

        readonly List<Bundle> bundles = new();
        readonly Stack<Transform> pool = new();
        EconomyManager economy;
        float nextCollect;
        int streak;

        public long StoredCash { get; private set; }
        public int BundleCount => bundles.Count;

        void Start() => Services.TryGet(out economy);

        /// <summary>Adds <paramref name="amount"/> cash, popping bundles out of <paramref name="from"/>.</summary>
        public void Add(long amount, Vector3 from)
        {
            if (amount <= 0) return;
            StoredCash += amount;

            int bundleValue = economy != null && economy.Config != null ? economy.Config.CashBundleValue : 5;
            int spawned = 0;
            while (amount > 0)
            {
                if (bundles.Count >= maxBundles)
                {
                    bundles[^1].Value += amount;
                    break;
                }

                long value = System.Math.Min(bundleValue, amount);
                amount -= value;
                SpawnBundle(value, from, spawned++ * 0.035f);
            }
        }

        void SpawnBundle(long value, Vector3 from, float delay)
        {
            var t = pool.Count > 0 ? pool.Pop() : Instantiate(bundlePrefab, stackRoot);
            t.gameObject.SetActive(true);
            t.SetParent(stackRoot, true);
            t.position = from;
            t.localRotation = Quaternion.Euler(0f, Random.Range(-8f, 8f), 0f);
            t.localScale = Vector3.one;

            int index = bundles.Count;
            bundles.Add(new Bundle { Transform = t, Value = value });
            t.DOKill();
            t.DOLocalJump(SlotPosition(index), 1.2f, 1, 0.4f).SetDelay(delay).SetEase(Ease.OutQuad)
                .OnComplete(() => t.DOPunchScale(Vector3.one * 0.25f, 0.18f, 6, 0.5f));
        }

        void Update()
        {
            var collector = FindCollector();
            if (collector == null)
            {
                streak = 0;
                return;
            }

            if (bundles.Count == 0 || Time.time < nextCollect) return;
            nextCollect = Time.time + collectInterval;
            CollectTop(collector);
        }

        void CollectTop(CashCollector collector)
        {
            var bundle = bundles[^1];
            bundles.RemoveAt(bundles.Count - 1);
            StoredCash -= bundle.Value;

            var t = bundle.Transform;
            t.DOKill();
            t.SetParent(collector.Target, true);
            long value = bundle.Value;
            float pitch = 1f + Mathf.Min(streak++, 20) * pitchStep;

            DOTween.Sequence()
                .Append(t.DOLocalJump(Vector3.zero, 1.4f, 1, flyDuration).SetEase(Ease.InQuad))
                .Join(t.DOScale(0.4f, flyDuration).SetEase(Ease.InQuad))
                .OnComplete(() =>
                {
                    Recycle(t);
                    if (economy != null) economy.AddCash(value, collector.Target.position);
                    GameFeedback.Sfx(collectSfx, pitch);
                });
        }

        void Recycle(Transform t)
        {
            t.DOKill();
            t.gameObject.SetActive(false);
            t.SetParent(stackRoot, false);
            pool.Push(t);
        }

        Vector3 SlotPosition(int index)
        {
            int perLayer = columns * rows;
            int layer = index / perLayer;
            int inLayer = index % perLayer;
            int x = inLayer % columns;
            int z = inLayer / columns;
            return new Vector3((x - (columns - 1) * 0.5f) * cellSize.x, layer * cellSize.y, (z - (rows - 1) * 0.5f) * cellSize.z);
        }

        CashCollector FindCollector()
        {
            if (pad == null) return null;
            var collectors = CashCollector.Active;
            for (int i = 0; i < collectors.Count; i++)
            {
                Vector3 local = pad.InverseTransformPoint(collectors[i].transform.position);
                if (Mathf.Abs(local.x) <= padSize.x * 0.5f && Mathf.Abs(local.z) <= padSize.y * 0.5f) return collectors[i];
            }

            return null;
        }

        void OnDrawGizmosSelected()
        {
            if (pad == null) return;
            Gizmos.color = new Color(0.3f, 1f, 0.3f, 0.8f);
            Gizmos.matrix = pad.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.up * 0.05f, new Vector3(padSize.x, 0.1f, padSize.y));
        }
    }
}

using System;
using System.Collections.Generic;
using ScrapYardKing.Core;
using ScrapYardKing.Items;
using UnityEngine;

namespace ScrapYardKing.Harvest
{
    /// <summary>
    /// Owns every loose <see cref="WorldItem"/> on the ground: spawns drop bursts (instances come from <see cref="ItemPool"/>)
    /// and answers "nearest collectable item" queries for collectors (player and Porter workers).
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public sealed class HarvestManager : ServiceBehaviour<HarvestManager>
    {
        [SerializeField] Transform looseItemRoot;
        [Tooltip("Hard cap on loose items to protect mobile frame time. Drops beyond it are skipped.")]
        [SerializeField, Min(16)] int maxLooseItems = 400;
        [SerializeField] float groundHeight;
        [Tooltip("Dropped items bounce inside this box. Grows as expansions open.")]
        [SerializeField] Bounds dropBounds = new(new Vector3(20f, 0f, 24f), new Vector3(38f, 20f, 26f));
        [SerializeField, Min(0f)] float collectDelay = 0.35f;

        readonly List<WorldItem> loose = new();
        ItemPool pool;
        bool warnedCap;

        public int LooseCount => loose.Count;
        public float GroundHeight => groundHeight;

        public Bounds DropBounds
        {
            get => dropBounds;
            set => dropBounds = value;
        }

        Transform Root => looseItemRoot != null ? looseItemRoot : transform;

        /// <summary>
        /// Throws <paramref name="count"/> items out of <paramref name="origin"/> in a burst.
        /// <paramref name="bias"/> (horizontal) skews the burst, e.g. away from the object's center.
        /// </summary>
        public void SpawnDrops(ItemDefinition item, int count, Vector3 origin, Vector3 bias, float launchSpeed, float spread)
        {
            if (item == null || item.Prefab == null || count <= 0) return;
            if (pool == null && !Services.TryGet(out pool)) return;

            bias.y = 0f;
            for (int i = 0; i < count; i++)
            {
                if (loose.Count >= maxLooseItems)
                {
                    if (!warnedCap) Debug.LogWarning($"[HarvestManager] Loose item cap ({maxLooseItems}) reached; skipping drops.", this);
                    warnedCap = true;
                    return;
                }

                var worldItem = pool.Get(item, origin, Quaternion.identity);
                worldItem.transform.SetParent(Root, true);
                Vector2 random = UnityEngine.Random.insideUnitCircle;
                Vector3 direction = new Vector3(random.x, 0f, random.y) + bias * 0.8f;
                if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;
                direction.Normalize();

                float horizontal = launchSpeed * UnityEngine.Random.Range(0.3f, 0.9f);
                float vertical = launchSpeed * UnityEngine.Random.Range(0.9f, 1.5f);
                Vector3 start = origin + new Vector3(random.x, 0f, random.y) * (spread * 0.3f);

                worldItem.Launch(start, direction * horizontal + Vector3.up * vertical, collectDelay, groundHeight, dropBounds);
                loose.Add(worldItem);
            }
        }

        /// <summary>Removes and returns the nearest collectable item within <paramref name="radius"/> (horizontal distance).</summary>
        public WorldItem TakeNearestCollectable(Vector3 position, float radius, Func<ItemDefinition, bool> accept)
        {
            float bestSqr = radius * radius;
            int bestIndex = -1;

            for (int i = 0; i < loose.Count; i++)
            {
                var item = loose[i];
                if (!item.IsCollectable) continue;
                if (accept != null && !accept(item.Definition)) continue;

                Vector3 delta = item.transform.position - position;
                float sqr = delta.x * delta.x + delta.z * delta.z;
                if (sqr > bestSqr) continue;
                bestSqr = sqr;
                bestIndex = i;
            }

            if (bestIndex < 0) return null;

            var taken = loose[bestIndex];
            loose[bestIndex] = loose[^1];
            loose.RemoveAt(loose.Count - 1);
            return taken;
        }

        /// <summary>
        /// Hops every loose item inside <paramref name="radius"/> of <paramref name="center"/> to just outside it,
        /// so respawning scrap never buries uncollected pieces inside its collider.
        /// </summary>
        public void PushLooseItemsOut(Vector3 center, float radius)
        {
            const float hopSpeed = 5f;
            foreach (var item in loose)
            {
                Vector3 offset = item.transform.position - center;
                offset.y = 0f;
                float distance = offset.magnitude;
                if (distance >= radius) continue;

                Vector3 direction = distance > 0.01f ? offset / distance : Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward;
                float flightTime = 2f * hopSpeed / Mathf.Max(1f, item.Gravity);
                float travel = radius + 0.6f - distance;
                item.Launch(item.transform.position, direction * (travel / flightTime) + Vector3.up * hopSpeed, 0.1f, groundHeight, dropBounds);
            }
        }

        /// <summary>Puts an item taken by <see cref="TakeNearestCollectable"/> back if the taker could not use it.</summary>
        public void ReturnToGround(WorldItem item)
        {
            if (item == null || loose.Contains(item)) return;
            item.transform.SetParent(Root, true);
            item.Launch(item.transform.position, Vector3.up * 2f, collectDelay, groundHeight, dropBounds);
            loose.Add(item);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.5f);
            Gizmos.DrawWireCube(dropBounds.center, dropBounds.size);
        }
    }
}

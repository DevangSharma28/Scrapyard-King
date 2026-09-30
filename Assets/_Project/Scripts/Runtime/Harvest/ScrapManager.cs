using System.Collections.Generic;
using ScrapYardKing.Core;
using UnityEngine;

namespace ScrapYardKing.Harvest
{
    /// <summary>Registry of live scrap (for cutter targeting) and the per-prefab spawn pools.</summary>
    [DefaultExecutionOrder(-500)]
    public sealed class ScrapManager : ServiceBehaviour<ScrapManager>
    {
        [SerializeField] Transform spawnedRoot;

        readonly List<ScrapObject> registered = new();
        readonly Dictionary<ScrapObject, Stack<ScrapObject>> pools = new();

        public IReadOnlyList<ScrapObject> Registered => registered;

        public void Register(ScrapObject scrap)
        {
            if (scrap != null && !registered.Contains(scrap)) registered.Add(scrap);
        }

        public void Unregister(ScrapObject scrap) => registered.Remove(scrap);

        /// <summary>Spawns <paramref name="prefab"/> (a variant of <paramref name="definition"/>) from its pool.</summary>
        public ScrapObject Spawn(ScrapDefinition definition, ScrapObject prefab, Vector3 position, Quaternion rotation, bool animate)
        {
            if (definition == null || prefab == null)
            {
                Debug.LogError($"[ScrapManager] Cannot spawn '{(definition != null ? definition.name : "null")}': missing definition or prefab.", this);
                return null;
            }

            ScrapObject scrap = null;
            if (pools.TryGetValue(prefab, out var pool))
                while (scrap == null && pool.Count > 0) scrap = pool.Pop();

            if (scrap == null)
            {
                scrap = Instantiate(prefab, position, rotation, spawnedRoot != null ? spawnedRoot : transform);
                scrap.SourcePrefab = prefab;
            }
            else
            {
                scrap.transform.SetPositionAndRotation(position, rotation);
                scrap.gameObject.SetActive(true);
            }

            scrap.Initialize(definition);
            if (animate) scrap.PlaySpawnAnimation();
            return scrap;
        }

        public void Release(ScrapObject scrap)
        {
            if (scrap == null) return;
            scrap.gameObject.SetActive(false);
            if (scrap.SourcePrefab == null) return;

            if (!pools.TryGetValue(scrap.SourcePrefab, out var pool))
            {
                pool = new Stack<ScrapObject>();
                pools[scrap.SourcePrefab] = pool;
            }

            pool.Push(scrap);
        }

        /// <summary>
        /// Nearest targetable scrap within <paramref name="range"/> of <paramref name="origin"/> that
        /// <paramref name="cutPower"/> can damage. <paramref name="current"/> gets a distance bonus to avoid target flicker.
        /// <paramref name="tooTough"/> reports the nearest in-range object that was skipped for lack of power.
        /// </summary>
        public ScrapObject FindTarget(Vector3 origin, float range, float cutPower, ScrapObject current, float stickiness,
            out ScrapObject tooTough)
        {
            ScrapObject best = null;
            tooTough = null;
            float bestScore = float.MaxValue, toughDistance = float.MaxValue;

            foreach (var scrap in registered)
            {
                if (scrap == null || !scrap.IsTargetable) continue;

                float distance = scrap.SurfaceDistance(origin);
                if (distance > range) continue;

                if (cutPower < scrap.Definition.MinCutPower)
                {
                    if (distance < toughDistance)
                    {
                        toughDistance = distance;
                        tooTough = scrap;
                    }
                    continue;
                }

                float score = scrap == current ? distance - stickiness : distance;
                if (score >= bestScore) continue;
                bestScore = score;
                best = scrap;
            }

            return best;
        }
    }
}

using System.Collections.Generic;
using ScrapYardKing.Core;
using UnityEngine;

namespace ScrapYardKing.Feedback
{
    /// <summary>Pools one-shot particle prefabs and recycles them when their lifetime ends.</summary>
    [DefaultExecutionOrder(-500)]
    public sealed class VFXManager : ServiceBehaviour<VFXManager>
    {
        struct ActiveEffect
        {
            public ParticleSystem Prefab;
            public ParticleSystem Instance;
            public float ReleaseAt;
        }

        readonly Dictionary<ParticleSystem, Stack<ParticleSystem>> pools = new();
        readonly Dictionary<ParticleSystem, float> lifetimes = new();
        readonly List<ActiveEffect> active = new();

        public ParticleSystem Play(ParticleSystem prefab, Vector3 position, Quaternion rotation, float scale = 1f)
        {
            if (prefab == null) return null;

            var instance = Acquire(prefab);
            var t = instance.transform;
            t.SetPositionAndRotation(position, rotation);
            t.localScale = Vector3.one * scale;
            instance.gameObject.SetActive(true);
            instance.Clear(true);
            instance.Play(true);

            active.Add(new ActiveEffect { Prefab = prefab, Instance = instance, ReleaseAt = Time.time + Lifetime(prefab) });
            return instance;
        }

        void Update()
        {
            float now = Time.time;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var effect = active[i];
                if (now < effect.ReleaseAt) continue;

                active.RemoveAt(i);
                if (effect.Instance == null) continue;
                effect.Instance.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                effect.Instance.gameObject.SetActive(false);
                pools[effect.Prefab].Push(effect.Instance);
            }
        }

        ParticleSystem Acquire(ParticleSystem prefab)
        {
            if (!pools.TryGetValue(prefab, out var pool))
            {
                pool = new Stack<ParticleSystem>();
                pools[prefab] = pool;
            }

            while (pool.Count > 0)
            {
                var pooled = pool.Pop();
                if (pooled != null) return pooled;
            }

            return Instantiate(prefab, transform);
        }

        float Lifetime(ParticleSystem prefab)
        {
            if (lifetimes.TryGetValue(prefab, out float cached)) return cached;

            float max = 0f;
            foreach (var system in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = system.main;
                max = Mathf.Max(max, main.startDelay.constantMax + main.duration + main.startLifetime.constantMax);
            }

            lifetimes[prefab] = max + 0.1f;
            return max + 0.1f;
        }
    }
}

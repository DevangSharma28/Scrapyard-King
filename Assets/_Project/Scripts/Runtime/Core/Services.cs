using System;
using System.Collections.Generic;
using UnityEngine;

namespace ScrapYardKing.Core
{
    /// <summary>
    /// Scene-scoped registry for game systems. Systems register in Awake (see <see cref="ServiceBehaviour{T}"/>);
    /// consumers resolve them in Start or lazily, never in Awake/OnEnable where registration order is not guaranteed.
    /// </summary>
    public static class Services
    {
        static readonly Dictionary<Type, object> Registry = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Registry.Clear();

        public static void Register<T>(T service) where T : class
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (TryGet<T>(out var existing) && !ReferenceEquals(existing, service))
                Debug.LogWarning($"[Services] {typeof(T).Name} registered twice; the newer instance wins.");
            Registry[typeof(T)] = service;
        }

        public static void Unregister<T>(T service) where T : class
        {
            if (Registry.TryGetValue(typeof(T), out var existing) && ReferenceEquals(existing, service))
                Registry.Remove(typeof(T));
        }

        public static bool TryGet<T>(out T service) where T : class
        {
            if (Registry.TryGetValue(typeof(T), out var value) && value is T typed && IsAlive(typed))
            {
                service = typed;
                return true;
            }

            service = null;
            return false;
        }

        public static T Get<T>() where T : class
        {
            if (TryGet(out T service)) return service;
            Debug.LogError($"[Services] {typeof(T).Name} is not registered. Add it under the scene's _Systems root.");
            return null;
        }

        static bool IsAlive(object service) => service is not UnityEngine.Object unityObject || unityObject != null;
    }

    /// <summary>Base for scene systems that expose themselves through <see cref="Services"/>.</summary>
    public abstract class ServiceBehaviour<T> : MonoBehaviour where T : ServiceBehaviour<T>
    {
        protected virtual void Awake() => Services.Register((T)this);
        protected virtual void OnDestroy() => Services.Unregister((T)this);
    }
}


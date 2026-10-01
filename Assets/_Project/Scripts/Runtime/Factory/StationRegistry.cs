using System.Collections.Generic;
using UnityEngine;

namespace ScrapYardKing.Factory
{
    /// <summary>A production or sales station with a stable id (its definition id).</summary>
    public interface IStation
    {
        string StationId { get; }
        Transform transform { get; }
    }

    /// <summary>Lookup of live stations by id, for guidance, tasks and workers.</summary>
    public static class StationRegistry
    {
        static readonly List<IStation> Stations = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Stations.Clear();

        public static IReadOnlyList<IStation> All => Stations;

        public static void Register(IStation station)
        {
            if (station != null && !Stations.Contains(station)) Stations.Add(station);
        }

        public static void Unregister(IStation station) => Stations.Remove(station);

        /// <summary>First station of type <typeparamref name="T"/> with <paramref name="id"/> (null/empty id = any).</summary>
        public static T Find<T>(string id = null) where T : class, IStation
        {
            foreach (var s in Stations)
                if (s is T typed && (string.IsNullOrEmpty(id) || s.StationId == id))
                    return typed;
            return null;
        }
    }
}

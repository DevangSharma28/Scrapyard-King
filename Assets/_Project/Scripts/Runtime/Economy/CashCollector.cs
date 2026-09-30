using System.Collections.Generic;
using UnityEngine;

namespace ScrapYardKing.Economy
{
    /// <summary>Marks a character that picks up cash from <see cref="CashPile"/>s. Cash flies to <see cref="Target"/>.</summary>
    public sealed class CashCollector : MonoBehaviour
    {
        static readonly List<CashCollector> ActiveCollectors = new();

        public static IReadOnlyList<CashCollector> Active => ActiveCollectors;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => ActiveCollectors.Clear();

        [SerializeField] Transform target;

        public Transform Target => target != null ? target : transform;

        void OnEnable()
        {
            if (!ActiveCollectors.Contains(this)) ActiveCollectors.Add(this);
        }

        void OnDisable() => ActiveCollectors.Remove(this);
    }
}

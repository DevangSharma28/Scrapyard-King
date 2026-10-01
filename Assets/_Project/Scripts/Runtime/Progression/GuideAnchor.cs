using System.Collections.Generic;
using UnityEngine;

namespace ScrapYardKing.Progression
{
    /// <summary>
    /// Named point the guide arrow can point at, e.g. "crusher/in" on the crusher's deposit pad.
    /// Ids follow "&lt;station id&gt;/&lt;role&gt;" with roles in, out, cash and boost. Set <see cref="station"/> + role
    /// to derive the id from the station, so one prefab works for every instance (two storages, two desks).
    /// </summary>
    public sealed class GuideAnchor : MonoBehaviour
    {
        static readonly Dictionary<string, GuideAnchor> Anchors = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Anchors.Clear();

        [SerializeField] string id;
        [Tooltip("Optional IStation; when set the id becomes <station id>/<role>.")]
        [SerializeField] MonoBehaviour station;
        [SerializeField] string role;

        string registered;

        public string Id => registered ?? id;

        public static bool TryGet(string anchorId, out GuideAnchor anchor) => Anchors.TryGetValue(anchorId ?? string.Empty, out anchor);

        public static string IdFor(string stationId, string role) => $"{stationId}/{role}";

        void OnEnable()
        {
            registered = station is Factory.IStation s && !string.IsNullOrEmpty(role) ? IdFor(s.StationId, role) : id;
            if (!string.IsNullOrEmpty(registered)) Anchors[registered] = this;
        }

        void OnDisable()
        {
            if (!string.IsNullOrEmpty(registered) && Anchors.TryGetValue(registered, out var a) && a == this) Anchors.Remove(registered);
            registered = null;
        }
    }
}

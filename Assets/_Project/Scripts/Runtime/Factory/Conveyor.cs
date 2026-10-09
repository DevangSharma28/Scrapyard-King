using System.Collections.Generic;
using ScrapYardKing.Items;
using UnityEngine;

namespace ScrapYardKing.Factory
{
    /// <summary>
    /// Carries items along a polyline at a fixed speed and spacing, then hands them to <see cref="destination"/>.
    /// Items queue up when the destination is full, which backs up into the machine feeding the belt.
    /// </summary>
    public sealed class Conveyor : MonoBehaviour, IItemReceiver
    {
        static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");

        sealed class Rider
        {
            public WorldItem Item;
            public float Distance;
            public bool OnBelt;
        }

        [Tooltip("Path waypoints in order. Items ride from the first to the last.")]
        [SerializeField] Transform[] points;
        [SerializeField, Min(0.1f)] float speed = 1.8f;
        [SerializeField, Min(0.1f)] float spacing = 0.55f;
        [SerializeField] float rideHeight = 0.05f;
        [Tooltip("IItemReceiver at the end of the belt.")]
        [SerializeField] MonoBehaviour destination;
        [Tooltip("Belt renderers whose texture scrolls while items move.")]
        [SerializeField] Renderer[] belts;
        [SerializeField] Vector2 beltScroll = new(0f, -1f);
        [Tooltip("Drums and rollers that turn with the belt (around their local X), radius in metres.")]
        [SerializeField] Transform[] rollers;
        [SerializeField, Min(0.01f)] float rollerRadius = 0.15f;

        readonly List<Rider> riders = new();
        IItemReceiver target;
        Vector3[] localPoints;
        float[] cumulative;
        float length, scroll;
        MaterialPropertyBlock block;

        public int Count => riders.Count;

        void Awake()
        {
            target = destination as IItemReceiver;
            if (destination != null && target == null) Debug.LogError($"[Conveyor] {name}: destination is not an IItemReceiver.", this);
            CachePath();
        }

        public bool CanAccept(ItemDefinition item)
        {
            if (item == null || localPoints == null || localPoints.Length < 2) return false;
            return riders.Count == 0 || riders[^1].Distance >= spacing;
        }

        public void Accept(WorldItem item)
        {
            var rider = new Rider { Item = item, Distance = 0f };
            riders.Add(rider);
            item.MoveTo(transform, PointAt(0f), Quaternion.identity, 0.18f, 0.35f, _ =>
            {
                item.SetHeld();
                rider.OnBelt = true;
            });
        }

        void Update()
        {
            float step = speed * Time.deltaTime;
            if (riders.Count == 0)
            {
                // an empty belt keeps running: only a jam stops it
                ScrollBelts(step);
                TurnRollers(step);
                return;
            }

            bool moved = false;
            for (int i = 0; i < riders.Count; i++)
            {
                var r = riders[i];
                if (!r.OnBelt) continue;

                float limit = i == 0 ? length : riders[i - 1].Distance - spacing;
                float next = Mathf.Min(r.Distance + step, Mathf.Max(limit, r.Distance));
                if (next > r.Distance + 0.0001f) moved = true;
                r.Distance = next;
                r.Item.transform.localPosition = PointAt(r.Distance);
            }

            var front = riders[0];
            if (front.OnBelt && front.Distance >= length - 0.001f && target != null && target.CanAccept(front.Item.Definition))
            {
                riders.RemoveAt(0);
                target.Accept(front.Item);
            }

            if (moved)
            {
                ScrollBelts(step);
                TurnRollers(step);
            }
        }

        void TurnRollers(float step)
        {
            if (rollers == null) return;
            float degrees = step / rollerRadius * Mathf.Rad2Deg;
            foreach (var r in rollers)
                if (r != null) r.Rotate(degrees, 0f, 0f, Space.Self);
        }

        void ScrollBelts(float step)
        {
            if (belts == null || belts.Length == 0) return;
            scroll += step;
            block ??= new MaterialPropertyBlock();
            var st = new Vector4(1f, 1f, beltScroll.x * scroll, beltScroll.y * scroll);
            foreach (var belt in belts)
            {
                if (belt == null) continue;
                belt.GetPropertyBlock(block);
                block.SetVector(BaseMapStId, st);
                belt.SetPropertyBlock(block);
            }
        }

        void CachePath()
        {
            if (points == null || points.Length < 2) return;
            localPoints = new Vector3[points.Length];
            cumulative = new float[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                localPoints[i] = transform.InverseTransformPoint(points[i].position) + Vector3.up * rideHeight;
                if (i > 0) cumulative[i] = cumulative[i - 1] + Vector3.Distance(localPoints[i - 1], localPoints[i]);
            }

            length = cumulative[^1];
        }

        Vector3 PointAt(float distance)
        {
            if (localPoints == null) return Vector3.zero;
            for (int i = 1; i < localPoints.Length; i++)
            {
                if (distance > cumulative[i] && i < localPoints.Length - 1) continue;
                float segment = cumulative[i] - cumulative[i - 1];
                float t = segment > 0f ? Mathf.Clamp01((distance - cumulative[i - 1]) / segment) : 1f;
                return Vector3.Lerp(localPoints[i - 1], localPoints[i], t);
            }

            return localPoints[^1];
        }

        void OnDrawGizmos()
        {
            if (points == null) return;
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);
            for (int i = 1; i < points.Length; i++)
                if (points[i - 1] != null && points[i] != null)
                    Gizmos.DrawLine(points[i - 1].position, points[i].position);
        }
    }
}

using System;
using UnityEngine;

namespace ScrapYardKing.Harvest
{
    /// <summary>
    /// Presentation only: makes a scrap object look more wrecked as its health drops (smoke starts, glass cracks, the
    /// body sags onto a missing wheel) on top of the parts it sheds. Reads <see cref="ScrapObject.Health01"/>, so a
    /// respawned object (full health) is whole again.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScrapDamageStages : MonoBehaviour
    {
        [Serializable]
        struct Stage
        {
            [Tooltip("Applies once health (0-1) drops to or below this.")]
            [Range(0f, 1f)] public float belowHealth;
            public GameObject[] show;
            public GameObject[] hide;
            [Tooltip("Body tilt around the sag axis at this stage, in degrees.")]
            public float sagDegrees;
        }

        [SerializeField] ScrapObject scrap;
        [SerializeField] Transform sagPivot;
        [SerializeField] Vector3 sagAxis = Vector3.forward;
        [SerializeField, Min(0.1f)] float sagSpeed = 6f;
        [Tooltip("Highest threshold first.")]
        [SerializeField] Stage[] stages;

        Quaternion pivotRest;
        float sag, targetSag;
        int applied = int.MinValue;

        void Awake()
        {
            if (scrap == null) scrap = GetComponentInParent<ScrapObject>();
            if (sagPivot != null) pivotRest = sagPivot.localRotation;
            Apply(-1);
            sag = 0f;
        }

        void Update()
        {
            if (scrap == null || stages == null) return;
            float health = scrap.Health01;
            int stage = -1;
            for (int i = 0; i < stages.Length; i++)
                if (health <= stages[i].belowHealth) stage = i;
            if (stage != applied) Apply(stage);

            if (sagPivot == null || Mathf.Approximately(sag, targetSag)) return;
            // Snap back instantly on respawn; settle with a small overshoot-free ease while damaged.
            sag = stage < 0 ? 0f : Mathf.MoveTowards(sag, targetSag, sagSpeed * Time.deltaTime * (1f + Mathf.Abs(targetSag - sag)));
            sagPivot.localRotation = pivotRest * Quaternion.AngleAxis(sag, sagAxis);
        }

        void Apply(int stage)
        {
            applied = stage;
            if (stages == null) return;
            for (int i = 0; i < stages.Length; i++)
            {
                bool on = i <= stage;
                if (stages[i].show != null)
                    foreach (var go in stages[i].show)
                        if (go != null) go.SetActive(on);
                if (stages[i].hide != null)
                    foreach (var go in stages[i].hide)
                        if (go != null) go.SetActive(!on);
            }

            targetSag = stage >= 0 ? stages[stage].sagDegrees : 0f;
            if (stage < 0 && sagPivot != null)
            {
                sag = 0f;
                sagPivot.localRotation = pivotRest;
            }
        }
    }
}

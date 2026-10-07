using UnityEngine;

namespace ScrapYardKing.Workers
{
    public enum WorkerRole
    {
        /// <summary>Carries raw/processed materials (blueprint unlock 15-30 min).</summary>
        Porter,
        /// <summary>Runs one machine faster (45-60 min).</summary>
        Operator,
        /// <summary>Moves finished goods to trucks (60-75 min).</summary>
        Loader,
        /// <summary>Serves customers (75-90 min).</summary>
        Seller
    }

    /// <summary>Hireable worker kind. Blueprint stats: speed, carry, efficiency. Porter unlock: 500 cash.</summary>
    [CreateAssetMenu(fileName = "Worker_", menuName = "Scrap Yard King/Workers/Worker Definition")]
    public sealed class WorkerDefinition : ScriptableObject
    {
        [SerializeField] string id;
        [SerializeField] string displayName;
        [SerializeField] Sprite icon;
        [Tooltip("One line for the hire announcement, e.g. HAULS SCRAP TO THE CRUSHER.")]
        [SerializeField] string tagline;
        [SerializeField] WorkerRole role;
        [SerializeField] Worker prefab;
        [Tooltip("WorkerSite id this kind works at. Empty = first site of its role.")]
        [SerializeField] string siteId;

        [Header("Stats")]
        [SerializeField, Min(0.1f)] float moveSpeed = 3.6f;
        [SerializeField, Min(1)] int carryCapacity = 6;
        [Tooltip("Multiplies pickup speed. 1 = same pace as the player.")]
        [SerializeField, Min(0.1f)] float efficiency = 0.8f;
        [SerializeField, Min(0.1f)] float pickupRadius = 1.6f;
        [Tooltip("Vacuums loose items off the ground (Scrap Porter). Off for workers that only use pads (Delivery Helper).")]
        [SerializeField] bool collectLooseItems = true;
        [Tooltip("Specialists at a post. Operator: machine speed multiplier while at the console. Seller: service speed multiplier at the counter.")]
        [SerializeField, Min(1f)] float workBoost = 1.5f;
        [Tooltip("Appears at the post instead of walking in from the yard entrance (posts outside the yard floor, e.g. the street side of a counter).")]
        [SerializeField] bool spawnAtSite;

        [Header("Hiring")]
        [Tooltip("Cost of the 1st, 2nd, ... hire. Its length is the maximum headcount.")]
        [SerializeField] long[] hireCosts = { 500 };

        public string Id => id;
        public string DisplayName => displayName;
        public Sprite Icon => icon;
        public string Tagline => tagline;
        public WorkerRole Role => role;
        public Worker Prefab => prefab;
        public string SiteId => siteId;
        public float MoveSpeed => moveSpeed;
        public int CarryCapacity => carryCapacity;
        public float Efficiency => efficiency;
        public float PickupRadius => pickupRadius;
        public bool CollectLooseItems => collectLooseItems;
        public float WorkBoost => workBoost;
        public bool SpawnAtSite => spawnAtSite;
        public int MaxCount => hireCosts.Length;

        public long HireCost(int alreadyHired) => hireCosts[Mathf.Clamp(alreadyHired, 0, hireCosts.Length - 1)];

        void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(id)) id = name;
            if (string.IsNullOrWhiteSpace(displayName)) displayName = name;
        }
    }
}

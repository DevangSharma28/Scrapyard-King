using ScrapYardKing.Feedback;
using ScrapYardKing.Items;
using UnityEngine;

namespace ScrapYardKing.Harvest
{
    /// <summary>Blueprint size classes. Each has a recommended drop range (blueprint p.7, Drop Table Rule).</summary>
    public enum ScrapSizeClass
    {
        Small,
        Medium,
        Large,
        Giant
    }

    /// <summary>
    /// Data for one kind of harvestable scrap (barrel, tire, car, excavator, giant truck...).
    /// Cut time is not authored separately: it falls out of <see cref="MaxHealth"/> and the cutter's damage per second,
    /// so cutter upgrades shorten it automatically (see <see cref="EstimateCutTime"/>).
    /// </summary>
    [CreateAssetMenu(fileName = "Scrap_", menuName = "Scrap Yard King/Harvest/Scrap Definition")]
    public sealed class ScrapDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] string id;
        [SerializeField] string displayName;
        [Tooltip("Blueprint tier: 1 = 0-15 min junk ... 4 = heavy machinery, 5 = boss/event scrap.")]
        [SerializeField, Range(1, 5)] int tier = 1;
        [SerializeField] ScrapSizeClass sizeClass;
        [SerializeField] ScrapObject prefab;
        [Tooltip("Optional look variants (e.g. sedan / SUV / hatchback wrecks). One is picked per spawn; empty = prefab only.")]
        [SerializeField] ScrapObject[] prefabVariants;

        [Header("Durability")]
        [SerializeField, Min(1f)] float maxHealth = 50f;
        [Tooltip("Cutters weaker than this cannot damage the object. Gates heavy scrap behind cut power upgrades.")]
        [SerializeField, Min(0f)] float minCutPower;

        [Header("Drops")]
        [SerializeField] ItemDefinition dropItem;
        [Tooltip("Inclusive piece range. Small 4-10, Medium 10-25, Large 25-60, Giant 120+.")]
        [SerializeField] Vector2Int dropAmount = new(4, 10);
        [Tooltip("Share of pieces released while cutting (as parts come off). The rest bursts out on break.")]
        [SerializeField, Range(0f, 1f)] float partDropShare = 0.35f;
        [SerializeField, Min(0f)] float dropLaunchSpeed = 6f;
        [SerializeField, Min(0f)] float dropSpread = 1f;
        [Tooltip("Seconds the break burst keeps erupting pieces (0 = all at once). Big objects read as a fountain of loot.")]
        [SerializeField, Min(0f)] float dropBurstDuration;

        [Header("Rare Drop")]
        [SerializeField] ItemDefinition rareDropItem;
        [SerializeField, Range(0f, 1f)] float rareDropChance;
        [SerializeField] Vector2Int rareDropAmount = new(1, 1);

        [Header("Rewards")]
        [SerializeField, Min(0)] int xpReward = 5;

        [Header("Spawning")]
        [SerializeField, Min(0f)] float respawnDelay = 8f;

        [Header("Feedback (optional overrides)")]
        [SerializeField] SfxDefinition hitSfx;
        [SerializeField] SfxDefinition breakSfx;
        [SerializeField] ParticleSystem hitVfx;
        [SerializeField] ParticleSystem breakVfx;
        [SerializeField, Min(0f)] float breakVfxScale = 1f;
        [SerializeField, Range(0f, 1f)] float breakShake = 0.35f;

        public string Id => id;
        public string DisplayName => displayName;
        public int Tier => tier;
        public ScrapSizeClass SizeClass => sizeClass;
        public ScrapObject Prefab => prefab;
        public float MaxHealth => maxHealth;
        public float MinCutPower => minCutPower;
        public ItemDefinition DropItem => dropItem;
        public Vector2Int DropAmount => dropAmount;
        public float PartDropShare => partDropShare;
        public float DropLaunchSpeed => dropLaunchSpeed;
        public float DropSpread => dropSpread;
        public float DropBurstDuration => dropBurstDuration;
        public ItemDefinition RareDropItem => rareDropItem;
        public float RareDropChance => rareDropChance;
        public int XpReward => xpReward;
        public float RespawnDelay => respawnDelay;
        public SfxDefinition HitSfx => hitSfx;
        public SfxDefinition BreakSfx => breakSfx;
        public ParticleSystem HitVfx => hitVfx;
        public ParticleSystem BreakVfx => breakVfx;
        public float BreakVfxScale => breakVfxScale;
        public float BreakShake => breakShake;

        /// <summary>Random look variant for the next spawn.</summary>
        public ScrapObject PickPrefab() =>
            prefabVariants != null && prefabVariants.Length > 0 ? prefabVariants[Random.Range(0, prefabVariants.Length)] : prefab;

        public int RollDropAmount() => Random.Range(dropAmount.x, dropAmount.y + 1);

        public int RollRareDropAmount() =>
            rareDropItem != null && Random.value < rareDropChance ? Random.Range(rareDropAmount.x, rareDropAmount.y + 1) : 0;

        public float EstimateCutTime(float damagePerSecond) =>
            damagePerSecond > 0f ? maxHealth / damagePerSecond : float.PositiveInfinity;

        public static Vector2Int RecommendedDropRange(ScrapSizeClass size) => size switch
        {
            ScrapSizeClass.Small => new Vector2Int(4, 10),
            ScrapSizeClass.Medium => new Vector2Int(10, 25),
            ScrapSizeClass.Large => new Vector2Int(25, 60),
            _ => new Vector2Int(120, 160)
        };

        void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(id)) id = name;
            if (string.IsNullOrWhiteSpace(displayName)) displayName = name;
            dropAmount.x = Mathf.Max(0, dropAmount.x);
            dropAmount.y = Mathf.Max(dropAmount.x, dropAmount.y);
            rareDropAmount.x = Mathf.Max(0, rareDropAmount.x);
            rareDropAmount.y = Mathf.Max(rareDropAmount.x, rareDropAmount.y);
        }
    }
}

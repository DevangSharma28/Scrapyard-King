using UnityEngine;

namespace ScrapYardKing.Items
{
    /// <summary>
    /// A physical resource that exists in the world: raw scrap, mixed metal, iron, copper, ingots, plates...
    /// Every resource is carried and moved as a <see cref="WorldItem"/> so the player can follow it through the chain.
    /// </summary>
    [CreateAssetMenu(fileName = "Item_", menuName = "Scrap Yard King/Items/Item Definition")]
    public sealed class ItemDefinition : ScriptableObject
    {
        [SerializeField] string id;
        [SerializeField] string displayName;
        [SerializeField] Sprite icon;
        [Tooltip("Used for popups and UI accents.")]
        [SerializeField] Color color = Color.white;
        [SerializeField] WorldItem prefab;
        [Tooltip("Vertical space one unit takes in a carry stack.")]
        [SerializeField, Min(0.01f)] float stackHeight = 0.3f;
        [Tooltip("Base sell value in cash (0 = cannot be sold, e.g. raw scrap). Final price is scaled by sell desk upgrades.")]
        [SerializeField, Min(0)] int baseValue;

        public string Id => id;
        public string DisplayName => displayName;
        public Sprite Icon => icon;
        public Color Color => color;
        public WorldItem Prefab => prefab;
        public float StackHeight => stackHeight;
        public int BaseValue => baseValue;

        void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(id)) id = name;
            if (string.IsNullOrWhiteSpace(displayName)) displayName = name;
        }
    }
}

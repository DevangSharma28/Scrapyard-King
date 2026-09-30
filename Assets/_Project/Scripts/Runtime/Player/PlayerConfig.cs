using UnityEngine;

namespace ScrapYardKing.Player
{
    /// <summary>
    /// Base player stats at level 0. Blueprint p.9 targets (start → 90 min): cut power 10 → 50, carry 8 → 25.
    /// Upgrades add modifiers on top via <see cref="PlayerStats"/>; this asset is never mutated at runtime.
    /// </summary>
    [CreateAssetMenu(fileName = "PlayerConfig", menuName = "Scrap Yard King/Player/Player Config")]
    public sealed class PlayerConfig : ScriptableObject
    {
        [Header("Movement")]
        [SerializeField, Min(0f)] float moveSpeed = 5.5f;
        [SerializeField, Min(0f)] float acceleration = 45f;
        [SerializeField, Min(0f)] float deceleration = 60f;
        [Tooltip("Degrees per second.")]
        [SerializeField, Min(0f)] float turnSpeed = 900f;

        [Header("Cutting")]
        [Tooltip("Damage per hit.")]
        [SerializeField, Min(0f)] float cutPower = 10f;
        [Tooltip("Hits per second.")]
        [SerializeField, Min(0.1f)] float cutRate = 5f;
        [Tooltip("Max distance from the player to a scrap surface for auto-cutting.")]
        [SerializeField, Min(0f)] float cutRange = 1.8f;

        [Header("Carrying")]
        [SerializeField, Min(1)] int carryCapacity = 8;
        [SerializeField, Min(0f)] float pickupRadius = 2.2f;

        public float MoveSpeed => moveSpeed;
        public float Acceleration => acceleration;
        public float Deceleration => deceleration;
        public float TurnSpeed => turnSpeed;
        public float CutPower => cutPower;
        public float CutRate => cutRate;
        public float CutRange => cutRange;
        public int CarryCapacity => carryCapacity;
        public float PickupRadius => pickupRadius;
    }
}

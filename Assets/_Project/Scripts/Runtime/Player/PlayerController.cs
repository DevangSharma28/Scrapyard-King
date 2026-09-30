using ScrapYardKing.Harvest;
using UnityEngine;

namespace ScrapYardKing.Player
{
    /// <summary>
    /// Camera-relative top-down movement on a CharacterController. Faces the move direction, or the cutter's
    /// target while standing still so cutting always reads clearly.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        [SerializeField] PlayerInputReader input;
        [SerializeField] PlayerStats stats;
        [SerializeField] HarvestTool harvestTool;
        [Tooltip("Movement is relative to this transform's yaw. Defaults to the main camera.")]
        [SerializeField] Transform cameraReference;
        [SerializeField] float gravity = -30f;

        CharacterController body;
        Vector3 planarVelocity;
        float verticalVelocity;

        public Vector3 Velocity => planarVelocity;
        public float Speed01 => stats != null && stats.MoveSpeed > 0f ? Mathf.Clamp01(planarVelocity.magnitude / stats.MoveSpeed) : 0f;

        void Awake() => body = GetComponent<CharacterController>();

        void Start()
        {
            if (cameraReference == null && Camera.main != null) cameraReference = Camera.main.transform;
        }

        /// <summary>Moves the character without interpolation (spawn, respawn, debug).</summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            body.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            body.enabled = true;
            planarVelocity = Vector3.zero;
            verticalVelocity = 0f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || input == null || stats == null) return;

            var config = stats.Config;
            Vector3 desired = ToWorld(input.Move) * stats.MoveSpeed;
            float rate = desired.sqrMagnitude > 0.0001f ? config.Acceleration : config.Deceleration;
            planarVelocity = Vector3.MoveTowards(planarVelocity, desired, rate * dt);

            verticalVelocity = body.isGrounded ? -2f : verticalVelocity + gravity * dt;
            body.Move((planarVelocity + Vector3.up * verticalVelocity) * dt);

            UpdateFacing(desired, config.TurnSpeed * dt);
        }

        void UpdateFacing(Vector3 desiredVelocity, float maxDegrees)
        {
            Vector3 look = desiredVelocity;
            if (look.sqrMagnitude < 0.01f && harvestTool != null && harvestTool.CurrentTarget != null)
                look = harvestTool.CurrentTarget.Center - transform.position;

            look.y = 0f;
            if (look.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(look), maxDegrees);
        }

        Vector3 ToWorld(Vector2 move)
        {
            Vector3 forward = cameraReference != null ? cameraReference.forward : Vector3.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            return forward * move.y + right * move.x;
        }
    }
}

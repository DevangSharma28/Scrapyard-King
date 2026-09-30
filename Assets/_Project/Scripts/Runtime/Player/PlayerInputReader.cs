using ScrapYardKing.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ScrapYardKing.Player
{
    /// <summary>
    /// Single source of movement intent. The on-screen joystick wins while touched; otherwise the Input System
    /// Move action (WASD/arrows/gamepad) is read, which keeps editor testing and device play on one code path.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInputReader : MonoBehaviour
    {
        const string ProjectWideMoveAction = "Player/Move";

        [Tooltip("Optional. Falls back to the project-wide 'Player/Move' action, then to built-in WASD/arrows/gamepad bindings.")]
        [SerializeField] InputActionReference moveAction;
        [SerializeField] VirtualJoystick joystick;

        InputAction move;
        bool ownsAction;

        public Vector2 Move { get; private set; }
        public bool HasInput => Move.sqrMagnitude > 0.0001f;

        public VirtualJoystick Joystick
        {
            get => joystick;
            set => joystick = value;
        }

        void OnEnable()
        {
            var projectActions = InputSystem.actions;
            move = moveAction != null ? moveAction.action
                : projectActions != null ? projectActions.FindAction(ProjectWideMoveAction)
                : null;
            if (move == null)
            {
                move = CreateFallbackMoveAction();
                ownsAction = true;
            }

            if (!move.enabled) move.Enable();
        }

        void OnDisable()
        {
            Move = Vector2.zero;
            if (!ownsAction || move == null) return;
            move.Disable();
            move.Dispose();
            move = null;
            ownsAction = false;
        }

        void Update()
        {
            Vector2 value = joystick != null && joystick.IsHeld
                ? joystick.Value
                : move != null ? move.ReadValue<Vector2>() : Vector2.zero;
            Move = Vector2.ClampMagnitude(value, 1f);
        }

        static InputAction CreateFallbackMoveAction()
        {
            var action = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
            action.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            action.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            action.AddBinding("<Gamepad>/leftStick");
            return action;
        }
    }
}

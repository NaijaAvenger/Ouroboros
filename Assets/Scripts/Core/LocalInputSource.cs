using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Ouroboros.Core
{
    /// <summary>
    /// Device-level input for the local player. Prefers the <b>Input System</b> package (keyboard, mouse
    /// and gamepad, through the rebindable <see cref="HeistInputActions"/>) whenever it is enabled in
    /// Player Settings ("Input System Package" or "Both"); falls back to the legacy Input Manager only
    /// when that is the sole active handler.
    ///
    /// This is the only place that reads devices. <c>GameSessionManager</c> turns these samples into
    /// <see cref="NetworkInputData"/>; nothing else should touch <c>UnityEngine.Input</c> or
    /// <c>UnityEngine.InputSystem</c> directly.
    ///
    /// Default gamepad map: left stick move · right stick look · A jump · X interact · L3 sprint ·
    /// D-pad abilities 1-4 · RT primary · LT secondary · LB utility · RB gadget · Start cursor · Select debug HUD.
    /// </summary>
    public static class LocalInputSource
    {
#if ENABLE_INPUT_SYSTEM
        public const bool UsingInputSystem = true;
#else
        public const bool UsingInputSystem = false;
#endif

        /// <summary>Legacy "Mouse X/Y" axes are pre-scaled by 0.1; apply the same to raw pixel deltas for parity.</summary>
        private const float MouseDeltaScale = 0.1f;

        /// <summary>True when the most recent meaningful input came from a gamepad (drives HUD hints).</summary>
        public static bool LastDeviceWasGamepad { get; private set; }

#if ENABLE_INPUT_SYSTEM
        private static HeistInputActions Actions => HeistInputActions.Instance;

        private static void NoteDevice(InputAction action)
        {
            var control = action.activeControl;
            if (control == null) return;
            LastDeviceWasGamepad = control.device is Gamepad;
        }
#endif

        // ------------------------------------------------------------------
        // Axes

        public static Vector2 Move()
        {
#if ENABLE_INPUT_SYSTEM
            // [v0.3] previously polled Keyboard.current / Gamepad.current directly; now action-based (rebindable)
            Vector2 v = Actions.Move.ReadValue<Vector2>();
            if (v.sqrMagnitude > 0.0001f) NoteDevice(Actions.Move);
            return Vector2.ClampMagnitude(v, 1f);
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Vector2.ClampMagnitude(new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")), 1f);
#else
            return Vector2.zero;
#endif
        }

        /// <summary>
        /// Look delta in degrees for this frame: mouse (sensitivity-scaled) plus right stick
        /// (degrees per second × deltaTime). x = yaw, y = pitch (positive = up).
        /// </summary>
        public static Vector2 LookDelta(float mouseSensitivity, float stickDegreesPerSecond, float deltaTime)
        {
#if ENABLE_INPUT_SYSTEM
            Vector2 look = Vector2.zero;

            Vector2 mouse = Actions.LookMouse.ReadValue<Vector2>() * MouseDeltaScale * mouseSensitivity;
            if (mouse.sqrMagnitude > 0f) { look += mouse; LastDeviceWasGamepad = false; }

            Vector2 stick = Actions.LookStick.ReadValue<Vector2>();
            if (stick.sqrMagnitude > 0.01f)
            {
                look += stick * stickDegreesPerSecond * deltaTime;
                LastDeviceWasGamepad = true;
            }
            return look;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * mouseSensitivity;
#else
            return Vector2.zero;
#endif
        }

        // ------------------------------------------------------------------
        // Buttons

        /// <summary>Button currently held.</summary>
        public static bool Held(InputButtons button)
        {
#if ENABLE_INPUT_SYSTEM
            var action = Actions.Get(button);
            if (action == null) return false;
            bool held = action.IsPressed();
            if (held) NoteDevice(action);
            return held;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return LegacyHeld(button);
#else
            return false;
#endif
        }

        /// <summary>Button pressed during this frame.</summary>
        public static bool Pressed(InputButtons button)
        {
#if ENABLE_INPUT_SYSTEM
            var action = Actions.Get(button);
            if (action == null) return false;
            bool pressed = action.WasPressedThisFrame();
            if (pressed) NoteDevice(action);
            return pressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return LegacyPressed(button);
#else
            return false;
#endif
        }

        /// <summary>Esc / Start: toggle cursor lock.</summary>
        public static bool CursorTogglePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Actions.CursorToggle.WasPressedThisFrame();
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton7);
#else
            return false;
#endif
        }

        /// <summary>F1 / Select: toggle the debug overlay.</summary>
        public static bool DebugHudTogglePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Actions.DebugHudToggle.WasPressedThisFrame();
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.F1) || Input.GetKeyDown(KeyCode.JoystickButton6);
#else
            return false;
#endif
        }

        // ------------------------------------------------------------------
        // HUD hints

        /// <summary>Short label for a button on the device last used (e.g. "F" or "X"). Reflects rebinds.</summary>
        public static string Hint(InputButtons button)
        {
#if ENABLE_INPUT_SYSTEM
            string s = Actions.DisplayString(button, LastDeviceWasGamepad);
            if (!string.IsNullOrEmpty(s) && s != "?") return s;
#endif
            return DefaultHint(button, LastDeviceWasGamepad);
        }

        public static string DefaultHint(InputButtons button, bool gamepad)
        {
            if (gamepad)
            {
                switch (button)
                {
                    case InputButtons.Jump:      return "A";
                    case InputButtons.Sprint:    return "L3";
                    case InputButtons.Interact:  return "X";
                    case InputButtons.Ability1:  return "D-Up";
                    case InputButtons.Ability2:  return "D-Right";
                    case InputButtons.Ability3:  return "D-Down";
                    case InputButtons.Ability4:  return "D-Left";
                    case InputButtons.Primary:   return "RT";
                    case InputButtons.Secondary: return "LT";
                    case InputButtons.Utility:   return "LB";
                    case InputButtons.Gadget:    return "RB";
                }
            }
            switch (button)
            {
                case InputButtons.Jump:      return "Space";
                case InputButtons.Sprint:    return "Shift";
                case InputButtons.Interact:  return "F";
                case InputButtons.Ability1:  return "1";
                case InputButtons.Ability2:  return "2";
                case InputButtons.Ability3:  return "3";
                case InputButtons.Ability4:  return "4";
                case InputButtons.Primary:   return "LMB";
                case InputButtons.Secondary: return "RMB";
                case InputButtons.Utility:   return "Q";
                case InputButtons.Gadget:    return "E";
            }
            return "?";
        }

        // ------------------------------------------------------------------
        // Legacy fallback: keyboard/mouse plus the standard Xbox button indices (Windows layout).

#if !ENABLE_INPUT_SYSTEM && ENABLE_LEGACY_INPUT_MANAGER
        private static bool LegacyHeld(InputButtons button)
        {
            switch (button)
            {
                case InputButtons.Jump:      return Input.GetKey(KeyCode.Space)     || Input.GetKey(KeyCode.JoystickButton0);
                case InputButtons.Sprint:    return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.JoystickButton8);
                case InputButtons.Interact:  return Input.GetKey(KeyCode.F)         || Input.GetKey(KeyCode.JoystickButton2);
                case InputButtons.Ability1:  return Input.GetKey(KeyCode.Alpha1);
                case InputButtons.Ability2:  return Input.GetKey(KeyCode.Alpha2);
                case InputButtons.Ability3:  return Input.GetKey(KeyCode.Alpha3);
                case InputButtons.Ability4:  return Input.GetKey(KeyCode.Alpha4);
                case InputButtons.Primary:   return Input.GetMouseButton(0);
                case InputButtons.Secondary: return Input.GetMouseButton(1);
                case InputButtons.Utility:   return Input.GetKey(KeyCode.Q)         || Input.GetKey(KeyCode.JoystickButton4);
                case InputButtons.Gadget:    return Input.GetKey(KeyCode.E)         || Input.GetKey(KeyCode.JoystickButton5);
            }
            return false;
        }

        private static bool LegacyPressed(InputButtons button)
        {
            switch (button)
            {
                case InputButtons.Jump:      return Input.GetKeyDown(KeyCode.Space)     || Input.GetKeyDown(KeyCode.JoystickButton0);
                case InputButtons.Sprint:    return Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.JoystickButton8);
                case InputButtons.Interact:  return Input.GetKeyDown(KeyCode.F)         || Input.GetKeyDown(KeyCode.JoystickButton2);
                case InputButtons.Ability1:  return Input.GetKeyDown(KeyCode.Alpha1);
                case InputButtons.Ability2:  return Input.GetKeyDown(KeyCode.Alpha2);
                case InputButtons.Ability3:  return Input.GetKeyDown(KeyCode.Alpha3);
                case InputButtons.Ability4:  return Input.GetKeyDown(KeyCode.Alpha4);
                case InputButtons.Primary:   return Input.GetMouseButtonDown(0);
                case InputButtons.Secondary: return Input.GetMouseButtonDown(1);
                case InputButtons.Utility:   return Input.GetKeyDown(KeyCode.Q)         || Input.GetKeyDown(KeyCode.JoystickButton4);
                case InputButtons.Gadget:    return Input.GetKeyDown(KeyCode.E)         || Input.GetKeyDown(KeyCode.JoystickButton5);
            }
            return false;
        }
#endif
    }
}

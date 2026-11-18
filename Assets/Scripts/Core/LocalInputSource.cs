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
    /// D-pad abilities 1-4 · RT fire · LT aim · LB utility · RB gadget · Select swap weapon · Start menu.
    /// Mouse: left fires, right aims, the wheel (or X) swaps weapon.
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
            // (v0.13) the wheel is not a real button: also read it directly, so a notch is never missed
            if (!pressed && (button == InputButtons.NextWeapon || button == InputButtons.PrevWeapon)) pressed = WheelNotch(button);
            return pressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return LegacyPressed(button);
#else
            return false;
#endif
        }

#if ENABLE_INPUT_SYSTEM
        /// <summary>(v0.13) True on a frame the mouse wheel moved in this button's direction.</summary>
        private static bool WheelNotch(InputButtons button)
        {
            var mouse = Mouse.current;
            if (mouse == null) return false;
            float y = mouse.scroll.ReadValue().y;
            bool notch = button == InputButtons.NextWeapon ? y > 0.01f : y < -0.01f;
            if (notch) LastDeviceWasGamepad = false;
            return notch;
        }
#endif

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
            // [v0.12] return Input.GetKeyDown(KeyCode.F1) || Input.GetKeyDown(KeyCode.JoystickButton6);
            return Input.GetKeyDown(KeyCode.F1); // (v0.13) Select now swaps weapons
#else
            return false;
#endif
        }

        // ------------------------------------------------------------------
        // Keypad (v0.7): digits, submit, cancel

        /// <summary>Digit pressed this frame (0-9), or -1.</summary>
        public static int DigitPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return -1;
            if (kb.digit0Key.wasPressedThisFrame || kb.numpad0Key.wasPressedThisFrame) return 0;
            if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) return 1;
            if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) return 2;
            if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) return 3;
            if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame) return 4;
            if (kb.digit5Key.wasPressedThisFrame || kb.numpad5Key.wasPressedThisFrame) return 5;
            if (kb.digit6Key.wasPressedThisFrame || kb.numpad6Key.wasPressedThisFrame) return 6;
            if (kb.digit7Key.wasPressedThisFrame || kb.numpad7Key.wasPressedThisFrame) return 7;
            if (kb.digit8Key.wasPressedThisFrame || kb.numpad8Key.wasPressedThisFrame) return 8;
            if (kb.digit9Key.wasPressedThisFrame || kb.numpad9Key.wasPressedThisFrame) return 9;
            return -1;
#elif ENABLE_LEGACY_INPUT_MANAGER
            for (int i = 0; i <= 9; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha0 + i) || Input.GetKeyDown(KeyCode.Keypad0 + i)) return i;
            }
            return -1;
#else
            return -1;
#endif
        }

        public static bool SubmitPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            return (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
                || (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.JoystickButton0);
#else
            return false;
#endif
        }

        public static bool CancelPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                || (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame);
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton1);
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

        /// <summary>
        /// (v0.13) Label for a button on a named device, for the controls list. Unlike <see cref="Hint"/> it does
        /// not depend on which device was touched last.
        /// </summary>
        public static string BindingLabel(InputButtons button, bool gamepad)
        {
#if ENABLE_INPUT_SYSTEM
            string s = Actions.DisplayString(button, gamepad);
            if (!string.IsNullOrEmpty(s) && s != "?") return s;
#endif
            return DefaultHint(button, gamepad);
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
                    case InputButtons.Primary:   return "RT";   // fire
                    case InputButtons.Secondary: return "LT";
                    case InputButtons.Utility:   return "LB";
                    case InputButtons.Gadget:    return "RB";
                    case InputButtons.Reload:    return "Y";
                    case InputButtons.Slide:     return "B";
                    case InputButtons.Grapple:   return "R3";
                    case InputButtons.NextWeapon: return "Select";
                    case InputButtons.PrevWeapon: return "Select";
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
                case InputButtons.Reload:    return "R";
                case InputButtons.Slide:     return "Ctrl";
                case InputButtons.Grapple:   return "G";
                case InputButtons.NextWeapon: return "Wheel";
                case InputButtons.PrevWeapon: return "Wheel";
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
                case InputButtons.Reload:    return Input.GetKey(KeyCode.R)         || Input.GetKey(KeyCode.JoystickButton3);
                case InputButtons.Slide:     return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.JoystickButton1);
                case InputButtons.Grapple:   return Input.GetKey(KeyCode.G)         || Input.GetKey(KeyCode.JoystickButton9);
                case InputButtons.NextWeapon: return Input.GetAxis("Mouse ScrollWheel") > 0f || Input.GetKey(KeyCode.X) || Input.GetKey(KeyCode.JoystickButton6);
                case InputButtons.PrevWeapon: return Input.GetAxis("Mouse ScrollWheel") < 0f;
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
                case InputButtons.Reload:    return Input.GetKeyDown(KeyCode.R)         || Input.GetKeyDown(KeyCode.JoystickButton3);
                case InputButtons.Slide:     return Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.JoystickButton1);
                case InputButtons.Grapple:   return Input.GetKeyDown(KeyCode.G)         || Input.GetKeyDown(KeyCode.JoystickButton9);
                case InputButtons.NextWeapon: return Input.GetAxis("Mouse ScrollWheel") > 0f || Input.GetKeyDown(KeyCode.X) || Input.GetKeyDown(KeyCode.JoystickButton6);
                case InputButtons.PrevWeapon: return Input.GetAxis("Mouse ScrollWheel") < 0f;
            }
            return false;
        }
#endif
    }
}

using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Ouroboros.Core
{
    /// <summary>
    /// Device-level input for the local player. Prefers the <b>Input System</b> package (keyboard, mouse
    /// and gamepad) whenever it is enabled in Player Settings ("Input System Package" or "Both"); falls
    /// back to the legacy Input Manager only when that is the sole active handler.
    ///
    /// This is the only place that reads devices. <c>GameSessionManager</c> turns these samples into
    /// <see cref="NetworkInputData"/>; nothing else should touch <c>UnityEngine.Input</c> or
    /// <c>UnityEngine.InputSystem</c> directly.
    ///
    /// Gamepad map: left stick move · right stick look · A jump · X interact · L3 sprint ·
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

        // ------------------------------------------------------------------
        // Axes

        public static Vector2 Move()
        {
#if ENABLE_INPUT_SYSTEM
            Vector2 v = Vector2.zero;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) v.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  v.x -= 1f;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed)    v.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed)  v.y -= 1f;
            }
            if (v.sqrMagnitude > 0f) LastDeviceWasGamepad = false;

            var pad = Gamepad.current;
            if (pad != null)
            {
                Vector2 stick = pad.leftStick.ReadValue();
                if (stick.sqrMagnitude > 0.01f) { v += stick; LastDeviceWasGamepad = true; }
            }
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
            var mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 d = mouse.delta.ReadValue() * MouseDeltaScale * mouseSensitivity;
                if (d.sqrMagnitude > 0f) { look += d; LastDeviceWasGamepad = false; }
            }
            var pad = Gamepad.current;
            if (pad != null)
            {
                Vector2 stick = pad.rightStick.ReadValue();
                if (stick.sqrMagnitude > 0.01f)
                {
                    look += stick * stickDegreesPerSecond * deltaTime;
                    LastDeviceWasGamepad = true;
                }
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
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            var pad = Gamepad.current;
            bool k = kb != null && KeyControl(kb, mouse, button) is { isPressed: true };
            bool g = pad != null && PadControl(pad, button) is { isPressed: true };
            if (g) LastDeviceWasGamepad = true; else if (k) LastDeviceWasGamepad = false;
            return k || g;
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
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            var pad = Gamepad.current;
            bool k = kb != null && KeyControl(kb, mouse, button) is { wasPressedThisFrame: true };
            bool g = pad != null && PadControl(pad, button) is { wasPressedThisFrame: true };
            if (g) LastDeviceWasGamepad = true; else if (k) LastDeviceWasGamepad = false;
            return k || g;
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
            return (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                || (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame);
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
            return (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
                || (Gamepad.current != null && Gamepad.current.selectButton.wasPressedThisFrame);
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.F1) || Input.GetKeyDown(KeyCode.JoystickButton6);
#else
            return false;
#endif
        }

        // ------------------------------------------------------------------
        // HUD hints

        /// <summary>Short label for a button on the device last used (e.g. "F" or "X").</summary>
        public static string Hint(InputButtons button)
        {
            if (LastDeviceWasGamepad)
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
        // Mappings

#if ENABLE_INPUT_SYSTEM
        private static UnityEngine.InputSystem.Controls.ButtonControl KeyControl(Keyboard kb, Mouse mouse, InputButtons button)
        {
            switch (button)
            {
                case InputButtons.Jump:      return kb.spaceKey;
                case InputButtons.Sprint:    return kb.leftShiftKey;
                case InputButtons.Interact:  return kb.fKey;
                case InputButtons.Ability1:  return kb.digit1Key;
                case InputButtons.Ability2:  return kb.digit2Key;
                case InputButtons.Ability3:  return kb.digit3Key;
                case InputButtons.Ability4:  return kb.digit4Key;
                case InputButtons.Primary:   return mouse != null ? mouse.leftButton : null;
                case InputButtons.Secondary: return mouse != null ? mouse.rightButton : null;
                case InputButtons.Utility:   return kb.qKey;
                case InputButtons.Gadget:    return kb.eKey;
            }
            return null;
        }

        private static UnityEngine.InputSystem.Controls.ButtonControl PadControl(Gamepad pad, InputButtons button)
        {
            switch (button)
            {
                case InputButtons.Jump:      return pad.buttonSouth;
                case InputButtons.Sprint:    return pad.leftStickButton;
                case InputButtons.Interact:  return pad.buttonWest;
                case InputButtons.Ability1:  return pad.dpad.up;
                case InputButtons.Ability2:  return pad.dpad.right;
                case InputButtons.Ability3:  return pad.dpad.down;
                case InputButtons.Ability4:  return pad.dpad.left;
                case InputButtons.Primary:   return pad.rightTrigger;
                case InputButtons.Secondary: return pad.leftTrigger;
                case InputButtons.Utility:   return pad.leftShoulder;
                case InputButtons.Gadget:    return pad.rightShoulder;
            }
            return null;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        // Legacy fallback: keyboard/mouse plus the standard Xbox button indices (Windows layout).
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

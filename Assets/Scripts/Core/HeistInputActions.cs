#if ENABLE_INPUT_SYSTEM
using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ouroboros.Core
{
    /// <summary>
    /// The game's Input System actions, built in code so there is no .inputactions asset to keep in
    /// sync. Every <see cref="InputButtons"/> entry has a Keyboard-group and a Gamepad-group binding,
    /// both rebindable at runtime; overrides persist in PlayerPrefs.
    ///
    /// Only <see cref="LocalInputSource"/> reads these. UI that wants to show or change bindings uses
    /// <see cref="DisplayString"/>, <see cref="StartRebind"/> and <see cref="ResetBindings"/>.
    /// </summary>
    public sealed class HeistInputActions : IDisposable
    {
        public const string KeyboardGroup = "Keyboard";
        public const string GamepadGroup = "Gamepad";
        private const string PrefsKey = "ouroboros.bindings";

        private static HeistInputActions instance;
        public static HeistInputActions Instance => instance ??= new HeistInputActions();

        public InputActionAsset Asset { get; }
        public InputActionMap Map { get; }
        public InputAction Move { get; }
        public InputAction LookMouse { get; }
        public InputAction LookStick { get; }
        public InputAction CursorToggle { get; }
        public InputAction DebugHudToggle { get; }

        private readonly InputAction[] buttons = new InputAction[(int)InputButtons.Reload + 1];
        private InputActionRebindingExtensions.RebindingOperation activeRebind;

        /// <summary>Raised after any rebind completes or bindings are reset.</summary>
        public event Action BindingsChanged;

        private HeistInputActions()
        {
            Asset = ScriptableObject.CreateInstance<InputActionAsset>();
            Asset.name = "OuroborosActions";
            Map = Asset.AddActionMap("Player");

            Move = Map.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w", groups: KeyboardGroup)
                .With("Down", "<Keyboard>/s", groups: KeyboardGroup)
                .With("Left", "<Keyboard>/a", groups: KeyboardGroup)
                .With("Right", "<Keyboard>/d", groups: KeyboardGroup);
            Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow", groups: KeyboardGroup)
                .With("Down", "<Keyboard>/downArrow", groups: KeyboardGroup)
                .With("Left", "<Keyboard>/leftArrow", groups: KeyboardGroup)
                .With("Right", "<Keyboard>/rightArrow", groups: KeyboardGroup);
            Move.AddBinding("<Gamepad>/leftStick", groups: GamepadGroup);

            LookMouse = Map.AddAction("LookMouse", InputActionType.Value, expectedControlLayout: "Vector2");
            LookMouse.AddBinding("<Mouse>/delta", groups: KeyboardGroup);

            LookStick = Map.AddAction("LookStick", InputActionType.Value, expectedControlLayout: "Vector2");
            LookStick.AddBinding("<Gamepad>/rightStick", groups: GamepadGroup);

            Button(InputButtons.Jump,      "Jump",      "<Keyboard>/space",     "<Gamepad>/buttonSouth");
            Button(InputButtons.Sprint,    "Sprint",    "<Keyboard>/leftShift", "<Gamepad>/leftStickPress");
            Button(InputButtons.Interact,  "Interact",  "<Keyboard>/f",         "<Gamepad>/buttonWest");
            Button(InputButtons.Ability1,  "Ability1",  "<Keyboard>/1",         "<Gamepad>/dpad/up");
            Button(InputButtons.Ability2,  "Ability2",  "<Keyboard>/2",         "<Gamepad>/dpad/right");
            Button(InputButtons.Ability3,  "Ability3",  "<Keyboard>/3",         "<Gamepad>/dpad/down");
            Button(InputButtons.Ability4,  "Ability4",  "<Keyboard>/4",         "<Gamepad>/dpad/left");
            Button(InputButtons.Primary,   "Primary",   "<Mouse>/leftButton",   "<Gamepad>/rightTrigger");
            Button(InputButtons.Secondary, "Secondary", "<Mouse>/rightButton",  "<Gamepad>/leftTrigger");
            Button(InputButtons.Utility,   "Utility",   "<Keyboard>/q",         "<Gamepad>/leftShoulder");
            Button(InputButtons.Gadget,    "Gadget",    "<Keyboard>/e",         "<Gamepad>/rightShoulder");
            Button(InputButtons.Reload,    "Reload",    "<Keyboard>/r",         "<Gamepad>/buttonNorth");

            CursorToggle = Map.AddAction("CursorToggle", InputActionType.Button);
            CursorToggle.AddBinding("<Keyboard>/escape", groups: KeyboardGroup);
            CursorToggle.AddBinding("<Gamepad>/start", groups: GamepadGroup);

            DebugHudToggle = Map.AddAction("DebugHudToggle", InputActionType.Button);
            DebugHudToggle.AddBinding("<Keyboard>/f1", groups: KeyboardGroup);
            DebugHudToggle.AddBinding("<Gamepad>/select", groups: GamepadGroup);

            LoadBindings();
            Map.Enable();
        }

        private void Button(InputButtons id, string name, string keyboardPath, string gamepadPath)
        {
            var action = Map.AddAction(name, InputActionType.Button);
            action.AddBinding(keyboardPath, groups: KeyboardGroup);
            action.AddBinding(gamepadPath, groups: GamepadGroup);
            buttons[(int)id] = action;
        }

        public InputAction Get(InputButtons button)
        {
            int i = (int)button;
            return i >= 0 && i < buttons.Length ? buttons[i] : null;
        }

        // ------------------------------------------------------------------
        // Display / rebinding

        /// <summary>Human-readable binding for the given device group (e.g. "F" or "X").</summary>
        public string DisplayString(InputButtons button, bool gamepad)
        {
            var action = Get(button);
            if (action == null) return "?";
            try
            {
                return action.GetBindingDisplayString(group: gamepad ? GamepadGroup : KeyboardGroup);
            }
            catch (Exception)
            {
                return "?";
            }
        }

        /// <summary>
        /// Starts listening for a new binding for <paramref name="button"/> on the keyboard/mouse or
        /// gamepad. <paramref name="onDone"/> receives true on success, false if cancelled.
        /// </summary>
        public void StartRebind(InputButtons button, bool gamepad, Action<bool> onDone = null)
        {
            var action = Get(button);
            if (action == null) { onDone?.Invoke(false); return; }

            CancelRebind();

            int bindingIndex = action.GetBindingIndex(group: gamepad ? GamepadGroup : KeyboardGroup);
            if (bindingIndex < 0) { onDone?.Invoke(false); return; }

            action.Disable();
            var op = action.PerformInteractiveRebinding(bindingIndex)
                .WithCancelingThrough("<Keyboard>/escape")
                .OnMatchWaitForAnother(0.1f);

            if (gamepad) op.WithControlsExcluding("<Keyboard>").WithControlsExcluding("<Mouse>");
            else op.WithControlsExcluding("<Gamepad>");

            op.OnComplete(o =>
            {
                o.Dispose();
                activeRebind = null;
                action.Enable();
                SaveBindings();
                BindingsChanged?.Invoke();
                onDone?.Invoke(true);
            });
            op.OnCancel(o =>
            {
                o.Dispose();
                activeRebind = null;
                action.Enable();
                onDone?.Invoke(false);
            });

            activeRebind = op.Start();
        }

        public void CancelRebind()
        {
            if (activeRebind == null) return;
            activeRebind.Cancel();
            activeRebind = null;
        }

        public void ResetBindings()
        {
            CancelRebind();
            Asset.RemoveAllBindingOverrides();
            PlayerPrefs.DeleteKey(PrefsKey);
            BindingsChanged?.Invoke();
        }

        private void SaveBindings()
        {
            PlayerPrefs.SetString(PrefsKey, Asset.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
        }

        private void LoadBindings()
        {
            string json = PlayerPrefs.GetString(PrefsKey, "");
            if (string.IsNullOrEmpty(json)) return;
            try { Asset.LoadBindingOverridesFromJson(json); }
            catch (Exception e) { Debug.LogWarning($"[HeistInputActions] Could not load binding overrides: {e.Message}"); }
        }

        public void Dispose()
        {
            CancelRebind();
            Map.Disable();
            if (Asset != null) UnityEngine.Object.Destroy(Asset);
            if (instance == this) instance = null;
        }
    }
}
#endif

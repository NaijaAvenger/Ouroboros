using UnityEngine;
using Fusion;

namespace Ouroboros.Player
{
    /// <summary>
    /// Player controller integrating class abilities, equipment, and network synchronization.
    ///
    /// v0.2: input arrives through Fusion's input pipeline (<see cref="Core.NetworkInputData"/>) so the
    /// same code runs on the input authority (prediction) and on the state authority (truth). Requires a
    /// <c>NetworkTransform</c> on the prefab so proxies see the resulting movement.
    /// </summary>
    [RequireComponent(typeof(Network.NetworkPlayer))]
    public class PlayerController : NetworkBehaviour
    {
        [Header("Components")]
        [SerializeField] private CharacterController characterController;
        [SerializeField] private Transform cameraTransform;

        [Header("Player Configuration")]
        // [v0.1] [SerializeField] private float mouseSensitivity = 2f; // moved to GameSessionManager (input is sampled there)
        [SerializeField] private float jumpHeight = 2f;
        [SerializeField] private float gravity = -9.81f;
        [SerializeField] private float defaultMovementSpeed = 5f;
        [SerializeField] private float groundingForce = -2f;

        private Network.NetworkPlayer networkPlayer;
        private Core.BasePlayerClass currentClass;
        private Equipment.EquipmentLoadout equipmentLoadout;
        private Equipment.NetworkLoadout networkLoadout;
        private NetworkTransform networkTransform;

        // Vertical velocity is part of the simulation state so prediction/resimulation stays consistent.
        [Networked] private float VerticalVelocity { get; set; }
        [Networked] private NetworkButtons PreviousButtons { get; set; }

        // [v0.1] private Vector3 velocity;
        // [v0.1] private float verticalRotation = 0f;
        private float localPitch;

        public Network.NetworkPlayer NetworkPlayer => networkPlayer;

        private void Awake()
        {
            networkPlayer = GetComponent<Network.NetworkPlayer>();
            equipmentLoadout = GetComponent<Equipment.EquipmentLoadout>();
            networkLoadout = GetComponent<Equipment.NetworkLoadout>();
            networkTransform = GetComponent<NetworkTransform>();
            if (characterController == null) characterController = GetComponent<CharacterController>();

            if (equipmentLoadout == null)
            {
                equipmentLoadout = gameObject.AddComponent<Equipment.EquipmentLoadout>();
            }

            networkPlayer.ClassChanged += OnClassChanged;
        }

        private void OnDestroy()
        {
            if (networkPlayer != null) networkPlayer.ClassChanged -= OnClassChanged;
        }

        private void OnClassChanged(Network.NetworkPlayer player, Core.BasePlayerClass playerClass)
        {
            SetPlayerClass(playerClass);
        }

        public override void Spawned()
        {
            // Lock cursor for first-person camera if this is the local player
            if (Object.HasInputAuthority)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                if (cameraTransform != null) cameraTransform.gameObject.SetActive(true);
            }
            else if (cameraTransform != null)
            {
                // Only the local player renders through its own camera
                var cam = cameraTransform.GetComponentInChildren<Camera>();
                if (cam != null) cam.enabled = false;
                var listener = cameraTransform.GetComponentInChildren<AudioListener>();
                if (listener != null) listener.enabled = false;
            }

            if (currentClass == null) currentClass = networkPlayer.CurrentClass;
        }

        public override void FixedUpdateNetwork()
        {
            // [v0.1] if (!Object.HasInputAuthority || !networkPlayer.IsAlive) return;   // ran only on the client
            // [v0.1] HandleMovement(); HandleAbilities(); HandleEquipment();           // polled UnityEngine.Input here

            if (!GetInput(out Core.NetworkInputData input)) return;

            NetworkButtons pressed = input.Buttons.GetPressed(PreviousButtons);
            PreviousButtons = input.Buttons;

            if (!networkPlayer.IsActiveInMatch)
            {
                return;
            }

            if (Object.HasStateAuthority)
            {
                networkPlayer.LookYaw = input.Yaw;
                networkPlayer.LookPitch = input.Pitch;
            }

            HandleMovement(input);

            if (Object.HasStateAuthority)
            {
                HandleAbilities(pressed);
                HandleInteract(input.Buttons);
                HandleEquipment(pressed, input.Buttons);
            }
        }

        public override void Render()
        {
            if (!Object.HasInputAuthority || cameraTransform == null) return;
            cameraTransform.localRotation = Quaternion.Euler(localPitch, 0f, 0f);
        }

        /// <summary>Called by the local input collector every frame so the camera doesn't wait for a tick.</summary>
        public void SetLocalPitch(float pitch)
        {
            localPitch = pitch;
        }

        private void HandleMovement(Core.NetworkInputData input)
        {
            if (characterController == null) return;

            // Yaw comes from input so server and client agree on facing.
            transform.rotation = Quaternion.Euler(0f, input.Yaw, 0f);

            Vector2 axes = Vector2.ClampMagnitude(input.Move, 1f);
            bool wantsSprint = input.Buttons.IsSet((int)Core.InputButtons.Sprint) && axes.y > 0.1f;
            bool canSprint = wantsSprint && networkPlayer.Stamina > Core.GameConstants.SPRINT_MIN_STAMINA
                             && !networkPlayer.HasStatus(Core.StatusFlags.Flashed);

            float speed = currentClass != null
                ? (canSprint ? currentClass.SprintSpeed : currentClass.MovementSpeed)
                : defaultMovementSpeed;
            if (networkLoadout != null) speed *= networkLoadout.SpeedMultiplier; // v0.4: armor / accessories

            if (Object.HasStateAuthority)
            {
                networkPlayer.SetStatus(Core.StatusFlags.Sprinting, canSprint);
                if (canSprint)
                {
                    networkPlayer.DrainStamina(Core.GameConstants.SPRINT_STAMINA_DRAIN_PER_SECOND * Runner.DeltaTime);
                }
            }

            Vector3 move = transform.right * axes.x + transform.forward * axes.y;
            characterController.Move(move * speed * Runner.DeltaTime);

            // Gravity
            if (characterController.isGrounded && VerticalVelocity < 0f)
            {
                VerticalVelocity = groundingForce;
            }

            // Jump
            if (input.Buttons.IsSet((int)Core.InputButtons.Jump) && characterController.isGrounded)
            {
                VerticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }

            VerticalVelocity += gravity * Runner.DeltaTime;
            characterController.Move(Vector3.up * VerticalVelocity * Runner.DeltaTime);
        }

        // [v0.1] private void HandleCameraRotation() { ... Input.GetAxis("Mouse X") ... }

        private void HandleAbilities(NetworkButtons pressed)
        {
            if (pressed.IsSet((int)Core.InputButtons.Ability1)) networkPlayer.UseAbility(0);
            if (pressed.IsSet((int)Core.InputButtons.Ability2)) networkPlayer.UseAbility(1);
            if (pressed.IsSet((int)Core.InputButtons.Ability3)) networkPlayer.UseAbility(2);
            if (pressed.IsSet((int)Core.InputButtons.Ability4)) networkPlayer.UseAbility(3);
        }

        private void HandleInteract(NetworkButtons held)
        {
            networkPlayer.SetStatus(Core.StatusFlags.Interacting, held.IsSet((int)Core.InputButtons.Interact));
        }

        // [v0.2] private void HandleEquipment(NetworkButtons pressed) { ...UseEquipment(slot) on press only... }
        private void HandleEquipment(NetworkButtons pressed, NetworkButtons held)
        {
            if (equipmentLoadout == null) return;
            Dispatch(Equipment.EquipmentSlotType.Primary,   Core.InputButtons.Primary,   pressed, held);
            Dispatch(Equipment.EquipmentSlotType.Secondary, Core.InputButtons.Secondary, pressed, held);
            Dispatch(Equipment.EquipmentSlotType.Utility,   Core.InputButtons.Utility,   pressed, held);
            Dispatch(Equipment.EquipmentSlotType.Gadget,    Core.InputButtons.Gadget,    pressed, held);

            if (pressed.IsSet((int)Core.InputButtons.Reload) && networkLoadout != null)
            {
                networkLoadout.ReloadAny();
            }
        }

        private void Dispatch(Equipment.EquipmentSlotType slot, Core.InputButtons button, NetworkButtons pressed, NetworkButtons held)
        {
            equipmentLoadout.UseEquipment(slot, pressed.IsSet((int)button), held.IsSet((int)button));
        }

        /// <summary>
        /// Sets the current player class.
        /// </summary>
        public void SetPlayerClass(Core.BasePlayerClass playerClass)
        {
            currentClass = playerClass;
        }

        /// <summary>
        /// Moves the player instantly (spawn / respawn). State authority only.
        /// </summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            if (!Object.HasStateAuthority) return;

            bool wasEnabled = characterController != null && characterController.enabled;
            if (characterController != null) characterController.enabled = false;

            if (networkTransform != null)
            {
                networkTransform.Teleport(position, rotation);
            }
            else
            {
                transform.SetPositionAndRotation(position, rotation);
            }

            VerticalVelocity = 0f;
            if (characterController != null) characterController.enabled = wasEnabled;
        }
    }
}

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
    ///
    /// v0.7 movement tech (all tick-simulated, state networked for prediction):
    ///   walk · sprint · slide (sprint + Slide) · jump · mantle (jump into a ledge) ·
    ///   class perks from <see cref="Core.MovementProfile"/>: double jump, glide (hold Jump while falling),
    ///   wall climb (hold Jump against a wall), grappling hook (Grapple button, aim at a surface).
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

        [Header("Movement Tech (v0.7)")]
        [SerializeField] private float slideDuration = 0.8f;
        [SerializeField] private float slideCooldown = 1.2f;
        [SerializeField] private float slideSpeedMultiplier = 1.5f;
        [SerializeField] private float slideHeight = 1.0f;
        [SerializeField] private float mantleMaxHeight = 2.2f;
        [SerializeField] private float mantleDuration = 0.35f;
        [SerializeField] private float glideFallSpeed = 1.5f;
        [SerializeField] private float glideSpeedBonus = 1.15f;
        [SerializeField] private float wallClimbSpeed = 3.5f;
        [SerializeField] private float wallClimbStaminaPerSecond = 20f;
        [SerializeField] private float grappleSpeed = 22f;
        [SerializeField] private float grappleCooldownSeconds = 6f;
        [SerializeField] private LayerMask worldMask = ~0;
        [SerializeField] private float cameraStandHeight = 1.6f;
        [SerializeField] private float cameraSlideHeight = 1.0f;
        
        private Network.NetworkPlayer networkPlayer;
        private Core.BasePlayerClass currentClass;
        private Equipment.EquipmentLoadout equipmentLoadout;
        private Equipment.NetworkLoadout networkLoadout;
        private NetworkTransform networkTransform;
        private float standHeight;
        private Vector3 standCenter;
        
        // Vertical velocity is part of the simulation state so prediction/resimulation stays consistent.
        [Networked] private float VerticalVelocity { get; set; }
        [Networked] private NetworkButtons PreviousButtons { get; set; }
        [Networked] private int JumpsUsed { get; set; }
        [Networked] private TickTimer SlideTimer { get; set; }
        [Networked] private TickTimer SlideCooldownTimer { get; set; }
        [Networked] private Vector3 SlideDirection { get; set; }
        [Networked] private float GlideTimeLeft { get; set; }
        [Networked] private float ClimbTimeLeft { get; set; }
        [Networked] private TickTimer MantleTimer { get; set; }
        [Networked] private Vector3 MantleTarget { get; set; }
        [Networked] private NetworkBool Grappling { get; set; }
        [Networked] private Vector3 GrapplePoint { get; set; }
        [Networked] private TickTimer GrappleCooldown { get; set; }

        // [v0.1] private Vector3 velocity;
        // [v0.1] private float verticalRotation = 0f;
        private float localPitch;

        public Network.NetworkPlayer NetworkPlayer => networkPlayer;
        public bool IsSliding => SlideTimer.IsRunning && !SlideTimer.Expired(Runner);
        public bool IsMantling => MantleTimer.IsRunning && !MantleTimer.Expired(Runner);
        public bool IsGrappling => Grappling;

        private Core.MovementProfile Profile => currentClass != null ? currentClass.Movement : Core.MovementProfile.Default;
        
        private void Awake()
        {
            networkPlayer = GetComponent<Network.NetworkPlayer>();
            equipmentLoadout = GetComponent<Equipment.EquipmentLoadout>();
            networkLoadout = GetComponent<Equipment.NetworkLoadout>();
            networkTransform = GetComponent<NetworkTransform>();
            if (characterController == null) characterController = GetComponent<CharacterController>();
            if (characterController != null) { standHeight = characterController.height; standCenter = characterController.center; }
            
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
                if (cameraTransform != null)
                {
                    cameraTransform.gameObject.SetActive(true);
                    var cam = cameraTransform.GetComponentInChildren<Camera>();
                    if (cam != null) { cam.enabled = true; cam.depth = 10f; } // above any scene / spectator camera
                }
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

            if (Object.HasInputAuthority) Network.GameSessionManager.NotifyLocalPlayer(true);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Object != null && Object.HasInputAuthority) Network.GameSessionManager.NotifyLocalPlayer(false);
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

            HandleMovement(input, pressed);

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
            float targetY = IsSliding ? cameraSlideHeight : cameraStandHeight;
            var lp = cameraTransform.localPosition;
            lp.y = Mathf.Lerp(lp.y, targetY, Time.deltaTime * 12f);
            cameraTransform.localPosition = lp;
        }

        /// <summary>Called by the local input collector every frame so the camera doesn't wait for a tick.</summary>
        public void SetLocalPitch(float pitch)
        {
            localPitch = pitch;
        }

        // ------------------------------------------------------------------
        // Movement
        
        private void HandleMovement(Core.NetworkInputData input, NetworkButtons pressed)
        {
            if (characterController == null) return;

            // Yaw comes from input so server and client agree on facing.
            transform.rotation = Quaternion.Euler(0f, input.Yaw, 0f);

            float dt = Runner.DeltaTime;
            var profile = Profile;
            bool grounded = characterController.isGrounded;
            bool jumpPressed = pressed.IsSet((int)Core.InputButtons.Jump);
            bool jumpHeld = input.Buttons.IsSet((int)Core.InputButtons.Jump);

            // ---- Mantle in progress: scripted move, nothing else
            if (IsMantling)
            {
                Vector3 toTarget = MantleTarget - transform.position;
                characterController.Move(toTarget * Mathf.Clamp01(dt / Mathf.Max(0.01f, MantleTimer.RemainingTime(Runner) ?? dt)));
                VerticalVelocity = 0f;
                return;
            }
            if (MantleTimer.IsRunning && MantleTimer.Expired(Runner)) MantleTimer = TickTimer.None;

            // ---- Grapple in progress
            if (Grappling)
            {
                Vector3 toPoint = GrapplePoint - transform.position;
                if (toPoint.magnitude < 1.5f || jumpPressed || grounded && toPoint.y < 0.5f)
                {
                    Grappling = false;
                    SetStatusAuth(Core.StatusFlags.Grappling, false);
                    VerticalVelocity = Mathf.Max(VerticalVelocity, 2f);
                }
                else
                {
                    characterController.Move(toPoint.normalized * grappleSpeed * dt);
                    VerticalVelocity = 0f;
                    return;
                }
            }

            if (grounded)
            {
                JumpsUsed = 0;
                GlideTimeLeft = profile.GlideSeconds;
                ClimbTimeLeft = profile.WallClimbSeconds;
                if (networkPlayer.HasStatus(Core.StatusFlags.Gliding)) SetStatusAuth(Core.StatusFlags.Gliding, false);
                if (networkPlayer.HasStatus(Core.StatusFlags.Climbing)) SetStatusAuth(Core.StatusFlags.Climbing, false);
            }

            Vector2 axes = Vector2.ClampMagnitude(input.Move, 1f);
            bool wantsSprint = input.Buttons.IsSet((int)Core.InputButtons.Sprint) && axes.y > 0.1f;
            bool canSprint = wantsSprint && networkPlayer.Stamina > Core.GameConstants.SPRINT_MIN_STAMINA
                             && !networkPlayer.HasStatus(Core.StatusFlags.Flashed);

            float speed = currentClass != null
                ? (canSprint ? currentClass.SprintSpeed : currentClass.MovementSpeed)
                : defaultMovementSpeed;
            if (networkLoadout != null) speed *= networkLoadout.SpeedMultiplier; // v0.4: armor / accessories
            speed *= networkPlayer.CarryWeightSpeedMultiplier;                      // v0.5: loot weight / case

            // ---- Slide: sprint + Slide while grounded
            bool sliding = IsSliding;
            if (!sliding && pressed.IsSet((int)Core.InputButtons.Slide) && grounded && canSprint && SlideCooldownTimer.ExpiredOrNotRunning(Runner))
            {
                SlideTimer = TickTimer.CreateFromSeconds(Runner, slideDuration);
                SlideCooldownTimer = TickTimer.CreateFromSeconds(Runner, slideDuration + slideCooldown);
                SlideDirection = transform.forward;
                sliding = true;
                SetStatusAuth(Core.StatusFlags.Sliding, true);
            }
            if (sliding)
            {
                float remaining = Mathf.Clamp01((SlideTimer.RemainingTime(Runner) ?? 0f) / Mathf.Max(0.01f, slideDuration));
                float slideSpeed = speed * Mathf.Lerp(1f, slideSpeedMultiplier, remaining);
                characterController.Move(SlideDirection * slideSpeed * dt);
                SetControllerHeight(slideHeight);
            }
            else
            {
                if (networkPlayer.HasStatus(Core.StatusFlags.Sliding)) SetStatusAuth(Core.StatusFlags.Sliding, false);
                SetControllerHeight(standHeight);

                if (Object.HasStateAuthority)
                {
                    networkPlayer.SetStatus(Core.StatusFlags.Sprinting, canSprint);
                    if (canSprint) networkPlayer.DrainStamina(Core.GameConstants.SPRINT_STAMINA_DRAIN_PER_SECOND * dt);
                }

                Vector3 move = transform.right * axes.x + transform.forward * axes.y;
                float airControl = grounded ? 1f : (networkPlayer.HasStatus(Core.StatusFlags.Gliding) ? glideSpeedBonus : 0.9f);
                characterController.Move(move * speed * airControl * dt);
            }

            // ---- Gravity base
            if (grounded && VerticalVelocity < 0f)
            {
                VerticalVelocity = groundingForce;
            }

            // ---- Jump / double jump
            if (jumpPressed)
            {
                if (grounded)
                {
                    VerticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                    JumpsUsed = 1;
                    if (sliding) { SlideTimer = TickTimer.None; SetStatusAuth(Core.StatusFlags.Sliding, false); } // slide-jump
                }
                else if (profile.CanDoubleJump && JumpsUsed < 2 && !TryStartMantle())
                {
                    VerticalVelocity = Mathf.Sqrt(jumpHeight * 0.85f * -2f * gravity);
                    JumpsUsed = 2;
                }
                else if (!grounded)
                {
                    TryStartMantle();
                }
            }

            // ---- Wall climb: hold Jump against a wall
            bool climbing = false;
            if (profile.CanWallClimb && jumpHeld && !grounded && ClimbTimeLeft > 0f && WallAhead(out _)
                && networkPlayer.Stamina > Core.GameConstants.SPRINT_MIN_STAMINA)
            {
                VerticalVelocity = wallClimbSpeed;
                ClimbTimeLeft -= dt;
                climbing = true;
                if (Object.HasStateAuthority) networkPlayer.DrainStamina(wallClimbStaminaPerSecond * dt);
                if (!networkPlayer.HasStatus(Core.StatusFlags.Climbing)) SetStatusAuth(Core.StatusFlags.Climbing, true);
                // Reaching a ledge while climbing mantles over it
                if (TryStartMantle()) return;
            }
            else if (networkPlayer.HasStatus(Core.StatusFlags.Climbing)) SetStatusAuth(Core.StatusFlags.Climbing, false);

            // ---- Glide: hold Jump while falling
            bool gliding = false;
            if (!climbing && profile.CanGlide && jumpHeld && !grounded && VerticalVelocity < 0f && GlideTimeLeft > 0f && JumpsUsed > 0)
            {
                VerticalVelocity = Mathf.Max(VerticalVelocity + gravity * dt, -glideFallSpeed);
                GlideTimeLeft -= dt;
                gliding = true;
                if (!networkPlayer.HasStatus(Core.StatusFlags.Gliding)) SetStatusAuth(Core.StatusFlags.Gliding, true);
            }
            else
            {
                if (networkPlayer.HasStatus(Core.StatusFlags.Gliding)) SetStatusAuth(Core.StatusFlags.Gliding, false);
                if (!climbing) VerticalVelocity += gravity * dt;
            }

            // ---- Grapple start
            if (profile.CanGrapple && pressed.IsSet((int)Core.InputButtons.Grapple) && GrappleCooldown.ExpiredOrNotRunning(Runner))
            {
                Vector3 origin = networkPlayer.EyePosition;
                if (Physics.Raycast(origin, networkPlayer.AimDirection, out RaycastHit hit, profile.GrappleRange, worldMask, QueryTriggerInteraction.Ignore)
                    && hit.collider.GetComponentInParent<Network.NetworkPlayer>() == null)
                {
                    Grappling = true;
                    GrapplePoint = hit.point + hit.normal * 0.6f;
                    GrappleCooldown = TickTimer.CreateFromSeconds(Runner, grappleCooldownSeconds);
                    SetStatusAuth(Core.StatusFlags.Grappling, true);
                    SlideTimer = TickTimer.None;
                }
            }

            characterController.Move(Vector3.up * VerticalVelocity * dt);
        }

        private bool WallAhead(out RaycastHit hit)
        {
            Vector3 origin = transform.position + Vector3.up * (standHeight * 0.5f);
            return Physics.Raycast(origin, transform.forward, out hit, 0.9f, worldMask, QueryTriggerInteraction.Ignore)
                   && hit.collider.GetComponentInParent<Network.NetworkPlayer>() == null;
        }

        /// <summary>Jumping into a ledge no taller than <see cref="mantleMaxHeight"/> hoists the player onto it.</summary>
        private bool TryStartMantle()
        {
            if (IsMantling) return false;
            if (!WallAhead(out RaycastHit wall)) return false;

            Vector3 feet = transform.position;
            Vector3 probeTop = new Vector3(wall.point.x, feet.y + mantleMaxHeight + 0.2f, wall.point.z) + transform.forward * 0.6f;
            if (!Physics.Raycast(probeTop, Vector3.down, out RaycastHit ledge, mantleMaxHeight + 0.2f, worldMask, QueryTriggerInteraction.Ignore)) return false;

            float rise = ledge.point.y - feet.y;
            if (rise < 0.4f || rise > mantleMaxHeight) return false;

            // Headroom check on the ledge
            if (Physics.CheckCapsule(ledge.point + Vector3.up * 0.5f, ledge.point + Vector3.up * (standHeight - 0.3f), 0.35f, worldMask, QueryTriggerInteraction.Ignore)) return false;

            MantleTarget = ledge.point + Vector3.up * 0.1f + transform.forward * 0.3f;
            MantleTimer = TickTimer.CreateFromSeconds(Runner, mantleDuration);
            VerticalVelocity = 0f;
            Grappling = false;
            return true;
        }

        private void SetControllerHeight(float height)
        {
            if (characterController == null || Mathf.Approximately(characterController.height, height)) return;
            characterController.height = height;
            characterController.center = new Vector3(standCenter.x, standCenter.y - (standHeight - height) * 0.5f, standCenter.z);
        }

        private void SetStatusAuth(Core.StatusFlags flag, bool on)
        {
            if (Object.HasStateAuthority) networkPlayer.SetStatus(flag, on);
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
            Grappling = false;
            MantleTimer = TickTimer.None;
            SlideTimer = TickTimer.None;
            if (characterController != null) characterController.enabled = wasEnabled;
        }
    }
}

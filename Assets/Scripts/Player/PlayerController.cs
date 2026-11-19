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
    /// v0.9: put a <c>NetworkCharacterController</c> on the prefab instead of <c>NetworkTransform</c> (the scene
    /// builder does this). It is used purely as a CharacterController-safe state sync; without it a client's
    /// mispredicted position is never corrected. Ground contact and velocity are networked for the same reason.
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

        [Header("Zero gravity (v0.12)")]
        [SerializeField] private float zeroGAcceleration = 14f;
        [SerializeField] private float zeroGMaxSpeed = 8f;
        [Tooltip("Fraction of speed lost per second while floating. Low: you keep drifting.")]
        [SerializeField] private float zeroGDrag = 0.5f;
        
        private Network.NetworkPlayer networkPlayer;
        private Core.BasePlayerClass currentClass;
        private Equipment.EquipmentLoadout equipmentLoadout;
        private Equipment.NetworkLoadout networkLoadout;
        private NetworkTransform networkTransform;
        /// <summary>
        /// (v0.9) Fusion's CharacterController-aware transform sync. Used only for its state copy (it disables the
        /// CharacterController while Fusion rewinds the transform, which a plain NetworkTransform does not, so
        /// client-side reconciliation actually takes effect). Movement itself is still simulated in this class.
        /// </summary>
        private NetworkCharacterController characterSync;
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
        /// <summary>
        /// (v0.9) Ground contact at the end of the last simulated tick. CharacterController.isGrounded is not
        /// rewound with the rest of the state (and resets whenever the controller is re-enabled), so the
        /// simulation reads this instead.
        /// </summary>
        [Networked] private NetworkBool GroundedState { get; set; }
        /// <summary>(v0.9) Velocity over the last simulated tick (for spread, AI aim and, later, animation).</summary>
        [Networked] private Vector3 SimVelocity { get; set; }
        /// <summary>(v0.12) Floating in a zero-gravity zone this tick.</summary>
        [Networked] private NetworkBool Floating { get; set; }
        /// <summary>(v0.12) Momentum while floating. Kept between ticks: in zero-g you drift.</summary>
        [Networked] private Vector3 FloatVelocity { get; set; }

        // [v0.1] private Vector3 velocity;
        // [v0.1] private float verticalRotation = 0f;
        private float localPitch;

        private AI.BotBrain bot;

        /// <summary>(v0.10) Hands control of this player to a bot brain. State authority only; see <c>AI.BotBrain</c>.</summary>
        public void SetBot(AI.BotBrain brain) { bot = brain; }

        public Network.NetworkPlayer NetworkPlayer => networkPlayer;
        public bool IsSliding => SlideTimer.IsRunning && !SlideTimer.Expired(Runner);
        public bool IsMantling => MantleTimer.IsRunning && !MantleTimer.Expired(Runner);
        public bool IsGrappling => Grappling;
        /// <summary>(v0.9) Ground contact as the simulation sees it (prediction-safe on every peer).</summary>
        public bool IsGrounded => characterSync != null ? (bool)GroundedState : (characterController != null && characterController.isGrounded);
        /// <summary>(v0.9) Velocity over the last simulated tick.</summary>
        public Vector3 Velocity => SimVelocity;
        /// <summary>(v0.12) True while floating in a zero-gravity zone.</summary>
        public bool IsFloating => Floating;

        private Core.MovementProfile Profile => currentClass != null ? currentClass.Movement : Core.MovementProfile.Default;
        
        private void Awake()
        {
            networkPlayer = GetComponent<Network.NetworkPlayer>();
            equipmentLoadout = GetComponent<Equipment.EquipmentLoadout>();
            networkLoadout = GetComponent<Equipment.NetworkLoadout>();
            networkTransform = GetComponent<NetworkTransform>();
            characterSync = GetComponent<NetworkCharacterController>();
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
            if (Object.HasInputAuthority) Local = this; // (v0.13)
            if (Object.HasInputAuthority && cameraTransform != null) Core.Juice.AttachShaker(cameraTransform); // (v0.15) Feel camera shake
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Object != null && Object.HasInputAuthority) Network.GameSessionManager.NotifyLocalPlayer(false);
            if (Local == this) Local = null; // (v0.13)
        }
        
        public override void FixedUpdateNetwork()
        {
            // [v0.1] if (!Object.HasInputAuthority || !networkPlayer.IsAlive) return;   // ran only on the client
            // [v0.1] HandleMovement(); HandleAbilities(); HandleEquipment();           // polled UnityEngine.Input here

            // [v0.9] if (!GetInput(out Core.NetworkInputData input)) return;
            // (v0.10) a bot has no input authority: its brain supplies the input on the state authority, and
            //         everything below treats it exactly like a person's.
            Core.NetworkInputData input;
            if (bot != null)
            {
                if (!Object.HasStateAuthority || !bot.TryGetInput(out input)) return;
            }
            else if (!GetInput(out input)) return;

            // (v0.11) jacked into a vault: the player is at the console. Only Interact gets through (it jacks out);
            //         the number keys go to the sign puzzle, not to abilities.
            if (networkPlayer.IsJackedIn)
            {
                bool interact = input.Buttons.IsSet((int)Core.InputButtons.Interact);
                input.Move = default;
                input.Buttons = default;
                input.Buttons.Set((int)Core.InputButtons.Interact, interact);
            }

            NetworkButtons pressed = input.Buttons.GetPressed(PreviousButtons);
            PreviousButtons = input.Buttons;

            if (!networkPlayer.IsActiveInMatch)
            {
                return;
            }

            // [v0.8] if (Object.HasStateAuthority) { networkPlayer.LookYaw = input.Yaw; networkPlayer.LookPitch = input.Pitch; }
            // (v0.9) written on the owning client too, so its predicted shots use this tick's aim rather than
            //        the last aim the server echoed back.
            networkPlayer.LookYaw = input.Yaw;
            networkPlayer.LookPitch = input.Pitch;

            HandleMovement(input, pressed);

            // (v0.9) weapons are simulated on the owning client as well (prediction): ammo, cooldown and the shot
            //        counter are networked state, damage is still dealt only by the state authority.
            HandleEquipment(pressed, input.Buttons);

            if (Object.HasStateAuthority)
            {
                HandleAbilities(pressed);
                HandleInteract(input.Buttons);
                if (pressed.IsSet((int)Core.InputButtons.Ping)) networkPlayer.TryPing(); // (v0.17)
                // (v0.11) quick hacks (hold Interact while aiming at a device) and deep hacks (press it at a vault)
                networkPlayer.TickHacking(input.Buttons.IsSet((int)Core.InputButtons.Interact), pressed.IsSet((int)Core.InputButtons.Interact));
                // [v0.8] HandleEquipment(pressed, input.Buttons);   // ran on the state authority only
            }
        }

        public override void Render()
        {
            if (!Object.HasInputAuthority || cameraTransform == null) return;
            // [v0.14] cameraTransform.localRotation = Quaternion.Euler(localPitch, 0f, 0f);
            float targetY = IsSliding ? cameraSlideHeight : cameraStandHeight;
            // [v0.14] var lp = cameraTransform.localPosition;
            // [v0.14] lp.y = Mathf.Lerp(lp.y, targetY, Time.deltaTime * 12f);
            // [v0.14] cameraTransform.localPosition = lp;
            // (v0.15) The eye height is tracked on its own, and the shake from Core.Juice is added on top of it,
            //         so a shake can never drift the camera or fight the slide.
            if (cameraBaseY <= 0f) cameraBaseY = cameraTransform.localPosition.y;
            cameraBaseY = Mathf.Lerp(cameraBaseY, targetY, Time.deltaTime * 12f);
            Vector3 shake = Core.Juice.ShakeOffset;
            // [v0.18] cameraTransform.localPosition = new Vector3(shake.x, cameraBaseY + shake.y, shake.z);
            // (v0.19) Landing has weight: the view dips and thumps in proportion to how fast the player was
            //         falling. Local and cosmetic only; scaled by the screen-shake setting like every other shake.
            UpdateLandingFeel();
            cameraTransform.localPosition = new Vector3(shake.x, cameraBaseY - landDip + shake.y, shake.z);
            cameraTransform.localRotation = Quaternion.Euler(localPitch - shake.y * 45f, shake.x * 35f, shake.x * -50f);
            UpdateAimView(); // (v0.13)
        }

        // ------------------------------------------------------------------
        // Aiming down sights: the owner's view (v0.13)

        /// <summary>The local player's controller, or null. Set while its object is spawned on this peer.</summary>
        public static PlayerController Local { get; private set; }

        /// <summary>0 from the hip, 1 with the sights fully up. Eased, for the view model and the crosshair.</summary>
        public float AimBlend { get; private set; }

        /// <summary>Look-speed multiplier for the current zoom, so aiming does not feel faster than hip fire.</summary>
        public float LookSensitivityScale { get; private set; } = 1f;

        private Camera viewCamera;
        private float cameraBaseY; // (v0.15) eye height without the shake

        // (v0.19) movement feel, the owner's view only
        private float moveFovScale = 1f, landDip, viewFallSpeed;
        private bool viewWasGrounded = true;

        private void UpdateLandingFeel()
        {
            bool grounded = IsGrounded;
            if (!grounded) viewFallSpeed = Mathf.Max(viewFallSpeed, -Velocity.y);
            else
            {
                if (!viewWasGrounded && viewFallSpeed > 5f && networkPlayer != null && networkPlayer.IsActiveInMatch)
                {
                    float feel = Mathf.Clamp01(Core.GameSettings.ScreenShake);
                    landDip = Mathf.Clamp((viewFallSpeed - 5f) * 0.018f, 0f, 0.2f) * feel;
                    Core.Juice.Shake(Mathf.Clamp((viewFallSpeed - 5f) * 0.004f, 0.008f, 0.05f), 0.16f, 20f);
                }
                viewFallSpeed = 0f;
            }
            viewWasGrounded = grounded;
            landDip = Mathf.MoveTowards(landDip, 0f, Time.deltaTime * 0.9f);
        }

        private void UpdateAimView()
        {
            if (viewCamera == null) viewCamera = cameraTransform.GetComponentInChildren<Camera>();
            if (viewCamera == null) return;

            var data = networkLoadout != null && networkLoadout.Object != null ? networkLoadout.ActiveData : null;
            bool aiming = data != null && networkLoadout.Aiming;
            float seconds = data != null ? Mathf.Max(0.03f, data.adsSeconds) : 0.18f;
            AimBlend = Mathf.MoveTowards(AimBlend, aiming ? 1f : 0f, Time.deltaTime / seconds);
            float eased = AimBlend * AimBlend * (3f - 2f * AimBlend);

            float baseFov = Core.GameSettings.FieldOfView;
            float aimFov = Core.CombatRules.ZoomedFov(baseFov, data != null ? data.adsZoom : 1f);
            float fov = Mathf.Lerp(baseFov, aimFov, eased);
            // [v0.18] viewCamera.fieldOfView = fov;
            // (v0.19) Speed is something you see: the view opens up a little at a sprint and more in a slide,
            //         and closes again when aiming. Look speed is still worked out from the unwidened view, so
            //         the mouse does not speed up with it. Screen shake at 0% switches this off too.
            Vector3 flatVelocity = Velocity; flatVelocity.y = 0f;
            bool sprinting = networkPlayer != null && networkPlayer.HasStatus(Core.StatusFlags.Sprinting) && flatVelocity.sqrMagnitude > 9f;
            float wantScale = 1f + (IsSliding ? 0.09f : sprinting ? 0.05f : 0f) * Mathf.Clamp01(Core.GameSettings.ScreenShake);
            moveFovScale = Mathf.Lerp(moveFovScale, wantScale, Time.deltaTime * 7f);
            viewCamera.fieldOfView = Mathf.Min(125f, Mathf.Lerp(baseFov * moveFovScale, aimFov, eased));
            LookSensitivityScale = Core.CombatRules.ZoomSensitivity(baseFov, fov) * Mathf.Lerp(1f, Core.GameSettings.AimSensitivity, eased);
        }

        /// <summary>Called by the local input collector every frame so the camera doesn't wait for a tick.</summary>
        public void SetLocalPitch(float pitch)
        {
            localPitch = pitch;
        }

        // ------------------------------------------------------------------
        // Movement
        
        /// <summary>(v0.9) Runs one tick of movement, then records ground contact and velocity as networked state.</summary>
        private void HandleMovement(Core.NetworkInputData input, NetworkButtons pressed)
        {
            if (characterController == null) return;

            Vector3 before = transform.position;
            SimulateMovement(input, pressed);

            GroundedState = characterController.isGrounded;
            SimVelocity = (transform.position - before) / Mathf.Max(0.0001f, Runner.DeltaTime);
        }

        // [v0.8] private void HandleMovement(Core.NetworkInputData input, NetworkButtons pressed)   // renamed; see wrapper above
        private void SimulateMovement(Core.NetworkInputData input, NetworkButtons pressed)
        {
            // [v0.8] if (characterController == null) return;   // checked by HandleMovement

            // Yaw comes from input so server and client agree on facing.
            transform.rotation = Quaternion.Euler(0f, input.Yaw, 0f);

            // (v0.12) Gravity off here: a different way of moving altogether.
            if (Interaction.GravityZone.IsZeroGAt(transform.position + Vector3.up))
            {
                SimulateZeroG(input);
                return;
            }
            if (Floating)
            {
                // Gravity is back (the zone was switched, or we drifted out): fall from wherever we are.
                Floating = false;
                VerticalVelocity = FloatVelocity.y;
                FloatVelocity = Vector3.zero;
                JumpsUsed = 1;
            }

            float dt = Runner.DeltaTime;
            var profile = Profile;
            // [v0.8] bool grounded = characterController.isGrounded;
            bool grounded = IsGrounded;
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
            // [v0.12] bool canSprint = wantsSprint && networkPlayer.Stamina > Core.GameConstants.SPRINT_MIN_STAMINA
            // [v0.12]                  && !networkPlayer.HasStatus(Core.StatusFlags.Flashed);
            bool aiming = networkLoadout != null && networkLoadout.Aiming; // (v0.13) sights up: no sprint, slower walk
            bool canSprint = wantsSprint && !aiming && networkPlayer.Stamina > Core.GameConstants.SPRINT_MIN_STAMINA
                             && !networkPlayer.HasStatus(Core.StatusFlags.Flashed);

            float speed = currentClass != null
                ? (canSprint ? currentClass.SprintSpeed : currentClass.MovementSpeed)
                : defaultMovementSpeed;
            if (networkLoadout != null) speed *= networkLoadout.SpeedMultiplier; // v0.4: armor / accessories
            speed *= networkPlayer.CarryWeightSpeedMultiplier;                      // v0.5: loot weight / case
            if (networkLoadout != null) speed *= networkLoadout.AimSpeedMultiplier; // v0.13: aiming down sights

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
            // [v0.13] if (profile.CanGrapple && pressed.IsSet((int)Core.InputButtons.Grapple) && GrappleCooldown.ExpiredOrNotRunning(Runner))
            // (v0.14) the Grapple Line gadget gives any specialist the Smith's grapple
            bool canGrapple = profile.CanGrapple || (networkLoadout != null && networkLoadout.HasGadget(Data.GadgetType.GrappleLine));
            if (canGrapple && pressed.IsSet((int)Core.InputButtons.Grapple) && GrappleCooldown.ExpiredOrNotRunning(Runner))
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

        /// <summary>
        /// (v0.12) Movement with gravity off. You fly where you look: forward and strafe follow the camera
        /// (pitch included), Jump thrusts up and Slide thrusts down. Speed is kept between ticks and only bleeds
        /// off slowly, so stopping takes a counter-thrust. Hitting something kills the speed into it.
        /// </summary>
        private void SimulateZeroG(Core.NetworkInputData input)
        {
            float dt = Runner.DeltaTime;
            Vector3 velocity = FloatVelocity;

            if (!Floating)
            {
                // Leaving the floor: keep what you were doing, plus a little lift so you actually leave it.
                Floating = true;
                velocity = SimVelocity;
                velocity.y = Mathf.Max(velocity.y, 1.5f);
                velocity = Vector3.ClampMagnitude(velocity, zeroGMaxSpeed);
                SlideTimer = TickTimer.None;
                MantleTimer = TickTimer.None;
                Grappling = false;
                SetControllerHeight(standHeight);
                if (Object.HasStateAuthority)
                {
                    networkPlayer.SetStatus(Core.StatusFlags.Sliding, false);
                    networkPlayer.SetStatus(Core.StatusFlags.Sprinting, false);
                    networkPlayer.SetStatus(Core.StatusFlags.Gliding, false);
                    networkPlayer.SetStatus(Core.StatusFlags.Climbing, false);
                    networkPlayer.SetStatus(Core.StatusFlags.Grappling, false);
                }
            }

            Vector2 axes = Vector2.ClampMagnitude(input.Move, 1f);
            Vector3 look = Quaternion.Euler(input.Pitch, input.Yaw, 0f) * Vector3.forward;
            float lift = (input.Buttons.IsSet((int)Core.InputButtons.Jump) ? 1f : 0f) - (input.Buttons.IsSet((int)Core.InputButtons.Slide) ? 1f : 0f);
            Vector3 wish = Vector3.ClampMagnitude(look * axes.y + transform.right * axes.x + Vector3.up * lift, 1f);

            velocity += wish * zeroGAcceleration * dt;
            velocity *= Mathf.Clamp01(1f - zeroGDrag * dt);
            velocity = Vector3.ClampMagnitude(velocity, zeroGMaxSpeed * networkPlayer.CarryWeightSpeedMultiplier);

            Vector3 before = transform.position;
            characterController.Move(velocity * dt);
            // What we actually moved is the velocity we keep: walls and floors stop the part that ran into them.
            Vector3 moved = (transform.position - before) / Mathf.Max(0.0001f, dt);
            FloatVelocity = Vector3.Lerp(velocity, moved, 0.85f);
            VerticalVelocity = FloatVelocity.y;
            JumpsUsed = 1;
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
            // [v0.12] Left click fired the Primary slot and right click fired the Secondary slot:
            // [v0.12] Dispatch(Equipment.EquipmentSlotType.Primary,   Core.InputButtons.Primary,   pressed, held);
            // [v0.12] Dispatch(Equipment.EquipmentSlotType.Secondary, Core.InputButtons.Secondary, pressed, held);
            // (v0.13) One weapon in hand. The wheel swaps, the fire button fires it, the aim button aims it.
            if (networkLoadout != null)
            {
                if (pressed.IsSet((int)Core.InputButtons.NextWeapon) || pressed.IsSet((int)Core.InputButtons.PrevWeapon))
                {
                    networkLoadout.CycleWeapon();
                }
                networkLoadout.SetAiming(held.IsSet((int)Core.InputButtons.Secondary));
                Dispatch((Equipment.EquipmentSlotType)networkLoadout.ActiveSlot, Core.InputButtons.Primary, pressed, held);
            }
            else
            {
                Dispatch(Equipment.EquipmentSlotType.Primary, Core.InputButtons.Primary, pressed, held);
            }
            Dispatch(Equipment.EquipmentSlotType.Utility,   Core.InputButtons.Utility,   pressed, held);
            Dispatch(Equipment.EquipmentSlotType.Gadget,    Core.InputButtons.Gadget,    pressed, held);

            if (pressed.IsSet((int)Core.InputButtons.Reload) && networkLoadout != null)
            {
                // [v0.12] networkLoadout.ReloadAny();
                networkLoadout.ReloadActive();
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
        /// (v0.11) Throws the player upward (grav lifts). Applied on the state authority; the owning client's
        /// prediction picks the new vertical velocity up from the next state it receives.
        /// </summary>
        public void Launch(float upwardSpeed)
        {
            if (!Object.HasStateAuthority || upwardSpeed <= 0f) return;
            VerticalVelocity = Mathf.Max(VerticalVelocity, upwardSpeed);
            GroundedState = false;
            JumpsUsed = 1; // the launch counts as the first jump, so double-jumpers keep one in the air
            SlideTimer = TickTimer.None;
            MantleTimer = TickTimer.None;
            Grappling = false;
        }

        /// <summary>
        /// Moves the player instantly (spawn / respawn). State authority only.
        /// </summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            if (!Object.HasStateAuthority) return;

            bool wasEnabled = characterController != null && characterController.enabled;
            if (characterController != null) characterController.enabled = false;

            if (characterSync != null)
            {
                characterSync.Teleport(position, rotation); // (v0.9)
            }
            else if (networkTransform != null)
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
            Floating = false;                 // (v0.12)
            FloatVelocity = Vector3.zero;
            GroundedState = false;
            SimVelocity = Vector3.zero;
            if (characterController != null) characterController.enabled = wasEnabled;
        }
    }
}

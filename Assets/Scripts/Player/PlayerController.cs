using UnityEngine;
using Fusion;

namespace Ouroboros.Player
{
    /// <summary>
    /// Player controller integrating class abilities, equipment, and network synchronization.
    /// </summary>
    public class PlayerController : NetworkBehaviour
    {
        [Header("Components")]
        [SerializeField] private CharacterController characterController;
        [SerializeField] private Transform cameraTransform;
        
        [Header("Player Configuration")]
        [SerializeField] private float mouseSensitivity = 2f;
        [SerializeField] private float jumpHeight = 2f;
        [SerializeField] private float gravity = -9.81f;
        [SerializeField] private float defaultMovementSpeed = 5f;
        
        private Network.NetworkPlayer networkPlayer;
        private Core.BasePlayerClass currentClass;
        private Equipment.EquipmentLoadout equipmentLoadout;
        
        private Vector3 velocity;
        private float verticalRotation = 0f;
        
        private void Awake()
        {
            networkPlayer = GetComponent<Network.NetworkPlayer>();
            equipmentLoadout = GetComponent<Equipment.EquipmentLoadout>();
            
            if (equipmentLoadout == null)
            {
                equipmentLoadout = gameObject.AddComponent<Equipment.EquipmentLoadout>();
            }
        }
        
        public override void Spawned()
        {
            // Lock cursor for first-person camera if this is the local player
            if (Object.HasInputAuthority)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
        
        public override void FixedUpdateNetwork()
        {
            if (!Object.HasInputAuthority || !networkPlayer.IsAlive) return;
            
            HandleMovement();
            HandleAbilities();
            HandleEquipment();
        }
        
        private void HandleMovement()
        {
            // Get input
            float moveX = Input.GetAxis("Horizontal");
            float moveZ = Input.GetAxis("Vertical");
            
            // Calculate movement speed
            float speed = currentClass != null ? currentClass.MovementSpeed : defaultMovementSpeed;
            
            // Apply movement
            Vector3 move = transform.right * moveX + transform.forward * moveZ;
            characterController.Move(move * speed * Runner.DeltaTime);
            
            // Apply gravity
            if (characterController.isGrounded && velocity.y < 0)
            {
                velocity.y = -2f;
            }
            
            // Jump
            if (Input.GetButton("Jump") && characterController.isGrounded)
            {
                velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }
            
            velocity.y += gravity * Runner.DeltaTime;
            characterController.Move(velocity * Runner.DeltaTime);
            
            // Camera rotation
            HandleCameraRotation();
        }
        
        private void HandleCameraRotation()
        {
            float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
            float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;
            
            // Rotate player horizontally
            transform.Rotate(Vector3.up * mouseX);
            
            // Rotate camera vertically
            verticalRotation -= mouseY;
            verticalRotation = Mathf.Clamp(verticalRotation, -90f, 90f);
            
            if (cameraTransform != null)
            {
                cameraTransform.localRotation = Quaternion.Euler(verticalRotation, 0f, 0f);
            }
        }
        
        private void HandleAbilities()
        {
            // Ability keys 1-4
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                UseAbility(0);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                UseAbility(1);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                UseAbility(2);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                UseAbility(3);
            }
        }
        
        private void UseAbility(int abilityIndex)
        {
            if (networkPlayer != null)
            {
                networkPlayer.UseAbility(abilityIndex);
            }
        }
        
        private void HandleEquipment()
        {
            // Primary weapon
            if (Input.GetMouseButtonDown(0))
            {
                equipmentLoadout?.UseEquipment(Equipment.EquipmentSlotType.Primary);
            }
            
            // Secondary weapon
            if (Input.GetMouseButtonDown(1))
            {
                equipmentLoadout?.UseEquipment(Equipment.EquipmentSlotType.Secondary);
            }
            
            // Utility (Q key)
            if (Input.GetKeyDown(KeyCode.Q))
            {
                equipmentLoadout?.UseEquipment(Equipment.EquipmentSlotType.Utility);
            }
            
            // Gadget (E key)
            if (Input.GetKeyDown(KeyCode.E))
            {
                equipmentLoadout?.UseEquipment(Equipment.EquipmentSlotType.Gadget);
            }
        }
        
        /// <summary>
        /// Sets the current player class.
        /// </summary>
        public void SetPlayerClass(Core.BasePlayerClass playerClass)
        {
            currentClass = playerClass;
        }
    }
}

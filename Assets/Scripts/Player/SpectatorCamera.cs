using UnityEngine;

namespace Ouroboros.Player
{
    /// <summary>
    /// Scene camera used while this peer has no player object (connecting, lobby before spawn, after a
    /// despawn). <c>GameSessionManager.NotifyLocalPlayer</c> switches it off when the local player's
    /// first-person camera takes over and back on if that object goes away.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class SpectatorCamera : MonoBehaviour
    {
        public static SpectatorCamera Instance { get; private set; }

        [SerializeField] private bool slowOrbit = true;
        [SerializeField] private float orbitDegreesPerSecond = 4f;

        private Camera cam;
        private AudioListener listener;
        private bool active = true;

        private void Awake()
        {
            Instance = this;
            cam = GetComponent<Camera>();
            listener = GetComponent<AudioListener>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (!active || !slowOrbit) return;
            transform.RotateAround(Vector3.zero, Vector3.up, orbitDegreesPerSecond * Time.deltaTime);
            transform.LookAt(Vector3.zero);
        }

        public void SetActiveView(bool enabled)
        {
            active = enabled;
            if (cam != null) cam.enabled = enabled;
            if (listener != null) listener.enabled = enabled;
        }
    }
}

using Fusion;
using UnityEngine;

namespace Ouroboros.Core
{
    /// <summary>
    /// Per-tick input snapshot sent from the input-authority client to the state authority.
    /// Collected in <c>GameSessionManager.OnInput</c>, consumed in <c>PlayerController.FixedUpdateNetwork</c>
    /// via <c>GetInput</c>. Never read <c>UnityEngine.Input</c> inside FixedUpdateNetwork: it runs
    /// zero or several times per frame, so key-down events get lost or duplicated.
    /// </summary>
    public struct NetworkInputData : INetworkInput
    {
        /// <summary>Normalised movement axes (x = strafe, y = forward).</summary>
        public Vector2 Move;
        /// <summary>Absolute yaw in degrees the player wants to face.</summary>
        public float Yaw;
        /// <summary>Absolute camera pitch in degrees (clamped on the client).</summary>
        public float Pitch;
        /// <summary>Packed button state; see <see cref="InputButtons"/>.</summary>
        public NetworkButtons Buttons;
    }
}

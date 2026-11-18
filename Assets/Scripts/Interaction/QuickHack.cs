using System.Collections.Generic;
using UnityEngine;
using Fusion;

namespace Ouroboros.Interaction
{
    /// <summary>
    /// A device anyone can hack by aiming at it and holding Interact (v0.11, milestone M3). Implemented by
    /// networked scene objects: cameras, doors, shutters, traps, power nodes, lifts, turrets. Implementers
    /// register with <see cref="QuickHack"/> in Spawned and unregister in Despawned.
    /// </summary>
    public interface IQuickHackable
    {
        /// <summary>Provided by <c>NetworkBehaviour</c>.</summary>
        NetworkObject Object { get; }
        /// <summary>World point the tether attaches to and line of sight is checked against.</summary>
        Vector3 HackPoint { get; }
        /// <summary>Seconds a non-specialist must hold the tether.</summary>
        float HackSeconds { get; }
        /// <summary>
        /// What hacking it would do for <paramref name="by"/> right now ("Blind camera"), or null when it cannot be
        /// hacked by them at the moment (already off, on cooldown, their own trap).
        /// </summary>
        string HackPrompt(Network.NetworkPlayer by);
        /// <summary>Applies the effect. State authority only.</summary>
        void CompleteHack(Network.NetworkPlayer by);
    }

    /// <summary>Registry of hackable devices on this peer, and the aiming rule shared by server and HUD.</summary>
    public static class QuickHack
    {
        /// <summary>Reach of a quick hack for a non-specialist, in metres.</summary>
        public const float BaseRange = 18f;

        public static readonly List<IQuickHackable> Devices = new List<IQuickHackable>();

        /// <summary>Fired on every peer when a quick hack completes (who, what it did).</summary>
        public static event System.Action<Network.NetworkPlayer, string> Completed;

        public static void Register(IQuickHackable device)
        {
            if (device != null && !Devices.Contains(device)) Devices.Add(device);
        }

        public static void Unregister(IQuickHackable device)
        {
            Devices.Remove(device);
        }

        public static void RaiseCompleted(Network.NetworkPlayer by, string what) => Completed?.Invoke(by, what);

        /// <summary>
        /// The device <paramref name="by"/> is aiming at: inside range, inside the aim cone for its distance,
        /// with a clear line to it, and currently hackable by them. Runs identically on the state authority
        /// (to decide) and on the owning client (to show the prompt).
        /// </summary>
        public static IQuickHackable FindTarget(Network.NetworkPlayer by, float range)
        {
            if (by == null || by.Object == null) return null;
            Vector3 eye = by.EyePosition;
            Vector3 aim = by.AimDirection;

            IQuickHackable best = null;
            float bestScore = float.MaxValue;
            for (int i = 0; i < Devices.Count; i++)
            {
                var device = Devices[i];
                if (device == null || device.Object == null) continue;

                Vector3 to = device.HackPoint - eye;
                float distance = to.magnitude;
                if (distance < 0.05f || distance > range) continue;
                float score = Core.HackRules.AimScore(Vector3.Angle(aim, to), distance, range);
                if (score < 0f || score >= bestScore) continue;
                if (string.IsNullOrEmpty(device.HackPrompt(by))) continue;
                if (!HasLine(eye, to / distance, distance, device, by)) continue;

                best = device;
                bestScore = score;
            }
            return best;
        }

        /// <summary>Clear line from the eye to the device: nothing in between except the device and the hacker themselves.</summary>
        private static bool HasLine(Vector3 eye, Vector3 direction, float distance, IQuickHackable device, Network.NetworkPlayer by)
        {
            if (!Physics.Raycast(eye, direction, out RaycastHit hit, Mathf.Max(0f, distance - 0.3f), ~0, QueryTriggerInteraction.Ignore)) return true;
            var deviceTransform = device.Object.transform;
            if (hit.collider.transform == deviceTransform || hit.collider.transform.IsChildOf(deviceTransform)) return true;
            return hit.collider.transform.IsChildOf(by.transform);
        }
    }
}

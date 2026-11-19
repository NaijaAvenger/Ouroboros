// Stand-ins for the parts of imported asset packs that game code calls (Tools/typecheck only).
//
// The offline type-check does not compile Feel: it is over a thousand files with optional integrations for
// packages this checker has no reference assemblies for. Instead, the handful of Feel types that
// Assets/Scripts/Core/Juice.cs uses are declared here with the same names and signatures as the real ones in
// Assets/Feel (read from MMCameraShaker.cs, MMWiggle.cs, MMChannel.cs and HapticPatterns.cs).
//
// If Juice.cs starts using more of Feel, add the matching declaration here, copied from the real source.
// DOTween needs no stand-in: the checker references the real DOTween.dll.
// This file is never seen by Unity.
using UnityEngine;

namespace MoreMountains.Feedbacks
{
    public enum MMChannelModes { Int, MMChannel }
    public class MMChannel : ScriptableObject { }
    public class MMChannelData
    {
        public MMChannelModes MMChannelMode;
        public int Channel;
        public MMChannel MMChannelDefinition;
        public MMChannelData(MMChannelModes mode, int channel, MMChannel channelDefinition) { }
    }

    public struct MMCameraShakeEvent
    {
        public static void Trigger(float duration, float amplitude, float frequency, float amplitudeX, float amplitudeY, float amplitudeZ,
            bool infinite = false, MMChannelData channelData = null, bool useUnscaledTime = false) { }
    }

    public enum WiggleTypes { None, Random, PingPong, Noise, Curve }

    [System.Serializable]
    public class WiggleProperties
    {
        public bool WigglePermitted = true;
        public WiggleTypes WiggleType = WiggleTypes.Random;
        public bool UseUnscaledTime = false;
        public float TimeMultiplier = 1f;
        public bool StartWigglingAutomatically = true;
        public float FrequencyMin = 0f;
        public float FrequencyMax = 1f;
        public Vector3 AmplitudeMin = Vector3.zero;
        public Vector3 AmplitudeMax = Vector3.one;
        public bool RelativeAmplitude = true;
        public bool LimitedTime = false;
        public float LimitedTimeTotal;
        public bool LimitedTimeResetValue = true;
        public Vector3 NoiseFrequencyMin = Vector3.zero;
        public Vector3 NoiseFrequencyMax = Vector3.one;
    }

    public class MMWiggle : MonoBehaviour
    {
        public bool PositionActive = false;
        public bool RotationActive = false;
        public bool ScaleActive = false;
        public WiggleProperties PositionWiggleProperties;
        public WiggleProperties RotationWiggleProperties;
        public WiggleProperties ScaleWiggleProperties;
        public virtual void WigglePosition(float duration) { }
    }

    [RequireComponent(typeof(MMWiggle))]
    public class MMCameraShaker : MonoBehaviour
    {
        public MMChannelModes ChannelMode = MMChannelModes.Int;
        public int Channel = 0;
        public float CooldownBetweenShakes = 0f;
    }
}

namespace Lofelt.NiceVibrations
{
    public static class HapticPatterns
    {
        public enum PresetType
        {
            Selection = 0, Success = 1, Warning = 2, Failure = 3, LightImpact = 4, MediumImpact = 5, HeavyImpact = 6, RigidImpact = 7, SoftImpact = 8, None = -1
        }
        public static void PlayEmphasis(float amplitude, float frequency) { }
        public static void PlayConstant(float amplitude, float frequency, float duration) { }
        public static void PlayPreset(PresetType presetType) { }
    }
}

using System;
using UnityEngine;

namespace Ouroboros.Core
{
    /// <summary>
    /// Player options (v0.13, milestone M5), stored in PlayerPrefs on this machine. Everything that reads a
    /// setting reads it from here, so the settings screen only has to write here and raise <see cref="Changed"/>.
    /// </summary>
    public static class GameSettings
    {
        private const string Prefix = "ouroboros.settings.";

        public const float MinFov = 60f, MaxFov = 105f, DefaultFov = 80f;
        public const float MinSensitivity = 0.2f, MaxSensitivity = 3f;

        private static bool loaded;
        private static float sensitivity = 1f, aimSensitivity = 1f, fieldOfView = DefaultFov;
        private static float masterVolume = 1f, musicVolume = 0.7f;
        private static bool invertY, teammateMarkers = true, crewRadio = true;
        // (v0.15) feel and look
        private static float screenShake = 1f;
        private static bool vibration = true, stylisedLook = true;

        /// <summary>Raised after any setting changes.</summary>
        public static event Action Changed;

        /// <summary>Multiplier on look speed from the hip (mouse and stick).</summary>
        public static float Sensitivity { get { Load(); return sensitivity; } set { Set(ref sensitivity, Mathf.Clamp(value, MinSensitivity, MaxSensitivity), "sensitivity"); } }
        /// <summary>Extra multiplier while aiming down sights, on top of the zoom scaling.</summary>
        public static float AimSensitivity { get { Load(); return aimSensitivity; } set { Set(ref aimSensitivity, Mathf.Clamp(value, MinSensitivity, MaxSensitivity), "aimSensitivity"); } }
        /// <summary>Vertical field of view from the hip, in degrees.</summary>
        public static float FieldOfView { get { Load(); return fieldOfView; } set { Set(ref fieldOfView, Mathf.Clamp(value, MinFov, MaxFov), "fov"); } }
        public static float MasterVolume { get { Load(); return masterVolume; } set { Set(ref masterVolume, Mathf.Clamp01(value), "masterVolume"); AudioListener.volume = masterVolume; } }
        public static float MusicVolume { get { Load(); return musicVolume; } set { Set(ref musicVolume, Mathf.Clamp01(value), "musicVolume"); } }
        public static bool InvertY { get { Load(); return invertY; } set { Set(ref invertY, value, "invertY"); } }
        public static bool TeammateMarkers { get { Load(); return teammateMarkers; } set { Set(ref teammateMarkers, value, "teammateMarkers"); } }
        public static bool CrewRadio { get { Load(); return crewRadio; } set { Set(ref crewRadio, value, "crewRadio"); } }

        /// <summary>(v0.15) Multiplier on camera shake. 0 switches it off.</summary>
        public static float ScreenShake { get { Load(); return screenShake; } set { Set(ref screenShake, Mathf.Clamp(value, 0f, 1.5f), "screenShake"); } }
        /// <summary>(v0.15) Controller rumble and phone haptics.</summary>
        public static bool Vibration { get { Load(); return vibration; } set { Set(ref vibration, value, "vibration"); } }
        /// <summary>(v0.15) The cel-shaded look: toon materials, ink outlines and the colour grade.</summary>
        public static bool StylisedLook { get { Load(); return stylisedLook; } set { Set(ref stylisedLook, value, "stylisedLook"); } }

        /// <summary>Reads stored values once and applies the ones that live in engine state (volume).</summary>
        public static void Load()
        {
            if (loaded) return;
            loaded = true;
            sensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "sensitivity", 1f), MinSensitivity, MaxSensitivity);
            aimSensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "aimSensitivity", 1f), MinSensitivity, MaxSensitivity);
            fieldOfView = Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "fov", DefaultFov), MinFov, MaxFov);
            masterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix + "masterVolume", 1f));
            musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix + "musicVolume", 0.7f));
            invertY = PlayerPrefs.GetInt(Prefix + "invertY", 0) != 0;
            teammateMarkers = PlayerPrefs.GetInt(Prefix + "teammateMarkers", 1) != 0;
            crewRadio = PlayerPrefs.GetInt(Prefix + "crewRadio", 1) != 0;
            screenShake = Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "screenShake", 1f), 0f, 1.5f);
            vibration = PlayerPrefs.GetInt(Prefix + "vibration", 1) != 0;
            stylisedLook = PlayerPrefs.GetInt(Prefix + "stylisedLook", 1) != 0;
            AudioListener.volume = masterVolume;
        }

        public static void ResetToDefaults()
        {
            Load();
            Sensitivity = 1f; AimSensitivity = 1f; FieldOfView = DefaultFov;
            MasterVolume = 1f; MusicVolume = 0.7f;
            InvertY = false; TeammateMarkers = true; CrewRadio = true;
            ScreenShake = 1f; Vibration = true; StylisedLook = true;
        }

        private static void Set(ref float field, float value, string key)
        {
            Load();
            if (Mathf.Approximately(field, value)) return;
            field = value;
            PlayerPrefs.SetFloat(Prefix + key, value);
            Changed?.Invoke();
        }

        private static void Set(ref bool field, bool value, string key)
        {
            Load();
            if (field == value) return;
            field = value;
            PlayerPrefs.SetInt(Prefix + key, value ? 1 : 0);
            Changed?.Invoke();
        }
    }
}

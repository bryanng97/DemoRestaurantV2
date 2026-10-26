using System;
using UnityEngine;

namespace DemoRestaurant.Game.Content
{
    /// <summary>
    /// Device preferences (not game progress): music volume, language and frame rate. Stored in PlayerPrefs so they
    /// stay with the phone, apply before the first frame, and never go through the save game or the server.
    /// </summary>
    public static class GameSettings
    {
        private const string MusicKey = "settings.musicVolume";
        private const string LanguageKey = "settings.language";
        private const string FrameRateKey = "settings.frameRate";

        public static readonly int[] FrameRates = { 30, 60 };

        /// <summary>0..1, multiplied with the theme's own music volume.</summary>
        public static float MusicVolume { get; private set; } = 1f;
        /// <summary>"" = follow the device language.</summary>
        public static string Language { get; private set; } = "";
        /// <summary>0 = the scene's default.</summary>
        public static int FrameRate { get; private set; }

        public static event Action Changed;

        /// <summary>Drops handlers left over from the previous play session (no domain reload in the editor).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => Changed = null;

        /// <summary>Loads the stored preferences and applies language and frame rate.</summary>
        public static void Apply(int defaultFrameRate)
        {
            MusicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicKey, 1f));
            Language = PlayerPrefs.GetString(LanguageKey, "");
            FrameRate = PlayerPrefs.GetInt(FrameRateKey, 0);

            Loc.SetLanguage(string.IsNullOrEmpty(Language) ? Loc.DetectDeviceLanguage() : Language);
            Application.targetFrameRate = FrameRate > 0 ? FrameRate : defaultFrameRate;
        }

        public static void SetMusicVolume(float volume)
        {
            MusicVolume = Mathf.Clamp01(volume);
            PlayerPrefs.SetFloat(MusicKey, MusicVolume);
            Changed?.Invoke();
        }

        public static void SetLanguage(string language)
        {
            Language = language ?? "";
            PlayerPrefs.SetString(LanguageKey, Language);
            PlayerPrefs.Save();
            Loc.SetLanguage(string.IsNullOrEmpty(Language) ? Loc.DetectDeviceLanguage() : Language);
            Changed?.Invoke();
        }

        public static void SetFrameRate(int fps)
        {
            FrameRate = fps;
            PlayerPrefs.SetInt(FrameRateKey, fps);
            PlayerPrefs.Save();
            if (fps > 0) Application.targetFrameRate = fps;
            Changed?.Invoke();
        }

        /// <summary>Saves pending slider changes (called when the settings dialog closes).</summary>
        public static void Flush() => PlayerPrefs.Save();

        /// <summary>Part of "delete account and data": forget every preference on this device.</summary>
        public static void ClearAll()
        {
            PlayerPrefs.DeleteKey(MusicKey);
            PlayerPrefs.DeleteKey(LanguageKey);
            PlayerPrefs.DeleteKey(FrameRateKey);
            PlayerPrefs.Save();
        }
    }
}

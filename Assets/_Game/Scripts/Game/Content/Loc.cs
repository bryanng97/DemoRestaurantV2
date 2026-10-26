using System;
using System.Collections.Generic;
using UnityEngine;

namespace DemoRestaurant.Game.Content
{
    /// <summary>
    /// UI string lookup backed by Resources/Localization/&lt;lang&gt;.json (en, ms, zh).
    /// Every player-facing string goes through here. Missing keys fall back to English, then to the key itself.
    /// </summary>
    public static class Loc
    {
        public static readonly string[] SupportedLanguages = { "en", "ms", "zh" };
        private const string Fallback = "en";

        private static Dictionary<string, string> _current = new Dictionary<string, string>();
        private static Dictionary<string, string> _fallback;

        public static string Language { get; private set; } = Fallback;
        public static event Action LanguageChanged;

        /// <summary>
        /// The editor's Enter Play Mode Options skip the domain reload, so static events would keep handlers from
        /// the previous play session (UI objects that are already destroyed). Start every session clean.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession() => LanguageChanged = null;

        public static void SetLanguage(string language)
        {
            if (Array.IndexOf(SupportedLanguages, language) < 0)
                language = Fallback;
            _fallback ??= LoadTable(Fallback);
            _current = language == Fallback ? _fallback : LoadTable(language);
            Language = language;
            LanguageChanged?.Invoke();
        }

        /// <summary>Picks en/ms/zh from the device language.</summary>
        public static string DetectDeviceLanguage()
        {
            switch (Application.systemLanguage)
            {
                case SystemLanguage.Chinese:
                case SystemLanguage.ChineseSimplified:
                case SystemLanguage.ChineseTraditional:
                    return "zh";
                case SystemLanguage.Indonesian: // closest SystemLanguage to Bahasa Malaysia
                    return "ms";
                default:
                    return "en";
            }
        }

        public static string Get(string key)
        {
            if (_fallback == null)
                SetLanguage(Language);
            if (_current.TryGetValue(key, out var value) || _fallback.TryGetValue(key, out value))
                return value;
            return key;
        }

        public static string Format(string key, params object[] args) => string.Format(Get(key), args);

        private static Dictionary<string, string> LoadTable(string language)
        {
            var table = new Dictionary<string, string>();
            var asset = Resources.Load<TextAsset>($"Localization/{language}");
            if (asset == null)
            {
                Debug.LogError($"Missing translation file Resources/Localization/{language}.json");
                return table;
            }
            var file = JsonUtility.FromJson<LocFile>(asset.text);
            foreach (var e in file.entries)
                table[e.key] = e.value;
            // Names for the private prototype catalog (git-ignored, never shipped), when present.
            foreach (var name in new[] { language, "exterior_" + language })
            {
                var proto = Resources.Load<TextAsset>($"Localization/RefProto/{name}");
                if (proto == null) continue;
                foreach (var e in JsonUtility.FromJson<LocFile>(proto.text)?.entries ?? new LocEntry[0])
                    if (!table.ContainsKey(e.key)) table[e.key] = e.value;
            }
            return table;
        }

#pragma warning disable 0649
        [Serializable] private class LocFile { public LocEntry[] entries = Array.Empty<LocEntry>(); }
        [Serializable] private class LocEntry { public string key; public string value; }
#pragma warning restore 0649
    }
}

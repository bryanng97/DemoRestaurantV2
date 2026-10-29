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
        private static readonly Dictionary<string, Dictionary<string, string>> _catalogNames = new Dictionary<string, Dictionary<string, string>>();
        private static readonly Dictionary<string, string> _recipeFallbackKeys = new Dictionary<string, string>();

        public static string Language { get; private set; } = Fallback;
        public static event Action LanguageChanged;

        /// <summary>
        /// The editor's Enter Play Mode Options skip the domain reload, so static events would keep handlers from
        /// the previous play session (UI objects that are already destroyed). Start every session clean.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            LanguageChanged = null;
            _catalogNames.Clear();
            _recipeFallbackKeys.Clear();
        }

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
            if (_catalogNames.TryGetValue(key, out var names))
            {
                if (names.TryGetValue(Language, out var translated)) return translated;
                if (_recipeFallbackKeys.TryGetValue(key, out var bundledKey) && _current.TryGetValue(bundledKey, out translated)) return translated;
                if (names.TryGetValue(Fallback, out translated)) return translated;
            }
            if (_current.TryGetValue(key, out var value) || _fallback.TryGetValue(key, out value))
                return value;
            return key;
        }

        public static string Format(string key, params object[] args) => string.Format(Get(key), args);

        /// <summary>Published names use the same en/ms/zh lookup as bundled text.</summary>
        public static void ApplyCatalog(Logic.Online.JsonValue config)
        {
            _catalogNames.Clear();
            _recipeFallbackKeys.Clear();
            foreach (var item in config["body"]["catalog"]["furniture"].Items)
            {
                string id = item["id"].AsString();
                if (string.IsNullOrEmpty(id)) continue;
                var names = new Dictionary<string, string>();
                foreach (string language in SupportedLanguages)
                {
                    string value = item["localizedNames"][language].AsString();
                    if (!string.IsNullOrEmpty(value)) names[language] = value;
                }
                if (!names.ContainsKey(Fallback) && item["name"].AsString() != null) names[Fallback] = item["name"].AsString();
                if (names.Count > 0) _catalogNames["catalog.furniture." + id] = names;
            }
            foreach (var row in config["body"]["catalog"]["configuration"]["categories"].Items)
            {
                var names = new Dictionary<string,string>();
                foreach (string language in SupportedLanguages)
                {
                    string value = row["names"][language].AsString();
                    if (!string.IsNullOrEmpty(value)) names[language] = value;
                }
                if (names.Count > 0) _catalogNames["catalog.category." + row["code"].AsString()] = names;
            }
            foreach (var row in config["body"]["economy"]["dishes"].Items)
            {
                if (row["assetKey"].AsString() == null) _recipeFallbackKeys["catalog.recipe." + row["id"].AsString()] = "dish." + row["id"].AsString();
                var names = new Dictionary<string,string>();
                foreach (string language in SupportedLanguages)
                {
                    string value = row["localizedNames"][language].AsString();
                    if (!string.IsNullOrEmpty(value)) names[language] = value;
                }
                if (!names.ContainsKey(Fallback) && row["name"].AsString() != null) names[Fallback] = row["name"].AsString();
                if (names.Count > 0) _catalogNames["catalog.recipe." + row["id"].AsString()] = names;
            }
            LanguageChanged?.Invoke();
        }

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

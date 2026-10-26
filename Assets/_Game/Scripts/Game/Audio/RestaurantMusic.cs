using DemoRestaurant.Game.Content;
using UnityEngine;

namespace DemoRestaurant.Game.Audio
{
    public enum MusicScene { Restaurant, Street, Editor }

    /// <summary>
    /// Background music per scene (restaurant, street, decorating), cross-fading when the scene changes.
    /// Each scene lists Resources paths in order of preference; the first one that loads plays, so private
    /// prototype tracks can sit in front of the shipped ones. Volume = theme volume x the player's music setting.
    /// </summary>
    public sealed class RestaurantMusic : MonoBehaviour
    {
        private const float FadeSeconds = 0.8f;

        private ThemeArt _theme;
        private AudioSource _current, _fading;
        private MusicScene? _scene;
        private float _themeVolume = 1f, _fade = 1f;

        public void Bind(ThemeArt theme)
        {
            _theme = theme;
            _themeVolume = Mathf.Clamp01(theme.musicVolume);
            _current = NewSource();
            _fading = NewSource();
            Play(MusicScene.Restaurant);
        }

        public void Play(MusicScene scene)
        {
            if (_theme == null || _scene == scene) return;
            _scene = scene;
            var clip = Load(PathsFor(scene));
            if (clip == null || clip == _current.clip) return;
            (_current, _fading) = (_fading, _current);
            _current.clip = clip;
            _current.Play();
            _fade = 0f;
            ApplyVolume();
        }

        private string[] PathsFor(MusicScene scene)
        {
            var paths = scene == MusicScene.Street ? _theme.streetMusic
                : scene == MusicScene.Editor ? _theme.editorMusic
                : _theme.restaurantMusic;
            return paths != null && paths.Length > 0 ? paths : new[] { _theme.backgroundMusic };
        }

        private static AudioClip Load(string[] paths)
        {
            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                var clip = Resources.Load<AudioClip>(path);
                if (clip != null) return clip;
            }
            Debug.LogWarning("No background music found in Resources: " + string.Join(", ", paths));
            return null;
        }

        private AudioSource NewSource()
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.loop = true;
            source.volume = 0f;
            return source;
        }

        private void Update()
        {
            if (_current == null || _fade >= 1f) return;
            _fade = Mathf.Min(1f, _fade + Time.unscaledDeltaTime / FadeSeconds);
            ApplyVolume();
            if (_fade >= 1f) _fading.Stop();
        }

        private void OnEnable() => GameSettings.Changed += ApplyVolume;
        private void OnDisable() => GameSettings.Changed -= ApplyVolume;

        private void ApplyVolume()
        {
            if (_current == null) return;
            float full = _themeVolume * GameSettings.MusicVolume;
            _current.volume = full * _fade;
            _fading.volume = full * (1f - _fade);
        }
    }
}

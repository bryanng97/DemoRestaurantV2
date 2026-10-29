using System;
using System.Collections.Generic;
using DemoRestaurant.Game.Content;
using UnityEngine;

namespace DemoRestaurant.Game.Render
{
    /// <summary>Drawing hints for one sprite in a skin (Art/&lt;skin&gt;/skin.json).</summary>
    [Serializable]
    public sealed class SkinSprite
    {
        public string key;
        /// <summary>The sprite faces the other isometric axis and must be mirrored to face SouthWest.</summary>
        public bool mirror;
        /// <summary>Extra size factor (0 = 1).</summary>
        public float scale;
        /// <summary>Furniture: left, front, right base corners in normalized sprite pixels (Y up). Optional.</summary>
        public Vector2[] groundCorners;
        /// <summary>Floor tiles: top, right, bottom, left of the flat face in normalized sprite pixels. Optional.</summary>
        public Vector2[] surfaceCorners;
        /// <summary>Street tiles: where the inner edge of the near sidewalk meets the tile's left end (normalized, Y up).</summary>
        public Vector2 anchor;
        /// <summary>Furniture: cells the art covers in front of its footprint (a stove's floor mat where the chef stands).</summary>
        public int frontCells;
        /// <summary>Chairs: height of the seat in object-height units (0 = the content's seatHeight).</summary>
        public float seatHeight;
        /// <summary>Animations: visible width in pixels of the first frame (e.g. the closed door); with <see cref="anchor"/> = its bottom-centre.</summary>
        public float firstFrameWidth;
        /// <summary>Wall pieces: how high the bottom hangs above the floor line, in world units (0 = 0.35).</summary>
        public float wallLift;
        /// <summary>Character frames: where the carrying hand is (normalized, Y up), for the plate a waiter holds.</summary>
        public Vector2 hand;
        public int animationFrames, animationColumns, animationRows, animationWidth, animationHeight;
        public float animationFps;
        public int[] frameMap;
        public string animationOwner;
    }

    /// <summary>
    /// A skin's manifest. A skin is one Resources folder of sprites named by art key (the keys content JSON uses:
    /// "table01", "wall01", ...) plus this optional skin.json. Reskinning the game = adding a folder with the same
    /// file names and listing it first in theme.json "skins".
    /// </summary>
    [Serializable]
    public sealed class SkinManifest
    {
        /// <summary>
        /// Width in pixels of one floor cell in this skin's art. When set, sprites are drawn at their native size
        /// (one cell = this many pixels) instead of being stretched to their footprint.
        /// </summary>
        public float tilePixelWidth;
        /// <summary>Grid cells one wall sprite covers (0 = theme.json wallCellsPerSegment).</summary>
        public int wallCellsPerSegment;
        /// <summary>Scene colours (hex, e.g. "#92BA45"); empty = the game's defaults.</summary>
        public string backgroundColor, roadColor, sidewalkColor;
        public SkinSprite[] sprites = Array.Empty<SkinSprite>();

        [NonSerialized] public Dictionary<string, SkinSprite> ByKey;
    }

    /// <summary>
    /// Sprite lookup by art key across the theme's skins, in order of preference, falling back to the pack's art
    /// folder. Sprites are imported with a bottom-centre pivot (see ArtImportRules) and packed into a Sprite Atlas.
    /// Returns null for unknown keys so callers can fall back to placeholder art.
    /// </summary>
    public sealed class ArtLibrary
    {
        private readonly string[] _folders;
        private readonly SkinManifest[] _manifests;
        private readonly Dictionary<string, (Sprite sprite, int folder)> _cache = new Dictionary<string, (Sprite, int)>();

        public static ArtLibrary For(ThemeArt theme)
        {
            var folders = new List<string>();
            if (theme.skins != null)
                foreach (var s in theme.skins)
                    if (!string.IsNullOrWhiteSpace(s)) folders.Add(s);
            folders.Add(theme.artFolder);
            return new ArtLibrary(folders.ToArray());
        }

        public ArtLibrary(params string[] resourcesFolders)
        {
            _folders = new string[resourcesFolders.Length];
            _manifests = new SkinManifest[resourcesFolders.Length];
            for (int i = 0; i < resourcesFolders.Length; i++)
            {
                _folders[i] = resourcesFolders[i].TrimEnd('/');
                _manifests[i] = LoadManifest(_folders[i]);
            }
        }

        /// <summary>A scene colour from the most preferred skin that sets it (see <see cref="SkinManifest"/>), else <paramref name="fallback"/>.</summary>
        public Color SceneColor(Func<SkinManifest, string> field, Color fallback)
        {
            foreach (var m in _manifests)
                if (m != null && ColorUtility.TryParseHtmlString(field(m) ?? "", out var c)) return c;
            return fallback;
        }

        public Sprite Get(string key) => Find(key, warn: true).sprite;

        /// <summary>Like <see cref="Get"/>, without a warning when no skin has the key (optional art such as animation frames).</summary>
        public Sprite GetOptional(string key) => Find(key, warn: false).sprite;

        public Texture2D AnimationTexture(string ownerKey, string sheetKey)
        {
            var owner=Find(ownerKey,false);
            return owner.folder>=0 ? Resources.Load<Texture2D>($"{_folders[owner.folder]}/{sheetKey}") : null;
        }

        /// <summary>The manifest of the skin that supplies <paramref name="key"/>, or null (plain art folder).</summary>
        public SkinManifest SkinOf(string key)
        {
            var (sprite, folder) = Find(key, warn: false);
            return sprite != null && folder >= 0 ? _manifests[folder] : null;
        }

        /// <summary>The skin's hints for <paramref name="key"/>, or null.</summary>
        public SkinSprite HintsFor(string key)
        {
            var manifest = SkinOf(key);
            if (manifest != null) return manifest.ByKey.TryGetValue(key, out var hints) ? hints : null;
            // Keys without a sprite of their own (an animation's base name, e.g. "door01_open"): the first skin that has them.
            if (string.IsNullOrEmpty(key)) return null;
            foreach (var m in _manifests)
                if (m != null && m.ByKey.TryGetValue(key, out var h)) return h;
            return null;
        }

        /// <summary>The first skin whose manifest has hints for <paramref name="key"/>, or null.</summary>
        public SkinManifest ManifestWithHints(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (var m in _manifests)
                if (m != null && m.ByKey.ContainsKey(key)) return m;
            return null;
        }

        /// <summary>
        /// The furniture's drawing settings for the sprite actually used: a skin's own hints replace the content's
        /// (which describe the base art), so other skins never inherit mirroring or base corners meant for it.
        /// </summary>
        public FurnitureArt Resolve(FurnitureArt art)
        {
            var manifest = SkinOf(art.Sprite);
            if (manifest == null) return art;
            manifest.ByKey.TryGetValue(art.Sprite, out var hints);
            return new FurnitureArt
            {
                Sprite = art.Sprite, SpriteBack = art.SpriteBack, Category = art.Category,
                PlaceholderColor = art.PlaceholderColor,
                MirrorSprite = hints != null && hints.mirror,
                Scale = hints != null && hints.scale > 0 ? hints.scale : 1f,
                GroundCorners = hints?.groundCorners != null && hints.groundCorners.Length == 3 ? hints.groundCorners : null,
                TilePixelWidth = manifest.tilePixelWidth,
                FrontCells = hints != null ? Math.Max(0, hints.frontCells) : 0,
            };
        }

        private (Sprite sprite, int folder) Find(string key, bool warn)
        {
            var remote = Online.RemoteAssetArt.Find(key);
            if (remote != null) return (remote.Sprite, -1);
            if (string.IsNullOrEmpty(key))
                return (null, -1);
            if (_cache.TryGetValue(key, out var hit))
                return hit;
            hit = (null, -1);
            for (int i = 0; i < _folders.Length && hit.sprite == null; i++)
            {
                var sprite = Resources.Load<Sprite>($"{_folders[i]}/{key}");
                if (sprite != null) hit = (sprite, i);
            }
            if (hit.sprite == null && warn)
                Debug.LogWarning($"Missing sprite '{key}' in Resources/{string.Join(", Resources/", _folders)}; using placeholder art.");
            _cache[key] = hit;
            return hit;
        }

        private static SkinManifest LoadManifest(string folder)
        {
            var text = Resources.Load<TextAsset>($"{folder}/skin");
            if (text == null) return null;
            var manifest = JsonUtility.FromJson<SkinManifest>(text.text) ?? new SkinManifest();
            manifest.ByKey = new Dictionary<string, SkinSprite>();
            foreach (var s in manifest.sprites ?? Array.Empty<SkinSprite>())
                if (!string.IsNullOrEmpty(s?.key)) manifest.ByKey[s.key] = s;
            var loops = Resources.Load<TextAsset>($"{folder}/furniture-loops");
            if (loops != null)
                foreach (var s in JsonUtility.FromJson<SkinManifest>(loops.text)?.sprites ?? Array.Empty<SkinSprite>())
                    if (!string.IsNullOrEmpty(s?.key)) manifest.ByKey[s.key] = s;
            return manifest;
        }
    }
}

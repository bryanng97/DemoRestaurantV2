using UnityEngine;

namespace DemoRestaurant.Game.UI
{
    /// <summary>Procedural UI pieces (rounded panels) until the UI kit arrives from the designers.</summary>
    public static class UiArt
    {
        private static Sprite _pill;
        private static Sprite _rounded;
        private static Sprite _circle;
        private static Sprite _pixel;
        private static Font _font;

        /// <summary>White anti-aliased disc, tinted through Image.color.</summary>
        public static Sprite Circle()
        {
            if (_circle != null)
                return _circle;
            const int size = 64;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - 32f) * (x + 0.5f - 32f) + (y + 0.5f - 32f) * (y + 0.5f - 32f)) - 31f;
                byte a = d <= -0.5f ? (byte)255 : d >= 0.5f ? (byte)0 : (byte)(255 * (0.5f - d));
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "ui_circle", wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            _circle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            _circle.name = "ui_circle";
            return _circle;
        }

        /// <summary>Plain white sprite for bars and fills.</summary>
        public static Sprite Pixel()
        {
            if (_pixel == null)
            {
                _pixel = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
                _pixel.name = "ui_pixel";
            }
            return _pixel;
        }

        /// <summary>White 9-sliced rounded rectangle, tinted per use through Image.color.</summary>
        public static Sprite Rounded()
        {
            if (_rounded != null)
                return _rounded;
            const int size = 64, radius = 22;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = DistanceOutsideRoundedRect(x + 0.5f, y + 0.5f, size, radius);
                byte a = d <= -0.5f ? (byte)255 : d >= 0.5f ? (byte)0 : (byte)(255 * (0.5f - d));
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "ui_rounded", wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            _rounded = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(radius + 1, radius + 1, radius + 1, radius + 1));
            _rounded.name = "ui_rounded";
            return _rounded;
        }

        /// <summary>Built-in dynamic font; falls back to OS fonts for Chinese glyphs.</summary>
        public static Font Font => _font ? _font : _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        /// <summary>9-sliced rounded rectangle: cream fill with a soft brown rim, like the concept's currency pills.</summary>
        public static Sprite Pill()
        {
            if (_pill != null)
                return _pill;

            const int size = 64, radius = 30, rim = 4;
            var fill = new Color32(253, 247, 234, 245);
            var edge = new Color32(176, 140, 102, 255);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = DistanceOutsideRoundedRect(x + 0.5f, y + 0.5f, size, radius);
                Color32 c;
                if (d > 0.5f) c = new Color32(0, 0, 0, 0);
                else if (d > -rim) c = edge;
                else c = fill;
                if (d > -0.5f && d <= 0.5f) c.a = (byte)(c.a * (0.5f - d)); // anti-aliased outer edge
                px[y * size + x] = c;
            }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "ui_pill", wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            _pill = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(radius + 1, radius + 1, radius + 1, radius + 1));
            _pill.name = "ui_pill";
            return _pill;
        }

        private static float DistanceOutsideRoundedRect(float x, float y, int size, int radius)
        {
            float cx = Mathf.Clamp(x, radius, size - radius);
            float cy = Mathf.Clamp(y, radius, size - radius);
            return Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - radius;
        }
    }
}

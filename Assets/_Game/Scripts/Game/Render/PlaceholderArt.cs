using System.Collections.Generic;
using UnityEngine;

namespace DemoRestaurant.Game.Render
{
    /// <summary>
    /// Generates flat-shaded placeholder sprites until the designers' atlases arrive.
    /// Everything is drawn in projected isometric space, so wall decorations are skewed to the wall plane.
    /// Replace call sites with atlas lookups; the pivot conventions here match what real art must use:
    /// furniture pivots on the footprint's back corner (grid origin, floor level), characters on their feet.
    /// </summary>
    public static class PlaceholderArt
    {
        private const float Ppu = IsoProjection.PixelsPerUnit;

        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        // Static fields survive Play mode when domain reload is disabled, but generated
        // Unity objects can be destroyed between sessions.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => Cache.Clear();

        /// <summary>All floor cells merged into one sprite. Pivot at grid (0,0).</summary>
        public static Sprite Floor(int width, int height, Color a, Color b)
        {
            var corners = new List<Vector2>
            {
                IsoProjection.GridToWorld(0, 0), IsoProjection.GridToWorld(width, 0),
                IsoProjection.GridToWorld(width, height), IsoProjection.GridToWorld(0, height),
            };
            var canvas = Canvas.FromWorldBounds(corners);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                Color c = ((x + y) & 1) == 0 ? a : b;
                canvas.Fill(Quad(x, y, 0, x + 1, y, 0, x + 1, y + 1, 0, x, y + 1, 0), c);
            }
            canvas.Stroke(corners.ToArray(), Darken(a, 0.7f));
            return canvas.ToSprite("floor");
        }

        /// <summary>
        /// The two back walls merged into one sprite, with windows and posts drawn on the wall planes.
        /// Back-right wall runs along grid Y = 0, back-left wall along grid X = 0. Pivot at grid (0,0) on the floor.
        /// </summary>
        public static Sprite BackWalls(int width, int height, float wallHeight, Color wallColor, Color trimColor,
            IReadOnlyList<float> rightWallWindows, IReadOnlyList<float> leftWallWindows)
        {
            var corners = new List<Vector2>
            {
                IsoProjection.GridToWorld(0, 0, 0), IsoProjection.GridToWorld(width, 0, 0),
                IsoProjection.GridToWorld(0, height, 0), IsoProjection.GridToWorld(0, 0, wallHeight),
                IsoProjection.GridToWorld(width, 0, wallHeight), IsoProjection.GridToWorld(0, height, wallHeight),
            };
            var canvas = Canvas.FromWorldBounds(corners);
            Color left = Darken(wallColor, 0.85f);
            Color glass = new Color(0.62f, 0.82f, 0.93f);
            Color frame = Darken(trimColor, 0.8f);

            // Back-left wall (X = 0 plane), then back-right wall (Y = 0 plane).
            canvas.Fill(Quad(0, 0, 0, 0, height, 0, 0, height, wallHeight, 0, 0, wallHeight), left);
            canvas.Fill(Quad(0, 0, 0, width, 0, 0, width, 0, wallHeight, 0, 0, wallHeight), wallColor);

            // Skirting boards.
            canvas.Fill(Quad(0, 0, 0, 0, height, 0, 0, height, 0.25f, 0, 0, 0.25f), Darken(trimColor, 0.85f));
            canvas.Fill(Quad(0, 0, 0, width, 0, 0, width, 0, 0.25f, 0, 0, 0.25f), trimColor);

            // Posts every 4 cells, lying flat on the wall surface.
            for (int x = 4; x < width; x += 4)
                canvas.Fill(Quad(x - 0.08f, 0, 0, x + 0.08f, 0, 0, x + 0.08f, 0, wallHeight, x - 0.08f, 0, wallHeight), trimColor);
            for (int y = 4; y < height; y += 4)
                canvas.Fill(Quad(0, y - 0.08f, 0, 0, y + 0.08f, 0, 0, y + 0.08f, wallHeight, 0, y - 0.08f, wallHeight), Darken(trimColor, 0.85f));

            // Windows: parallelograms in the wall plane, so they follow the isometric slope.
            const float w = 1.4f, z0 = 1.0f, z1 = 2.3f, f = 0.08f;
            foreach (float x in rightWallWindows)
            {
                canvas.Fill(Quad(x - f, 0, z0 - f, x + w + f, 0, z0 - f, x + w + f, 0, z1 + f, x - f, 0, z1 + f), frame);
                canvas.Fill(Quad(x, 0, z0, x + w, 0, z0, x + w, 0, z1, x, 0, z1), glass);
                canvas.Fill(Quad(x + w / 2 - f / 2, 0, z0, x + w / 2 + f / 2, 0, z0, x + w / 2 + f / 2, 0, z1, x + w / 2 - f / 2, 0, z1), frame);
            }
            foreach (float y in leftWallWindows)
            {
                canvas.Fill(Quad(0, y - f, z0 - f, 0, y + w + f, z0 - f, 0, y + w + f, z1 + f, 0, y - f, z1 + f), Darken(frame, 0.85f));
                canvas.Fill(Quad(0, y, z0, 0, y + w, z0, 0, y + w, z1, 0, y, z1), Darken(glass, 0.9f));
                canvas.Fill(Quad(0, y + w / 2 - f / 2, z0, 0, y + w / 2 + f / 2, z0, 0, y + w / 2 + f / 2, z1, 0, y + w / 2 - f / 2, z1), Darken(frame, 0.85f));
            }
            return canvas.ToSprite("walls");
        }

        /// <summary>An isometric box covering a footprint. Pivot at the footprint's back corner on the floor.</summary>
        public static Sprite Prism(int sizeX, int sizeY, float height, Color color)
        {
            string key = $"prism_{sizeX}_{sizeY}_{height:0.00}_{ColorUtility.ToHtmlStringRGB(color)}";
            if (Cache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            const float inset = 0.08f;
            float x0 = inset, y0 = inset, x1 = sizeX - inset, y1 = sizeY - inset, h = Mathf.Max(0.05f, height);
            var corners = new List<Vector2>
            {
                IsoProjection.GridToWorld(x0, y0, 0), IsoProjection.GridToWorld(x1, y0, 0),
                IsoProjection.GridToWorld(x1, y1, 0), IsoProjection.GridToWorld(x0, y1, 0),
                IsoProjection.GridToWorld(x0, y0, h), IsoProjection.GridToWorld(x1, y0, h),
                IsoProjection.GridToWorld(x1, y1, h), IsoProjection.GridToWorld(x0, y1, h),
                IsoProjection.GridToWorld(0, 0, 0),
            };
            var canvas = Canvas.FromWorldBounds(corners);
            Color edge = Darken(color, 0.55f);

            var leftFace = Quad(x0, y1, 0, x1, y1, 0, x1, y1, h, x0, y1, h);
            var rightFace = Quad(x1, y0, 0, x1, y1, 0, x1, y1, h, x1, y0, h);
            var top = Quad(x0, y0, h, x1, y0, h, x1, y1, h, x0, y1, h);
            canvas.Fill(leftFace, Darken(color, 0.82f));
            canvas.Fill(rightFace, Darken(color, 0.66f));
            canvas.Fill(top, Lighten(color, 0.12f));
            canvas.Stroke(leftFace, edge);
            canvas.Stroke(rightFace, edge);
            canvas.Stroke(top, edge);

            var sprite = canvas.ToSprite(key);
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>A white 1x1 sprite (0.01 world units at 100 PPU), pivot at the left edge: scale it for bars.</summary>
        public static Sprite Pixel()
        {
            const string key = "pixel";
            if (Cache.TryGetValue(key, out var cached) && cached != null)
                return cached;
            var sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0f, 0.5f), 100f);
            sprite.name = key;
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>Soft ellipse used for floor shadows. Pivot at its centre.</summary>
        public static Sprite Shadow()
        {
            const string key = "shadow";
            if (Cache.TryGetValue(key, out var cached) && cached != null)
                return cached;
            int w = 64, h = 32;
            var canvas = new Canvas(w, h, new Vector2(0.5f, 0.5f));
            canvas.Ellipse(w / 2f, h / 2f, w / 2f - 1, h / 2f - 1, new Color(0, 0, 0, 0.28f));
            var sprite = canvas.ToSprite(key);
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>White torso, tinted per outfit at runtime. Pivot at the feet.</summary>
        public static Sprite CharacterBody()
        {
            const string key = "char_body";
            if (Cache.TryGetValue(key, out var cached) && cached != null)
                return cached;
            int w = 48, h = 72;
            var canvas = new Canvas(w, h, new Vector2(0.5f, 0f));
            canvas.Ellipse(w / 2f, 22, 20, 20, Color.white);
            canvas.Fill(new[] { new Vector2(4, 22), new Vector2(44, 22), new Vector2(40, 66), new Vector2(8, 66) }, Color.white);
            canvas.Ellipse(w / 2f, 66, 16, 6, Color.white);
            canvas.Fill(new[] { new Vector2(10, 0), new Vector2(20, 0), new Vector2(20, 8), new Vector2(10, 8) }, new Color(0.25f, 0.2f, 0.2f));
            canvas.Fill(new[] { new Vector2(28, 0), new Vector2(38, 0), new Vector2(38, 8), new Vector2(28, 8) }, new Color(0.25f, 0.2f, 0.2f));
            var sprite = canvas.ToSprite(key);
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>Head with face (front) or hair only (back). Pivot at the neck.</summary>
        public static Sprite CharacterHead(bool back, Color skin, Color hair)
        {
            string key = back ? "char_head_back" : "char_head_front";
            if (Cache.TryGetValue(key, out var cached) && cached != null)
                return cached;
            int s = 48;
            var canvas = new Canvas(s, s, new Vector2(0.5f, 0.05f));
            canvas.Ellipse(24, 24, 22, 22, back ? hair : skin);
            if (back)
            {
                canvas.Ellipse(24, 30, 22, 16, Darken(hair, 0.9f));
            }
            else
            {
                canvas.Fill(new[] { new Vector2(2, 30), new Vector2(46, 30), new Vector2(42, 46), new Vector2(6, 46) }, hair);
                canvas.Ellipse(30, 22, 3, 4, new Color(0.15f, 0.1f, 0.1f));
                canvas.Ellipse(40, 22, 3, 4, new Color(0.15f, 0.1f, 0.1f));
                canvas.Ellipse(35, 13, 4, 2, new Color(0.85f, 0.45f, 0.45f));
            }
            var sprite = canvas.ToSprite(key);
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>Speech bubble shown while a customer waits for food. Pivot at the tail.</summary>
        public static Sprite Bubble()
        {
            const string key = "bubble";
            if (Cache.TryGetValue(key, out var cached) && cached != null)
                return cached;
            var canvas = new Canvas(40, 40, new Vector2(0.3f, 0f));
            canvas.Fill(new[] { new Vector2(8, 0), new Vector2(20, 12), new Vector2(10, 14) }, Color.white);
            canvas.Ellipse(20, 24, 18, 15, Color.white);
            canvas.Ellipse(20, 24, 9, 7, new Color(1f, 0.6f, 0.2f));
            var sprite = canvas.ToSprite(key);
            Cache[key] = sprite;
            return sprite;
        }

        public static Color Darken(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);
        public static Color Lighten(Color c, float k) => Color.Lerp(c, Color.white, k);

        private static Vector2[] Quad(float x0, float y0, float z0, float x1, float y1, float z1,
            float x2, float y2, float z2, float x3, float y3, float z3)
        {
            return new[]
            {
                IsoProjection.GridToWorld(x0, y0, z0), IsoProjection.GridToWorld(x1, y1, z1),
                IsoProjection.GridToWorld(x2, y2, z2), IsoProjection.GridToWorld(x3, y3, z3),
            };
        }

        /// <summary>Minimal software rasteriser. Accepts world-space polygons once created via FromWorldBounds.</summary>
        private sealed class Canvas
        {
            private readonly int _w, _h;
            private readonly Color32[] _px;
            private readonly Vector2 _pivot;
            private readonly Vector2 _worldOrigin;
            private readonly bool _worldSpace;

            public Canvas(int w, int h, Vector2 pivot)
            {
                _w = w;
                _h = h;
                _px = new Color32[w * h];
                _pivot = pivot;
            }

            private Canvas(int w, int h, Vector2 pivot, Vector2 worldOrigin) : this(w, h, pivot)
            {
                _worldOrigin = worldOrigin;
                _worldSpace = true;
            }

            /// <summary>Canvas covering the given world points, with the sprite pivot at world (0,0).</summary>
            public static Canvas FromWorldBounds(IReadOnlyList<Vector2> points)
            {
                Vector2 min = points[0], max = points[0];
                foreach (var p in points)
                {
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                }
                min = Vector2.Min(min, Vector2.zero) - Vector2.one * (2f / Ppu);
                max = Vector2.Max(max, Vector2.zero) + Vector2.one * (2f / Ppu);
                int w = Mathf.CeilToInt((max.x - min.x) * Ppu);
                int h = Mathf.CeilToInt((max.y - min.y) * Ppu);
                var pivot = new Vector2(-min.x * Ppu / w, -min.y * Ppu / h);
                return new Canvas(w, h, pivot, min);
            }

            private Vector2 ToPixels(Vector2 p) => _worldSpace ? (p - _worldOrigin) * Ppu : p;

            public void Fill(Vector2[] poly, Color color)
            {
                var pts = new Vector2[poly.Length];
                Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = -min;
                for (int i = 0; i < poly.Length; i++)
                {
                    pts[i] = ToPixels(poly[i]);
                    min = Vector2.Min(min, pts[i]);
                    max = Vector2.Max(max, pts[i]);
                }
                int x0 = Mathf.Max(0, Mathf.FloorToInt(min.x)), x1 = Mathf.Min(_w - 1, Mathf.CeilToInt(max.x));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(min.y)), y1 = Mathf.Min(_h - 1, Mathf.CeilToInt(max.y));
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    if (Inside(pts, x + 0.5f, y + 0.5f))
                        Blend(x, y, color);
                }
            }

            public void Stroke(Vector2[] poly, Color color)
            {
                for (int i = 0; i < poly.Length; i++)
                {
                    Vector2 a = ToPixels(poly[i]), b = ToPixels(poly[(i + 1) % poly.Length]);
                    int steps = Mathf.CeilToInt(Vector2.Distance(a, b)) + 1;
                    for (int s = 0; s <= steps; s++)
                    {
                        Vector2 p = Vector2.Lerp(a, b, s / (float)steps);
                        int px = Mathf.Clamp(Mathf.RoundToInt(p.x), 0, _w - 1), py = Mathf.Clamp(Mathf.RoundToInt(p.y), 0, _h - 1);
                        Blend(px, py, color);
                    }
                }
            }

            public void Ellipse(float cx, float cy, float rx, float ry, Color color)
            {
                int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - rx)), x1 = Mathf.Min(_w - 1, Mathf.CeilToInt(cx + rx));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - ry)), y1 = Mathf.Min(_h - 1, Mathf.CeilToInt(cy + ry));
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
                    if (dx * dx + dy * dy <= 1f)
                        Blend(x, y, color);
                }
            }

            public Sprite ToSprite(string name)
            {
                var tex = new Texture2D(_w, _h, TextureFormat.RGBA32, false)
                {
                    name = name,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                tex.SetPixels32(_px);
                tex.Apply(false, true);
                var sprite = Sprite.Create(tex, new Rect(0, 0, _w, _h), _pivot, Ppu, 0, SpriteMeshType.FullRect);
                sprite.name = name;
                return sprite;
            }

            private void Blend(int x, int y, Color color)
            {
                int i = y * _w + x;
                Color dst = _px[i];
                float a = color.a + dst.a * (1 - color.a);
                Color outC = a > 0 ? (color * color.a + dst * dst.a * (1 - color.a)) / a : Color.clear;
                outC.a = a;
                _px[i] = outC;
            }

            private static bool Inside(Vector2[] poly, float x, float y)
            {
                bool inside = false;
                for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                {
                    if ((poly[i].y > y) != (poly[j].y > y)
                        && x < (poly[j].x - poly[i].x) * (y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                        inside = !inside;
                }
                return inside;
            }
        }
    }
}

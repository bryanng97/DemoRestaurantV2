using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Profile;
using UnityEngine;

namespace DemoRestaurant.Game.Render
{
    /// <summary>
    /// Uses the installed native avatar model, texture layers and animation for <see cref="AvatarLook"/>.
    /// Packs without that model retain the original procedural fallback: a big
    /// round head, large glossy eyes, a fluffy fringe, a small body with short legs, a soft dark-brown outline and
    /// simple cel shading. Every part is a signed-distance shape, so new hair, hats or outfits are a few lines.
    /// A stand-in until the designers deliver layered avatar art (AvatarPartDef.Sprite).
    /// Front and back views (for walking away from the camera in the restaurant) are cached per look.
    /// </summary>
    public sealed class AvatarPainter : IDisposable
    {
        private const int Width = 160;
        private const int Height = 236;
        /// <summary>Figure coordinates are y-down from the top of the head; this leaves room for tall hats.</summary>
        private const float Top = 36f;
        private const float OutlineWidth = 2.1f;
        private const int CacheSize = 12;
        /// <summary>Where the legs start, in figure coordinates.</summary>
        private const float HipY = 148f;

        private static readonly Color Ink = new Color32(74, 46, 32, 255);
        private static readonly Color Shoe = new Color32(122, 78, 48, 255);
        private static readonly Color White = new Color32(250, 248, 242, 255);
        private static readonly Color EyeDark = new Color32(52, 30, 22, 255);

        private readonly AvatarCatalog _catalog;
        private readonly Dictionary<string, Entry> _cache = new Dictionary<string, Entry>();
        private readonly List<string> _order = new List<string>();
        private readonly Dictionary<string, int> _retained = new Dictionary<string, int>();
        private readonly Color[] _px = new Color[Width * Height];

        private readonly NativeAvatarRenderer _native;
        public bool NativeAvailable=>_native!=null && _native.Available;
        public AvatarPainter(AvatarCatalog catalog,bool native=true)
        {
            _catalog=catalog;
            if(native)_native=new NativeAvatarRenderer(catalog);
        }
        public CharacterFrames NativeFrames(AvatarLook look)=>NativeAvailable?_native.Get(look).Frames:null;

        private sealed class Entry
        {
            public Texture2D FrontTex, BackTex;
            public Sprite Body, Head, Back;
        }

        /// <summary>Full-body front view, feet at the pivot (profile preview, standing in the restaurant).</summary>
        public Sprite Body(AvatarLook look) => NativeAvailable?_native.Get(look).Body:Get(look).Body;

        /// <summary>Full-body back view, for walking away from the camera.</summary>
        public Sprite Back(AvatarLook look) => NativeAvailable?_native.Get(look).Back:Get(look).Back;

        /// <summary>Square head-and-shoulders crop (HUD profile card, staff list).</summary>
        public Sprite Head(AvatarLook look) => NativeAvailable?_native.Get(look).Head:Get(look).Head;

        /// <summary>Feet-to-hip share of the full-body sprite, where the walk animation cuts the legs.</summary>
        public const float LegFraction = (Height - Top - HipY) / Height;

        /// <summary>
        /// Keeps a look's sprites alive while something in the world or the HUD shows them (previews in the profile
        /// dialog come and go and would otherwise push them out of the cache). Returns the key for <see cref="Release"/>.
        /// </summary>
        public string Retain(AvatarLook look)
        {
            if(NativeAvailable){_native.Retain(look);return look.Key;}
            Get(look);
            string key = look.Key;
            _retained[key] = (_retained.TryGetValue(key, out int n) ? n : 0) + 1;
            return key;
        }

        public void Release(string key)
        {
            if(NativeAvailable){_native.Release(key);return;}
            if (key == null || !_retained.TryGetValue(key, out int n)) return;
            if (n <= 1) _retained.Remove(key);
            else _retained[key] = n - 1;
        }

        public void Dispose()
        {
            _native?.Dispose();
            foreach(var entry in _cache.Values)
            {
                foreach(var obj in new UnityEngine.Object[]{entry.Body,entry.Head,entry.Back,entry.FrontTex,entry.BackTex})
                    if(Application.isPlaying)UnityEngine.Object.Destroy(obj);else UnityEngine.Object.DestroyImmediate(obj);
            }
            _cache.Clear();_retained.Clear();_order.Clear();
        }

        private Entry Get(AvatarLook look)
        {
            string key = look.Key;
            if (_cache.TryGetValue(key, out var hit))
                return hit;

            var front = Render(look, back: false);
            var backTex = Render(look, back: true);
            var entry = new Entry
            {
                FrontTex = front,
                BackTex = backTex,
                // FullRect so the walk animation can cut legs from the texture rect.
                Body = Sprite.Create(front, new Rect(0, 0, Width, Height), new Vector2(0.5f, 0f), 100f, 0, SpriteMeshType.FullRect),
                Back = Sprite.Create(backTex, new Rect(0, 0, Width, Height), new Vector2(0.5f, 0f), 100f, 0, SpriteMeshType.FullRect),
                // Head crop: figure y -20..124 (hat, face and shoulders).
                Head = Sprite.Create(front, new Rect(8, Height - (Top + 124f), 144, 144), new Vector2(0.5f, 0.5f), 100f),
            };

            _cache[key] = entry;
            _order.Add(key);
            // Evict the oldest look nobody retains (the cache may grow past its size while all are in use).
            int victim = _order.Count > CacheSize ? _order.FindIndex(0, _order.Count - 1, k => !_retained.ContainsKey(k)) : -1;
            if (victim >= 0)
            {
                string old = _order[victim];
                _order.RemoveAt(victim);
                var e = _cache[old];
                _cache.Remove(old);
                UnityEngine.Object.Destroy(e.Body);
                UnityEngine.Object.Destroy(e.Back);
                UnityEngine.Object.Destroy(e.Head);
                UnityEngine.Object.Destroy(e.FrontTex);
                UnityEngine.Object.Destroy(e.BackTex);
            }
            return entry;
        }

        private Texture2D Render(AvatarLook look, bool back)
        {
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
            {
                name = back ? "avatar_back" : "avatar", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            };
            Paint(look, back);
            tex.SetPixels(_px);
            tex.Apply(false, true);
            return tex;
        }

        private Color ColorOf(AvatarLook look, AvatarSlot slot, string fallback)
        {
            var part = _catalog.Find(slot, look[slot]);
            string hex = part != null && !string.IsNullOrEmpty(part.Color) ? part.Color : fallback;
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
        }

        // ---------------------------------------------------------------------------------------------------------
        // The figure. Coordinates: x 0..160 (centre 80), y down from the top of the head (y = 0), feet at y ~196.

        private sealed class Look
        {
            public bool Female, Back;
            public Color Skin, Hair, Shirt, Pants;
            public string HairStyle, Eyes, Brows, Hat, ShirtStyle, PantsStyle;
            public float TorsoHalf;
            public bool HairHidden => Hat == "tudung";
            public bool HatOn => Hat != "none";
        }

        private void Paint(AvatarLook avatar, bool back)
        {
            Array.Clear(_px, 0, _px.Length);
            var k = new Look
            {
                Back = back,
                Female = avatar[AvatarSlot.Gender] != "male",
                Skin = ColorOf(avatar, AvatarSlot.SkinTone, "#F3D2B8"),
                Hair = ColorOf(avatar, AvatarSlot.HairColor, "#6B4226"),
                Shirt = ColorOf(avatar, AvatarSlot.ShirtColor, "#2E3A5C"),
                Pants = ColorOf(avatar, AvatarSlot.PantsColor, "#2E3A5C"),
                HairStyle = avatar[AvatarSlot.HairStyle] ?? "bob",
                Eyes = avatar[AvatarSlot.Eyes] ?? "round",
                Brows = avatar[AvatarSlot.Eyebrows] ?? "soft",
                Hat = avatar[AvatarSlot.Hat] ?? "none",
                ShirtStyle = avatar[AvatarSlot.Shirt] ?? "tee",
                PantsStyle = avatar[AvatarSlot.Pants] ?? "trousers",
            };
            k.TorsoHalf = k.Female ? 23f : 26f;

            if (!back && !k.HairHidden) HairBehind(k);   // long hair, ponytail, bun: behind the head and body
            if (back && k.Hat == "tudung") TudungDrape(k); // from behind the drape covers the back

            LegsAndShoes(k);
            ArmsAndTorso(k);
            if (!back && k.Hat == "tudung") TudungDrape(k);

            if (k.Hat == "tudung")
            {
                if (back) TudungHead(k);
                else { TudungHead(k); Face(k, small: true); }
                Hat(k);
                return;
            }

            // Head.
            if (!(k.Hat == "cat_hood"))
            {
                Fill((x, y) => Circle(x, y, 31, 78, 7f), k.Skin);
                Fill((x, y) => Circle(x, y, 129, 78, 7f), k.Skin);
            }
            Fill(HeadShape, k.Skin);
            if (back)
            {
                HairFromBehind(k);
            }
            else
            {
                Face(k, small: false);
                if (k.Brows != "none") Brows(k);
                HairFront(k);
            }
            Hat(k);
        }

        private static float HeadShape(float x, float y) =>
            SmoothMin(Ellipse(x, y, 80, 58, 50, 47), Ellipse(x, y, 80, 76, 42, 36), 10f);

        // ------------------------------------------------------------------------------------------- body

        private void LegsAndShoes(Look k)
        {
            // Shoes: rounded toes with a darker sole.
            Fill((x, y) => Min(Ellipse(x, y, 70, 190, 11, 7), Ellipse(x, y, 90, 190, 11, 7)), Shoe);
            Fill((x, y) => Max(Min(Ellipse(x, y, 70, 190, 11, 7), Ellipse(x, y, 90, 190, 11, 7)), 193f - y), Darken(Shoe, 0.7f), 0f);

            Func<float, float, float> legs = (x, y) => Min(Capsule(x, y, 71, HipY, 71, 184, 7), Capsule(x, y, 89, HipY, 89, 184, 7));
            switch (k.PantsStyle)
            {
                case "shorts":
                    Fill(legs, k.Skin);
                    Fill((x, y) => Min(Capsule(x, y, 71, HipY - 2, 71, 162, 9.5f), Capsule(x, y, 89, HipY - 2, 89, 162, 9.5f)), k.Pants);
                    Shade((x, y) => Min(Capsule(x, y, 71, HipY - 2, 71, 162, 9.5f), Capsule(x, y, 89, HipY - 2, 89, 162, 9.5f)), k.Pants, 88f);
                    break;
                case "skirt":
                    Fill(legs, k.Skin);
                    Fill((x, y) => Trapezoid(x, y, 80, HipY - 4, k.TorsoHalf - 1, k.TorsoHalf + 9, 22), k.Pants);
                    Shade((x, y) => Trapezoid(x, y, 80, HipY - 4, k.TorsoHalf - 1, k.TorsoHalf + 9, 22), k.Pants, 88f);
                    break;
                case "sarong":
                    Fill((x, y) => Box(x, y, 80, 166, k.TorsoHalf, 20, 6), k.Pants);
                    Fill((x, y) => Box(x, y, 80, 160, k.TorsoHalf - 1, 2f, 1), Darken(k.Pants, 0.75f), 0f);
                    Fill((x, y) => Box(x, y, 80, 172, k.TorsoHalf - 1, 2f, 1), Darken(k.Pants, 0.75f), 0f);
                    Shade((x, y) => Box(x, y, 80, 166, k.TorsoHalf, 20, 6), k.Pants, 90f);
                    break;
                default: // trousers
                    Fill((x, y) => Min(Capsule(x, y, 71, HipY - 2, 71, 184, 8.5f), Capsule(x, y, 89, HipY - 2, 89, 184, 8.5f)), k.Pants);
                    Shade((x, y) => Capsule(x, y, 89, HipY - 2, 89, 184, 8.5f), k.Pants, 90f);
                    break;
            }
        }

        private void ArmsAndTorso(Look k)
        {
            float h = k.TorsoHalf;
            bool vest = k.ShirtStyle == "vest";
            bool longSleeves = k.ShirtStyle == "kurung" || k.ShirtStyle == "melayu";
            var sleeve = vest ? White : k.Shirt;
            float sleeveEnd = longSleeves ? 136 : 128;

            // Hands, then sleeves over them.
            Fill((x, y) => Circle(x, y, 80 - h - 6, 140, 7f), k.Skin);
            Fill((x, y) => Circle(x, y, 80 + h + 6, 140, 7f), k.Skin);
            Fill((x, y) => Capsule(x, y, 80 - h + 4, 110, 80 - h - 5, sleeveEnd, 8.5f), sleeve);
            Fill((x, y) => Capsule(x, y, 80 + h - 4, 110, 80 + h + 5, sleeveEnd, 8.5f), sleeve);

            float bottom = k.ShirtStyle == "kurung" ? 160f : k.ShirtStyle == "melayu" ? 154f : 150f;
            float cy = (104f + bottom) * 0.5f, hy = (bottom - 104f) * 0.5f;
            Func<float, float, float> torso = (x, y) => Box(x, y, 80, cy, h, hy, 11);
            Fill(torso, vest ? White : k.Shirt);

            if (k.Back)
            {
                if (vest) Fill(torso, k.Shirt);
                if (k.ShirtStyle == "melayu") Fill((x, y) => Box(x, y, 80, 146, h + 1, 8, 4), new Color32(214, 170, 72, 255));
                if (k.ShirtStyle == "apron") Fill((x, y) => Box(x, y, 80, 132, 8, 4, 2), White, 1.4f); // apron bow
                Shade(torso, vest ? k.Shirt : k.Shirt, 80f + h * 0.4f);
                return;
            }

            switch (k.ShirtStyle)
            {
                case "vest":
                    // White shirt with a coloured vest: two front panels and a V-neck with a white collar.
                    Fill((x, y) => Max(torso(x, y), -Triangle(x, y, 68, 100, 92, 100, 80, 126)), k.Shirt);
                    Shade((x, y) => Max(torso(x, y), -Triangle(x, y, 68, 100, 92, 100, 80, 126)), k.Shirt, 80f + h * 0.4f);
                    Fill((x, y) => Min(Triangle(x, y, 68, 103, 80, 106, 72, 114), Triangle(x, y, 92, 103, 80, 106, 88, 114)), White, 1.4f);
                    for (int i = 0; i < 2; i++)
                    {
                        float by = 132 + i * 9;
                        Fill((x, y) => Circle(x, y, 80, by, 1.8f), Darken(k.Shirt, 0.55f), 0f);
                    }
                    break;
                case "collared":
                    Shade(torso, k.Shirt, 80f + h * 0.4f);
                    Fill((x, y) => Min(Triangle(x, y, 68, 103, 80, 106, 72, 115), Triangle(x, y, 92, 103, 80, 106, 88, 115)), White, 1.4f);
                    for (int i = 0; i < 3; i++)
                    {
                        float by = 120 + i * 9;
                        Fill((x, y) => Circle(x, y, 80, by, 1.8f), Darken(k.Shirt, 0.55f), 0f);
                    }
                    break;
                case "apron":
                    Shade(torso, k.Shirt, 80f + h * 0.4f);
                    Fill((x, y) => Box(x, y, 80, 132, h - 7, 18, 5), White, 1.4f);
                    Fill((x, y) => Box(x, y, 80, 136, 8, 5, 2), new Color32(230, 224, 210, 255), 1.1f);
                    Fill((x, y) => Max(Circle(x, y, 80, 104, 8), y - 108f), k.Skin, 1.2f);
                    break;
                case "kurung":
                    Shade(torso, k.Shirt, 80f + h * 0.4f);
                    Fill((x, y) => Max(Circle(x, y, 80, 103, 7), y - 108f), k.Skin, 1.2f);
                    Fill((x, y) => Box(x, y, 80, 158, h - 1, 1.5f, 1), Darken(k.Shirt, 0.8f), 0f);
                    break;
                case "melayu":
                    Shade(torso, k.Shirt, 80f + h * 0.4f);
                    Fill((x, y) => Box(x, y, 80, 105, 7, 4.5f, 2), Darken(k.Shirt, 0.88f), 1.2f); // standing collar
                    Fill((x, y) => Box(x, y, 80, 146, h + 1, 8, 4), new Color32(214, 170, 72, 255), 1.4f); // samping
                    break;
                default: // tee
                    Shade(torso, k.Shirt, 80f + h * 0.4f);
                    Fill((x, y) => Max(Circle(x, y, 80, 103, 8), y - 108f), k.Skin, 1.2f);
                    break;
            }
        }

        // ------------------------------------------------------------------------------------------- face

        private void Face(Look k, bool small)
        {
            float eyeY = small ? 78f : 74f, spread = small ? 16f : 18f;
            var blush = new Color(0.97f, 0.55f, 0.58f, k.Female ? 0.5f : 0.35f);
            Fill((x, y) => Ellipse(x, y, 80 - spread - 7, eyeY + 14, 8, 4.5f), blush, 0f);
            Fill((x, y) => Ellipse(x, y, 80 + spread + 7, eyeY + 14, 8, 4.5f), blush, 0f);

            foreach (float cx in new[] { 80 - spread, 80 + spread })
                Eye(k, cx, eyeY, cx > 80);

            float my = eyeY + 18;
            Fill((x, y) => Min(Capsule(x, y, 76, my, 80, my + 2.6f, 1.5f), Capsule(x, y, 80, my + 2.6f, 84, my, 1.5f)),
                new Color32(130, 58, 48, 255), 0f);
        }

        private void Eye(Look k, float cx, float cy, bool right)
        {
            float side = right ? 1f : -1f;
            switch (k.Eyes)
            {
                case "happy":
                    Fill((x, y) => Min(Capsule(x, y, cx - 8, cy + 3, cx, cy - 5, 2.6f), Capsule(x, y, cx, cy - 5, cx + 8, cy + 3, 2.6f)), EyeDark, 0f);
                    return;
                case "sleepy":
                    Fill((x, y) => Max(Ellipse(x, y, cx, cy + 1, 9, 10), cy - 1 - y), EyeDark, 0f);
                    Fill((x, y) => Max(Ellipse(x, y, cx, cy + 3, 7, 7), cy - y), new Color32(120, 72, 42, 255), 0f);
                    Fill((x, y) => Capsule(x, y, cx - 10, cy - 1, cx + 10, cy - 1, 2.2f), EyeDark, 0f);
                    Fill((x, y) => Circle(x, y, cx - 3, cy + 3, 1.6f), Color.white, 0f);
                    return;
            }

            bool sparkle = k.Eyes == "sparkle";
            Color iris = sparkle ? new Color32(84, 104, 186, 255) : new Color32(112, 66, 38, 255);
            Color glow = sparkle ? new Color32(150, 186, 240, 255) : new Color32(184, 120, 66, 255);
            // Dark rim, coloured iris that lightens towards the bottom, pupil, and two highlights.
            Fill((x, y) => Ellipse(x, y, cx, cy, 9.5f, 12f), EyeDark, 0f);
            Fill((x, y) => Ellipse(x, y, cx, cy + 2, 8f, 9.5f), iris, 0f);
            Fill((x, y) => Ellipse(x, y, cx, cy + 6.5f, 5.5f, 4f), glow, 0f);
            Fill((x, y) => Ellipse(x, y, cx, cy, 4f, 5f), EyeDark, 0f);
            Fill((x, y) => Circle(x, y, cx - 3.2f, cy - 4.5f, sparkle ? 4f : 3.5f), Color.white, 0f);
            Fill((x, y) => Circle(x, y, cx + 3.4f, cy + 6.5f, 1.7f), Color.white, 0f);
            if (sparkle) Fill((x, y) => Circle(x, y, cx + 3.5f, cy - 5.5f, 1.4f), Color.white, 0f);
            // Upper lid line, thicker at the outer corner; a lash flick for female avatars.
            Fill((x, y) => Min(Capsule(x, y, cx - 10, cy - 3, cx - 4, cy - 11, 2.2f),
                Min(Capsule(x, y, cx - 4, cy - 11, cx + 4, cy - 11.5f, 2.2f), Capsule(x, y, cx + 4, cy - 11.5f, cx + side * 0 + 10.5f * (right ? 1 : 1), cy - 4, 2.4f))), EyeDark, 0f);
            if (k.Female)
                Fill((x, y) => Capsule(x, y, cx + side * 9, cy - 8, cx + side * 13, cy - 11, 1.5f), EyeDark, 0f);
        }

        private void Brows(Look k)
        {
            var c = Darken(k.Hair, 0.75f);
            float y0 = 58f;
            switch (k.Brows)
            {
                case "bold":
                    Fill((x, y) => Min(Capsule(x, y, 55, y0, 67, y0 - 1, 2.8f), Capsule(x, y, 93, y0 - 1, 105, y0, 2.8f)), c, 0f);
                    break;
                case "angled":
                    Fill((x, y) => Min(Capsule(x, y, 55, y0 - 3, 67, y0 + 1, 2.1f), Capsule(x, y, 93, y0 + 1, 105, y0 - 3, 2.1f)), c, 0f);
                    break;
                default:
                    Fill((x, y) => Min(Capsule(x, y, 56, y0, 66, y0 - 1.5f, 1.6f), Capsule(x, y, 94, y0 - 1.5f, 104, y0, 1.6f)), c, 0f);
                    break;
            }
        }

        // ------------------------------------------------------------------------------------------- hair

        private void HairBehind(Look k)
        {
            switch (k.HairStyle)
            {
                case "long": Fill((x, y) => Box(x, y, 80, 98, 54, 56, 26), k.Hair); break;
                case "bob": Fill((x, y) => Box(x, y, 80, 76, 56, 32, 22), k.Hair); break;
                case "ponytail":
                    Fill((x, y) => Min(Capsule(x, y, 122, 40, 138, 96, 13), Triangle(x, y, 128, 92, 148, 92, 140, 118)), k.Hair);
                    break;
                case "bun": Fill((x, y) => Circle(x, y, 80, 2, 17), k.Hair); break;
            }
        }

        /// <summary>Crown, fringe of pointed locks and side locks: one shape, so no outlines between the locks.</summary>
        private void HairFront(Look k)
        {
            string s = k.HairStyle;
            float sideBottom = s == "long" ? 126f : s == "bob" ? 104f : s == "short" || s == "spiky" ? 80f : 92f;
            float sideWidth = s == "bob" ? 13f : 9f;
            bool spikes = s == "spiky" && !k.HatOn;

            Func<float, float, float> hair = (x, y) =>
            {
                float d = Max(Ellipse(x, y, 80, 50, 55, 47), y - 50f);
                // Fringe: six pointed locks leaning alternately, tips just above the eyes.
                for (int i = 0; i < 6; i++)
                {
                    float x0 = 36 + i * 15.6f, tip = (i % 2 == 0) ? 62f : 57f, lean = (i % 2 == 0) ? 4f : -3f;
                    d = Min(d, Triangle(x, y, x0, 42, x0 + 20, 42, x0 + 10 + lean, tip));
                }
                // Side locks framing the face.
                d = Min(d, Triangle(x, y, 28, 44, 30 + sideWidth * 2, 44, 33, sideBottom));
                d = Min(d, Triangle(x, y, 132, 44, 130 - sideWidth * 2, 44, 127, sideBottom));
                if (spikes)
                {
                    d = Min(d, Triangle(x, y, 46, 22, 66, 12, 44, -6));
                    d = Min(d, Triangle(x, y, 68, 10, 92, 10, 80, -14));
                    d = Min(d, Triangle(x, y, 94, 12, 114, 22, 116, -6));
                }
                return d;
            };
            Fill(hair, k.Hair);
            // Shading under the crown's right side, and a soft shine on the left.
            Fill((x, y) => Max(hair(x, y), 104f - x), Darken(k.Hair, 0.84f), 0f);
            if (!k.HatOn || k.Hat == "headband" || k.Hat == "cat_hood")
                Fill((x, y) => Capsule(x, y, 52, 30, 72, 20, 3.4f), Lighten(k.Hair, 0.35f), 0f);
        }

        /// <summary>Back view: the hair covers the back of the head, down to the length of the style.</summary>
        private void HairFromBehind(Look k)
        {
            string s = k.HairStyle;
            float bottom = s == "long" ? 150f : s == "bob" ? 104f : s == "short" || s == "spiky" ? 86f : 94f;
            bool spikes = s == "spiky" && !k.HatOn;
            Func<float, float, float> hair = (x, y) =>
            {
                float d = Max(Ellipse(x, y, 80, 54, 55, 50), y - Math.Min(bottom, 96f));
                if (s == "long") d = Min(d, Box(x, y, 80, 104, 52, 46, 24));
                if (s == "bob") d = Min(d, Box(x, y, 80, 84, 56, 20, 16));
                // Pointed ends along the bottom edge.
                for (int i = 0; i < 5; i++)
                {
                    float x0 = 34 + i * 19f;
                    d = Min(d, Triangle(x, y, x0, bottom - 12, x0 + 22, bottom - 12, x0 + 11, bottom + 6));
                }
                if (spikes)
                {
                    d = Min(d, Triangle(x, y, 46, 22, 66, 12, 44, -6));
                    d = Min(d, Triangle(x, y, 68, 10, 92, 10, 80, -14));
                    d = Min(d, Triangle(x, y, 94, 12, 114, 22, 116, -6));
                }
                return d;
            };
            Fill(hair, k.Hair);
            Fill((x, y) => Max(hair(x, y), 106f - x), Darken(k.Hair, 0.84f), 0f);
            Fill((x, y) => Capsule(x, y, 80, 14, 80, 40, 1.3f), Darken(k.Hair, 0.7f), 0f); // crown parting
            if (s == "ponytail")
            {
                Fill((x, y) => Min(Capsule(x, y, 80, 56, 80, 112, 12), Triangle(x, y, 68, 108, 92, 108, 80, 130)), k.Hair);
                Fill((x, y) => Circle(x, y, 80, 56, 6), new Color32(214, 70, 64, 255), 1.2f);
            }
            else if (s == "bun")
                Fill((x, y) => Circle(x, y, 80, 18, 18), k.Hair);
        }

        // ------------------------------------------------------------------------------------------- hats

        private static readonly Color Lavender = new Color32(184, 156, 214, 255);

        private void TudungDrape(Look k) =>
            Fill((x, y) => Trapezoid(x, y, 80, 84, 46, 62, k.Back ? 64 : 48), Lavender);

        private void TudungHead(Look k)
        {
            Fill((x, y) => Ellipse(x, y, 80, 62, 56, 54), Lavender);
            Fill((x, y) => Max(Ellipse(x, y, 80, 62, 56, 54), 102f - x), Darken(Lavender, 0.86f), 0f);
            if (k.Back) return;
            // The face shows through an oval opening framed by the scarf.
            Fill((x, y) => Ellipse(x, y, 80, 80, 40, 34), k.Skin);
            Fill((x, y) => Max(Ellipse(x, y, 80, 74, 46, 42), -Ellipse(x, y, 80, 80, 40, 34)), Darken(Lavender, 0.92f), 0f);
            Fill((x, y) => Max(Max(Ellipse(x, y, 80, 74, 46, 42), -Ellipse(x, y, 80, 80, 40, 34)), y - 50f), Lavender, 0f);
        }

        private void Hat(Look k)
        {
            switch (k.Hat)
            {
                case "chef":
                {
                    Func<float, float, float> puff = (x, y) => Min(Min(Circle(x, y, 54, -2, 22), Circle(x, y, 80, -12, 25)),
                        Min(Circle(x, y, 106, -2, 22), Box(x, y, 80, 8, 46, 14, 10)));
                    Fill(puff, White);
                    Fill((x, y) => Max(puff(x, y), 98f - x), new Color32(226, 224, 218, 255), 0f);
                    Fill((x, y) => Box(x, y, 80, 24, 52, 10, 6), new Color32(240, 238, 232, 255));
                    break;
                }
                case "cap":
                    Fill((x, y) => Max(Ellipse(x, y, 80, 36, 57, 36), y - 36f), k.Shirt);
                    Fill((x, y) => Max(Max(Ellipse(x, y, 80, 36, 57, 36), y - 36f), 100f - x), Darken(k.Shirt, 0.85f), 0f);
                    if (!k.Back) Fill((x, y) => Box(x, y, 104, 36, 38, 6, 6), Darken(k.Shirt, 0.75f));
                    Fill((x, y) => Circle(x, y, 80, 2, 4), Darken(k.Shirt, 0.75f), 1.2f);
                    break;
                case "beanie":
                    Fill((x, y) => Max(Ellipse(x, y, 80, 38, 58, 40), y - 38f), k.Shirt);
                    Fill((x, y) => Box(x, y, 80, 36, 58, 9, 7), Darken(k.Shirt, 0.82f));
                    Fill((x, y) => Circle(x, y, 80, -6, 10), White);
                    break;
                case "headband":
                {
                    var red = new Color32(214, 70, 64, 255);
                    Fill((x, y) => Max(Ellipse(x, y, 80, 54, 56, 50), Math.Abs(y - 34f) - 6f), red);
                    float knot = k.Back ? 30f : 130f, tail = k.Back ? -11f : 11f;
                    Fill((x, y) => Min(Capsule(x, y, knot, 36, knot + tail, 28, 4.5f), Capsule(x, y, knot, 36, knot + tail * 1.2f, 46, 4.5f)), red);
                    break;
                }
                case "songkok":
                {
                    var black = new Color32(40, 34, 40, 255);
                    Fill((x, y) => Box(x, y, 80, 16, 45, 18, 5), black);
                    Fill((x, y) => Box(x, y, 66, 6, 20, 2f, 1), new Color32(92, 84, 96, 255), 0f); // sheen
                    break;
                }
                case "straw_hat":
                {
                    var straw = new Color32(230, 186, 98, 255);
                    Fill((x, y) => Ellipse(x, y, 80, 32, 70, 16), straw);
                    Fill((x, y) => Max(Ellipse(x, y, 80, 32, 70, 16), 108f - x), Darken(straw, 0.88f), 0f);
                    Fill((x, y) => Box(x, y, 80, 14, 38, 20, 16), straw);
                    Fill((x, y) => Box(x, y, 80, 26, 38, 5, 2), new Color32(150, 74, 48, 255), 1.4f);
                    break;
                }
                case "cat_hood":
                    CatHood(k);
                    break;
            }
        }

        /// <summary>White cat hood with ears and a calico patch, the face showing through (as on the character sheet).</summary>
        private void CatHood(Look k)
        {
            var fur = new Color32(248, 244, 236, 255);
            var patch = new Color32(196, 132, 80, 255);
            var pink = new Color32(242, 182, 190, 255);
            Func<float, float, float> outer = (x, y) => Min(Ellipse(x, y, 80, 56, 60, 56),
                Min(Triangle(x, y, 26, 34, 52, 12, 26, -8), Triangle(x, y, 134, 34, 108, 12, 134, -8)));
            Func<float, float, float> hood = k.Back ? outer : (x, y) => Max(outer(x, y), -Ellipse(x, y, 80, 80, 44, 44));
            Fill(hood, fur);
            Fill((x, y) => Max(hood(x, y), Ellipse(x, y, 40, 30, 22, 20)), patch, 0f);           // calico patch over the left ear
            Fill((x, y) => Max(hood(x, y), 112f - x), Darken(fur, 0.9f), 0f);                     // shade
            if (!k.Back)
            {
                Fill((x, y) => Triangle(x, y, 32, 28, 46, 16, 32, 2), pink, 0f);
                Fill((x, y) => Triangle(x, y, 128, 28, 114, 16, 128, 2), pink, 0f);
            }
        }

        // ------------------------------------------------------------------------------------------- rasteriser

        /// <summary>Darker band on the right side of a shape (light comes from the upper left).</summary>
        private void Shade(Func<float, float, float> shape, Color c, float fromX) =>
            Fill((x, y) => Max(shape(x, y), fromX - x), Darken(c, 0.86f), 0f);

        /// <summary>
        /// Fills a shape (distance &lt; 0) with an anti-aliased outline in a dark ink tone. Blocks of 8x8 pixels far
        /// outside the shape are skipped, which keeps a full avatar to a few milliseconds.
        /// </summary>
        private void Fill(Func<float, float, float> sdf, Color fill, float outline = OutlineWidth)
        {
            var edge = Color.Lerp(Darken(fill, 0.45f), Ink, 0.6f);
            edge.a = fill.a;
            const int block = 8;
            const float reach = block * 0.75f + 3f;
            for (int by = 0; by < Height; by += block)
            for (int bx = 0; bx < Width; bx += block)
            {
                float cy = Height - (by + block * 0.5f) - Top;
                if (sdf(bx + block * 0.5f, cy) - outline > reach * 1.6f) continue;
                int yEnd = Math.Min(by + block, Height), xEnd = Math.Min(bx + block, Width);
                for (int py = by; py < yEnd; py++)
                {
                    float figureY = Height - py - 0.5f - Top; // texture rows are bottom-up, figure y is down
                    int row = py * Width;
                    for (int px = bx; px < xEnd; px++)
                    {
                        float d = sdf(px + 0.5f, figureY);
                        float cover = Mathf.Clamp01(0.5f - (d - outline));
                        if (cover <= 0f) continue;
                        float inner = outline > 0f ? Mathf.Clamp01(0.5f - d) : 1f;
                        var src = Color.Lerp(edge, fill, inner);
                        float a = cover * src.a;
                        var dst = _px[row + px];
                        float outA = a + dst.a * (1f - a);
                        if (outA <= 0f) continue;
                        _px[row + px] = new Color(
                            (src.r * a + dst.r * dst.a * (1f - a)) / outA,
                            (src.g * a + dst.g * dst.a * (1f - a)) / outA,
                            (src.b * a + dst.b * dst.a * (1f - a)) / outA,
                            outA);
                    }
                }
            }
        }

        private static Color Darken(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);
        private static Color Lighten(Color c, float k) => Color.Lerp(c, Color.white, k);

        private static float Min(float a, float b) => a < b ? a : b;
        private static float Max(float a, float b) => a > b ? a : b;

        private static float SmoothMin(float a, float b, float k)
        {
            float h = Math.Max(k - Math.Abs(a - b), 0f) / k;
            return Math.Min(a, b) - h * h * k * 0.25f;
        }

        private static float Circle(float x, float y, float cx, float cy, float r) =>
            Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r;

        private static float Ellipse(float x, float y, float cx, float cy, float rx, float ry)
        {
            float nx = (x - cx) / rx, ny = (y - cy) / ry;
            return (Mathf.Sqrt(nx * nx + ny * ny) - 1f) * Mathf.Min(rx, ry);
        }

        private static float Box(float x, float y, float cx, float cy, float hx, float hy, float r)
        {
            float qx = Mathf.Abs(x - cx) - hx + r, qy = Mathf.Abs(y - cy) - hy + r;
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        private static float Capsule(float x, float y, float ax, float ay, float bx, float by, float r)
        {
            float pax = x - ax, pay = y - ay, bax = bx - ax, bay = by - ay;
            float h = Mathf.Clamp01((pax * bax + pay * bay) / (bax * bax + bay * bay));
            float dx = pax - bax * h, dy = pay - bay * h;
            return Mathf.Sqrt(dx * dx + dy * dy) - r;
        }

        /// <summary>Exact distance to triangle ABC (negative inside).</summary>
        private static float Triangle(float x, float y, float ax, float ay, float bx, float by, float cx, float cy)
        {
            float e0x = bx - ax, e0y = by - ay, e1x = cx - bx, e1y = cy - by, e2x = ax - cx, e2y = ay - cy;
            float v0x = x - ax, v0y = y - ay, v1x = x - bx, v1y = y - by, v2x = x - cx, v2y = y - cy;
            float h0 = Mathf.Clamp01((v0x * e0x + v0y * e0y) / (e0x * e0x + e0y * e0y));
            float h1 = Mathf.Clamp01((v1x * e1x + v1y * e1y) / (e1x * e1x + e1y * e1y));
            float h2 = Mathf.Clamp01((v2x * e2x + v2y * e2y) / (e2x * e2x + e2y * e2y));
            float p0x = v0x - e0x * h0, p0y = v0y - e0y * h0;
            float p1x = v1x - e1x * h1, p1y = v1y - e1y * h1;
            float p2x = v2x - e2x * h2, p2y = v2y - e2y * h2;
            float s = Math.Sign(e0x * e2y - e0y * e2x);
            float d0 = p0x * p0x + p0y * p0y, s0 = s * (v0x * e0y - v0y * e0x);
            float d1 = p1x * p1x + p1y * p1y, s1 = s * (v1x * e1y - v1y * e1x);
            float d2 = p2x * p2x + p2y * p2y, s2 = s * (v2x * e2y - v2y * e2x);
            float d = Math.Min(d0, Math.Min(d1, d2));
            float sg = Math.Min(s0, Math.Min(s1, s2));
            return -Mathf.Sqrt(d) * Math.Sign(sg);
        }

        /// <summary>Skirt or scarf shape: top half-width <paramref name="top"/>, bottom half-width <paramref name="bottom"/>.</summary>
        private static float Trapezoid(float x, float y, float cx, float topY, float top, float bottom, float height)
        {
            float t = Mathf.Clamp01((y - topY) / height);
            float half = Mathf.Lerp(top, bottom, t);
            return Mathf.Max(Mathf.Abs(x - cx) - half, Mathf.Max(topY - y, y - (topY + height)));
        }
    }
}

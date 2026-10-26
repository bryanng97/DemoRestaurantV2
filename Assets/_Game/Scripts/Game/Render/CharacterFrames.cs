using System.Collections.Generic;
using UnityEngine;

namespace DemoRestaurant.Game.Render
{
    /// <summary>
    /// A character's baked animation frames from the skin: sprites named <c>{key}_{anim}_{f|b}_00..</c>, where
    /// <c>f</c> faces SouthEast (towards the viewer) and <c>b</c> NorthEast (away); the west facings are mirrors.
    /// Every frame shares one canvas whose <c>anchor</c> hint is the point between the feet, so frames stay aligned.
    /// Optional per-animation hints: <c>hand</c> on carry/hold, where the plate goes.
    /// </summary>
    public sealed class CharacterFrames
    {
        public const string Walk = "walk", Carry = "carry", Hold = "hold", Idle = "idle", Cook = "cook",
            Sit = "sit", Eat = "eat", Clean = "clean", Fix = "fix";
        private static readonly string[] Names = { Walk, Carry, Hold, Idle, Cook, Sit, Eat, Clean, Fix };

        private readonly Dictionary<string, Sprite[]> _frames = new Dictionary<string, Sprite[]>();
        private readonly Dictionary<string, Vector2> _hands = new Dictionary<string, Vector2>();

        /// <summary>Ground point in normalized canvas coordinates (Y up).</summary>
        public Vector2 Anchor { get; private set; }
        /// <summary>Visible height of the first idle frame in sprite units, so every frame scales the same.</summary>
        public float StandingHeight { get; private set; }

        /// <summary>Null when the skin has no frames for this key (at least idle and walk are needed).</summary>
        public static CharacterFrames Load(ArtLibrary art, string key)
        {
            var set = new CharacterFrames();
            foreach (var anim in Names)
            foreach (var view in new[] { "f", "b" })
            {
                string name = key + "_" + anim + "_" + view;
                var list = new List<Sprite>();
                for (int i = 0; i < 100; i++)
                {
                    var s = art.GetOptional(name + "_" + i.ToString("D2"));
                    if (s == null) break;
                    list.Add(s);
                }
                if (list.Count == 0) continue;
                set._frames[name.Substring(key.Length + 1)] = list.ToArray();
                var hints = art.HintsFor(name);
                if (hints != null && hints.hand != Vector2.zero) set._hands[anim + "_" + view] = hints.hand;
                if (anim == Idle && view == "f")
                {
                    set.Anchor = hints != null && hints.anchor != Vector2.zero ? hints.anchor : new Vector2(0.5f, 0f);
                    set.StandingHeight = InkHeight(list[0]);
                }
            }
            return set.Has(Idle) && set.Has(Walk) && set.StandingHeight > 0f ? set : null;
        }

        public bool Has(string anim) => _frames.ContainsKey(anim + "_f");

        /// <summary>Frames for an animation and view (back falls back to front).</summary>
        public Sprite[] Get(string anim, bool back)
        {
            if (back && _frames.TryGetValue(anim + "_b", out var b)) return b;
            return _frames.TryGetValue(anim + "_f", out var f) ? f : null;
        }

        /// <summary>Normalized carrying-hand position for this animation and view, if the skin gave one.</summary>
        public bool TryHand(string anim, bool back, out Vector2 hand) =>
            _hands.TryGetValue(anim + (back ? "_b" : "_f"), out hand) || _hands.TryGetValue(anim + "_f", out hand);

        /// <summary>Offset (sprite units) from the sprite's pivot to a normalized canvas point.</summary>
        public static Vector2 FromPivot(Sprite sprite, Vector2 normalized)
        {
            var r = sprite.rect;
            return new Vector2(normalized.x * r.width - sprite.pivot.x, normalized.y * r.height - sprite.pivot.y) / sprite.pixelsPerUnit;
        }

        private static float InkHeight(Sprite sprite)
        {
            var vertices = sprite.vertices; // tight mesh: excludes the transparent canvas
            if (vertices.Length == 0) return sprite.bounds.size.y;
            float min = vertices[0].y, max = min;
            foreach (var v in vertices) { min = Mathf.Min(min, v.y); max = Mathf.Max(max, v.y); }
            return max - min;
        }
    }
}

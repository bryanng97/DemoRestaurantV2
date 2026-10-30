using System;
using System.Collections.Generic;

namespace DemoRestaurant.Logic.Layout
{
    public enum ExteriorKind { Wall, Roof, Door, Window, Sign, WallDecor, Ground }

    [Serializable]
    public sealed class ExteriorItemDef
    {
        public string id, nameKey, sprite;
        public ExteriorKind kind;
        public int price, unlockLevel = 1, starterOwned;
        public float width, height;
        /// <summary>Roof registration origin in its full sprite (X from left, Y from top), attached to the wall top.</summary>
        public float roofAnchorX = .5f, roofAnchorY = 1f;
        /// <summary>Legacy prototype crop metadata, retained for older catalogs. Complete roof art rests on the wall top.</summary>
        public float wallTop;
    }

    [Serializable]
    public sealed class ExteriorPiece
    {
        public int instance;
        public string itemId;
        // Bottom-centre in a 1000 x 680 design space. Independent of screen resolution and art pixels.
        public float x, y;
        public bool flipped;
        public ExteriorPiece Copy() => (ExteriorPiece)MemberwiseClone();
    }

    /// <summary>From this restaurant level the shop front is this big (design units), as the reference game's building bases.</summary>
    [Serializable]
    public sealed class ExteriorStep { public int level; public float width, height; }

    /// <summary>The facade rectangle in the 1000 x 680 design space; its bottom is the pavement line.</summary>
    public readonly struct ExteriorBox
    {
        public readonly float Left, Right, Top, Bottom;
        public ExteriorBox(float left, float right, float top, float bottom) { Left = left; Right = right; Top = top; Bottom = bottom; }
        public float Width => Right - Left;
        public float Height => Bottom - Top;
    }

    [Serializable]
    public sealed class ExteriorOwned { public string itemId; public int count; }

    [Serializable]
    public sealed class ExteriorSave
    {
        public List<ExteriorPiece> pieces = new List<ExteriorPiece>();
        public List<ExteriorOwned> owned = new List<ExteriorOwned>();
        public ExteriorSave Copy()
        {
            var result = new ExteriorSave();
            if (pieces != null) foreach (var p in pieces) if (p != null) result.pieces.Add(p.Copy());
            if (owned != null) foreach (var o in owned) if (o != null)
                result.owned.Add(new ExteriorOwned { itemId = o.itemId, count = o.count });
            return result;
        }
    }

    public sealed class ExteriorCatalog
    {
        private readonly Dictionary<string, ExteriorItemDef> _items = new Dictionary<string, ExteriorItemDef>();
        public IReadOnlyCollection<ExteriorItemDef> Items => _items.Values;
        private readonly ExteriorSave _starter;
        private readonly List<ExteriorStep> _steps = new List<ExteriorStep>();

        /// <summary>The level-1 shop front; it only ever grows, so pieces placed earlier stay valid.</summary>
        public const float BaseWidth = 800f, BaseHeight = 318f, Ground = 620f, MaxWidth = 1200f, MaxHeight = 600f;

        public ExteriorCatalog(IEnumerable<ExteriorItemDef> items, IEnumerable<ExteriorPiece> starter, IEnumerable<ExteriorStep> steps = null)
        {
            if (steps != null) foreach (var s in steps) if (s != null) _steps.Add(s);
            foreach (var d in items)
            {
                if (string.IsNullOrEmpty(d.id) || d.price < 0 || d.width <= 0 || d.height <= 0 ||
                    _items.ContainsKey(d.id)) throw new ArgumentException("Invalid exterior catalog item.");
                _items.Add(d.id, d);
            }
            _starter = new ExteriorSave();
            foreach (var d in Items) if (d.starterOwned > 0)
                _starter.owned.Add(new ExteriorOwned { itemId = d.id, count = d.starterOwned });
            foreach (var p in starter) _starter.pieces.Add(p.Copy());
            if (!Validate(_starter.pieces)) throw new ArgumentException("Invalid starter exterior.");
        }
        /// <summary>The shop front at a restaurant level: the largest step reached, centred, standing on the pavement.</summary>
        public ExteriorBox BoxFor(int level)
        {
            float w = BaseWidth, h = BaseHeight;
            foreach (var s in _steps)
                if (s.level <= level) { w = Math.Max(w, s.width); h = Math.Max(h, s.height); }
            w = Math.Min(w, MaxWidth); h = Math.Min(h, MaxHeight);
            return new ExteriorBox(500f - w / 2f, 500f + w / 2f, Ground - h, Ground);
        }

        /// <summary>How many building steps a level has reached (0 = the starter front): picks the building base art.</summary>
        public int StepFor(int level)
        {
            int n = 0;
            foreach (var s in _steps) if (s.level <= level) n++;
            return n;
        }

        public ExteriorItemDef Get(string id) => id != null && _items.TryGetValue(id, out var d) ? d : null;
        public ExteriorSave Starter() => _starter.Copy();
        public static bool Fixed(ExteriorKind k) => k == ExteriorKind.Wall || k == ExteriorKind.Roof;
        public static bool Single(ExteriorKind k) => Fixed(k) || k == ExteriorKind.Door || k == ExteriorKind.Sign;

        public bool CanPlace(IReadOnlyList<ExteriorPiece> pieces, ExteriorPiece candidate, int level = 1)
        {
            var box = BoxFor(level);
            var d = Get(candidate.itemId);
            if (d == null || float.IsNaN(candidate.x) || float.IsInfinity(candidate.x) ||
                float.IsNaN(candidate.y) || float.IsInfinity(candidate.y)) return false;
            if (Fixed(d.kind)) return true;
            float left = candidate.x - d.width / 2, right = candidate.x + d.width / 2;
            float top = candidate.y - d.height;
            if (d.kind == ExteriorKind.Ground)
            {
                if (left < Math.Min(0f, box.Left - 250f) || right > Math.Max(1000f, box.Right + 250f) || candidate.y != 650) return false;
            }
            else if (left < box.Left + 10 || right > box.Right - 10 || top < box.Top + 10 || candidate.y > Ground ||
                (d.kind == ExteriorKind.Door && candidate.y != Ground)) return false;
            foreach (var p in pieces)
            {
                if (p.instance == candidate.instance) continue;
                var other = Get(p.itemId);
                if (other == null) return false;
                if (Single(d.kind) && d.kind == other.kind) return false;
                if (Fixed(other.kind)) continue;
                bool ground = d.kind == ExteriorKind.Ground, otherGround = other.kind == ExteriorKind.Ground;
                if (ground != otherGround)
                {
                    // Keep the pavement in front of the entrance clear.
                    var door = d.kind == ExteriorKind.Door ? candidate : other.kind == ExteriorKind.Door ? p : null;
                    if (door == null) continue;
                    var prop = ground ? candidate : p;
                    var propDef = ground ? d : other;
                    if (Math.Abs(door.x - prop.x) < (Get(door.itemId).width + propDef.width) / 2 + 20) return false;
                    continue;
                }
                if (left < p.x + other.width / 2 && right > p.x - other.width / 2 &&
                    top < p.y && candidate.y > p.y - other.height) return false;
            }
            return true;
        }

        /// <param name="kept">The front as it was: a piece left exactly where it stood there is accepted even if it no
        /// longer fits (fronts saved at a larger size can stand outside today's wall). Moved and new pieces must fit.</param>
        public bool Validate(IReadOnlyList<ExteriorPiece> pieces, int level = 1, IReadOnlyList<ExteriorPiece> kept = null)
        {
            if (pieces == null || pieces.Count > 64) return false;
            var ids = new HashSet<int>();
            var required = new HashSet<ExteriorKind>();
            foreach (var p in pieces)
            {
                if (p == null || !ids.Add(p.instance) || p.instance <= 0 || (!CanPlace(pieces, p, level) && !Unmoved(p, kept))) return false;
                var kind = Get(p.itemId).kind;
                if (Single(kind) && !required.Add(kind)) return false;
            }
            return required.Contains(ExteriorKind.Wall) && required.Contains(ExteriorKind.Roof) && required.Contains(ExteriorKind.Door);
        }

        private static bool Unmoved(ExteriorPiece p, IReadOnlyList<ExteriorPiece> kept)
        {
            if (kept == null) return false;
            foreach (var k in kept)
                if (k != null && k.instance == p.instance && k.itemId == p.itemId && k.x == p.x && k.y == p.y && k.flipped == p.flipped)
                    return true;
            return false;
        }

        public bool TryQuote(ExteriorSave current, IReadOnlyList<ExteriorPiece> pieces, int level, out long cost,
            out List<ExteriorOwned> ownership)
        {
            cost = 0;
            ownership = new List<ExteriorOwned>();
            if (current == null || !Validate(pieces, level, current.pieces)) return false;
            var counts = new Dictionary<string, int>();
            if (current.owned != null) foreach (var o in current.owned)
                if (o != null && Get(o.itemId) != null) counts[o.itemId] = Math.Max(0, o.count);
            var placed = new Dictionary<string, int>();
            foreach (var p in pieces) placed[p.itemId] = placed.TryGetValue(p.itemId, out int n) ? n + 1 : 1;
            foreach (var pair in placed)
            {
                counts.TryGetValue(pair.Key, out int have);
                if (pair.Value <= have) continue;
                var d = Get(pair.Key);
                if (d.unlockLevel > level) return false;
                cost += (long)(pair.Value - have) * d.price;
                counts[pair.Key] = pair.Value;
            }
            foreach (var pair in counts) ownership.Add(new ExteriorOwned { itemId = pair.Key, count = pair.Value });
            return true;
        }

        public ExteriorSave Restore(ExteriorSave saved)
        {
            if (saved == null) return Starter(); // v1-v4 saves receive the starter facade.
            var result = saved.Copy();
            // Saved fronts were checked at their level when bought; the largest front is the bound here.
            if (!Validate(result.pieces, int.MaxValue)) return Starter();
            // A malformed save cannot turn unpaid copies into owned stock.
            return TryQuote(result, result.pieces, int.MaxValue, out long cost, out _) && cost == 0 ? result : Starter();
        }
    }
}

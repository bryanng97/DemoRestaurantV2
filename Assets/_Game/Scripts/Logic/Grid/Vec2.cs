using System;

namespace DemoRestaurant.Logic.Grid
{
    /// <summary>
    /// Continuous position in grid space (1 unit = 1 cell). Cell (x, y) spans [x, x+1) x [y, y+1),
    /// so the centre of a cell is (x + 0.5, y + 0.5). Engine-independent replacement for Vector2.
    /// </summary>
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        public readonly float X;
        public readonly float Y;

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static Vec2 CellCentre(GridPos p) => new Vec2(p.X + 0.5f, p.Y + 0.5f);

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Y * s);

        public float Length => (float)Math.Sqrt(X * X + Y * Y);

        public GridPos Cell => new GridPos((int)Math.Floor(X), (int)Math.Floor(Y));

        /// <summary>Moves toward target by at most maxDistance. Returns true when the target is reached.</summary>
        public static bool MoveTowards(Vec2 current, Vec2 target, float maxDistance, out Vec2 result)
        {
            Vec2 delta = target - current;
            float dist = delta.Length;
            if (dist <= maxDistance || dist < 1e-6f)
            {
                result = target;
                return true;
            }
            result = current + delta * (maxDistance / dist);
            return false;
        }

        public bool Equals(Vec2 other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object obj) => obj is Vec2 other && Equals(other);
        public override int GetHashCode() => (X.GetHashCode() * 397) ^ Y.GetHashCode();
        public override string ToString() => $"({X:0.##},{Y:0.##})";
    }
}

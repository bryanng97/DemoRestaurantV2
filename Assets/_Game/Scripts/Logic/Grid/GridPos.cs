using System;

namespace DemoRestaurant.Logic.Grid
{
    /// <summary>Integer cell coordinate on the restaurant floor grid.</summary>
    public readonly struct GridPos : IEquatable<GridPos>
    {
        public readonly int X;
        public readonly int Y;

        public GridPos(int x, int y)
        {
            X = x;
            Y = y;
        }

        public static GridPos operator +(GridPos a, GridPos b) => new GridPos(a.X + b.X, a.Y + b.Y);
        public static GridPos operator -(GridPos a, GridPos b) => new GridPos(a.X - b.X, a.Y - b.Y);
        public static bool operator ==(GridPos a, GridPos b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(GridPos a, GridPos b) => !(a == b);

        public int ManhattanDistance(GridPos other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);

        public bool Equals(GridPos other) => this == other;
        public override bool Equals(object obj) => obj is GridPos other && this == other;
        public override int GetHashCode() => (X * 397) ^ Y;
        public override string ToString() => $"({X},{Y})";

        public static readonly GridPos Up = new GridPos(0, -1);
        public static readonly GridPos Down = new GridPos(0, 1);
        public static readonly GridPos Left = new GridPos(-1, 0);
        public static readonly GridPos Right = new GridPos(1, 0);
        public static readonly GridPos[] Neighbours4 = { Up, Right, Down, Left };
    }
}

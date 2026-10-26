using System;

namespace DemoRestaurant.Logic.Grid
{
    /// <summary>Walkability map of the single restaurant floor.</summary>
    public sealed class FloorGrid
    {
        private int[] _blockers;

        public int Width { get; private set; }
        public int Height { get; private set; }

        internal void Resize(int width, int height)
        {
            var cells = new int[checked(width * height)];
            for (int y = 0; y < Math.Min(height, Height); y++)
                Array.Copy(_blockers, y * Width, cells, y * width, Math.Min(width, Width));
            Width = width; Height = height; _blockers = cells;
        }

        /// <summary>Unblocks every cell (the layout re-blocks what it needs).</summary>
        internal void Clear() => Array.Clear(_blockers, 0, _blockers.Length);

        public FloorGrid(int width, int height)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "Grid dimensions must be positive.");
            Width = width;
            Height = height;
            _blockers = new int[width * height];
        }

        public bool InBounds(GridPos p) => p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;

        public bool IsWalkable(GridPos p) => InBounds(p) && _blockers[Index(p)] == 0;

        /// <summary>Blocks every cell in the rectangle. Counted, so overlapping blockers unblock correctly.</summary>
        public void Block(GridPos origin, int sizeX, int sizeY) => Adjust(origin, sizeX, sizeY, +1);

        public void Unblock(GridPos origin, int sizeX, int sizeY) => Adjust(origin, sizeX, sizeY, -1);

        public bool IsAreaFree(GridPos origin, int sizeX, int sizeY)
        {
            for (int y = origin.Y; y < origin.Y + sizeY; y++)
            for (int x = origin.X; x < origin.X + sizeX; x++)
            {
                if (!IsWalkable(new GridPos(x, y)))
                    return false;
            }
            return true;
        }

        private void Adjust(GridPos origin, int sizeX, int sizeY, int delta)
        {
            for (int y = origin.Y; y < origin.Y + sizeY; y++)
            for (int x = origin.X; x < origin.X + sizeX; x++)
            {
                var p = new GridPos(x, y);
                if (!InBounds(p))
                    throw new ArgumentOutOfRangeException(nameof(origin), $"Cell {p} is outside the grid.");
                int i = Index(p);
                _blockers[i] = Math.Max(0, _blockers[i] + delta);
            }
        }

        private int Index(GridPos p) => p.Y * Width + p.X;
    }
}

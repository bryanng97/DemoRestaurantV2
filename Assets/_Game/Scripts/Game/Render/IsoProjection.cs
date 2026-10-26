using DemoRestaurant.Logic.Grid;
using UnityEngine;

namespace DemoRestaurant.Game.Render
{
    /// <summary>
    /// Grid space to world space for a 2:1 isometric view.
    /// Grid +X runs down-right on screen, +Y runs down-left; cell (0,0) is the top (back) corner.
    /// One cell is 1 world unit wide and 0.5 tall. Art is authored at <see cref="PixelsPerUnit"/>.
    /// </summary>
    public static class IsoProjection
    {
        public const float HalfTileWidth = 0.5f;
        public const float HalfTileHeight = 0.25f;
        /// <summary>World units of screen height per cell unit of object height.</summary>
        public const float HeightScale = 0.6f;
        public const float PixelsPerUnit = 128f;

        public static Vector2 GridToWorld(float x, float y, float z = 0f) =>
            new Vector2((x - y) * HalfTileWidth, -(x + y) * HalfTileHeight + z * HeightScale);

        public static Vector2 GridToWorld(Vec2 p, float z = 0f) => GridToWorld(p.X, p.Y, z);

        public static Vector2 GridToWorld(GridPos p) => GridToWorld(p.X, p.Y);

        /// <summary>Inverse of <see cref="GridToWorld(float,float,float)"/> on the floor plane (z = 0).</summary>
        public static Vec2 WorldToGrid(Vector2 world)
        {
            float a = world.x / HalfTileWidth;   // x - y
            float b = -world.y / HalfTileHeight; // x + y
            return new Vec2((a + b) * 0.5f, (b - a) * 0.5f);
        }
    }
}

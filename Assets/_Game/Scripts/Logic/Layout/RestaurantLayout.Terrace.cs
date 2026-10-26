using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Grid;

namespace DemoRestaurant.Logic.Layout
{
    /// <summary>
    /// The room and the terrace bought in front of it (as in the reference game's outside area). The grid covers both:
    /// room cells are X &lt; <see cref="RoomWidth"/>, Y &lt; <see cref="RoomHeight"/>; terrace cells continue from the room's
    /// front-left edge (X &lt; <see cref="TerraceWidth"/>, Y from <see cref="RoomHeight"/> for <see cref="TerraceDepth"/>
    /// rows), along the street side. Any other cell of the grid is not floor and stays blocked. The room's front edges
    /// have no walls, so the terrace is walked onto straight from the room. Without a terrace the grid is just the room.
    /// </summary>
    public sealed partial class RestaurantLayout
    {
        public int RoomWidth { get; private set; }
        public int RoomHeight { get; private set; }
        public int TerraceWidth { get; private set; }
        public int TerraceDepth { get; private set; }
        public bool HasTerrace => TerraceWidth > 0 && TerraceDepth > 0;

        public bool IsRoomCell(GridPos p) => p.X >= 0 && p.Y >= 0 && p.X < RoomWidth && p.Y < RoomHeight;

        public bool IsTerraceCell(GridPos p) =>
            HasTerrace && p.X >= 0 && p.X < TerraceWidth && p.Y >= RoomHeight && p.Y < RoomHeight + TerraceDepth;

        /// <summary>A cell people can stand on and furniture can go on: the room or the terrace.</summary>
        public bool IsFloorCell(GridPos p) => IsRoomCell(p) || IsTerraceCell(p);

        /// <summary>Sets the terrace bought in front of the room (0 x 0 = none). Its furniture keeps its place.</summary>
        internal void SetTerrace(int width, int depth) => Reshape(RoomWidth, RoomHeight, width, depth);

        /// <summary>
        /// Changes the room and terrace sizes. Furniture and floor tiles on the terrace move with the room's front edge;
        /// cells that are neither room nor terrace are blocked. Callers make sure everything still fits.
        /// </summary>
        internal void Reshape(int roomWidth, int roomHeight, int terraceWidth, int terraceDepth)
        {
            if (roomWidth <= 0 || roomHeight <= 0) throw new ArgumentOutOfRangeException(nameof(roomWidth));
            if (terraceWidth <= 0 || terraceDepth <= 0) terraceWidth = terraceDepth = 0;
            if (roomWidth == RoomWidth && roomHeight == RoomHeight && terraceWidth == TerraceWidth && terraceDepth == TerraceDepth
                && Grid.Width == Math.Max(roomWidth, terraceWidth) && Grid.Height == roomHeight + terraceDepth) return;

            int oldRoomHeight = RoomHeight, oldWidth = Grid.Width, oldHeight = Grid.Height;
            int dy = roomHeight - oldRoomHeight; // the terrace follows the room's front edge
            GridPos Moved(GridPos p) => p.Y >= oldRoomHeight && p.X >= 0 ? new GridPos(p.X, p.Y + dy) : p;

            // Floor tiles, moved with their cells.
            int width = Math.Max(roomWidth, terraceWidth), height = roomHeight + terraceDepth;
            var tiles = new string[checked(width * height)];
            for (int y = 0; y < oldHeight; y++)
            for (int x = 0; x < oldWidth; x++)
            {
                var to = Moved(new GridPos(x, y));
                if (to.X < width && to.Y >= 0 && to.Y < height) tiles[to.Y * width + to.X] = _floorTiles[y * oldWidth + x];
            }

            // Indoor furniture is lifted off, the grid rebuilt, and everything put back (terrace pieces shifted).
            var indoor = new List<PlacedFurniture>();
            foreach (var f in _furniture) if (!IsOutdoorItem(f)) indoor.Add(f);
            RoomWidth = roomWidth; RoomHeight = roomHeight; TerraceWidth = terraceWidth; TerraceDepth = terraceDepth;
            Grid.Resize(width, height);
            Grid.Clear();
            _floorTiles = tiles;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (!IsFloorCell(new GridPos(x, y))) Grid.Block(new GridPos(x, y), 1, 1);
            foreach (var f in indoor)
            {
                if (dy != 0 && f.Origin.Y >= oldRoomHeight)
                {
                    FurnitureRemoved?.Invoke(f);
                    f.Origin = new GridPos(f.Origin.X, f.Origin.Y + dy);
                    FurniturePlaced?.Invoke(f);
                }
                if (f.StackedOn == null) Grid.Block(f.Origin, f.SizeX, f.SizeY);
            }
            BoundsChanged?.Invoke();
        }
    }
}

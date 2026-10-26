using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;

namespace DemoRestaurant.Logic.Layout
{
    /// <summary>The stove body is solid; its front work tile is walkable but reserved when editing furniture.</summary>
    public static class FurnitureWorkArea
    {
        public static GridPos FrontTile(FurnitureDef def, GridPos origin, Facing facing)
        {
            bool rotated = facing == Facing.SouthEast || facing == Facing.NorthWest;
            int sx = rotated ? def.SizeY : def.SizeX, sy = rotated ? def.SizeX : def.SizeY;
            switch (facing)
            {
                case Facing.SouthWest: return new GridPos(origin.X, origin.Y + sy);
                case Facing.SouthEast: return new GridPos(origin.X + sx, origin.Y);
                case Facing.NorthEast: return new GridPos(origin.X, origin.Y - 1);
                default: return new GridPos(origin.X - 1, origin.Y);
            }
        }

        /// <summary>Checks new placements/moves, without invalidating the geometry of existing legacy saves.</summary>
        public static bool CanEdit(RestaurantLayout layout, FurnitureDef def, GridPos origin, Facing facing, PlacedFurniture moving = null)
        {
            if (def == null || def.Kind == FurnitureKind.WallDecor) return true;
            bool rotated = facing == Facing.SouthEast || facing == Facing.NorthWest;
            int sx = rotated ? def.SizeY : def.SizeX, sy = rotated ? def.SizeX : def.SizeY;
            var front = FrontTile(def, origin, facing);
            if (def.Kind == FurnitureKind.Stove)
            {
                if (!layout.IsFloorCell(front) || front == layout.Entrance ||
                    (!layout.Grid.IsWalkable(front) && (moving == null || !moving.Covers(front)))) return false;
                foreach (var waiting in layout.WaitingSpots) if (waiting == front) return false;
            }
            foreach (var other in layout.Furniture)
            {
                if (other == moving || other.Def.Kind != FurnitureKind.Stove) continue;
                var reserved = FrontTile(other.Def, other.Origin, other.Facing);
                if (reserved.X >= origin.X && reserved.X < origin.X + sx && reserved.Y >= origin.Y && reserved.Y < origin.Y + sy) return false;
                if (def.Kind == FurnitureKind.Stove && front == reserved) return false;
            }
            return true;
        }
    }
}

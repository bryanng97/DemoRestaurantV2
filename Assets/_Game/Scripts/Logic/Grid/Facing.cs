namespace DemoRestaurant.Logic.Grid
{
    /// <summary>
    /// The four isometric facings. Art only needs two of them (e.g. SouthEast and NorthEast);
    /// the other two are horizontal mirrors.
    /// </summary>
    public enum Facing
    {
        /// <summary>+Y, towards the viewer on the left.</summary>
        SouthWest,
        /// <summary>+X, towards the viewer on the right.</summary>
        SouthEast,
        /// <summary>-Y, away from the viewer on the right.</summary>
        NorthEast,
        /// <summary>-X, away from the viewer on the left.</summary>
        NorthWest,
    }

    public static class FacingUtil
    {
        public static Facing FromDelta(float dx, float dy)
        {
            if (System.Math.Abs(dx) >= System.Math.Abs(dy))
                return dx >= 0 ? Facing.SouthEast : Facing.NorthWest;
            return dy >= 0 ? Facing.SouthWest : Facing.NorthEast;
        }

        public static GridPos ToDelta(Facing facing)
        {
            switch (facing)
            {
                case Facing.SouthWest: return GridPos.Down;
                case Facing.SouthEast: return GridPos.Right;
                case Facing.NorthEast: return GridPos.Up;
                default: return GridPos.Left;
            }
        }

        /// <summary>True when the sprite for this facing is the horizontal mirror of a drawn one.</summary>
        public static bool IsMirrored(Facing facing) => facing == Facing.SouthWest || facing == Facing.NorthWest;

        /// <summary>True when the character shows its back to the camera.</summary>
        public static bool IsBackView(Facing facing) => facing == Facing.NorthEast || facing == Facing.NorthWest;
    }
}

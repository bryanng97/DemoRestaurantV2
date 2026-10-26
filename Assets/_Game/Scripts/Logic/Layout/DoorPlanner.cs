using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Grid;

namespace DemoRestaurant.Logic.Layout
{
    /// <summary>Which back wall a door is on. Wall segments are <c>cellsPerSegment</c> cells wide.</summary>
    public enum DoorWall
    {
        /// <summary>Back-left wall, along grid X = 0.</summary>
        Left,
        /// <summary>Back-right wall, along grid Y = 0.</summary>
        Right,
    }

    /// <summary>
    /// Moving the door in the build editor: the door snaps to the middle of a wall segment (so the wall art lines
    /// up), and the waiting area is the nearest free cells inside the door, closest first.
    /// </summary>
    public static class DoorPlanner
    {
        public const int MinWaitingSpots = 3;

        /// <summary>
        /// The door cell on the back-left wall (the street side) for the segment level with <paramref name="tapped"/>.
        /// The back-right wall faces the farm, so the door never goes there.
        /// </summary>
        public static GridPos DoorNear(GridPos tapped, int cellsPerSegment, int width, int height)
        {
            int n = Math.Max(1, cellsPerSegment);
            int seg = Clamp(tapped.Y, height) / n;
            return new GridPos(0, Math.Min(seg * n + n / 2, height - 1));
        }

        /// <summary>Which wall and segment a door cell is drawn on.</summary>
        public static (DoorWall wall, int segment) SegmentOf(GridPos door, int cellsPerSegment)
        {
            int n = Math.Max(1, cellsPerSegment);
            return door.X == 0 ? (DoorWall.Left, door.Y / n) : (DoorWall.Right, door.X / n);
        }

        /// <summary>
        /// Up to <paramref name="count"/> free cells nearest the door (walking distance, breadth first), front of the
        /// line first. Fewer when the door is boxed in.
        /// </summary>
        public static List<GridPos> WaitingSpotsFor(RestaurantLayout layout, GridPos door, int count)
        {
            var spots = new List<GridPos>();
            if (!layout.Grid.IsWalkable(door)) return spots;
            var seen = new HashSet<GridPos> { door };
            var frontier = new Queue<GridPos>();
            frontier.Enqueue(door);
            while (frontier.Count > 0 && spots.Count < count)
            {
                var cell = frontier.Dequeue();
                foreach (var d in GridPos.Neighbours4)
                {
                    var next = cell + d;
                    if (!seen.Add(next) || !layout.Grid.IsWalkable(next)) continue;
                    spots.Add(next);
                    frontier.Enqueue(next);
                    if (spots.Count >= count) break;
                }
            }
            return spots;
        }

        private static int Clamp(int v, int size) => Math.Max(0, Math.Min(v, size - 1));
    }
}

using System;
using System.Collections.Generic;

namespace DemoRestaurant.Logic.Layout
{
    /// <summary>One row of the room-size table: from this restaurant level the room is at least this big.</summary>
    [Serializable]
    public sealed class RoomGrowthStep
    {
        public int level, width, height;
    }

    /// <summary>
    /// The indoor room grows by itself with the restaurant level, for free (as in the reference game): there is no land
    /// to buy indoors. A room never shrinks, so rooms from older saves that bought land stay as big as they are.
    /// The authority (server) applies the same rule when a layout is saved.
    /// </summary>
    public sealed class RoomGrowth
    {
        private readonly List<RoomGrowthStep> _steps;

        public IReadOnlyList<RoomGrowthStep> Steps => _steps;

        public RoomGrowth(IEnumerable<RoomGrowthStep> steps)
        {
            _steps = new List<RoomGrowthStep>();
            foreach (var s in steps ?? throw new ArgumentNullException(nameof(steps)))
            {
                if (s == null || s.level < 1 || s.width < 2 || s.height < 2 || s.width > 128 || s.height > 128)
                    throw new ArgumentException("Room growth steps need a level of at least 1 and a size from 2 to 128.");
                _steps.Add(s);
            }
            if (_steps.Count == 0) throw new ArgumentException("Room growth needs at least one step.");
            _steps.Sort((a, b) => a.level.CompareTo(b.level));
            for (int i = 1; i < _steps.Count; i++)
                if (_steps[i].width < _steps[i - 1].width || _steps[i].height < _steps[i - 1].height)
                    throw new ArgumentException("Room growth steps must never shrink the room.");
        }

        /// <summary>The room size for a restaurant level (the first step's size below its level).</summary>
        public (int width, int height) SizeFor(int level)
        {
            var size = _steps[0];
            foreach (var s in _steps)
                if (s.level <= level) size = s;
            return (size.width, size.height);
        }

        /// <summary>The room after growing to <paramref name="level"/>: each side the larger of now and the table.</summary>
        public (int width, int height) Grow(int width, int height, int level)
        {
            var (w, h) = SizeFor(level);
            return (Math.Max(width, w), Math.Max(height, h));
        }

        /// <summary>A saved room may be as big as the level allows, or as big as it already was.</summary>
        public bool Allows(int ownedWidth, int ownedHeight, int width, int height, int level)
        {
            var (w, h) = Grow(ownedWidth, ownedHeight, level);
            return width > 0 && height > 0 && width <= w && height <= h;
        }
    }
}

using System.Collections.Generic;
using DemoRestaurant.Logic.Grid;

namespace DemoRestaurant.Logic.Simulation
{
    /// <summary>Anything that walks around the floor: customers and staff. Mutated only by <see cref="RestaurantSim"/>.</summary>
    public abstract class Agent
    {
        internal readonly List<GridPos> Path = new List<GridPos>();
        internal int PathIndex;
        internal float Timer;

        public int Id { get; }
        public Vec2 Position { get; internal set; }
        /// <summary>Height above the floor in cell units (non-zero while seated).</summary>
        public float Z { get; internal set; }
        public Facing Facing { get; internal set; }

        /// <summary>Cells per second.</summary>
        public abstract float WalkSpeed { get; }

        /// <summary>True while following a path.</summary>
        public bool IsWalking => PathIndex < Path.Count;

        /// <summary>Seconds left in the current timed activity (ordering, cooking, eating, idling).</summary>
        public float TimeRemaining => Timer;

        protected Agent(int id, Vec2 position)
        {
            Id = id;
            Position = position;
            Facing = Facing.NorthWest;
        }
    }
}

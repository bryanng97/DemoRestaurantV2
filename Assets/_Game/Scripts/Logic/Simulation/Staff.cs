using System;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;

namespace DemoRestaurant.Logic.Simulation
{
    public enum StaffState
    {
        /// <summary>At (or walking back to) their home spot: free for work, or resting.</summary>
        Idle,
        // Chef
        WalkingToStove,
        Cooking,
        // Waiter fetching a drink
        WalkingToDrinks,
        Pouring,
        // Waiter
        WalkingToPickup,
        WalkingToCustomer,
        // Cleaner (or a waiter with nothing to serve)
        WalkingToClean,
        Cleaning,
        // Cleaner fixing a broken toilet
        WalkingToFix,
        Fixing,
        WalkingToTrash,
        PickingTrash,
    }

    /// <summary>
    /// A chef, waiter or cleaner, or the player's own avatar (<see cref="IsOwner"/>).
    /// Read by the renderer, mutated only by <see cref="RestaurantSim"/>.
    /// </summary>
    public sealed class Staff : Agent
    {
        private readonly StaffEnergyRules _energyRules;
        private float _energy = StaffEnergyRules.Max;

        /// <summary>Tier and role. Only the owner's changes (when the player gives their avatar another job).</summary>
        public StaffTypeDef Type { get; internal set; }
        public StaffRole Role => Type.Role;
        public bool IsOwner => Type.IsOwner;
        /// <summary>The backend's id (UUID) for this staff member; null until hired online or synced.</summary>
        public string ServerId { get; set; }
        public StaffState State { get; internal set; }
        /// <summary>Where they wait when there is no work, and where they rest. Renovations can relocate it.</summary>
        public GridPos Home { get; internal set; }
        /// <summary>The ticket being cooked, carried or delivered, if any.</summary>
        public OrderTicket Ticket { get; internal set; }
        /// <summary>The stove a chef is using.</summary>
        public PlacedFurniture Stove { get; internal set; }
        /// <summary>A chef's own stove: they stand in front of it between orders (as in the reference game).</summary>
        public PlacedFurniture Station { get; internal set; }
        /// <summary>Seconds before an idle chef with no reachable stove looks for one again.</summary>
        internal float StationRetry;
        internal float CookingBaseSeconds;
        /// <summary>A waiter has been placed at their post beside a stove (they appear there once, then walk).</summary>
        internal bool Posted;
        /// <summary>The seat whose dirty dishes are being cleared, if any.</summary>
        public PlacedFurniture CleaningSeat { get; internal set; }
        /// <summary>The broken toilet being fixed, if any.</summary>
        public PlacedFurniture FixTarget { get; internal set; }
        public TrashPiece TrashTarget { get; internal set; }

        /// <summary>0-100. Drains while on duty; recovers while resting.</summary>
        public float Energy
        {
            get => _energy;
            internal set => _energy = Math.Max(0f, Math.Min(StaffEnergyRules.Max, value));
        }

        /// <summary>Off duty, recovering energy. Takes no jobs.</summary>
        public bool IsResting { get; internal set; }
        /// <summary>Sent to rest while busy: finishes the current job first.</summary>
        public bool RestRequested { get; internal set; }
        public bool IsOnDuty => !IsResting;
        /// <summary>In the middle of a job (cooking, carrying, serving or cleaning).</summary>
        public bool IsBusy => Ticket != null || CleaningSeat != null || FixTarget != null || TrashTarget != null;

        /// <summary>Current speed factor from energy (1 = full speed, 0.05 when exhausted).</summary>
        public float SpeedMultiplier => _energyRules.SpeedMultiplier(_energy);
        /// <summary>Cooking or cleaning speed right now (base work speed times the energy factor).</summary>
        public float EffectiveCookSpeed => Type.CookSpeed * SpeedMultiplier;

        /// <summary>True when the renderer should draw a plate in their hands.</summary>
        public bool IsCarryingPlate => State == StaffState.WalkingToCustomer;

        /// <summary>0..1 progress of the dish being cooked or the table being cleared (Cooking / Cleaning).</summary>
        public float CookProgress { get; internal set; }

        public override float WalkSpeed => Type.WalkSpeed * SpeedMultiplier;

        internal Staff(int id, StaffTypeDef type, GridPos home, StaffEnergyRules energyRules) : base(id, Vec2.CellCentre(home))
        {
            Type = type;
            Home = home;
            _energyRules = energyRules ?? new StaffEnergyRules();
            Facing = Facing.SouthWest;
        }
    }
}

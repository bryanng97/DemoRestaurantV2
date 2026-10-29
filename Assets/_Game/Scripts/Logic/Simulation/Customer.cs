using System;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;

namespace DemoRestaurant.Logic.Simulation
{
    public enum CustomerState
    {
        /// <summary>Walking along the street to the door (only with <see cref="SimSettings.StreetWalk"/>).</summary>
        ArrivingOnStreet,
        /// <summary>Every seat is taken: walking to their spot in the waiting area.</summary>
        WalkingToQueue,
        /// <summary>Standing in the waiting area until a seat frees up.</summary>
        WaitingInQueue,
        WalkingToSeat,
        /// <summary>Sat on a chair with no table beside it: complaining, about to leave unhappy.</summary>
        NoTable,
        Ordering,
        /// <summary>Order is with the kitchen; waiting for a waiter to bring it. Patience runs down.</summary>
        WaitingForFood,
        Eating,
        /// <summary>After paying: walking to a toilet, sitting on it, then washing hands at a sink.</summary>
        WalkingToToilet,
        OnToilet,
        WalkingToSink,
        UsingSink,
        WalkingToExit,
        /// <summary>Out of the door, walking away along the street.</summary>
        LeavingOnStreet,
        Left,
        WalkingToEntertainment,
        PlayingEntertainment,
    }

    /// <summary>Simulation state of one customer. Read by the renderer, mutated only by <see cref="RestaurantSim"/>.</summary>
    public sealed class Customer : Agent
    {
        internal float WaitedForSeat;
        internal float WaitedForFood;
        internal int QueueSpot = -1;

        public CustomerTypeDef Type { get; }
        public CustomerState State { get; internal set; }
        public PlacedFurniture Seat { get; internal set; }
        /// <summary>Reserved dining table, shared by any chairs beside it; only one guest may use it.</summary>
        public PlacedFurniture Table { get; internal set; }
        public DishDef Order { get; internal set; }
        public PlacedFurniture Entertainment { get; internal set; }
        /// <summary>The kitchen ticket for the current order, if one has been placed.</summary>
        public OrderTicket Ticket { get; internal set; }
        /// <summary>Left because the food took too long.</summary>
        public bool IsAngry { get; internal set; }
        /// <summary>0..1 of food patience left when the dish arrived (1 until then). Fast service raises the rating more.</summary>
        public float ServedPatienceLeft { get; internal set; } = 1f;

        public override float WalkSpeed => Type.WalkSpeed;

        /// <summary>The sink being walked to or used.</summary>
        internal Layout.PlacedFurniture Sink;
        /// <summary>The bathroom fixture currently in use (not merely being approached).</summary>
        public Layout.PlacedFurniture BathroomFixture => State == CustomerState.OnToilet ? Seat : State == CustomerState.UsingSink ? Sink : null;

        public bool IsMoving => State == CustomerState.WalkingToEntertainment || State == CustomerState.WalkingToSeat
                                || State == CustomerState.WalkingToToilet
                                || State == CustomerState.WalkingToSink
                                || State == CustomerState.WalkingToExit
                                || State == CustomerState.WalkingToQueue
                                || IsOnStreet;

        /// <summary>Outside, walking along the street to or from the door.</summary>
        public bool IsOnStreet => State == CustomerState.ArrivingOnStreet || State == CustomerState.LeavingOnStreet;

        public bool IsSeated => Seat != null && !IsMoving && State != CustomerState.Left;
        public bool IsQueuing => State == CustomerState.WalkingToQueue || State == CustomerState.WaitingInQueue;
        /// <summary>Place in the waiting-area line (0 = next in), or -1 when not queuing.</summary>
        public int QueuePosition => QueueSpot;

        /// <summary>1 = full patience, 0 = about to leave. For the waiting-area or food-wait patience bar.</summary>
        public float PatienceLeft
        {
            get
            {
                if (State == CustomerState.WaitingForFood)
                    return 1f - Math.Min(1f, WaitedForFood / FoodPatienceSeconds);
                if (IsQueuing && Type.PatienceSeconds > 0)
                    return 1f - Math.Min(1f, WaitedForSeat / Type.PatienceSeconds);
                return 1f;
            }
        }

        /// <summary>Seconds this guest waits for food: the type's time scaled by the published rules when it arrived.</summary>
        public float FoodPatienceSeconds { get; internal set; }

        internal Customer(int id, CustomerTypeDef type, Vec2 position) : base(id, position)
        {
            Type = type;
            FoodPatienceSeconds = type.FoodPatienceSeconds;
        }
    }
}

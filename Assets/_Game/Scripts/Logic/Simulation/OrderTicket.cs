using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;

namespace DemoRestaurant.Logic.Simulation
{
    public enum TicketState
    {
        /// <summary>Waiting for a chef and a free stove.</summary>
        Queued,
        /// <summary>A chef is walking to a stove or cooking.</summary>
        Cooking,
        /// <summary>Just cooked (passes straight to Ready).</summary>
        Plating,
        /// <summary>Waiting on its stove for a waiter.</summary>
        Ready,
        /// <summary>A waiter is fetching or carrying it to the customer.</summary>
        Delivering,
        Served,
        /// <summary>The customer left before being served.</summary>
        Cancelled,
    }

    /// <summary>One customer's order moving through the kitchen.</summary>
    public sealed class OrderTicket
    {
        public int Id { get; }
        public Customer Customer { get; }
        public DishDef Dish { get; }
        public TicketState State { get; internal set; }
        /// <summary>The cell of the stove the plate is waiting on (null once collected).</summary>
        public GridPos? CounterSpot { get; internal set; }
        /// <summary>The stove holding the finished plate until a waiter collects it.</summary>
        public PlacedFurniture Stove { get; internal set; }
        public Staff Chef { get; internal set; }
        public Staff Waiter { get; internal set; }

        /// <summary>True while the plate is waiting on its stove and should be drawn there.</summary>
        public bool IsOnCounter => CounterSpot.HasValue && (State == TicketState.Ready
                                   || (State == TicketState.Delivering && Waiter != null && Waiter.State == StaffState.WalkingToPickup));

        internal OrderTicket(int id, Customer customer, DishDef dish)
        {
            Id = id;
            Customer = customer;
            Dish = dish;
        }
    }
}

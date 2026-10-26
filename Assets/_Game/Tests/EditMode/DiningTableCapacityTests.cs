using System.Linq;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Menu;
using DemoRestaurant.Logic.Simulation;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    public sealed class DiningTableCapacityTests
    {
        private static readonly FurnitureDef Table = new FurnitureDef("table","t",FurnitureKind.Table,1,1,.75f,0,1);
        private static readonly FurnitureDef Chair = new FurnitureDef("chair","c",FurnitureKind.Chair,1,1,.8f,.35f,1);
        private static readonly FurnitureDef Stove = new FurnitureDef("stove","s",FurnitureKind.Stove,1,1,1,0,1);
        private static readonly DishDef Meal = new DishDef("meal","m",10,.2f,.2f);
        private static readonly CustomerTypeDef Guest = new CustomerTypeDef("guest","g",10,999,1,999);
        private static RestaurantSim Create()
        {
            var layout = new RestaurantLayout(10,10,new GridPos(0,9));
            layout.TryPlace(Table,new GridPos(4,5),Facing.SouthWest,out _);
            layout.TryPlace(Chair,new GridPos(4,4),Facing.SouthWest,out _);
            layout.TryPlace(Chair,new GridPos(4,6),Facing.NorthEast,out _);
            layout.TryPlace(Stove,new GridPos(1,1),Facing.SouthWest,out _);
            layout.SetWaitingArea(new[]{new GridPos(0,8),new GridPos(0,7),new GridPos(0,6)});
            return new RestaurantSim(layout,new RestaurantMenu(new[]{Meal},1),7,new SimSettings { CleanSeconds=4 });
        }
        [Test] public void OtherChairsCannotReserveTheSameTableEvenWhileTheFirstGuestIsWalking()
        {
            var sim=Create(); var first=sim.SpawnCustomer(Guest); var second=sim.SpawnCustomer(Guest);
            Assert.IsNotNull(first.Seat); Assert.IsNotNull(first.Table);
            Assert.IsNull(second.Seat); Assert.IsTrue(second.IsQueuing);
            Assert.AreEqual(1,SeatFacing.DiningCapacity(sim.Layout,false));
            sim.Despawn(first); sim.Tick(.01f);
            Assert.IsNotNull(second.Seat); Assert.IsNotNull(second.Table);
        }
        [Test] public void DirtyDishesBlockEveryChairUntilTheSharedTableIsCleared()
        {
            var sim=Create();
            sim.Hire(new StaffTypeDef("chef","c",StaffRole.Chef,10,10),new GridPos(1,2));
            sim.Hire(new StaffTypeDef("waiter","w",StaffRole.Waiter,10),new GridPos(3,7));
            var first=sim.SpawnCustomer(Guest);
            for(int i=0;i<5000 && sim.DirtySeatCount==0;i++) sim.Tick(.01f);
            Assert.AreEqual(1,sim.DirtySeatCount);
            var dirtySeat=sim.DirtySeats.Single();
            Assert.AreSame(Meal,sim.DirtyDishFor(dirtySeat),"Keep the finished meal's container after its guest leaves.");
            var next=sim.SpawnCustomer(Guest);
            Assert.IsNull(next.Seat,"The other clean chair must not bypass dishes on its shared table.");
            for(int i=0;i<5000 && next.Seat==null;i++) sim.Tick(.01f);
            Assert.AreEqual(0,sim.DirtySeatCount); Assert.IsNotNull(next.Seat);
            Assert.IsNull(sim.DirtyDishFor(dirtySeat),"Cleaning must discard the old meal's container.");
        }
        [Test] public void DifferentTablesRemainIndependentAndLoneChairsOnlyCountWhenAllowed()
        {
            var sim=Create();
            sim.Layout.TryPlace(Table,new GridPos(7,5),Facing.SouthWest,out _);
            sim.Layout.TryPlace(Chair,new GridPos(7,6),Facing.NorthEast,out _);
            Assert.AreEqual(2,SeatFacing.DiningCapacity(sim.Layout,false));
            var first=sim.SpawnCustomer(Guest); var second=sim.SpawnCustomer(Guest);
            Assert.IsNotNull(first.Seat); Assert.IsNotNull(second.Seat);
            Assert.IsNotNull(first.Table); Assert.IsNotNull(second.Table);
            Assert.AreNotEqual(first.Table,second.Table);
            sim.Layout.TryPlace(Chair,new GridPos(8,8),Facing.SouthWest,out _);
            Assert.AreEqual(2,SeatFacing.DiningCapacity(sim.Layout,false));
            Assert.AreEqual(3,SeatFacing.DiningCapacity(sim.Layout,true));
        }
    }
}

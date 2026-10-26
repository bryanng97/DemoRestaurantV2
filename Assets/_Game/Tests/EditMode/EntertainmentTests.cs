using System;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Economy;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Menu;
using DemoRestaurant.Logic.Save;
using DemoRestaurant.Logic.Session;
using DemoRestaurant.Logic.Simulation;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    public sealed class EntertainmentTests
    {
        private static readonly FurnitureDef Machine = new FurnitureDef("arcade", "k", FurnitureKind.Entertainment, 1, 1, 1.6f, 0, 8000, breakCount: 2);
        private static readonly FurnitureDef Block = new FurnitureDef("block", "k", FurnitureKind.Decor, 1, 1, 1, 0, 1);
        private static readonly FurnitureDef Chair = new FurnitureDef("chair", "k", FurnitureKind.Chair, 1, 1, 1, .4f, 1);
        private static readonly FurnitureDef Stove = new FurnitureDef("stove", "k", FurnitureKind.Stove, 1, 1, 1, 0, 1);
        private static readonly CustomerTypeDef Guest = new CustomerTypeDef("guest", "g", 4, 100, 1);
        private static readonly DishDef Dish = new DishDef("dish", "d", 10, 1, 1);
        private static (RestaurantLayout room, PlacedFurniture machine, RestaurantSim sim) Room()
        {
            var room = new RestaurantLayout(8, 8, new GridPos(0, 7));
            Assert.IsTrue(room.TryPlace(Machine, new GridPos(3, 2), Facing.SouthWest, out var machine));
            return (room, machine, new RestaurantSim(room, new RestaurantMenu(new[] { Dish }, 1), 5,
                new SimSettings { EntertainmentSeconds = 2 }));
        }
        private static void Run(RestaurantSim sim, Func<bool> done, float max = 30)
        { for (float t = 0; t < max && !done(); t += .05f) sim.Tick(.05f); }

        [Test]
        public void NoSeat_ReservesOneMachine_PlaysPaysOnceAndLeaves()
        {
            var (_, machine, sim) = Room(); int completed = 0, unhappy = 0;
            sim.EntertainmentCompleted += (_, f) => { Assert.AreSame(machine, f); completed++; };
            sim.CustomerTurnedAway += _ => unhappy++;
            var first = sim.SpawnCustomer(Guest); var second = sim.SpawnCustomer(Guest);
            Assert.AreEqual(CustomerState.WalkingToEntertainment, first.State);
            Assert.AreNotEqual(CustomerState.WalkingToEntertainment, second.State);
            Assert.AreEqual(1, unhappy);
            Run(sim, () => first.State == CustomerState.PlayingEntertainment);
            Assert.IsFalse(first.IsSeated); Assert.IsFalse(first.IsMoving);
            Assert.IsFalse(machine.Covers(first.Position.Cell));
            sim.Tick(.5f); Assert.AreEqual(.25f, sim.EntertainmentProgress(first), .001f);
            Run(sim, () => first.State == CustomerState.Left);
            Assert.AreEqual(1, completed); Assert.AreEqual(1, sim.UsesOf(machine));
            Assert.IsFalse(sim.IsEntertainmentBusy(machine)); Assert.IsNull(first.Entertainment);
            sim.Tick(10); Assert.AreEqual(1, completed);
        }

        [Test]
        public void AvailableDiningSeat_TakesPriorityOverEntertainment()
        {
            var (room, _, sim) = Room();
            room.TryPlace(Stove, new GridPos(6, 0), Facing.SouthWest, out _);
            room.TryPlace(Chair, new GridPos(5, 5), Facing.SouthWest, out _);
            Assert.AreEqual(CustomerState.WalkingToSeat, sim.SpawnCustomer(Guest).State);
        }

        [Test]
        public void BlockedMachineFront_CannotBeUsedFromBehindOrThroughFurniture()
        {
            var (room, machine, sim) = Room();
            room.TryPlace(Block, new GridPos(3, 3), Facing.SouthWest, out _);
            Assert.AreEqual(CustomerState.Left, sim.SpawnCustomer(Guest).State);
            Assert.IsFalse(sim.IsEntertainmentBusy(machine));
        }

        [Test]
        public void BrokenMachine_RejectsGuestsUntilRepaired_AndWearSurvivesLayoutRestore()
        {
            var (room, machine, sim) = Room(); sim.SetUses(machine, 1);
            var guest = sim.SpawnCustomer(Guest); Run(sim, () => guest.State == CustomerState.Left);
            Assert.IsTrue(sim.IsBroken(machine));
            var pack = new ContentPack("test", new[] { Dish }, new[] { Machine }, new[] { Guest }, Array.Empty<StaffTypeDef>());
            var snapshot = LayoutSnapshot.Capture(room);
            sim.ApplyLayout(snapshot, pack);
            var restored = room.FindAt(new GridPos(3, 2));
            Assert.IsTrue(sim.IsBroken(restored));
            Assert.AreEqual(CustomerState.Left, sim.SpawnCustomer(Guest).State);
            Assert.IsTrue(sim.Fix(restored)); Assert.IsFalse(sim.IsBroken(restored));
            Assert.AreEqual(CustomerState.WalkingToEntertainment, sim.SpawnCustomer(Guest).State);
        }

        [Test]
        public void Despawn_ReleasesMachineWithoutPayment()
        {
            var (_, machine, sim) = Room(); int completed = 0;
            sim.EntertainmentCompleted += (_, __) => completed++;
            var guest = sim.SpawnCustomer(Guest); sim.Despawn(guest);
            Assert.IsFalse(sim.IsEntertainmentBusy(machine)); Assert.AreEqual(0, sim.UsesOf(machine));
            Assert.AreEqual(CustomerState.WalkingToEntertainment, sim.SpawnCustomer(Guest).State);
            Assert.AreEqual(0, completed);
        }

        [Test]
        public void LocalAuthority_UsesOwnPayout_RejectsUnknownAndDuplicateReceipts()
        {
            var rules = new GameRules { Entertainment = new EntertainmentRules { coinsPerUse = 3 } };
            var pack = new ContentPack("test", new[] { Dish }, new[] { Machine, Block }, new[] { Guest }, Array.Empty<StaffTypeDef>(), rules: rules);
            var server = new LocalEconomyServer(pack); int accepted = 0;
            server.SaleSettled += r => { if (r.Accepted) accepted++; };
            server.SubmitEntertainment(1, Machine.Id); server.SubmitEntertainment(1, Machine.Id);
            server.SubmitEntertainment(2, "missing"); server.SubmitEntertainment(3, Block.Id);
            Assert.AreEqual(3, server.Balance); Assert.AreEqual(1, accepted);
        }

        [Test]
        public void Session_PaysOnceAndSavesLedgerAndMachineWear_WithoutInventingMealXp()
        {
            var (room, machine, _) = Room();
            var rules = TestContent.Rules(); rules.Entertainment = new EntertainmentRules { playSeconds = 1, coinsPerUse = 2 };
            var pack = new ContentPack("test", TestContent.Dishes, new[] { Machine }, new[] { Guest }, Array.Empty<StaffTypeDef>(),
                TestContent.AllIngredients, rules).WithEconomy();
            var session = new RestaurantSession(pack, room, 2, startingCoins: 100) { ArrivalsEnabled = false };
            var guest = session.Sim.SpawnCustomer(Guest);
            for (float t = 0; t < 60 && guest.State != CustomerState.Left; t += .05f) session.Tick(.05f);
            Assert.AreEqual(102, session.Wallet.ConfirmedCoins);
            Assert.AreEqual(0, session.TotalCustomersServed); Assert.AreEqual(0, session.Progress.Xp);
            var save = session.CaptureSave();
            Assert.That(save.ledger.Exists(e => e.kind == LedgerKind.EntertainmentIncome.ToString() && e.amount == 2));
            var copy = new RestaurantLayout(8, 8, new GridPos(0, 7));
            Assert.IsTrue(LayoutSnapshot.Capture(room).TryRestore(copy, pack));
            var restored = new RestaurantSession(pack, copy, 2) { ArrivalsEnabled = false };
            restored.ApplySave(save);
            Assert.AreEqual(102, restored.Wallet.ConfirmedCoins);
            Assert.AreEqual(1, restored.Sim.UsesOf(copy.FindAt(machine.Origin)));
            restored.Tick(10); Assert.AreEqual(102, restored.Wallet.ConfirmedCoins);
        }

        [TestCase(Facing.SouthWest)]
        [TestCase(Facing.SouthEast)]
        [TestCase(Facing.NorthEast)]
        [TestCase(Facing.NorthWest)]
        public void LegacyMachineAxes_PreserveBlockedCellsNeighborsWearAndMoney_AndUpgradeOnce(Facing facing)
        {
            var oldMachine = new FurnitureDef("pinball", "k", FurnitureKind.Entertainment, 2, 1, 1.6f, 0, 12000, breakCount: 10);
            var newMachine = new FurnitureDef("pinball", "k", FurnitureKind.Entertainment, 1, 2, 1.6f, 0, 12000, breakCount: 10);
            var room = new RestaurantLayout(8,8,new GridPos(0,7));
            room.TryPlace(oldMachine,new GridPos(3,3),facing,out var original);
            room.TryPlace(Block,new GridPos(2,3),Facing.SouthWest,out _);
            var sim = new RestaurantSim(room,new RestaurantMenu(new[]{Dish},1),1);sim.SetUses(original,7);
            var snapshot = LayoutSnapshot.Capture(room); snapshot.entertainmentAxesVersion=0;
            snapshot.hasBalance=true;snapshot.confirmedCoins=81234;
            snapshot.SetOwned(new System.Collections.Generic.Dictionary<string,int>{{oldMachine.Id,2},{Block.Id,1}});
            var pack = new ContentPack("test",new[]{Dish},new[]{newMachine,Block},new[]{Guest},Array.Empty<StaffTypeDef>());
            Assert.IsTrue(LayoutMigration.UpgradeEntertainmentAxes(snapshot,pack));
            Assert.IsFalse(LayoutMigration.UpgradeEntertainmentAxes(snapshot,pack));
            var restored = new RestaurantLayout(8,8,new GridPos(0,7));Assert.IsTrue(snapshot.TryRestore(restored,pack));
            var piece = restored.FindAt(original.Origin);
            Assert.AreEqual(original.SizeX,piece.SizeX);Assert.AreEqual(original.SizeY,piece.SizeY);
            Assert.AreEqual((Facing)(((int)facing+1)%4),piece.Facing);
            Assert.AreEqual(7,piece.UsageCount);Assert.AreEqual(81234,snapshot.confirmedCoins);
            Assert.AreEqual(2,snapshot.OwnedCounts()[newMachine.Id]);
            for(int y=0;y<8;y++)for(int x=0;x<8;x++)
                Assert.AreEqual(room.Grid.IsWalkable(new GridPos(x,y)),restored.Grid.IsWalkable(new GridPos(x,y)));
            var current = LayoutSnapshot.Capture(restored);
            Assert.IsFalse(LayoutMigration.UpgradeEntertainmentAxes(current,pack));
        }
    }
}

using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Economy;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Menu;
using DemoRestaurant.Logic.Session;
using DemoRestaurant.Logic.Simulation;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    public sealed class ExpansionTests
    {
        private static ContentPack Pack()
        {
            var rules = new GameRules { Expansion = new ExpansionRules {
                baseWidth = 8, baseHeight = 8, maxWidth = 14, maxHeight = 14, step = 3, firstPrice = 100, priceIncrement = 100 } };
            return new ContentPack("test", new[] { new DishDef("dish", "d", 10, 1, 1, isStarter: true) },
                new[] { new FurnitureDef("chair", "c", FurnitureKind.Chair, 1, 1, 1, .4f, 1),
                    new FurnitureDef("stove", "s", FurnitureKind.Stove, 1, 1, 1, 0, 1),
                    new FurnitureDef("counter", "p", FurnitureKind.ServingCounter, 1, 1, 1, 0, 1) },
                new[] { new CustomerTypeDef("customer", "c", 5, 120, 1, 120) },
                new[] { new StaffTypeDef("chef", "c", StaffRole.Chef, 5), new StaffTypeDef("waiter", "w", StaffRole.Waiter, 5) }, rules: rules).WithEconomy();
        }

        private static RestaurantLayout Room(ContentPack pack)
        {
            var room = new RestaurantLayout(8, 8, new GridPos(0, 6));
            room.SetWaitingArea(new[] { new GridPos(1, 6) });
            room.TryPlace(pack.GetFurniture("stove"), new GridPos(2, 0), Facing.SouthWest, out _);
            room.TryPlace(pack.GetFurniture("counter"), new GridPos(2, 3), Facing.SouthWest, out _);
            room.TryPlace(pack.GetFurniture("chair"), new GridPos(4, 5), Facing.NorthEast, out _);
            room.SetFloor(new GridPos(3, 2), "stone");
            return room;
        }

        [Test]
        public void DraftExpansion_PreservesContentAndSupportsUndoRedo()
        {
            var pack = Pack(); var source = Room(pack); var editor = new LayoutEditor(source, pack);
            Assert.That(editor.Expand(true), Is.True); Assert.That(editor.Expand(false), Is.True);
            Assert.That(editor.Draft.Grid.Width, Is.EqualTo(11)); Assert.That(editor.Draft.Grid.Height, Is.EqualTo(11));
            Assert.That(editor.Draft.FloorAt(new GridPos(3, 2)), Is.EqualTo("stone"));
            Assert.That(editor.Draft.Furniture.Count, Is.EqualTo(3)); Assert.That(source.Grid.Width, Is.EqualTo(8));
            Assert.That(editor.Draft.Grid.IsWalkable(new GridPos(10, 10)), Is.True);
            Assert.That(editor.ValidateForService(), Is.EqualTo(ServiceLayoutIssue.None));
            editor.Undo(); Assert.That(editor.Draft.Grid.Height, Is.EqualTo(8));
            editor.Undo(); Assert.That(editor.Draft.Grid.Width, Is.EqualTo(8));
            editor.Redo(); editor.Redo(); Assert.That(editor.Draft.Grid.Height, Is.EqualTo(11));
            Assert.That(editor.Draft.Entrance, Is.EqualTo(source.Entrance));
        }

        [Test]
        public void PathfinderExistingInstance_HandlesGrowthAndSameAreaDimensionChanges()
        {
            var pack = Pack(); var source = Room(pack); var finder = new Pathfinder(source.Grid); var path = new List<GridPos>();
            finder.TryFindPath(source.Entrance, new GridPos(7, 7), path);
            var editor = new LayoutEditor(source, pack); editor.Expand(true);
            LayoutSnapshot.Capture(editor.Draft).TryRestore(source, pack);
            Assert.That(finder.TryFindPath(source.Entrance, new GridPos(10, 7), path), Is.True);
            var swapped = new RestaurantLayout(8, 11, source.Entrance); swapped.SetWaitingArea(source.WaitingSpots);
            Assert.That(LayoutSnapshot.Capture(swapped).TryRestore(source, pack), Is.True);
            Assert.That(finder.TryFindPath(source.Entrance, new GridPos(7, 10), path), Is.True);
            Assert.That(path[path.Count - 1], Is.EqualTo(new GridPos(7, 10)));
        }

        [Test]
        public void SnapshotExpansion_RestoresIntoStarterRoomAndRejectsOversizeAtomically()
        {
            var pack = Pack(); var room = Room(pack); var editor = new LayoutEditor(room, pack); editor.Expand(true);
            Assert.That(LayoutSnapshot.Capture(editor.Draft).TryRestore(room, pack), Is.True);
            Assert.That(room.Grid.Width, Is.EqualTo(11)); Assert.That(room.FloorAt(new GridPos(3, 2)), Is.EqualTo("stone"));
            var snapshot = LayoutSnapshot.Capture(room); snapshot.width = int.MaxValue;
            Assert.That(snapshot.TryRestore(room, pack), Is.False); Assert.That(room.Grid.Width, Is.EqualTo(11));
        }

        [Test]
        public void PriceAndLimits_AreCalculatedFromOwnedLand()
        {
            var rules = Pack().Rules.Expansion;
            Assert.That(rules.TryQuote(8, 8, 11, 11, out long price), Is.True); Assert.That(price, Is.EqualTo(300));
            Assert.That(rules.TryQuote(11, 11, 14, 11, out price), Is.True); Assert.That(price, Is.EqualTo(300));
            Assert.That(rules.TryQuote(8, 8, 9, 8, out _), Is.False);
            Assert.That(rules.TryQuote(11, 8, 8, 8, out _), Is.False);
            Assert.That(rules.TryQuote(8, 8, int.MaxValue, 8, out _), Is.False);
            var editor = new LayoutEditor(Room(Pack()), Pack());
            editor.Expand(true); editor.Expand(true);
            Assert.That(editor.Expand(true), Is.False); Assert.That(editor.Draft.Grid.Width, Is.EqualTo(14));
        }

        [Test]
        public void Purchase_ChargesOnceAndPersistenceFailureNeverDebits()
        {
            var server = new LocalEconomyServer(Pack(), startingCoins: 350, ownedWidth: 8, ownedHeight: 8);
            long savedBalance = -1;
            Assert.That(server.CommitExpansion(11, 8, balance => { savedBalance = balance; return false; }), Is.EqualTo(LandPurchaseResult.SaveFailed));
            Assert.That(server.Balance, Is.EqualTo(350)); Assert.That(savedBalance, Is.EqualTo(250));
            Assert.That(server.CommitExpansion(11, 8, balance => { savedBalance = balance; return true; }), Is.EqualTo(LandPurchaseResult.Success));
            Assert.That(server.Balance, Is.EqualTo(250));
            Assert.That(server.CommitExpansion(11, 8, _ => true), Is.EqualTo(LandPurchaseResult.Success));
            Assert.That(server.Balance, Is.EqualTo(250));
            Assert.That(server.CommitExpansion(11, 11, _ => true), Is.EqualTo(LandPurchaseResult.Success));
            Assert.That(server.Balance, Is.EqualTo(50));
        }

        [Test]
        public void Purchase_RejectsInsufficientCoinsInvalidDimensionsAndReentrantCommit()
        {
            var server = new LocalEconomyServer(Pack(), startingCoins: 50, ownedWidth: 8, ownedHeight: 8);
            Assert.That(server.CommitExpansion(11, 8, _ => throw new Exception()), Is.EqualTo(LandPurchaseResult.InsufficientCoins));
            Assert.That(server.CommitExpansion(9, 8, _ => throw new Exception()), Is.EqualTo(LandPurchaseResult.InvalidSize));
            Assert.That(server.CommitExpansion(8, 8, _ => {
                Assert.That(server.CommitExpansion(8, 8, __ => true), Is.EqualTo(LandPurchaseResult.Unavailable)); return true;
            }), Is.EqualTo(LandPurchaseResult.Success));
            Assert.That(server.Balance, Is.EqualTo(50));
        }

        [Test]
        public void Purchase_OnlySettledAuthoritativeSalesCanPay()
        {
            var server = new LocalEconomyServer(Pack(), startingCoins: 90, ownedWidth: 8, ownedHeight: 8);
            server.SubmitSale(new SaleReport(1, "dish", 10000));
            Assert.That(server.CommitExpansion(11, 8, _ => true), Is.EqualTo(LandPurchaseResult.InsufficientCoins));
            server.Tick(1); Assert.That(server.Balance, Is.EqualTo(100));
            server.SubmitSale(new SaleReport(1, "dish", 10000)); server.Tick(1);
            Assert.That(server.Balance, Is.EqualTo(100));
            Assert.That(server.CommitExpansion(11, 8, _ => true), Is.EqualTo(LandPurchaseResult.Success));
            Assert.That(server.Balance, Is.Zero);
        }

        [Test]
        public void SessionLandPurchase_UsesRestoredBalanceAndKeepsSaveInSync()
        {
            var pack = Pack();
            var session = new RestaurantSession(pack, Room(pack), 7);
            var save = session.CaptureSave(); save.coins = 350;
            session.ApplySave(save);
            var server = (LocalEconomyServer)session.Economy;
            Assert.That(server.Balance, Is.EqualTo(350));
            Assert.That(server.CommitExpansion(11, 8, _ => true), Is.EqualTo(LandPurchaseResult.Success));
            Assert.That(session.Wallet.ConfirmedCoins, Is.EqualTo(250));
            Assert.That(session.CaptureSave().coins, Is.EqualTo(250));
            Assert.That(server.CommitExpansion(11, 8, _ => true), Is.EqualTo(LandPurchaseResult.Success));
            Assert.That(session.Wallet.ConfirmedCoins, Is.EqualTo(250));
        }

        [Test]
        public void SessionLandPurchase_AccountsForOfflineIncomeSpendingAndPendingSales()
        {
            var pack = Pack();
            var session = new RestaurantSession(pack, Room(pack), 7, startingCoins: 90);
            var server = (LocalEconomyServer)session.Economy;
            session.Wallet.CreditOffline(40);
            var item = new ItemDef("drink", "drink", 40, 40);
            Assert.That(session.BuyItem(item), Is.True);
            Assert.That(server.Balance, Is.EqualTo(90));
            long receipt = session.Wallet.Predict(10000);
            server.SubmitSale(new SaleReport(receipt, "dish", 10000));
            Assert.That(server.CommitExpansion(11, 8, _ => true), Is.EqualTo(LandPurchaseResult.InsufficientCoins));
            server.Tick(1);
            Assert.That(server.Balance, Is.EqualTo(100));
            Assert.That(session.Wallet.PendingCount, Is.Zero);
            Assert.That(server.CommitExpansion(11, 8, _ => false), Is.EqualTo(LandPurchaseResult.SaveFailed));
            Assert.That(session.Wallet.ConfirmedCoins, Is.EqualTo(100));
            Assert.That(server.CommitExpansion(11, 8, _ => true), Is.EqualTo(LandPurchaseResult.Success));
            Assert.That(session.Wallet.ConfirmedCoins, Is.Zero);
        }

        [Test]
        public void ExpandedRestaurant_CustomerUsesDoorAndNewSeatThenExitsThroughDoor()
        {
            var pack = Pack(); var room = Room(pack); var editor = new LayoutEditor(room, pack);
            editor.Expand(true); editor.Remove(editor.Draft.Furniture[2]);
            editor.Place(pack.GetFurniture("chair"), new GridPos(9, 5), Facing.NorthWest);
            var sim = new RestaurantSim(room, new RestaurantMenu(pack.Dishes, 3), 7);
            sim.Hire(pack.StaffTypes[0], new GridPos(1, 1)); sim.Hire(pack.StaffTypes[1], new GridPos(3, 3));
            sim.ApplyLayout(LayoutSnapshot.Capture(editor.Draft), pack);
            var customer = sim.SpawnCustomer(pack.CustomerTypes[0]);
            Assert.That(customer.Position.Cell, Is.EqualTo(room.Entrance));
            bool seated = false, exiting = false;
            for (int i = 0; i < 2500 && customer.State != CustomerState.Left; i++)
            {
                sim.Tick(.05f);
                if (customer.IsSeated) { seated = true; Assert.That(customer.Facing, Is.EqualTo(Facing.NorthWest)); }
                if (customer.State == CustomerState.WalkingToExit) exiting = true;
            }
            Assert.That(seated && exiting, Is.True); Assert.That(customer.Position.Cell, Is.EqualTo(room.Entrance));
            Assert.That(customer.State, Is.EqualTo(CustomerState.Left));
        }

        [Test]
        public void ShelfFootprintMigration_PreservesNeighborsFloorsAndCoins()
        {
            var original = Pack();
            ContentPack WithShelf(int depth)
            {
                var furniture = new List<FurnitureDef>(original.Furniture) {
                    new FurnitureDef("bread_shelf", "s", FurnitureKind.Decor, 2, depth, 2, 0, 1) };
                return new ContentPack("test", original.Dishes, furniture, original.CustomerTypes,
                    original.StaffTypes, rules: original.Rules).WithEconomy();
            }
            var oldPack = WithShelf(1); var newPack = WithShelf(2);
            var room = Room(oldPack);
            room.TryPlace(oldPack.GetFurniture("bread_shelf"), new GridPos(5, 3), Facing.SouthWest, out _);
            room.TryPlace(oldPack.GetFurniture("chair"), new GridPos(5, 4), Facing.NorthEast, out _);
            room.TryPlace(oldPack.GetFurniture("bread_shelf"), new GridPos(6, 7), Facing.SouthWest, out _);
            var save = LayoutSnapshot.Capture(room); save.hasBalance = true; save.confirmedCoins = 350;
            var target = Room(newPack);
            Assert.That(save.TryRestore(target, newPack), Is.False);
            Assert.That(LayoutMigration.TryUpdateShelfFootprints(save, target, newPack, out var migrated), Is.True);
            Assert.That(migrated.TryRestore(target, newPack), Is.True);
            Assert.That(target.Furniture.Count, Is.EqualTo(room.Furniture.Count));
            Assert.That(target.FindAt(new GridPos(5, 4)).Def.Kind, Is.EqualTo(FurnitureKind.Chair));
            Assert.That(target.FloorAt(new GridPos(3, 2)), Is.EqualTo("stone"));
            Assert.That(migrated.confirmedCoins, Is.EqualTo(350));
            Assert.That(new LayoutEditor(target, newPack).ValidateForService(), Is.EqualTo(ServiceLayoutIssue.None));
        }

        [Test]
        public void LegacyEntranceMigration_RetainsFurnitureAndFloorWithoutBlockingDoor()
        {
            var pack = Pack(); var source = Room(pack);
            // Legacy layout's entrance was on the opposite edge; furniture could cover today's door.
            var legacy = new RestaurantLayout(8, 8, new GridPos(7, 7));
            foreach (var f in source.Furniture) legacy.TryPlace(f.Def, f.Origin, f.Facing, out _);
            legacy.TryPlace(pack.GetFurniture("chair"), source.Entrance, Facing.SouthEast, out _);
            legacy.SetFloor(new GridPos(0, 6), "stone");
            var snapshot = LayoutSnapshot.Capture(legacy); snapshot.version = 1;
            Assert.That(LayoutMigration.TryMoveEntrance(snapshot, source, pack, out var migrated), Is.True);
            Assert.That(migrated.TryRestore(source, pack), Is.True);
            Assert.That(source.Furniture.Count, Is.EqualTo(4)); Assert.That(source.Grid.IsWalkable(source.Entrance), Is.True);
            Assert.That(source.FloorAt(source.Entrance), Is.EqualTo("stone"));
        }
    }
}

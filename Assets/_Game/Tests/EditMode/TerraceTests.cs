using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Economy;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    public class TerraceTests
    {
        private static readonly FurnitureDef Chair = new FurnitureDef("chair", "c", FurnitureKind.Chair, 1, 1, 0.8f, 0.4f, 10);
        private static readonly TerraceStep[] Steps =
        {
            new TerraceStep { width = 4, depth = 3, level = 2, price = 100 },
            new TerraceStep { width = 6, depth = 4, level = 3, price = 300 },
        };

        private static ContentPack Pack() => new ContentPack("test", new[] { new DishDef("d", "d", 1, 1, 1, isStarter: true) },
            new[] { Chair }, new[] { new CustomerTypeDef("c", "c", 2, 10, 1) }, rules: new GameRules { TerraceSteps = Steps });

        /// <summary>5 x 4 room, door on the back-left wall.</summary>
        private static RestaurantLayout Room() => new RestaurantLayout(5, 4, new GridPos(0, 2));

        [Test]
        public void Terrace_ExtendsTheFloorInFrontOfTheRoom_FromTheStreetSide()
        {
            var room = Room();
            room.SetTerrace(3, 2);
            Assert.AreEqual(5, room.Grid.Width);
            Assert.AreEqual(6, room.Grid.Height);
            Assert.IsTrue(room.IsTerraceCell(new GridPos(0, 4)));
            Assert.IsTrue(room.IsTerraceCell(new GridPos(2, 5)));
            Assert.IsFalse(room.IsFloorCell(new GridPos(3, 4)), "beside the narrower terrace: lawn");
            Assert.IsFalse(room.Grid.IsWalkable(new GridPos(3, 4)));
            Assert.IsTrue(room.CanPlace(Chair, new GridPos(1, 5), Facing.SouthWest));
            Assert.IsFalse(room.CanPlace(Chair, new GridPos(4, 5), Facing.SouthWest));
            Assert.AreEqual(4, room.WallLength(WallSide.Left), "walls stay the room's");
        }

        [Test]
        public void WiderTerrace_LeavesTheGridBesideTheRoomBlocked()
        {
            var room = Room();
            room.SetTerrace(7, 2);
            Assert.AreEqual(7, room.Grid.Width);
            Assert.IsFalse(room.Grid.IsWalkable(new GridPos(6, 1)), "beside the room, not floor");
            Assert.IsTrue(room.Grid.IsWalkable(new GridPos(6, 5)));
        }

        [Test]
        public void RoomGrowth_MovesTheTerraceAndItsFurnitureForward()
        {
            var room = Room();
            room.SetTerrace(3, 2);
            room.TryPlace(Chair, new GridPos(1, 5), Facing.SouthWest, out var chair);
            room.SetFloor(new GridPos(2, 4), "stone");
            room.Resize(6, 6);
            Assert.AreEqual(new GridPos(1, 7), chair.Origin);
            Assert.AreEqual("stone", room.FloorAt(new GridPos(2, 6)));
            Assert.IsFalse(room.Grid.IsWalkable(new GridPos(1, 7)));
            Assert.IsTrue(room.Grid.IsWalkable(new GridPos(1, 5)), "now room floor");
            Assert.AreEqual(8, room.Grid.Height);
        }

        [Test]
        public void Snapshot_KeepsTheTerrace()
        {
            var room = Room();
            room.SetTerrace(3, 2);
            room.TryPlace(Chair, new GridPos(1, 5), Facing.SouthWest, out _);
            var copy = Room();
            Assert.IsTrue(LayoutSnapshot.Capture(room).TryRestore(copy, Pack()));
            Assert.AreEqual(3, copy.TerraceWidth);
            Assert.AreEqual(2, copy.TerraceDepth);
            Assert.AreEqual(new GridPos(1, 5), copy.Furniture[0].Origin);
        }

        [Test]
        public void Server_SellsTerraceStepsInOrder_FromTheirLevel_AtItsPrice()
        {
            int level = 1;
            var server = new LocalEconomyServer(Pack(), startingCoins: 350, ownedWidth: 5, ownedHeight: 4);
            server.SetLevelSource(() => level);
            Assert.AreEqual(LandPurchaseResult.Unavailable, server.CommitTerrace(1, _ => true), "level too low");
            level = 3;
            Assert.AreEqual(LandPurchaseResult.InvalidSize, server.CommitTerrace(2, _ => true), "in order only");
            Assert.AreEqual(LandPurchaseResult.SaveFailed, server.CommitTerrace(1, _ => false));
            Assert.AreEqual(350, server.Balance, "nothing taken when the save fails");
            Assert.AreEqual(LandPurchaseResult.Success, server.CommitTerrace(1, _ => true));
            Assert.AreEqual(250, server.Balance);
            Assert.AreEqual(LandPurchaseResult.InsufficientCoins, server.CommitTerrace(2, _ => true));
        }
    }
}

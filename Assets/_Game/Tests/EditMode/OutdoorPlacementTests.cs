using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    public class OutdoorPlacementTests
    {
        private static readonly FurnitureDef Indoor = new FurnitureDef("plant", "p", FurnitureKind.Plant, 1, 1, 1, 0, 5);
        private static readonly FurnitureDef Outdoor = new FurnitureDef("outdoor", "o", FurnitureKind.Decor, 1, 1, 1, 0, 5, outdoorDecor: true);
        private static readonly ContentPack Pack = new ContentPack("test", new[] { new DishDef("d", "d", 1, 1, 1) },
            new[] { Indoor, Outdoor }, new CustomerTypeDef[0]);
        private static RestaurantLayout Room() => new RestaurantLayout(8, 6, new GridPos(0, 3));

        [Test]
        public void IndoorDecor_CannotBePlacedOrMovedAcrossTheWall()
        {
            var room = Room();
            Assert.IsFalse(room.CanPlace(Indoor, new GridPos(-1, 1), Facing.SouthWest));
            Assert.IsFalse(room.TryPlace(Indoor, new GridPos(-1, 1), Facing.SouthWest, out _));
            Assert.IsTrue(room.TryPlace(Indoor, new GridPos(2, 2), Facing.SouthWest, out var piece));
            Assert.IsFalse(room.TryMove(piece, new GridPos(-1, 1), Facing.SouthWest));
            Assert.AreEqual(new GridPos(2, 2), piece.Origin);
            Assert.IsTrue(room.CanPlace(Outdoor, new GridPos(-1, 1), Facing.SouthWest));
            Assert.IsFalse(room.CanPlace(Outdoor, new GridPos(RestaurantLayout.StreetLaneX, 1), Facing.SouthWest));
            Assert.IsFalse(room.CanPlace(Outdoor, room.DoorStep, Facing.SouthWest));
        }

        [TestCase(false)] [TestCase(true)]
        public void LegacyOutdoorIndoorDecor_ReturnsToStorage_WithoutChangingMoneyOrOtherFurniture(bool hasOwnership)
        {
            var legacyPlant = new FurnitureDef("plant", "p", FurnitureKind.Plant, 1, 1, 1, 0, 5, outdoorDecor: true);
            var room = Room();
            room.TryPlace(legacyPlant, new GridPos(-1, 1), Facing.SouthWest, out _);
            room.TryPlace(legacyPlant, new GridPos(2, 2), Facing.SouthWest, out _);
            room.TryPlace(Outdoor, new GridPos(-3, 1), Facing.SouthWest, out _);
            room.SetFloor(new GridPos(1, 1), "wood");
            var before = LayoutSnapshot.Capture(room); before.hasBalance = true; before.confirmedCoins = 1234;
            if (hasOwnership) before.SetOwned(new System.Collections.Generic.Dictionary<string, int> { ["plant"] = 5, ["outdoor"] = 1 });
            var restored = Room();
            Assert.IsFalse(before.TryRestore(restored, Pack));
            Assert.IsTrue(LayoutMigration.TryStoreIndoorStreetDecor(before, restored, Pack, out var migrated));
            Assert.IsTrue(migrated.TryRestore(restored, Pack));
            Assert.IsNull(restored.FindAt(new GridPos(-1, 1)));
            Assert.AreSame(Indoor, restored.FindAt(new GridPos(2, 2)).Def);
            Assert.AreSame(Outdoor, restored.FindAt(new GridPos(-3, 1)).Def);
            Assert.AreEqual("wood", restored.FloorAt(new GridPos(1, 1)));
            Assert.AreEqual(1234, migrated.confirmedCoins); Assert.IsTrue(migrated.hasBalance);
            Assert.AreEqual(hasOwnership ? 5 : 2, migrated.OwnedCounts()["plant"]);
            Assert.AreEqual(3, before.furniture.Length, "Migration leaves the input save intact.");
            Assert.IsFalse(LayoutMigration.TryStoreIndoorStreetDecor(migrated, restored, Pack, out _));
        }

        [Test]
        public void Migration_DoesNotSilentlyAcceptOverlapsOrRoadPlacements()
        {
            var room = Room();
            var save = LayoutSnapshot.Capture(room);
            save.furniture = new[] { new LayoutSnapshot.FurnitureRecord { id = "plant", x = -1, y = 1 },
                new LayoutSnapshot.FurnitureRecord { id = "plant", x = -1, y = 1 } };
            Assert.IsFalse(LayoutMigration.TryStoreIndoorStreetDecor(save, room, Pack, out _));
            save.furniture = new[] { new LayoutSnapshot.FurnitureRecord { id = "plant", x = -20, y = 1 } };
            Assert.IsFalse(LayoutMigration.TryStoreIndoorStreetDecor(save, room, Pack, out _));
        }
    }
}

using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    public class StackingTests
    {
        private static readonly FurnitureDef Table = new FurnitureDef("table", "t", FurnitureKind.Table, 2, 1, 0.75f, 0f, 1);
        private static readonly FurnitureDef Tv = new FurnitureDef("tv", "v", FurnitureKind.Decor, 1, 1, 0.5f, 0f, 1, stackable: true);
        private static readonly FurnitureDef Vase = new FurnitureDef("vase", "a", FurnitureKind.Decor, 1, 1, 0.5f, 0f, 1, stackable: true);
        private static readonly FurnitureDef Plant = new FurnitureDef("plant", "p", FurnitureKind.Plant, 1, 1, 1.5f, 0f, 1);
        private static readonly ContentPack Pack = new ContentPack("test", new[] { new DishDef("d", "d", 1, 1, 1, isStarter: true) },
            new[] { Table, Tv, Vase, Plant }, new[] { new CustomerTypeDef("c", "c", 2, 10, 1) });

        private static RestaurantLayout Room() => new RestaurantLayout(8, 6, new GridPos(0, 5));

        [Test]
        public void StackablePieces_StandOnTables_AndBlockNothingMore()
        {
            var room = Room();
            room.TryPlace(Table, new GridPos(3, 2), Facing.SouthWest, out var table);
            Assert.IsTrue(room.TryPlace(Tv, new GridPos(3, 2), Facing.SouthWest, out var tv));
            Assert.AreSame(table, tv.StackedOn);
            Assert.AreSame(table, room.FindAt(new GridPos(3, 2)), "the floor piece is still the table");
            Assert.IsTrue(room.TryPlace(Vase, new GridPos(4, 2), Facing.SouthWest, out _), "the other half of the table");
            Assert.IsFalse(room.CanStack(Vase, new GridPos(3, 2), Facing.SouthWest), "one piece per spot");
            Assert.IsFalse(room.CanPlace(Plant, new GridPos(3, 2), Facing.SouthWest), "ordinary pieces don't stack");
            Assert.IsTrue(room.TryPlace(Tv, new GridPos(6, 4), Facing.SouthWest, out var onFloor));
            Assert.IsNull(onFloor.StackedOn, "on free floor it stands on the floor");
        }

        [Test]
        public void MovingATable_CarriesWhatStandsOnIt_RemovingItTakesItAway()
        {
            var room = Room();
            room.TryPlace(Table, new GridPos(3, 2), Facing.SouthWest, out var table);
            room.TryPlace(Tv, new GridPos(4, 2), Facing.SouthWest, out var tv);
            Assert.IsTrue(room.TryMove(table, new GridPos(2, 4), Facing.SouthWest));
            Assert.AreEqual(new GridPos(3, 4), tv.Origin);
            Assert.IsTrue(room.Grid.IsWalkable(new GridPos(4, 2)));
            room.Remove(table);
            Assert.AreEqual(0, room.Furniture.Count);
        }

        [Test]
        public void AStackedPieceMovesOntoTheFloor_OrAnotherSurface()
        {
            var room = Room();
            room.TryPlace(Table, new GridPos(3, 2), Facing.SouthWest, out _);
            room.TryPlace(Tv, new GridPos(3, 2), Facing.SouthWest, out var tv);
            Assert.IsFalse(room.Grid.IsWalkable(new GridPos(3, 2)));
            Assert.IsTrue(room.TryMove(tv, new GridPos(1, 1), Facing.SouthWest));
            Assert.IsNull(tv.StackedOn);
            Assert.IsFalse(room.Grid.IsWalkable(new GridPos(1, 1)));
            Assert.IsFalse(room.Grid.IsWalkable(new GridPos(3, 2)), "the table still blocks its cell");
            Assert.IsTrue(room.TryMove(tv, new GridPos(4, 2), Facing.SouthWest));
            Assert.IsNotNull(tv.StackedOn);
            Assert.IsTrue(room.Grid.IsWalkable(new GridPos(1, 1)));
        }

        [Test]
        public void Snapshot_PutsStackedPiecesBackOnTheirTables()
        {
            var room = Room();
            room.TryPlace(Table, new GridPos(3, 2), Facing.SouthWest, out _);
            room.TryPlace(Tv, new GridPos(4, 2), Facing.SouthWest, out _);
            var copy = Room();
            Assert.IsTrue(LayoutSnapshot.Capture(room).TryRestore(copy, Pack));
            Assert.AreEqual(2, copy.Furniture.Count);
            Assert.IsNotNull(copy.Furniture[1].StackedOn);
        }
    }
}

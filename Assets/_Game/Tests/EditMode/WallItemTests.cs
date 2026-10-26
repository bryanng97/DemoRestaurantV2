using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    public class WallItemTests
    {
        private static readonly FurnitureDef Window = new FurnitureDef("window", "w", FurnitureKind.WallDecor, 1, 1, 1f, 0f, 50);
        private static readonly FurnitureDef WidePicture = new FurnitureDef("picture", "p", FurnitureKind.WallDecor, 2, 1, 1f, 0f, 80);
        private static readonly FurnitureDef Chair = new FurnitureDef("chair", "c", FurnitureKind.Chair, 1, 1, 0.8f, 0.4f, 10);
        private static readonly ContentPack Pack = new ContentPack("test", new[] { new DishDef("d", "d", 1, 1, 1, isStarter: true) },
            new[] { Window, WidePicture, Chair }, new[] { new CustomerTypeDef("c", "c", 2, 10, 1) });

        /// <summary>8 x 6 room, door on the back-left wall at Y = 3.</summary>
        private static RestaurantLayout Room() => new RestaurantLayout(8, 6, new GridPos(0, 3));

        [Test]
        public void WallPieces_HangOnEitherWall_InsideItsLength()
        {
            var room = Room();
            Assert.IsTrue(room.TryHang(Window, WallSide.Right, 7, out _), "last cell of the 8-wide back-right wall");
            Assert.IsFalse(room.CanHang(Window, WallSide.Right, 8));
            Assert.IsFalse(room.CanHang(WidePicture, WallSide.Left, 5), "two cells wide: runs past the 6-long wall");
            Assert.IsTrue(room.CanHang(WidePicture, WallSide.Left, 4));
            Assert.IsFalse(room.CanHang(Window, WallSide.Left, -1));
        }

        [Test]
        public void WallPieces_NeverCoverTheDoorOrEachOther()
        {
            var room = Room();
            Assert.IsFalse(room.CanHang(Window, WallSide.Left, 3), "the doorway");
            Assert.IsFalse(room.CanHang(WidePicture, WallSide.Left, 2), "would reach the doorway");
            Assert.IsTrue(room.TryHang(WidePicture, WallSide.Right, 2, out _));
            Assert.IsFalse(room.CanHang(Window, WallSide.Right, 3));
            Assert.IsTrue(room.CanHang(Window, WallSide.Right, 4));
            Assert.IsTrue(room.CanHang(Window, WallSide.Left, 2), "the same index on the other wall is free");
        }

        [Test]
        public void WallPieces_TakeNoFloor_AndFloorPiecesCantHang()
        {
            var room = Room();
            Assert.IsFalse(room.CanPlace(Window, new GridPos(2, 2), Facing.SouthWest), "wall pieces don't stand on the floor");
            Assert.IsFalse(room.CanHang(Chair, WallSide.Left, 1));
            room.TryHang(Window, WallSide.Right, 1, out _);
            Assert.IsTrue(room.Grid.IsWalkable(new GridPos(1, 0)));
        }

        [Test]
        public void DoorCantMoveBehindAHungPiece()
        {
            var room = Room();
            room.TryHang(Window, WallSide.Left, 1, out _);
            Assert.IsFalse(room.CanMoveEntrance(new GridPos(0, 1), new GridPos[0]));
            Assert.IsTrue(room.CanMoveEntrance(new GridPos(0, 5), new GridPos[0]));
        }

        [Test]
        public void Editor_HangsMovesAndTakesDown_WithUndo_AndCountsThemAsPlaced()
        {
            var editor = new LayoutEditor(Room(), Pack);
            Assert.IsTrue(editor.Hang(Window, WallSide.Right, 1));
            var item = editor.Draft.WallItemAt(WallSide.Right, 1);
            Assert.IsTrue(editor.MoveOnWall(item, WallSide.Left, 0));
            Assert.AreEqual(WallSide.Left, item.Side);
            Assert.AreEqual(1, FurnitureOwnership.CountPlaced(editor.Draft)["window"]);
            Assert.IsTrue(editor.TakeDown(item));
            Assert.AreEqual(0, editor.Draft.WallItems.Count);
            Assert.IsTrue(editor.Undo());
            Assert.AreEqual(1, editor.Draft.WallItems.Count);
            Assert.AreSame(Window, editor.Draft.WallItems[0].Def);
        }

        [Test]
        public void StoreAll_EmptiesFloorAndWalls()
        {
            var editor = new LayoutEditor(Room(), Pack);
            editor.Hang(Window, WallSide.Right, 1);
            editor.Place(Chair, new GridPos(4, 4), Facing.SouthWest);
            Assert.IsTrue(editor.StoreAll());
            Assert.AreEqual(0, editor.Draft.Furniture.Count);
            Assert.AreEqual(0, editor.Draft.WallItems.Count);
            Assert.IsTrue(editor.Undo());
            Assert.AreEqual(1, editor.Draft.Furniture.Count);
            Assert.AreEqual(1, editor.Draft.WallItems.Count);
        }

        [Test]
        public void OldSave_WithAWallPieceOnTheFloor_StillLoads()
        {
            var room = Room();
            room.TryPlace(Chair, new GridPos(4, 4), Facing.SouthWest, out _);
            var snapshot = LayoutSnapshot.Capture(room);
            // Before it hung on the wall, the menu holder stood on the floor.
            snapshot.furniture = new[] { snapshot.furniture[0], new LayoutSnapshot.FurnitureRecord { id = "window", x = 2, y = 2 } };
            snapshot.wall = null;
            var copy = Room();
            Assert.IsTrue(snapshot.TryRestore(copy, Pack));
            Assert.AreEqual(1, copy.Furniture.Count, "the chair");
        }

        [Test]
        public void Snapshot_KeepsWallPieces_AndDropsRetiredOnes()
        {
            var room = Room();
            room.TryHang(Window, WallSide.Right, 1, out _);
            room.TryHang(WidePicture, WallSide.Left, 4, out _);
            var snapshot = LayoutSnapshot.Capture(room);
            snapshot.wall[1].id = "retired";
            var copy = Room();
            Assert.IsTrue(snapshot.TryRestore(copy, Pack));
            Assert.AreEqual(1, copy.WallItems.Count);
            Assert.AreEqual(WallSide.Right, copy.WallItems[0].Side);
            Assert.AreEqual(1, copy.WallItems[0].Index);

            snapshot.wall = null; // a save from before wall pieces
            Assert.IsTrue(snapshot.TryRestore(copy, Pack));
            Assert.AreEqual(0, copy.WallItems.Count);
        }
    }
}

using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Menu;
using DemoRestaurant.Logic.Simulation;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    public sealed class LayoutEditorTests
    {
        private static readonly FurnitureDef Bench = new FurnitureDef("bench", "b", FurnitureKind.Decor, 2, 1, 1, 0, 1);
        private static readonly FurnitureDef Vase = new FurnitureDef("vase", "v", FurnitureKind.Decor, 1, 1, 1, 0, 1);
        private static readonly FurnitureDef Chair = new FurnitureDef("chair", "c", FurnitureKind.Chair, 1, 1, 1, 0.4f, 1);
        private static readonly FurnitureDef Stove = new FurnitureDef("stove", "s", FurnitureKind.Stove, 1, 1, 1, 0, 1);
        private static readonly FurnitureDef Counter = new FurnitureDef("counter", "p", FurnitureKind.ServingCounter, 2, 1, 1, 0, 1);
        private static readonly DishDef Dish = new DishDef("dish", "dish", 10, 1, 1);
        private static readonly CustomerTypeDef CustomerType = new CustomerTypeDef("customer", "customer", 5, 120, 1, 120);
        private static readonly StaffTypeDef Chef = new StaffTypeDef("chef", "chef", StaffRole.Chef, 5);
        private static readonly StaffTypeDef Waiter = new StaffTypeDef("waiter", "waiter", StaffRole.Waiter, 5);
        private static readonly ContentPack Pack = new ContentPack("test", new[] { Dish }, new[] { Bench, Vase, Chair, Stove, Counter },
            new[] { CustomerType }, new[] { Chef, Waiter });

        private static RestaurantLayout Room()
        {
            var room = new RestaurantLayout(8, 8, new GridPos(7, 7));
            room.SetWaitingArea(new[] { new GridPos(6, 7) });
            return room;
        }

        private static RestaurantLayout WorkingRoom()
        {
            var room = Room();
            room.TryPlace(Stove, new GridPos(1, 0), Facing.SouthWest, out _);
            room.TryPlace(Counter, new GridPos(1, 3), Facing.SouthWest, out _);
            room.TryPlace(Chair, new GridPos(3, 5), Facing.NorthEast, out _);
            return room;
        }

        [TestCase(Facing.SouthWest)]
        [TestCase(Facing.SouthEast)]
        [TestCase(Facing.NorthEast)]
        [TestCase(Facing.NorthWest)]
        public void Decorations_CannotShareAnyOccupiedTile_WhenPlacingOrMoving(Facing facing)
        {
            var room = Room();
            room.TryPlace(Bench, new GridPos(2, 2), Facing.SouthWest, out _);
            room.TryPlace(Vase, new GridPos(5, 5), Facing.SouthWest, out _);
            var editor = new LayoutEditor(room, Pack);
            var vase = editor.Draft.FindAt(new GridPos(5, 5));
            Assert.That(editor.Place(Vase, new GridPos(3, 2), facing), Is.False, "Even the second tile of the shelf is occupied.");
            Assert.That(editor.Place(Bench, new GridPos(2, 2), facing), Is.False);
            Assert.That(editor.Move(vase, new GridPos(2, 2), facing), Is.False);
            Assert.That(editor.Draft.Furniture.Count, Is.EqualTo(2));
            Assert.That(vase.Origin, Is.EqualTo(new GridPos(5, 5)));
            Assert.That(editor.CanUndo, Is.False, "Rejected overlap must not create an undo operation.");
            Assert.That(editor.Move(vase, new GridPos(4, 2), facing), Is.True, "The neighbouring empty tile remains usable.");
            Assert.That(editor.Undo(), Is.True);
            Assert.That(editor.Draft.FindAt(new GridPos(5, 5)), Is.Not.Null);
        }

        [Test]
        public void DraftEditsAndUndo_DoNotMutateOriginal()
        {
            var original = Room();
            var editor = new LayoutEditor(original, Pack);
            Assert.That(editor.Place(Bench, new GridPos(1, 1), Facing.SouthWest), Is.True);
            Assert.That(editor.Paint(new GridPos(1, 1), "stone"), Is.True);
            Assert.That(original.Furniture.Count, Is.Zero);
            Assert.That(original.FloorAt(new GridPos(1, 1)), Is.Null);
            Assert.That(editor.Undo(), Is.True);
            Assert.That(editor.Draft.Furniture.Count, Is.EqualTo(1));
            Assert.That(editor.Draft.FloorAt(new GridPos(1, 1)), Is.Null);
            Assert.That(editor.Undo(), Is.True);
            Assert.That(editor.Draft.Furniture.Count, Is.Zero);
            Assert.That(editor.Redo(), Is.True);
            Assert.That(editor.Redo(), Is.True);
            Assert.That(editor.Draft.FloorAt(new GridPos(1, 1)), Is.EqualTo("stone"));
        }

        [Test]
        public void Move_CanOverlapOwnFootprint_AndRetainsIdentity()
        {
            var room = Room();
            room.TryPlace(Bench, new GridPos(2, 2), Facing.SouthWest, out var item);
            int id = item.InstanceId;
            Assert.That(room.CanMove(item, new GridPos(3, 2), Facing.SouthWest), Is.True);
            Assert.That(item.Origin, Is.EqualTo(new GridPos(2, 2)));
            Assert.That(room.TryMove(item, new GridPos(3, 2), Facing.SouthWest), Is.True);
            Assert.That(item.InstanceId, Is.EqualTo(id));
            Assert.That(room.Grid.IsWalkable(new GridPos(2, 2)), Is.True);
            Assert.That(room.Grid.IsWalkable(new GridPos(4, 2)), Is.False);
        }

        [Test]
        public void InvalidMove_DoesNotLoseFurnitureOrFireEvents()
        {
            var room = Room();
            room.TryPlace(Bench, new GridPos(1, 1), Facing.SouthWest, out var item);
            room.TryPlace(Chair, new GridPos(4, 1), Facing.SouthWest, out _);
            int changes = 0;
            room.FurniturePlaced += _ => changes++;
            room.FurnitureRemoved += _ => changes++;
            Assert.That(room.TryMove(item, new GridPos(3, 1), Facing.SouthWest), Is.False);
            Assert.That(room.TryMove(item, new GridPos(7, 7), Facing.SouthWest), Is.False);
            Assert.That(changes, Is.Zero);
            Assert.That(item.Origin, Is.EqualTo(new GridPos(1, 1)));
            Assert.That(room.Grid.IsWalkable(item.Origin), Is.False);
        }

        [Test]
        public void Rotation_SwapsFootprint_AndRejectsOutOfBounds()
        {
            var room = Room();
            room.TryPlace(Bench, new GridPos(5, 5), Facing.SouthWest, out var item);
            Assert.That(room.TryMove(item, item.Origin, Facing.SouthEast), Is.True);
            Assert.That(item.SizeX, Is.EqualTo(1)); Assert.That(item.SizeY, Is.EqualTo(2));
            Assert.That(room.Grid.IsWalkable(new GridPos(6, 5)), Is.True);
            Assert.That(room.Grid.IsWalkable(new GridPos(5, 6)), Is.False);
            Assert.That(room.TryMove(item, new GridPos(7, 6), Facing.SouthWest), Is.False);
        }

        [Test]
        public void EntranceQueueAndExtremeCoordinates_AreProtected()
        {
            var room = Room();
            Assert.That(room.CanPlace(Chair, room.Entrance, Facing.SouthWest), Is.False);
            Assert.That(room.CanPlace(Chair, room.WaitingSpots[0], Facing.SouthWest), Is.False);
            Assert.That(room.CanPlace(Bench, new GridPos(int.MaxValue, 1), Facing.SouthWest), Is.False);
            Assert.That(room.CanPlace(Chair, new GridPos(-1, 0), Facing.SouthWest), Is.False);
            Assert.That(room.CanPlace(Chair, new GridPos(1, 1), (Facing)99), Is.False);
        }

        [Test]
        public void FillFloor_IsOneUndoStep_AndDoesNotChangeWalkability()
        {
            var room = WorkingRoom(); var editor = new LayoutEditor(room, Pack);
            Assert.That(editor.FillFloor("pattern"), Is.True);
            Assert.That(editor.Draft.FloorAt(new GridPos(1, 0)), Is.EqualTo("pattern"));
            Assert.That(editor.Draft.Grid.IsWalkable(new GridPos(1, 0)), Is.False);
            Assert.That(editor.Draft.Grid.IsWalkable(editor.Draft.Entrance), Is.True);
            Assert.That(editor.Undo(), Is.True);
            Assert.That(editor.Draft.FloorAt(new GridPos(7, 7)), Is.Null);
            Assert.That(editor.CanUndo, Is.False);
        }

        [Test]
        public void PaintArea_FillsTheDraggedRectangle_InOneUndoStep()
        {
            var editor = new LayoutEditor(Room(), Pack);
            Assert.That(editor.PaintArea(new GridPos(3, 2), new GridPos(1, 1), "stone"), Is.True, "corners in any order");
            for (int y = 1; y <= 2; y++)
            for (int x = 1; x <= 3; x++)
                Assert.That(editor.Draft.FloorAt(new GridPos(x, y)), Is.EqualTo("stone"));
            Assert.That(editor.Draft.FloorAt(new GridPos(4, 2)), Is.Null);
            Assert.That(editor.PaintArea(new GridPos(1, 1), new GridPos(3, 2), "stone"), Is.False, "nothing new to paint");
            Assert.That(editor.PaintArea(new GridPos(-5, -5), new GridPos(0, 0), "wood"), Is.True, "cells off the floor are skipped");
            Assert.That(editor.Undo(), Is.True);
            Assert.That(editor.Undo(), Is.True);
            Assert.That(editor.Draft.FloorAt(new GridPos(2, 2)), Is.Null);
            Assert.That(editor.CanUndo, Is.False);
        }

        [Test]
        public void Wallpaper_PaintsWallCells_UndoesAndSurvivesASave()
        {
            var editor = new LayoutEditor(Room(), Pack);
            int length = editor.Draft.WallLength(WallSide.Right);
            Assert.That(editor.PaintWall(WallSide.Right, 3, 1, "brick"), Is.True, "indexes in any order");
            Assert.That(editor.Draft.WallpaperAt(WallSide.Right, 2), Is.EqualTo("brick"));
            Assert.That(editor.Draft.WallpaperAt(WallSide.Right, 4), Is.Null);
            Assert.That(editor.Draft.WallpaperAt(WallSide.Left, 2), Is.Null, "the other wall is separate");
            Assert.That(editor.Draft.SetWallpaper(WallSide.Right, length, "brick"), Is.False, "past the wall's end");
            Assert.That(editor.PaintWall(WallSide.Right, 1, 3, "brick"), Is.False, "nothing new");

            Assert.That(editor.FillWalls("stripes"), Is.True);
            Assert.That(editor.Draft.WallpaperAt(WallSide.Left, 0), Is.EqualTo("stripes"));
            var copy = Room();
            Assert.That(LayoutSnapshot.Capture(editor.Draft).TryRestore(copy, Pack), Is.True);
            Assert.That(copy.WallpaperAt(WallSide.Right, length - 1), Is.EqualTo("stripes"));

            Assert.That(editor.Undo(), Is.True);
            Assert.That(editor.Draft.WallpaperAt(WallSide.Left, 0), Is.Null);
            Assert.That(editor.Draft.WallpaperAt(WallSide.Right, 1), Is.EqualTo("brick"));
            Assert.That(editor.Undo(), Is.True);
            Assert.That(editor.Draft.WallpaperAt(WallSide.Right, 1), Is.Null);
        }

        [Test]
        public void Wallpaper_EmptyStringsFromJsonUtility_LoadAsTheDefaultWall()
        {
            var room = Room();
            room.SetWallpaper(WallSide.Left, 1, "brick");
            var snapshot = LayoutSnapshot.Capture(room);
            // JsonUtility saves null array entries as "".
            for (int i = 0; i < snapshot.wallpaperLeft.Length; i++) snapshot.wallpaperLeft[i] ??= "";
            for (int i = 0; i < snapshot.wallpaperRight.Length; i++) snapshot.wallpaperRight[i] ??= "";
            var copy = Room();
            Assert.That(snapshot.TryRestore(copy, Pack), Is.True);
            Assert.That(copy.WallpaperAt(WallSide.Left, 1), Is.EqualTo("brick"));
            Assert.That(copy.WallpaperAt(WallSide.Left, 0), Is.Null);
        }

        [Test]
        public void FailedAndNoOpEdits_DoNotConsumeHistory()
        {
            var editor = new LayoutEditor(Room(), Pack);
            Assert.That(editor.Place(Chair, editor.Draft.Entrance, Facing.SouthWest), Is.False);
            Assert.That(editor.Paint(new GridPos(1, 1), null), Is.False);
            Assert.That(editor.FillFloor(null), Is.False);
            Assert.That(editor.CanUndo, Is.False);
            editor.Paint(new GridPos(1, 1), "wood"); editor.Undo();
            editor.Paint(new GridPos(2, 2), "stone");
            Assert.That(editor.CanRedo, Is.False);
        }

        [Test]
        public void MoveRemoveUndo_RestoresBothOccupancyAndFacing()
        {
            var room = Room(); room.TryPlace(Bench, new GridPos(1, 1), Facing.SouthWest, out _);
            var editor = new LayoutEditor(room, Pack);
            Assert.That(editor.Move(editor.Draft.Furniture[0], new GridPos(3, 3), Facing.NorthWest), Is.True);
            Assert.That(editor.Remove(editor.Draft.Furniture[0]), Is.True);
            editor.Undo();
            Assert.That(editor.Draft.Furniture[0].Facing, Is.EqualTo(Facing.NorthWest));
            editor.Undo();
            Assert.That(editor.Draft.Furniture[0].Origin, Is.EqualTo(new GridPos(1, 1)));
            Assert.That(editor.Draft.Grid.IsWalkable(new GridPos(3, 3)), Is.True);
        }

        [Test]
        public void SnapshotRoundTrip_RestoresFurnitureFloorsAndRotation()
        {
            var room = WorkingRoom(); room.SetFloor(new GridPos(1, 2), "stone");
            room.TryPlace(Bench, new GridPos(6, 3), Facing.NorthWest, out _);
            var copy = Room();
            Assert.That(LayoutSnapshot.Capture(room).TryRestore(copy, Pack), Is.True);
            Assert.That(copy.Furniture.Count, Is.EqualTo(room.Furniture.Count));
            Assert.That(copy.Furniture[3].SizeY, Is.EqualTo(2));
            Assert.That(copy.FloorAt(new GridPos(1, 2)), Is.EqualTo("stone"));
            Assert.That(copy.WaitingSpots.Count, Is.EqualTo(1));
        }

        [Test]
        public void InvalidSnapshot_IsAtomic()
        {
            var room = WorkingRoom(); var original = room.Furniture[0];
            var snapshot = LayoutSnapshot.Capture(room);
            snapshot.furniture[1].id = "chair";
            snapshot.furniture[1].x = room.Entrance.X;
            snapshot.furniture[1].y = room.Entrance.Y;
            Assert.That(snapshot.TryRestore(room, Pack), Is.False);
            Assert.That(room.Furniture[0], Is.SameAs(original));
            Assert.That(room.Furniture.Count, Is.EqualTo(3));
        }

        [Test]
        public void RetiredFurniture_IsDropped_RestOfTheLayoutLoads()
        {
            var room = WorkingRoom();
            var snapshot = LayoutSnapshot.Capture(room);
            snapshot.furniture[1].id = "retired_piece";
            var copy = Room();
            Assert.That(snapshot.TryRestore(copy, Pack), Is.True);
            Assert.That(copy.Furniture.Count, Is.EqualTo(room.Furniture.Count - 1));
        }

        [Test]
        public void SaveFormat_RejectsWrongVersionSizeOrFloorArray()
        {
            var room = WorkingRoom(); var snapshot = LayoutSnapshot.Capture(room);
            snapshot.version = 99; Assert.That(snapshot.TryRestore(room, Pack), Is.False);
            snapshot.version = 1; snapshot.width++; Assert.That(snapshot.TryRestore(room, Pack), Is.False);
            snapshot.width--; snapshot.floors = new string[0]; Assert.That(snapshot.TryRestore(room, Pack), Is.False);
        }

        [Test]
        public void Validation_RequiresWorkingKitchenAndSeat()
        {
            var editor = new LayoutEditor(Room(), Pack);
            Assert.That(editor.ValidateForService(), Is.EqualTo(ServiceLayoutIssue.MissingChair));
            editor.Place(Chair, new GridPos(2, 2), Facing.SouthWest);
            Assert.That(editor.ValidateForService(), Is.EqualTo(ServiceLayoutIssue.MissingStove));
            editor.Place(Stove, new GridPos(0, 0), Facing.SouthWest);
            Assert.That(editor.ValidateForService(), Is.EqualTo(ServiceLayoutIssue.None));
        }

        [Test]
        public void Validation_AllowsEmptyFloorBoxedInByFurniture()
        {
            var editor = new LayoutEditor(WorkingRoom(), Pack);
            // Four chairs around (5,2): that one floor cell can't be reached, but nothing needs it.
            editor.Place(Chair, new GridPos(4, 2), Facing.SouthWest);
            editor.Place(Chair, new GridPos(6, 2), Facing.SouthWest);
            editor.Place(Chair, new GridPos(5, 1), Facing.SouthWest);
            editor.Place(Chair, new GridPos(5, 3), Facing.SouthWest);
            Assert.That(editor.ValidateForService(), Is.EqualTo(ServiceLayoutIssue.None));
        }

        [Test]
        public void Validation_RejectsDisconnectedAisles()
        {
            var editor = new LayoutEditor(WorkingRoom(), Pack);
            for (int x = 0; x < 8; x++) editor.Place(Chair, new GridPos(x, 6), Facing.SouthWest);
            Assert.That(editor.ValidateForService(), Is.EqualTo(ServiceLayoutIssue.BlockedAccess));
        }

        [Test]
        public void Validation_NamesTheUnreachablePiece()
        {
            var editor = new LayoutEditor(WorkingRoom(), Pack);
            for (int x = 0; x < 8; x++) editor.Place(Chair, new GridPos(x, 6), Facing.SouthWest);
            Assert.That(editor.ValidateForService(out var blocked), Is.EqualTo(ServiceLayoutIssue.BlockedAccess));
            Assert.IsNotNull(blocked, "a seat or station on the far side of the wall of chairs");
        }

        [Test]
        public void Renovation_ResetsTicketsRelocatesStaffAndCanServeAgain()
        {
            var room = WorkingRoom(); var sim = new RestaurantSim(room, new RestaurantMenu(new[] { Dish }, 3), 7);
            var chef = sim.Hire(Chef, new GridPos(0, 1));
            var waiter = sim.Hire(Waiter, new GridPos(4, 4));
            sim.SpawnCustomer(CustomerType);
            for (int i = 0; i < 100; i++) sim.Tick(0.05f);
            var editor = new LayoutEditor(room, Pack);
            editor.Place(Bench, chef.Home, Facing.SouthWest);
            int penalties = 0, sales = 0;
            sim.CustomerAngry += _ => penalties++;
            sim.CustomerPaid += (_, coins) => sales += coins;
            sim.ApplyLayout(LayoutSnapshot.Capture(editor.Draft), Pack);
            Assert.That(sim.Staff[0], Is.SameAs(chef)); Assert.That(sim.Staff[1], Is.SameAs(waiter));
            Assert.That(room.Grid.IsWalkable(chef.Home), Is.True);
            Assert.That(chef.Position.Cell, Is.EqualTo(chef.Home));
            Assert.That(sim.Tickets.Count, Is.Zero);
            Assert.That(penalties, Is.Zero); Assert.That(sales, Is.Zero);
            if (sim.Customers.Count == 0) sim.SpawnCustomer(CustomerType);
            for (int i = 0; i < 1200 && sales == 0; i++) sim.Tick(0.05f);
            Assert.That(sales, Is.EqualTo(Dish.Price));
        }

        [Test]
        public void InvalidRenovation_KeepsCurrentCustomersAndStaff()
        {
            var room = WorkingRoom(); var sim = new RestaurantSim(room, new RestaurantMenu(new[] { Dish }, 3), 7);
            var chef = sim.Hire(Chef, new GridPos(0, 1)); var customer = sim.SpawnCustomer(CustomerType);
            var snapshot = LayoutSnapshot.Capture(room); snapshot.version = -1;
            Assert.Throws<ArgumentException>(() => sim.ApplyLayout(snapshot, Pack));
            Assert.That(sim.Customers[0], Is.SameAs(customer)); Assert.That(sim.Staff[0], Is.SameAs(chef));
        }
        }

    public class SeatFacingTests
    {
        private static readonly FurnitureDef Table = new FurnitureDef("table", "t", FurnitureKind.Table, 2, 2, 0.8f, 0f, 1);

        private static RestaurantLayout Room()
        {
            var room = new RestaurantLayout(8, 8, new GridPos(7, 7));
            room.TryPlace(Table, new GridPos(3, 3), Facing.SouthWest, out _); // covers (3..4, 3..4)
            return room;
        }

        [TestCase(3, 5, Facing.NorthEast)] // below (+Y): look -Y
        [TestCase(3, 2, Facing.SouthWest)] // above (-Y): look +Y
        [TestCase(5, 3, Facing.NorthWest)] // right (+X): look -X
        [TestCase(2, 4, Facing.SouthEast)] // left (-X): look +X
        public void ChairNextToTable_FacesIt(int x, int y, Facing expected)
        {
            Assert.AreEqual(expected, SeatFacing.TowardTable(Room(), new GridPos(x, y), Facing.SouthWest));
        }

        [Test]
        public void AwayFromTables_KeepsTheChosenFacing()
        {
            Assert.AreEqual(Facing.SouthEast, SeatFacing.TowardTable(Room(), new GridPos(0, 0), Facing.SouthEast));
        }

        [Test]
        public void AlreadyFacingATable_IsKept()
        {
            // Corner cell (2,5) touches nothing diagonally; (5,4) touches the table on its -X side only.
            Assert.AreEqual(Facing.NorthWest, SeatFacing.TowardTable(Room(), new GridPos(5, 4), Facing.NorthWest));
        }
    }

    public class DoorTests
    {
        private static readonly FurnitureDef Chair = new FurnitureDef("chair", "c", FurnitureKind.Chair, 1, 1, 0.8f, 0.4f, 1);
        private static readonly FurnitureDef Plant = new FurnitureDef("plant", "p", FurnitureKind.Plant, 1, 1, 1f, 0f, 1, outdoorDecor: true);
        private static readonly ContentPack Pack = new ContentPack("test", new[] { new DishDef("d", "d", 1, 1, 1, isStarter: true) },
            new[] { Chair, Plant }, new CustomerTypeDef[0]);

        private static RestaurantLayout Room()
        {
            var room = new RestaurantLayout(12, 12, new GridPos(0, 10));
            room.SetWaitingArea(new[] { new GridPos(1, 10), new GridPos(1, 9), new GridPos(0, 9) });
            return room;
        }

        [TestCase(0, 4, 0, 4)]   // on the left wall, segment 1 (cells 3-5): middle cell 4
        [TestCase(5, 7, 0, 7)]   // anywhere on that row: the door stays on the left (street) wall
        [TestCase(8, 0, 0, 1)]   // tapping the right wall: still the left wall, top segment
        [TestCase(-3, 2, 0, 1)]  // tapping the street itself works
        public void DoorSnapsToTheLeftWallSegment(int tx, int ty, int dx, int dy)
        {
            Assert.AreEqual(new GridPos(dx, dy), DoorPlanner.DoorNear(new GridPos(tx, ty), 3, 12, 12));
        }

        [Test]
        public void SegmentOf_IsTheLeftWallSegment()
        {
            Assert.AreEqual((DoorWall.Left, 3), DoorPlanner.SegmentOf(new GridPos(0, 10), 3));
        }

        [Test]
        public void TheDoorNeverGoesOnTheFarmWall()
        {
            var room = Room();
            Assert.IsFalse(room.TryMoveEntrance(new GridPos(7, 0), new[] { new GridPos(7, 1), new GridPos(6, 1), new GridPos(8, 1) }));
        }

        [Test]
        public void MoveEntrance_MovesDoorAndQueue_AndUndoRestoresThem()
        {
            var editor = new LayoutEditor(Room(), Pack);
            Assert.AreEqual(LayoutEditor.DoorMove.Moved, editor.MoveEntrance(new GridPos(3, 4), 3));
            Assert.AreEqual(new GridPos(0, 4), editor.Draft.Entrance);
            Assert.AreEqual(3, editor.Draft.WaitingSpots.Count, "queue size kept");

            editor.Undo();
            Assert.AreEqual(new GridPos(0, 10), editor.Draft.Entrance);
            CollectionAssert.AreEqual(new[] { new GridPos(1, 10), new GridPos(1, 9), new GridPos(0, 9) }, editor.Draft.WaitingSpots);
        }

        [Test]
        public void MoveEntrance_OntoFurniture_IsBlocked()
        {
            var editor = new LayoutEditor(Room(), Pack);
            editor.Place(Chair, new GridPos(0, 4), Facing.SouthWest);
            Assert.AreEqual(LayoutEditor.DoorMove.Blocked, editor.MoveEntrance(new GridPos(0, 4), 3));
            Assert.AreEqual(new GridPos(0, 10), editor.Draft.Entrance);
        }

        [Test]
        public void MoveEntrance_BehindStreetDecoration_IsBlocked()
        {
            var editor = new LayoutEditor(Room(), Pack);
            Assert.IsTrue(editor.Place(Plant, new GridPos(-1, 4), Facing.SouthWest));
            Assert.AreEqual(LayoutEditor.DoorMove.Blocked, editor.MoveEntrance(new GridPos(0, 4), 3));
        }

        [Test]
        public void Snapshot_CarriesTheDoor()
        {
            var editor = new LayoutEditor(Room(), Pack);
            editor.MoveEntrance(new GridPos(0, 4), 3);
            var saved = LayoutSnapshot.Capture(editor.Draft);

            var loaded = Room();
            Assert.IsTrue(saved.TryRestore(loaded, Pack));
            Assert.AreEqual(new GridPos(0, 4), loaded.Entrance);
            CollectionAssert.AreEqual(editor.Draft.WaitingSpots, loaded.WaitingSpots);
        }

        [Test]
        public void OldSnapshot_WithoutDoor_KeepsTheRestaurantsDoor()
        {
            var saved = LayoutSnapshot.Capture(Room());
            saved.entranceX = saved.entranceY = -1;
            saved.waiting = null;
            var loaded = Room();
            Assert.IsTrue(saved.TryRestore(loaded, Pack));
            Assert.AreEqual(new GridPos(0, 10), loaded.Entrance);
        }
    }

    public class StreetTests
    {
        private static readonly FurnitureDef Plant = new FurnitureDef("plant", "p", FurnitureKind.Plant, 1, 1, 1f, 0f, 1, outdoorDecor: true);
        private static readonly FurnitureDef Sign = new FurnitureDef("sign", "s", FurnitureKind.Decor, 1, 2, 1f, 0f, 1, outdoorDecor: true);
        private static readonly FurnitureDef Chair = new FurnitureDef("chair", "c", FurnitureKind.Chair, 1, 1, 0.8f, 0.4f, 1);
        private static readonly ContentPack Pack = new ContentPack("test", new[] { new DishDef("d", "d", 1, 1, 1, isStarter: true) },
            new[] { Plant, Sign, Chair }, new CustomerTypeDef[0], rules: new GameRules
            {
                Expansion = new ExpansionRules { baseWidth = 8, baseHeight = 6, maxWidth = 14, maxHeight = 12, step = 3, firstPrice = 100, priceIncrement = 100 },
            });

        private static RestaurantLayout Room() => new RestaurantLayout(8, 6, new GridPos(0, 3));

        [Test]
        public void StreetRunsAlongTheDoorWall_AndOverhangsBothEnds()
        {
            var room = Room();
            Assert.IsTrue(room.IsOutdoor(new GridPos(-1, 0)));
            Assert.IsTrue(room.IsOutdoor(new GridPos(-3, -3)), "back end");
            Assert.IsTrue(room.IsOutdoor(new GridPos(RestaurantLayout.StreetOuterX, 8)), "front end (6 + 3 - 1)");
            Assert.IsTrue(room.IsSidewalkCell(new GridPos(-1, 2)), "the sidewalk runs right along the wall");
            Assert.IsTrue(room.IsSidewalkCell(new GridPos(RestaurantLayout.StreetLaneX, 2)));
            Assert.IsFalse(room.IsOutdoor(new GridPos(RestaurantLayout.StreetOuterX - 1, 2)), "the road is scenery");
            Assert.IsFalse(room.IsOutdoor(new GridPos(-1, 9)), "past the front end");
            Assert.IsFalse(room.IsOutdoor(new GridPos(3, -1)), "behind the farm wall is not street");
            Assert.IsFalse(room.IsOutdoor(new GridPos(8, 2)), "the open front is not street");
        }

        [Test]
        public void DecorationGoesOnTheSideLanes_NotTheWalkingLaneOrTheDoorstep()
        {
            var room = Room();
            Assert.IsTrue(room.TryPlace(Plant, new GridPos(-3, 1), Facing.SouthWest, out _), "far side lane");
            Assert.IsTrue(room.TryPlace(Plant, new GridPos(-1, 1), Facing.SouthWest, out _), "wall side lane");
            Assert.IsFalse(room.CanPlace(Plant, new GridPos(RestaurantLayout.StreetLaneX, 1), Facing.SouthWest), "walking lane");
            Assert.IsFalse(room.CanPlace(Plant, room.DoorStep, Facing.SouthWest), "in front of the door");
            Assert.IsFalse(room.CanPlace(Chair, new GridPos(-3, 4), Facing.SouthWest), "no seats outside");
        }

        [Test]
        public void StreetDecoration_DoesNotOverlap_OrCrossTheLane()
        {
            var room = Room();
            Assert.IsTrue(room.TryPlace(Sign, new GridPos(-3, 0), Facing.SouthWest, out _));
            Assert.IsFalse(room.CanPlace(Plant, new GridPos(-3, 1), Facing.SouthWest), "taken");
            Assert.IsFalse(room.CanPlace(Sign, new GridPos(RestaurantLayout.StreetOuterX, 4), Facing.SouthEast), "rotated across the lane");
        }

        [Test]
        public void Moving_BetweenInsideAndTheStreet_KeepsTheGridRight()
        {
            var room = Room();
            room.TryPlace(Plant, new GridPos(4, 2), Facing.SouthWest, out var plant);
            Assert.IsFalse(room.Grid.IsWalkable(new GridPos(4, 2)));

            Assert.IsTrue(room.TryMove(plant, new GridPos(-3, 2), Facing.SouthWest));
            Assert.IsTrue(room.Grid.IsWalkable(new GridPos(4, 2)), "freed indoors");

            Assert.IsTrue(room.TryMove(plant, new GridPos(5, 1), Facing.SouthWest));
            Assert.IsFalse(room.Grid.IsWalkable(new GridPos(5, 1)), "blocks again indoors");
        }

        [Test]
        public void Expanding_KeepsStreetDecorationInPlace()
        {
            var editor = new LayoutEditor(Room(), Pack);
            editor.Place(Plant, new GridPos(-3, 2), Facing.SouthWest);
            Assert.IsTrue(editor.Expand(alongX: false), "6 -> 9 deep: the street gets longer");
            Assert.IsNotNull(editor.Draft.FindAt(new GridPos(-3, 2)));
            Assert.IsTrue(editor.Draft.IsOutdoor(new GridPos(-3, 11)), "sidewalk now reaches 9 + 3 - 1");
        }

        [Test]
        public void Snapshot_KeepsStreetDecoration()
        {
            var editor = new LayoutEditor(Room(), Pack);
            editor.Place(Sign, new GridPos(-3, 4), Facing.SouthWest);
            var saved = LayoutSnapshot.Capture(editor.Draft);
            var loaded = Room();
            Assert.IsTrue(saved.TryRestore(loaded, Pack));
            Assert.AreSame(Sign, loaded.FindAt(new GridPos(-3, 5))?.Def);
        }

        [Test]
        public void RoutesGoFromTheFrontEndToTheDoor_AndOutToTheBackEnd()
        {
            var room = Room();
            var inRoute = room.StreetRouteIn();
            Assert.AreEqual(new GridPos(RestaurantLayout.StreetLaneX, room.StreetMaxY), inRoute[0]);
            Assert.AreEqual(room.DoorStep, inRoute[inRoute.Count - 2]);
            Assert.AreEqual(room.Entrance, inRoute[inRoute.Count - 1]);
            for (int i = 1; i < inRoute.Count; i++)
                Assert.AreEqual(1, Math.Abs(inRoute[i].X - inRoute[i - 1].X) + Math.Abs(inRoute[i].Y - inRoute[i - 1].Y), "one step at a time");

            var outRoute = room.StreetRouteOut();
            Assert.AreEqual(room.DoorStep, outRoute[0]);
            Assert.AreEqual(new GridPos(RestaurantLayout.StreetLaneX, room.StreetMinY), outRoute[outRoute.Count - 1]);
        }
    }
}

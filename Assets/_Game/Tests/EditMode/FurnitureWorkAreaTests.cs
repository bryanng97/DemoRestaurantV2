using System;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    public sealed class FurnitureWorkAreaTests
    {
        private static readonly FurnitureDef Stove=new FurnitureDef("stove","s",FurnitureKind.Stove,1,1,1,0,10);
        private static readonly FurnitureDef Decor=new FurnitureDef("decor","d",FurnitureKind.Decor,1,1,1,0,10);
        private static ContentPack Pack()=>new ContentPack("test",Array.Empty<DishDef>(),new[]{Stove,Decor},Array.Empty<CustomerTypeDef>(),Array.Empty<StaffTypeDef>());
        private static RestaurantLayout Room()=>new RestaurantLayout(10,10,new GridPos(0,9));

        [TestCase(Facing.SouthWest)]
        [TestCase(Facing.SouthEast)]
        [TestCase(Facing.NorthEast)]
        [TestCase(Facing.NorthWest)]
        public void Stove_ReservesItsWalkableFrontTile_InEveryFacing(Facing facing)
        {
            var editor=new LayoutEditor(Room(),Pack());var origin=new GridPos(4,4);
            Assert.IsTrue(editor.Place(Stove,origin,facing));
            var front=FurnitureWorkArea.FrontTile(Stove,origin,facing);
            Assert.AreEqual(origin+FacingUtil.ToDelta(facing),front);
            Assert.IsFalse(editor.Draft.Grid.IsWalkable(origin));Assert.IsTrue(editor.Draft.Grid.IsWalkable(front));
            Assert.IsFalse(editor.Place(Decor,front,Facing.SouthWest),"Furniture cannot cover the chef's work mat.");
            Assert.IsNull(editor.Draft.FindAt(front));Assert.IsTrue(editor.Draft.Grid.IsWalkable(front));
            Assert.IsTrue(editor.Remove(editor.Draft.FindAt(origin)));
            Assert.IsTrue(editor.Place(Decor,front,Facing.SouthWest));
        }

        [Test]
        public void NewStove_RejectsOccupiedOutsideAndSharedWorkTiles_WithoutMovingNeighbors()
        {
            var room=Room();room.TryPlace(Decor,new GridPos(4,5),Facing.SouthWest,out _);
            var editor=new LayoutEditor(room,Pack());
            Assert.IsFalse(editor.Place(Stove,new GridPos(4,4),Facing.SouthWest));
            Assert.IsFalse(editor.Place(Stove,new GridPos(4,9),Facing.SouthWest));
            Assert.AreEqual(1,editor.Draft.Furniture.Count);
            Assert.IsTrue(editor.Place(Stove,new GridPos(7,4),Facing.SouthWest));
            Assert.IsFalse(editor.Place(Stove,new GridPos(7,6),Facing.NorthEast));
        }

        [Test]
        public void MovingAndRotating_ReleasesOldWorkTile_AndUndoRestoresReservation()
        {
            var editor=new LayoutEditor(Room(),Pack());var origin=new GridPos(4,4);
            editor.Place(Stove,origin,Facing.SouthWest);var stove=editor.Draft.FindAt(origin);
            editor.Place(Decor,new GridPos(7,7),Facing.SouthWest);var decor=editor.Draft.FindAt(new GridPos(7,7));
            Assert.IsFalse(editor.Move(decor,new GridPos(4,5),Facing.SouthWest));
            Assert.AreEqual(new GridPos(7,7),decor.Origin);
            Assert.IsTrue(editor.Move(stove,origin,Facing.NorthEast));
            Assert.IsTrue(FurnitureWorkArea.CanEdit(editor.Draft,Decor,new GridPos(4,5),Facing.SouthWest));
            Assert.IsFalse(FurnitureWorkArea.CanEdit(editor.Draft,Decor,new GridPos(4,3),Facing.SouthWest));
            Assert.IsTrue(editor.Undo());
            Assert.IsFalse(FurnitureWorkArea.CanEdit(editor.Draft,Decor,new GridPos(4,5),Facing.SouthWest));
        }

        [Test]
        public void LegacyBlockedWorkTile_StillLoadsWithoutRearrangingOrLosingPieces()
        {
            var room=Room();room.TryPlace(Stove,new GridPos(4,4),Facing.SouthWest,out _);
            room.TryPlace(Decor,new GridPos(4,5),Facing.SouthWest,out _);
            var save=LayoutSnapshot.Capture(room);var restored=Room();
            Assert.IsTrue(save.TryRestore(restored,Pack()));Assert.AreEqual(2,restored.Furniture.Count);
            var editor=new LayoutEditor(restored,Pack());Assert.AreEqual(2,editor.Draft.Furniture.Count);
            Assert.IsTrue(editor.Move(editor.Draft.FindAt(new GridPos(4,5)),new GridPos(7,7),Facing.SouthWest));
            Assert.IsFalse(editor.Place(Decor,new GridPos(4,5),Facing.SouthWest));
        }
    }
}

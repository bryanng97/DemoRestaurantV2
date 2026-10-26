using System.Collections.Generic;
using DemoRestaurant.Logic.Iso;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    public class IsoDepthSorterTests
    {
        private static DepthItem Cell(float x, float y, float z = 0f) => DepthItem.FromFootprint(x, y, x + 1, y + 1, z);

        /// <summary>A character's feet: a small box around a point in grid space.</summary>
        private static DepthItem Feet(float x, float y, float z = 0f) =>
            DepthItem.FromFootprint(x - 0.2f, y - 0.2f, x + 0.2f, y + 0.2f, z);

        private static List<int> Sort(params DepthItem[] items)
        {
            var order = new List<int>();
            new IsoDepthSorter().Sort(items, order);
            return order;
        }

        private static void AssertBehind(List<int> order, int back, int front)
        {
            Assert.Less(order.IndexOf(back), order.IndexOf(front), $"item {back} should draw before item {front}");
        }

        [Test]
        public void SingleCells_SortByDistanceFromViewer()
        {
            var order = Sort(Cell(2, 2), Cell(0, 0), Cell(1, 1));
            CollectionAssert.AreEqual(new[] { 1, 2, 0 }, order);
        }

        [Test]
        public void CharacterInFrontOfLongCounter_DrawsInFront()
        {
            // Counter runs along X from (0,0) to (5,1). Character stands just in front (higher Y) near the far end.
            // Its x+y is smaller than the counter's far corner, so naive x+y sorting would hide it.
            var counter = DepthItem.FromFootprint(0, 0, 5, 1);
            var character = Feet(0.5f, 1.5f);

            AssertBehind(Sort(counter, character), back: 0, front: 1);
        }

        [Test]
        public void CharacterBehindLongCounter_DrawsBehind()
        {
            var counter = DepthItem.FromFootprint(0, 2, 5, 3);
            var character = Feet(4.5f, 1.5f); // behind (lower Y) at the near end, large x+y

            AssertBehind(Sort(counter, character), back: 1, front: 0);
        }

        [Test]
        public void CharacterBesideTallShelf_OnFarSideOfXAxis_DrawsBehind()
        {
            var shelf = DepthItem.FromFootprint(3, 0, 4, 4);
            var character = Feet(2.5f, 3.5f);

            AssertBehind(Sort(shelf, character), back: 1, front: 0);
        }

        [Test]
        public void SeatedCustomer_DrawsAboveChairSharingTheCell()
        {
            var chair = Cell(2, 2);
            var seated = Feet(2.5f, 2.5f, z: 0.4f);

            AssertBehind(Sort(seated, chair), back: 1, front: 0);
        }

        [Test]
        public void TransitiveOrder_IsRespectedAcrossManyItems()
        {
            var items = new List<DepthItem>
            {
                DepthItem.FromFootprint(0, 0, 4, 1), // 0: long counter at the back
                Feet(1.5f, 1.5f),                    // 1: in front of counter
                DepthItem.FromFootprint(0, 2, 2, 3), // 2: table in front of character 1
                Feet(0.5f, 3.5f),                    // 3: in front of table
                Cell(6, 6),                          // 4: far away near the viewer
            };
            var order = new List<int>();
            new IsoDepthSorter().Sort(items, order);

            Assert.AreEqual(items.Count, order.Count);
            AssertBehind(order, 0, 1);
            AssertBehind(order, 1, 2);
            AssertBehind(order, 2, 3);
        }

        [Test]
        public void NonOverlappingScreenBounds_AreNotConstrained_AndAllItemsAreEmitted()
        {
            var a = Cell(0, 0);
            a.ScreenMinX = 0; a.ScreenMaxX = 1; a.ScreenMinY = 0; a.ScreenMaxY = 1;
            var b = Cell(5, 5);
            b.ScreenMinX = 10; b.ScreenMaxX = 11; b.ScreenMinY = 10; b.ScreenMaxY = 11;

            var order = Sort(b, a);
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, order);
        }

        [Test]
        public void SorterIsReusable_WithDifferentCounts()
        {
            var sorter = new IsoDepthSorter();
            var order = new List<int>();
            var many = new List<DepthItem>();
            for (int i = 0; i < 300; i++)
                many.Add(Feet(i % 20 + 0.5f, i / 20 + 0.5f));

            sorter.Sort(many, order);
            Assert.AreEqual(300, order.Count);

            sorter.Sort(new[] { Cell(1, 1), Cell(0, 0) }, order);
            CollectionAssert.AreEqual(new[] { 1, 0 }, order);
        }
    }
}

using System.Collections.Generic;
using DemoRestaurant.Logic.Grid;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    public class PathfinderTests
    {
        [Test]
        public void StraightLine_ReturnsShortestPathExcludingStart()
        {
            var grid = new FloorGrid(5, 5);
            var path = new List<GridPos>();

            Assert.IsTrue(new Pathfinder(grid).TryFindPath(new GridPos(0, 0), new GridPos(3, 0), path));

            CollectionAssert.AreEqual(new[] { new GridPos(1, 0), new GridPos(2, 0), new GridPos(3, 0) }, path);
        }

        [Test]
        public void RoutesAroundWall()
        {
            var grid = new FloorGrid(5, 5);
            grid.Block(new GridPos(2, 0), 1, 4); // wall with a gap at y=4
            var path = new List<GridPos>();

            Assert.IsTrue(new Pathfinder(grid).TryFindPath(new GridPos(0, 0), new GridPos(4, 0), path));

            Assert.AreEqual(12, path.Count, "down 4, across 4, up 4");
            CollectionAssert.Contains(path, new GridPos(2, 4));
            foreach (var p in path)
                Assert.IsTrue(grid.IsWalkable(p), $"{p} should be walkable");
        }

        [Test]
        public void NoPath_ReturnsFalse()
        {
            var grid = new FloorGrid(5, 5);
            grid.Block(new GridPos(2, 0), 1, 5);
            var path = new List<GridPos>();

            Assert.IsFalse(new Pathfinder(grid).TryFindPath(new GridPos(0, 0), new GridPos(4, 0), path));
            Assert.IsEmpty(path);
        }

        [Test]
        public void BlockedGoal_CanBeEnteredAsFinalStep()
        {
            var grid = new FloorGrid(5, 5);
            grid.Block(new GridPos(3, 3), 1, 1); // a chair
            var path = new List<GridPos>();

            Assert.IsTrue(new Pathfinder(grid).TryFindPath(new GridPos(0, 3), new GridPos(3, 3), path, allowBlockedGoal: true));
            Assert.AreEqual(new GridPos(3, 3), path[path.Count - 1]);
        }

        [Test]
        public void BlockedGoal_RequiresExplicitPermission_EvenWhenAlreadyThere()
        {
            var grid = new FloorGrid(5, 5);
            var blocked = new GridPos(3, 3);
            grid.Block(blocked, 1, 1);
            var finder = new Pathfinder(grid);
            var path = new List<GridPos>();
            Assert.That(finder.TryFindPath(new GridPos(0, 3), blocked, path), Is.False);
            Assert.That(path, Is.Empty);
            Assert.That(finder.TryFindPath(blocked, blocked, path), Is.False);
        }

        [Test]
        public void BlockedGoalPermission_DoesNotPermitCrossingOtherBlockers()
        {
            var grid = new FloorGrid(5, 5);
            grid.Block(new GridPos(2, 0), 1, 5);
            grid.Block(new GridPos(4, 3), 1, 1);
            Assert.That(new Pathfinder(grid).TryFindPath(new GridPos(0, 3), new GridPos(4, 3),
                new List<GridPos>(), allowBlockedGoal: true), Is.False);
        }

        [Test]
        public void BlockedStart_CanBeLeftWithoutEnteringAnotherObstacle()
        {
            var grid = new FloorGrid(5, 5);
            var start = new GridPos(3, 3);
            grid.Block(start, 1, 1);
            var path = new List<GridPos>();
            Assert.That(new Pathfinder(grid).TryFindPath(start, new GridPos(0, 3), path), Is.True);
            foreach (var cell in path) Assert.That(grid.IsWalkable(cell), Is.True);
        }

        [Test]
        public void ReusingPathfinder_GivesSameResults()
        {
            var grid = new FloorGrid(8, 8);
            grid.Block(new GridPos(4, 1), 1, 6);
            var pf = new Pathfinder(grid);
            var a = new List<GridPos>();
            var b = new List<GridPos>();

            pf.TryFindPath(new GridPos(0, 4), new GridPos(7, 4), a);
            pf.TryFindPath(new GridPos(7, 7), new GridPos(0, 0), b);
            var again = new List<GridPos>();
            pf.TryFindPath(new GridPos(0, 4), new GridPos(7, 4), again);

            CollectionAssert.AreEqual(a, again);
        }

        [Test]
        public void OverlappingBlockers_UnblockIndependently()
        {
            var grid = new FloorGrid(3, 3);
            grid.Block(new GridPos(1, 1), 1, 1);
            grid.Block(new GridPos(1, 1), 1, 1);
            grid.Unblock(new GridPos(1, 1), 1, 1);

            Assert.IsFalse(grid.IsWalkable(new GridPos(1, 1)));
            grid.Unblock(new GridPos(1, 1), 1, 1);
            Assert.IsTrue(grid.IsWalkable(new GridPos(1, 1)));
        }
    }
}

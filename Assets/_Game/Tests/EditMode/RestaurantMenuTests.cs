using System.Collections.Generic;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Menu;
using DemoRestaurant.Logic.Simulation;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    public class RestaurantMenuTests
    {
        private static readonly DishDef NasiLemak = new DishDef("nasi_lemak", "dish.nasi_lemak", 12, 1, 1);
        private static readonly DishDef Laksa = new DishDef("laksa", "dish.laksa", 13, 1, 1);
        private static readonly DishDef Satay = new DishDef("satay", "dish.satay", 15, 1, 1);
        private static readonly DishDef Cendol = new DishDef("cendol", "dish.cendol", 7, 1, 1);
        private static readonly DishDef[] All = { NasiLemak, Laksa, Satay, Cendol };

        [Test]
        public void DefaultsToFirstDishesUpToCapacity()
        {
            var menu = new RestaurantMenu(All, capacity: 3);
            CollectionAssert.AreEqual(new[] { NasiLemak, Laksa, Satay }, menu.Active);
            Assert.IsTrue(menu.IsFull);
        }

        [Test]
        public void CannotAddBeyondCapacity()
        {
            var menu = new RestaurantMenu(All, 3);
            int changes = 0;
            menu.Changed += () => changes++;

            Assert.AreEqual(MenuChange.MenuFull, menu.Toggle(Cendol));
            Assert.IsFalse(menu.IsActive(Cendol));
            Assert.AreEqual(0, changes);
        }

        [Test]
        public void RemoveThenAdd_SwapsADish()
        {
            var menu = new RestaurantMenu(All, 3);

            Assert.AreEqual(MenuChange.Removed, menu.Toggle(Laksa));
            Assert.AreEqual(MenuChange.Added, menu.Toggle(Cendol));

            CollectionAssert.AreEquivalent(new[] { NasiLemak, Satay, Cendol }, menu.Active);
        }

        [Test]
        public void CannotRemoveLastDish()
        {
            var menu = new RestaurantMenu(All, 3, initial: new[] { Satay });

            Assert.AreEqual(MenuChange.LastDish, menu.Toggle(Satay));
            CollectionAssert.AreEqual(new[] { Satay }, menu.Active);
        }

        [Test]
        public void RejectsDishOutsideCatalogue()
        {
            var menu = new RestaurantMenu(new[] { NasiLemak, Laksa }, 3);
            Assert.AreEqual(MenuChange.NotAvailable, menu.TryAdd(Satay));
        }

        [Test]
        public void InitialSelection_IsTrimmedToCapacity_AndDeduplicated()
        {
            var menu = new RestaurantMenu(All, 2, initial: new[] { Cendol, Cendol, Satay, Laksa });
            CollectionAssert.AreEqual(new[] { Cendol, Satay }, menu.Active);
        }

        [Test]
        public void Customers_OrderOnlyActiveDishes_AndFollowMenuChanges()
        {
            var chair = new FurnitureDef("chair", "c", FurnitureKind.Chair, 1, 1, 0.8f, 0.4f, 1);
            var walker = new CustomerTypeDef("w", "w", 3f, 60f, 1);
            var layout = new RestaurantLayout(10, 10, new GridPos(9, 9));
            for (int x = 0; x < 8; x += 2)
                layout.TryPlace(chair, new GridPos(x, 0), Facing.SouthWest, out _);

            var menu = new RestaurantMenu(All, 3, initial: new[] { Laksa, Satay });
            var sim = new RestaurantSim(layout, menu, seed: 5);
            var ordered = new HashSet<DishDef>();
            sim.CustomerOrdered += (_, dish) => ordered.Add(dish);

            for (int i = 0; i < 4; i++) sim.SpawnCustomer(walker);
            for (int i = 0; i < 400; i++) sim.Tick(0.05f);
            CollectionAssert.AreEquivalent(new[] { Laksa, Satay }, ordered);

            // No kitchen staff here, so the first group never gets food; clear them to free the chairs.
            foreach (var seated in new List<Customer>(sim.Customers))
                sim.Despawn(seated);

            // Swap the whole menu to Cendol only; new orders must follow.
            menu.TryAdd(Cendol);
            menu.TryRemove(Laksa);
            menu.TryRemove(Satay);
            ordered.Clear();
            for (int i = 0; i < 4; i++) sim.SpawnCustomer(walker);
            for (int i = 0; i < 400; i++) sim.Tick(0.05f);
            CollectionAssert.AreEqual(new[] { Cendol }, ordered);
        }
    }

    public class MenuCategoryTests
    {
        private static readonly DishDef Roti = new DishDef("roti", "d", 10, 1, 1, category: DishCategory.Starter);
        private static readonly DishDef Satay = new DishDef("satay", "d", 14, 1, 1, category: DishCategory.Starter);
        private static readonly DishDef Lemak = new DishDef("lemak", "d", 12, 1, 1, category: DishCategory.Main);
        private static readonly DishDef Cendol = new DishDef("cendol", "d", 9, 1, 1, category: DishCategory.Dessert);
        private static readonly DishDef Teh = new DishDef("teh", "d", 8, 1, 1, category: DishCategory.Drink);

        private int _perCategory;

        [SetUp]
        public void SetUp() => _perCategory = 1;

        private RestaurantMenu Menu(params DishDef[] known) =>
            new RestaurantMenu(known, c => c == DishCategory.Drink ? 0 : _perCategory);

        [Test]
        public void OneSlotPerCategory_FillsEachCategory()
        {
            var menu = Menu(Roti, Satay, Lemak, Cendol);
            CollectionAssert.AreEquivalent(new[] { Roti, Lemak, Cendol }, menu.Active);
            Assert.AreEqual(3, menu.Capacity);
            Assert.AreEqual(MenuChange.MenuFull, menu.TryAdd(Satay), "starter slot taken");
        }

        [Test]
        public void SwappingWithinACategory()
        {
            var menu = Menu(Roti, Satay, Lemak);
            Assert.AreEqual(MenuChange.Removed, menu.TryRemove(Roti));
            Assert.AreEqual(MenuChange.Added, menu.TryAdd(Satay));
            CollectionAssert.AreEquivalent(new[] { Satay, Lemak }, menu.Active);
        }

        [Test]
        public void ClosedCategory_CantBeServed()
        {
            var menu = Menu(Lemak, Teh);
            CollectionAssert.AreEqual(new[] { Lemak }, menu.Active);
            Assert.AreEqual(MenuChange.MenuFull, menu.TryAdd(Teh));
            Assert.AreEqual(0, menu.SlotsFor(DishCategory.Drink));
        }

        [Test]
        public void MoreSlots_AreFilledAfterLevelUp()
        {
            var menu = Menu(Roti, Satay, Lemak);
            Assert.IsFalse(menu.IsActive(Satay));
            _perCategory = 2;
            menu.FillFreeSlots();
            Assert.IsTrue(menu.IsActive(Satay));
            Assert.AreEqual(2, menu.ActiveCount(DishCategory.Starter));
        }

        [Test]
        public void Learning_GoesOnTheMenuOnlyIfItsCategoryHasRoom()
        {
            var menu = Menu(Roti);
            menu.MakeAvailable(Lemak);
            menu.MakeAvailable(Satay);
            Assert.IsTrue(menu.IsActive(Lemak));
            Assert.IsFalse(menu.IsActive(Satay));
            Assert.IsTrue(menu.IsAvailable(Satay));
        }
    }
}

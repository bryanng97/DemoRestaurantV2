using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Menu;
using DemoRestaurant.Logic.Progression;
using DemoRestaurant.Logic.Session;
using DemoRestaurant.Logic.Simulation;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    public class StreetWalkTests
    {
        private static readonly FurnitureDef Chair = new FurnitureDef("chair", "c", FurnitureKind.Chair, 1, 1, 0.8f, 0.4f, 1);
        private static readonly DishDef Teh = new DishDef("teh", "d", 4, 1, 2, isStarter: true);
        private static readonly CustomerTypeDef Local = new CustomerTypeDef("local", "c", 3f, 60f, 1, foodPatienceSeconds: 120f);

        private static RestaurantSim Sim(bool street, bool withSeat = true)
        {
            var layout = new RestaurantLayout(8, 6, new GridPos(0, 3));
            layout.SetWaitingArea(new[] { new GridPos(1, 3) });
            if (withSeat) layout.TryPlace(Chair, new GridPos(4, 3), Facing.SouthWest, out _);
            return new RestaurantSim(layout, new RestaurantMenu(new[] { Teh }, 1), 1, new SimSettings { StreetWalk = street });
        }

        private static void Run(RestaurantSim sim, float seconds)
        {
            for (float t = 0; t < seconds; t += 0.05f) sim.Tick(0.05f);
        }

        [Test]
        public void WithoutStreet_GuestsAppearAtTheDoor()
        {
            var sim = Sim(street: false);
            var c = sim.SpawnCustomer(Local);
            Assert.AreEqual(Vec2.CellCentre(sim.Layout.Entrance), c.Position);
            Assert.AreEqual(CustomerState.WalkingToSeat, c.State);
        }

        [Test]
        public void GuestsWalkTheStreetToTheDoor_ThenTakeASeat()
        {
            var sim = Sim(street: true);
            var c = sim.SpawnCustomer(Local);
            Assert.AreEqual(CustomerState.ArrivingOnStreet, c.State);
            Assert.Less(c.Position.X, 0f, "starts outside");
            Assert.IsTrue(c.IsOnStreet);

            Run(sim, 10f);
            Assert.IsFalse(c.IsOnStreet);
            Assert.IsTrue(c.State == CustomerState.WalkingToSeat || c.IsSeated, c.State.ToString());
        }

        [Test]
        public void TurnedAwayGuests_WalkBackDownTheStreet()
        {
            var sim = Sim(street: true, withSeat: false);
            sim.SpawnCustomer(Local); // takes the only queue spot
            var second = sim.SpawnCustomer(Local);
            bool turnedAway = false;
            sim.CustomerTurnedAway += c => turnedAway |= c == second;

            Run(sim, 10f);
            Assert.IsTrue(turnedAway);
            Assert.IsTrue(second.State == CustomerState.LeavingOnStreet || second.State == CustomerState.Left);
        }

        [Test]
        public void LeavingGuests_WalkOutAlongTheStreet_ThenAreGone()
        {
            var sim = Sim(street: true);
            var c = sim.SpawnCustomer(Local);
            bool left = false;
            sim.CustomerLeft += x => left |= x == c;
            sim.Despawn(c); // a renovation or the stress test: gone at once, wherever they are
            Assert.IsTrue(left);
        }
    }

    public class GardenTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 24, 8, 0, 0, DateTimeKind.Utc);
        private static readonly IngredientDef Chili = new IngredientDef("chili", "i", 10, 1, IngredientSource.Garden, 24);
        private static readonly IngredientDef Rice = new IngredientDef("rice", "i", 10, 1, IngredientSource.Market);

        private static EconomySettings Settings() => new EconomySettings
        {
            gardenPlotLevels = new[] { 3, 6, 9, 13, 18, 22, 26, 30, 34 }, wateringHours = 9, seedCost = 300,
        };

        private static Garden NewGarden() => new Garden(new[] { Chili, Rice }, Settings(), 3, new Random(1));

        [Test]
        public void FieldSitsBehindTheBackRightWall_NearTheBackCorner()
        {
            Assert.AreEqual(new GridPos(3, -9), Garden.FieldOrigin(15));
            Assert.AreEqual(new GridPos(0, -9), Garden.FieldOrigin(6), "narrow room: starts at the corner");
            Assert.AreEqual(new GridPos(5, -9), Garden.PlotOrigin(1, 15));
            Assert.AreEqual(new GridPos(3, -7), Garden.PlotOrigin(3, 15));
            Assert.AreEqual(5, Garden.PlotAt(new GridPos(7, -7), 15));
            Assert.AreEqual(-1, Garden.PlotAt(new GridPos(7, -2), 15), "the lawn between the fence and the wall");
            Assert.AreEqual(-1, Garden.PlotAt(new GridPos(13, -7), 15));
        }

        [Test]
        public void PlotsOpenAtTheSheetsLevels()
        {
            var g = NewGarden();
            Assert.AreEqual(PlotState.Locked, g.StateOf(0, 2, T0));
            Assert.AreEqual(PlotState.Empty, g.StateOf(0, 3, T0));
            Assert.AreEqual(PlotState.Locked, g.StateOf(1, 5, T0));
            Assert.AreEqual(PlotState.Empty, g.StateOf(2, 9, T0));
            Assert.IsNull(g.Plant(1, 5, T0), "locked plots can't be planted");
        }

        [Test]
        public void Planting_GrowsOnlyGardenCrops_AndWatersOnce()
        {
            var g = NewGarden();
            Assert.AreSame(Chili, g.Plant(0, 3, T0));
            Assert.AreEqual(PlotState.Growing, g.StateOf(0, 3, T0));
            Assert.AreEqual(TimeSpan.FromHours(9), g.WaterLeft(0, T0));
            Assert.IsNull(g.Plant(0, 3, T0), "already planted");
        }

        [Test]
        public void DryPlants_PauseInsteadOfDying()
        {
            var g = NewGarden();
            g.Plant(0, 3, T0);
            var later = T0.AddHours(20); // 9 h of water, then 11 h dry
            Assert.AreEqual(PlotState.Dry, g.StateOf(0, 3, later));
            Assert.AreEqual(9.0 / 24.0, g.Progress(0, later), 1e-9);
            Assert.AreEqual(TimeSpan.FromHours(15), g.TimeToHarvest(0, later));
        }

        [Test]
        public void Watering_ResumesGrowth_UntilReady_ThenHarvestEmptiesThePlot()
        {
            var g = NewGarden();
            g.Plant(0, 3, T0);
            var t = T0.AddHours(9);
            Assert.IsTrue(g.Water(0, 3, t));      // 9 h done
            t = t.AddHours(9);
            Assert.IsTrue(g.Water(0, 3, t));      // 18 h done
            t = t.AddHours(6);                    // 24 h done
            Assert.AreEqual(PlotState.Ready, g.StateOf(0, 3, t));
            Assert.IsFalse(g.Water(0, 3, t), "nothing to water when ready");

            Assert.AreSame(Chili, g.Harvest(0, 3, t));
            Assert.AreEqual(PlotState.Empty, g.StateOf(0, 3, t));
        }

        [Test]
        public void Watering_RefillsRatherThanStacks()
        {
            var g = NewGarden();
            g.Plant(0, 3, T0);
            g.Water(0, 3, T0.AddHours(1));
            Assert.AreEqual(TimeSpan.FromHours(9), g.WaterLeft(0, T0.AddHours(1)));
        }

        [Test]
        public void ClockSetBackwards_GrowsNothing()
        {
            var g = NewGarden();
            g.Plant(0, 3, T0);
            Assert.AreEqual(0.0, g.Progress(0, T0.AddHours(-5)));
        }

        [Test]
        public void SaveAndRestore_KeepsGrowthAndTimes()
        {
            var g = NewGarden();
            g.Plant(0, 3, T0);
            g.Water(0, 3, T0.AddHours(4));
            var saved = g.Capture();

            var loaded = NewGarden();
            loaded.Restore(saved);
            var later = T0.AddHours(10);
            Assert.AreSame(Chili, loaded.CropOf(0));
            Assert.AreEqual(g.Progress(0, later), loaded.Progress(0, later), 1e-9);
            Assert.AreEqual(g.StateOf(0, 3, later), loaded.StateOf(0, 3, later));
        }
    }

    public class GardenSessionTests
    {
        private DateTime _now = new DateTime(2026, 10, 24, 8, 0, 0, DateTimeKind.Utc);

        private RestaurantSession Session(long coins)
        {
            var chili = new IngredientDef("chili", "i", 10, 1, IngredientSource.Garden, 24);
            var pack = new ContentPack("test", TestContent.Dishes, new FurnitureDef[0], new CustomerTypeDef[0], null,
                new[] { TestContent.Tea, TestContent.Milk, chili }, new GameRules { GardenHarvest = 3 }).WithEconomy();
            pack.Economy.settings.gardenPlotLevels = new[] { 3, 6, 9 };
            pack.Economy.settings.wateringHours = 9;
            pack.Economy.settings.seedCost = 300;
            var s = new RestaurantSession(pack, new RestaurantLayout(6, 6, new GridPos(0, 3)), 1, coins, utcNow: () => _now);
            s.Progress.Restore(3, 0);
            return s;
        }

        [Test]
        public void SeedsCostCoins_AndHarvestFillsTheInventory()
        {
            var s = Session(coins: 500);
            Assert.AreEqual(RestaurantSession.GardenResult.Locked, s.BuySeed(1));
            Assert.AreEqual(RestaurantSession.GardenResult.Done, s.BuySeed(0));
            Assert.AreEqual(200, s.Wallet.DisplayCoins);
            Assert.AreEqual(RestaurantSession.GardenResult.NotEmpty, s.BuySeed(0));

            Assert.AreEqual(RestaurantSession.GardenResult.NotReady, s.HarvestPlot(0));
            s.Garden.DebugGrow(TimeSpan.FromHours(24), _now);
            var crop = s.Garden.CropOf(0);
            Assert.AreEqual(RestaurantSession.GardenResult.Done, s.HarvestPlot(0));
            Assert.AreEqual(3, s.Inventory.Count(crop));
        }

        [Test]
        public void NotEnoughCoins_PlantsNothing()
        {
            var s = Session(coins: 100);
            Assert.AreEqual(RestaurantSession.GardenResult.NotEnoughCoins, s.BuySeed(0));
            Assert.AreEqual(PlotState.Empty, s.Garden.StateOf(0, 3, _now));
        }

        [Test]
        public void GardenSurvivesSaveAndLoad_AndKeepsGrowingWhileClosed()
        {
            var s = Session(coins: 500);
            s.BuySeed(0);
            var save = s.CaptureSave();

            _now = _now.AddHours(9); // closed for 9 hours: fully watered the whole time
            var loaded = Session(coins: 0);
            loaded.ApplySave(save);
            Assert.AreEqual(9.0 / 24.0, loaded.Garden.Progress(0, _now), 1e-9);
        }
    }
}

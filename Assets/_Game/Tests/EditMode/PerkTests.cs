using System;
using System.Linq;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Progression;
using DemoRestaurant.Logic.Session;
using DemoRestaurant.Logic.Simulation;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    /// <summary>Perks (Phase 4C): timed staff items on one employee or the restaurant, as the reference game's snacks.</summary>
    public class PerkTests
    {
        private static readonly FurnitureDef Chair = new FurnitureDef("chair", "c", FurnitureKind.Chair, 1, 1, 0.8f, 0.4f, 1);
        private static readonly FurnitureDef Stove = new FurnitureDef("stove", "s", FurnitureKind.Stove, 1, 1, 1f, 0f, 1);
        private static readonly FurnitureDef Pass = new FurnitureDef("pass", "p", FurnitureKind.ServingCounter, 2, 1, 1f, 0f, 1);
        private static readonly StaffTypeDef Chef = new StaffTypeDef("chef", "staff.chef", StaffRole.Chef, 4f);
        private static readonly StaffTypeDef Waiter = new StaffTypeDef("waiter", "staff.waiter", StaffRole.Waiter, 4f);
        private static readonly CustomerTypeDef Local = new CustomerTypeDef("local", "c", 3f, 60f, 1, foodPatienceSeconds: 120f);
        private static readonly DateTime Start = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

        private DateTime _now;

        private static ItemDef Coffee() => new ItemDef("coffee", "i.coffee", 40, 30);

        private static ItemDef Speed() => new ItemDef("speed_snack", "i.speed", 0, 50)
        {
            Effects = new PerkEffects { WorkSpeedPercent = 20, EnergyDrainPercent = 100 },
            DurationMinutes = 30,
        };

        private static ItemDef Party() => new ItemDef("party", "i.party", 0, 80)
        {
            Target = ItemTarget.Restaurant,
            Effects = new PerkEffects { XpPercent = 100, PopularityPercent = 100 },
            DurationMinutes = 10,
        };

        [SetUp]
        public void SetUp() => _now = Start;

        private RestaurantSession Session(params ItemDef[] items)
        {
            var rules = TestContent.Rules();
            rules.StartingIngredients = TestContent.R(TestContent.Rice, TestContent.Coconut, TestContent.Anchovy, TestContent.Chili);
            rules.Perks = new PerkRules { Enabled = true, SlotsPerEmployee = 1, SlotsRestaurant = 1 };
            var pack = new ContentPack("test", TestContent.Dishes, new[] { Chair, Stove, Pass }, new[] { Local },
                new[] { Chef, Waiter }, TestContent.AllIngredients, rules, items).WithEconomy();
            var layout = new RestaurantLayout(12, 10, new GridPos(11, 9));
            layout.TryPlace(Stove, new GridPos(0, 0), Facing.SouthWest, out _);
            layout.TryPlace(Pass, new GridPos(2, 3), Facing.SouthWest, out _);
            for (int i = 0; i < 4; i++)
                layout.TryPlace(Chair, new GridPos(i * 2, 7), Facing.NorthEast, out _);
            layout.SetWaitingArea(new[] { new GridPos(11, 8), new GridPos(11, 7), new GridPos(11, 6) });
            var s = new RestaurantSession(pack, layout, seed: 5, startingCoins: 1000, utcNow: () => _now);
            foreach (var item in items) s.Items.Add(item, 3);
            return s;
        }

        private static void Run(RestaurantSession s, float seconds)
        {
            for (float t = 0; t < seconds; t += 0.05f) s.Tick(0.05f);
        }

        [Test]
        public void PerkBook_SlotsTargetsAndSwitch()
        {
            var rules = new PerkRules { SlotsPerEmployee = 1, SlotsRestaurant = 1 };
            var book = new PerkBook(() => rules);
            var speed = Speed();
            var party = Party();
            var staff = Session().Sim.Hire(Chef, new GridPos(5, 5));

            Assert.AreEqual(PerkCheck.WrongTarget, book.Check(speed, null, Start));
            Assert.AreEqual(PerkCheck.WrongTarget, book.Check(party, staff, Start));
            Assert.AreEqual(PerkCheck.Ok, book.Check(Coffee(), staff, Start), "no timed effect: nothing to check");
            book.Start(speed, staff, Start);
            Assert.AreEqual(PerkCheck.SlotsFull, book.Check(speed, staff, Start));
            Assert.AreEqual(PerkCheck.Ok, book.Check(party, null, Start), "the restaurant has its own slots");
            Assert.AreEqual(PerkCheck.Ok, book.Check(speed, staff, Start.AddMinutes(30)), "a slot frees when the perk ends");
            rules.Enabled = false;
            Assert.AreEqual(PerkCheck.Disabled, book.Check(party, null, Start));
        }

        [Test]
        public void PerkBook_StaffEffectsAddOwnAndRestaurantPerks_UntilTheyEnd()
        {
            var rules = new PerkRules { SlotsPerEmployee = 3, SlotsRestaurant = 3 };
            var book = new PerkBook(() => rules);
            var crew = Session().Sim;
            var a = crew.Hire(Chef, new GridPos(5, 5));
            var b = crew.Hire(Chef, new GridPos(6, 5));
            var allStaff = new ItemDef("rally", "i.rally", 0, 1)
            {
                Target = ItemTarget.Restaurant, Effects = new PerkEffects { WorkSpeedPercent = 10 }, DurationMinutes = 60,
            };
            book.Start(Speed(), a, Start);
            book.Start(Speed(), a, Start);
            book.Start(allStaff, null, Start);
            Assert.AreEqual(50, book.StaffTotal(a, Start, e => e.WorkSpeedPercent), "two copies add up, plus the restaurant's");
            Assert.AreEqual(10, book.StaffTotal(b, Start, e => e.WorkSpeedPercent));
            Assert.AreEqual(10, book.StaffTotal(a, Start.AddMinutes(45), e => e.WorkSpeedPercent));
            book.Prune(Start.AddMinutes(45));
            Assert.AreEqual(1, book.Active.Count);
            book.RemoveStaff(a);
            Assert.AreEqual(1, book.Active.Count, "restaurant perks stay");
        }

        [Test]
        public void SpeedAndDrainPerk_MakesTheEmployeeFaster_AndKeepsTheirEnergy()
        {
            var speed = Speed();
            var s = Session(speed);
            s.Hire(Chef, new GridPos(1, 1), out var chef, free: true);
            s.Sim.SetEnergy(chef, 90f, resting: false);
            float before = chef.SpeedMultiplier;

            Assert.IsTrue(s.UseItem(speed, chef));
            Assert.AreEqual(2, s.Items.Count(speed));
            Assert.AreEqual(before * 1.2f, chef.SpeedMultiplier, 0.001f);
            Run(s, 30f);
            Assert.AreEqual(90f, chef.Energy, 0.01f, "100% less drain");
            Assert.AreEqual(PerkCheck.SlotsFull, s.CheckPerk(speed, chef));
            Assert.IsFalse(s.UseItem(speed, chef), "one slot per employee in these rules");

            _now = Start.AddMinutes(31);
            Run(s, 1f);
            Assert.AreEqual(before, chef.SpeedMultiplier, 0.001f, "ended");
            Assert.Less(chef.Energy, 90f);
        }

        [Test]
        public void RestaurantPerk_DoublesXpAndPopularityPerServedGuest_AndIsLedgeredWithoutStaff()
        {
            var party = Party();
            var plain = Session(party);
            var boosted = Session(party);
            foreach (var s in new[] { plain, boosted })
            {
                s.Hire(Chef, new GridPos(1, 1), out _, free: true);
                s.Hire(Waiter, new GridPos(6, 5), out _, free: true);
            }
            Assert.IsFalse(boosted.UseItem(party, boosted.Sim.Staff[0]), "a restaurant item is not used on an employee");
            Assert.IsTrue(boosted.UseRestaurantItem(party));
            var use = boosted.ServerLog.Items.Last(a => a.type == "item_use");
            StringAssert.Contains("party", use.payload);
            StringAssert.DoesNotContain("staffId", use.payload);

            int plainXp = 0, boostedXp = 0, plainServed = 0, boostedServed = 0;
            plain.CustomerServed += (_, __, xp) => { plainXp += xp; plainServed++; };
            boosted.CustomerServed += (_, __, xp) => { boostedXp += xp; boostedServed++; };
            double plainDemand = plain.Demand.Value, boostedDemand = boosted.Demand.Value;
            Run(plain, 60f);
            Run(boosted, 60f);

            Assert.Greater(boostedServed, 0);
            Assert.AreEqual(boostedServed * 16, boostedXp, "8 XP per dish, doubled");
            Assert.AreEqual(plainServed * 8, plainXp);
            Assert.AreEqual(boostedServed * 2.0, boosted.Demand.Value - boostedDemand, 0.001, "+1 popularity per guest, doubled");
        }

        [Test]
        public void SaleWindow_LimitsBuying_AndPerksSurviveASaveAndLoad()
        {
            var speed = Speed();
            speed.SaleEndUtc = Start.AddDays(1);
            var s = Session(speed);
            Assert.IsTrue(s.CanBuyItem(speed));
            _now = Start.AddDays(2);
            Assert.IsFalse(s.CanBuyItem(speed), "the event is over");
            _now = Start;

            s.Hire(Chef, new GridPos(1, 1), out var chef, free: true);
            Assert.IsTrue(s.UseItem(speed, chef));
            var save = s.CaptureSave();
            Assert.AreEqual(1, save.perks.Count);

            var loaded = Session(speed);
            loaded.ApplySave(save);
            Assert.AreEqual(1, loaded.Perks.Active.Count);
            Assert.AreSame(loaded.Sim.Staff[save.perks[0].staffIndex], loaded.Perks.Active[0].Staff);
            Assert.AreEqual(Start.AddMinutes(30), loaded.Perks.Active[0].ExpiresUtc);
        }

        [Test]
        public void PerkOnlyItems_AreNotUsedByRefillAll_ButWorkAtFullEnergy()
        {
            var speed = Speed();
            var s = Session(speed);
            s.Hire(Chef, new GridPos(1, 1), out var chef, free: true);
            s.Sim.SetEnergy(chef, 100f, resting: false);
            Assert.AreEqual(0, s.RefillAll(speed));
            Assert.IsTrue(s.UseItem(speed, chef), "a perk starts even when energy is full");
            Assert.IsFalse(s.UseItem(Coffee(), chef), "plain energy at full energy");
        }
    }
}

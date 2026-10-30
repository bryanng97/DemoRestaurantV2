using DemoRestaurant.Logic.Online;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    /// <summary>Phase 5: the random street, a visit's help state, help results and the first-visit reward.</summary>
    public class StreetVisitTests
    {
        [Test]
        public void TheStreetKeepsTheServersOrder_AndSkipsEntriesWithoutAnId()
        {
            var street = StreetRestaurant.ParseStreet(JsonValue.Parse(@"{ ""items"": [
              { ""restaurantId"": ""a"", ""name"": ""Kedai A"", ""ownerName"": ""Ali"", ""level"": 4, ""popularity"": 12.5, ""activeRecently"": true },
              { ""name"": ""broken"" },
              { ""restaurantId"": ""b"", ""name"": ""Kedai B"", ""ownerName"": ""Bee"", ""level"": 0, ""popularity"": 3, ""activeRecently"": false } ] }"));
            Assert.AreEqual(2, street.Count);
            Assert.AreEqual("a", street[0].RestaurantId);
            Assert.AreEqual(12.5, street[0].Popularity, 1e-9);
            Assert.IsTrue(street[0].ActiveRecently);
            Assert.AreEqual(1, street[1].Level, "a level is at least 1");
        }

        [Test]
        public void VisitState_ReadsLitterPlotsAndRewards_AndOnlyPlantedGrowingPlotsCanBeWatered()
        {
            var state = VisitState.Parse(JsonValue.Parse(@"{ ""restaurantId"": ""r"", ""trash"": 3, ""coinsToday"": 4,
              ""rewards"": { ""trashCoinsMin"": 1, ""trashCoinsMax"": 2, ""trashXp"": 1, ""waterCoins"": 1, ""waterMinHours"": 3, ""dailyCoinCap"": 5 },
              ""garden"": { ""plots"": [
                { ""index"": 0, ""open"": true, ""crop"": { ""ingredientId"": ""rice"", ""watered"": false, ""ready"": false } },
                { ""index"": 1, ""open"": true, ""crop"": null },
                { ""index"": 2, ""open"": true, ""crop"": { ""ingredientId"": ""egg"", ""watered"": true, ""ready"": true } },
                { ""index"": 3, ""open"": false, ""crop"": null } ] } }"));
            Assert.AreEqual(3, state.Trash);
            Assert.AreEqual(5, state.DailyCoinCap);
            Assert.IsFalse(state.CapReached);
            Assert.AreEqual(4, state.Plots.Count);
            Assert.IsTrue(state.Plots[0].CanWater);
            Assert.IsFalse(state.Plots[1].CanWater, "empty");
            Assert.IsFalse(state.Plots[2].CanWater, "ready to harvest");
            Assert.IsFalse(state.Plots[3].CanWater, "locked");
            Assert.AreEqual("rice", state.Plots[0].IngredientId);
        }

        [Test]
        public void NoDailyCapMeansNull_AndHelpResultsFoldBackIntoTheState()
        {
            var state = VisitState.Parse(JsonValue.Parse(@"{ ""trash"": 2, ""coinsToday"": 0,
              ""rewards"": { ""dailyCoinCap"": null }, ""garden"": { ""plots"": [] } }"));
            Assert.IsNull(state.DailyCoinCap);
            Assert.IsFalse(state.CapReached);

            var litter = JsonValue.Parse(@"{ ""coins"": 105, ""reward"": 2, ""capped"": false, ""trashLeft"": 1 }");
            var result = HelpResult.Parse(litter);
            Assert.AreEqual(2, result.Reward);
            Assert.AreEqual(105, result.Coins);
            state.Apply(result, litter);
            Assert.AreEqual(1, state.Trash);
            Assert.AreEqual(2, state.CoinsToday);

            var water = JsonValue.Parse(@"{ ""coins"": 106, ""reward"": 1, ""addedHours"": 9,
              ""garden"": { ""plots"": [ { ""index"": 0, ""open"": true, ""crop"": { ""ingredientId"": ""rice"", ""watered"": true, ""ready"": false } } ] } }");
            state.Apply(HelpResult.Parse(water), water);
            Assert.AreEqual(1, state.Plots.Count);
            Assert.IsTrue(state.Plots[0].Watered);
            Assert.AreEqual(3, state.CoinsToday);
            Assert.AreEqual(1, state.Trash, "watering leaves the litter count alone");
        }

        [Test]
        public void FirstVisitReward_OnlyOnTheFirstVisitWithAnIngredient()
        {
            var first = FirstVisitReward.Parse(JsonValue.Parse(@"{ ""firstVisit"": true, ""reward"": { ""ingredientId"": ""rice"", ""quantity"": 1 } }"));
            Assert.AreEqual("rice", first.IngredientId);
            Assert.AreEqual(1, first.Quantity);
            Assert.IsNull(FirstVisitReward.Parse(JsonValue.Parse(@"{ ""firstVisit"": false, ""reward"": null }")));
            Assert.IsNull(FirstVisitReward.Parse(JsonValue.Parse(@"{ ""visitId"": ""v"" }")), "older servers send no reward");
        }
    }
}

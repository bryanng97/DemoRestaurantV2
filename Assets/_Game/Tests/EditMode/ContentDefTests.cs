using System;
using DemoRestaurant.Logic.Content;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    public class ContentDefTests
    {
        [Test]
        public void Dish_DefaultsToMainAtLevelOne()
        {
            var dish = new DishDef("x", "dish.x", 10, 4, 5);
            Assert.AreEqual(DishCategory.Main, dish.Category);
            Assert.AreEqual(1, dish.UnlockLevel);
        }

        [Test]
        public void Dish_KeepsCategoryAndClampsUnlockLevel()
        {
            var dish = new DishDef("x", "dish.x", 10, 4, 5, category: DishCategory.Dessert, unlockLevel: 0);
            Assert.AreEqual(DishCategory.Dessert, dish.Category);
            Assert.AreEqual(1, dish.UnlockLevel);
        }

        [TestCase(1, 10)]
        [TestCase(2, 8)]
        [TestCase(3, 6)]
        [TestCase(5, 2)]
        [TestCase(6, 1)]
        public void MarketWeight_FollowsRarity(int rarity, int weight) =>
            Assert.AreEqual(weight, IngredientDef.WeightForRarity(rarity));

        [Test]
        public void Ingredient_RarityMustBeOneToFive()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new IngredientDef("x", "i.x", 1, rarity: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new IngredientDef("x", "i.x", 1, rarity: 6));
        }

        [Test]
        public void GardenIngredient_NeedsGrowTime()
        {
            Assert.Throws<ContentException>(() => new IngredientDef("x", "i.x", 1, 1, IngredientSource.Garden, 0));
            var chili = new IngredientDef("chili", "i.chili", 10, 1, IngredientSource.Garden, 24);
            Assert.AreEqual(IngredientSource.Garden, chili.Source);
            Assert.AreEqual(24, chili.GrowHours);
        }
    }
}

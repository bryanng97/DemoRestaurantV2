using DemoRestaurant.Logic.Progression;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Session;
using DemoRestaurant.Logic.Economy;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    public sealed class RecipeUpgradeTests
    {
        [Test] public void LearningAndEachUpgradeConsumeTheCatalogRecipe()
        {
            var book = new RecipeBook(TestContent.Dishes);
            var inv = new Inventory();
            var dish = TestContent.NasiLemak;
            foreach (var a in dish.Recipe) inv.Add(a.Ingredient,a.Count * 4);
            int discoveries = 0, upgrades = 0;
            book.Discovered += _ => discoveries++;
            book.Upgraded += (_, level) => { upgrades++; Assert.AreEqual(2,level); };
            Assert.AreEqual(RecipeTrainResult.Learned,book.Train(dish,inv,10,10,1));
            Assert.AreEqual(1,book.LevelOf(dish));
            Assert.AreEqual(RecipeTrainResult.Upgraded,book.Train(dish,inv,10,10,2));
            Assert.AreEqual(2,book.LevelOf(dish));
            foreach (var a in dish.Recipe) Assert.AreEqual(a.Count,inv.Count(a.Ingredient));
            Assert.AreEqual(1,discoveries); Assert.AreEqual(1,upgrades);
        }
        [Test] public void DebugShortcuts_GiveEveryIngredient_AndLearnEveryRecipe()
        {
            var pack = new ContentPack("test",TestContent.Dishes,new FurnitureDef[0],
                new[] { new CustomerTypeDef("local","c",3,60,1) },new StaffTypeDef[0],TestContent.AllIngredients,TestContent.Rules()).WithEconomy();
            var session = new RestaurantSession(pack,new RestaurantLayout(12,10,new GridPos(11,9)),7);
            var first = TestContent.AllIngredients[0];
            int had = session.Inventory.Count(first);
            session.DebugAddIngredients(10);
            Assert.AreEqual(had + 10,session.Inventory.Count(first));
            int unknown = 0;
            foreach (var d in session.Recipes.AllDishes) if (!session.Recipes.IsKnown(d)) unknown++;
            Assert.Greater(unknown,0);
            Assert.AreEqual(unknown,session.DebugLearnAllRecipes());
            foreach (var d in session.Recipes.AllDishes)
            {
                Assert.IsTrue(session.Recipes.IsKnown(d),d.Id);
                Assert.AreEqual(1,session.Recipes.LevelOf(d));
            }
            Assert.AreEqual(0,session.DebugLearnAllRecipes());
        }
        [Test] public void LockedMissingAndMaxLevelDoNotConsumeAnything()
        {
            var book = new RecipeBook(TestContent.Dishes); var inv = new Inventory(); var d = TestContent.NasiLemak;
            Assert.AreEqual(RecipeTrainResult.NotEnoughIngredients,book.Train(d,inv,10,10,1));
            foreach (var a in d.Recipe) inv.Add(a.Ingredient,100);
            Assert.AreEqual(RecipeTrainResult.LevelTooLow,book.Train(d,inv,0,10,1));
            book.Train(d,inv,10,10,1); book.RestoreLevel(d,999,10);
            int before = inv.Count(d.Recipe[0].Ingredient);
            Assert.AreEqual(RecipeTrainResult.MaxLevel,book.Train(d,inv,10,10,1));
            Assert.AreEqual(before,inv.Count(d.Recipe[0].Ingredient)); Assert.AreEqual(10,book.LevelOf(d));
        }
        [Test] public void SessionSavesLevelsAndAuthorityUsesWorkbookPrices()
        {
            var pack = new ContentPack("test",TestContent.Dishes,new FurnitureDef[0],
                new[] { new CustomerTypeDef("local","c",3,60,1) },new StaffTypeDef[0],TestContent.AllIngredients,TestContent.Rules()).WithEconomy();
            RestaurantSession Create() => new RestaurantSession(pack,new RestaurantLayout(12,10,new GridPos(11,9)),7);
            var session = Create(); var d = TestContent.TehTarik;
            foreach (var a in d.Recipe) session.Inventory.Add(a.Ingredient,10);
            int before = session.Progress.Xp;
            Assert.AreEqual(RecipeTrainResult.Upgraded,session.TrainRecipe(d));
            Assert.AreEqual(2,session.Recipes.LevelOf(d));
            Assert.AreEqual(before + session.Progress.XpForRecipeLevel(2),session.Progress.Xp);
            Assert.AreEqual(EconomyFormulas.DishPrice(pack.Economy.settings,d.Price,2),session.PriceForDish(d));
            var reload = Create(); reload.ApplySave(session.CaptureSave());
            Assert.AreEqual(2,reload.Recipes.LevelOf(d));
            SaleResult settled = default;
            reload.Economy.SaleSettled += sale => settled = sale;
            reload.Economy.SubmitSale(new SaleReport(100,d.Id,999999));
            reload.TickEconomy(1);
            Assert.AreEqual(reload.PriceForDish(d),settled.AwardedCoins,"The client claim must not set the price.");
            var old = session.CaptureSave(); old.version = 4; old.recipeLevels = null;
            var legacy = Create(); legacy.ApplySave(old); Assert.AreEqual(1,legacy.Recipes.LevelOf(d));
        }
    }
}

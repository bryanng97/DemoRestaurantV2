using DemoRestaurant.Game.Content;
using DemoRestaurant.Logic.Online;
using NUnit.Framework;

namespace DemoRestaurant.Render.Tests
{
    public sealed class CatalogControlsPresentationTests
    {
        [Test]
        public void NumericRecipeUpdatesPreserveBundledTranslationsWithoutExplicitNameOverrides()
        {
            string before=Loc.Language;
            try
            {
                Loc.ApplyCatalog(JsonValue.Object());Loc.SetLanguage("zh");
                string chinese=Loc.Get("dish.roti_canai");
                var config=JsonValue.Parse(@"{""body"":{""economy"":{""dishes"":[{""id"":""roti_canai"",""name"":""Updated Roti"",""cookSeconds"":8}]}}}");
                Loc.ApplyCatalog(config);
                Assert.AreEqual(chinese,Loc.Get("catalog.recipe.roti_canai"));
                Loc.SetLanguage("en");Assert.AreEqual("Updated Roti",Loc.Get("catalog.recipe.roti_canai"));
            }
            finally {Loc.ApplyCatalog(JsonValue.Object());Loc.SetLanguage(before);}
        }

        [Test]
        public void BuildShopRendersAndRefreshesAnAdminCreatedCategoryOutsideTheEnum()
        {
            var content=ContentLoader.Load("malaysia");
            var config=JsonValue.Parse(@"{""rulesetId"":""malaysia"",""version"":3100,""body"":{""catalog"":{""furniture"":[],
              ""configuration"":{""categories"":[{""code"":""Premium"",""active"":true,""sortOrder"":1,
                ""names"":{""en"":""Premium Shop"",""ms"":""Istimewa"",""zh"":""高级""}}]}}}}");
            ServerCatalog.FromConfig(config).ApplyFurniture(content.Pack);
            var host=new UnityEngine.GameObject("Managed category test");
            string before=Loc.Language;
            try
            {
                Loc.ApplyCatalog(config);Loc.SetLanguage("en");
                var panel=host.AddComponent<DemoRestaurant.Game.UI.RestaurantEditorPanel>();
                var root=new UnityEngine.GameObject("Categories",typeof(UnityEngine.RectTransform)).GetComponent<UnityEngine.RectTransform>();
                root.SetParent(host.transform,false);
                var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
                var type=typeof(DemoRestaurant.Game.UI.RestaurantEditorPanel);
                type.GetField("_content",flags).SetValue(panel,content);
                type.GetField("_categoryButtonsRoot",flags).SetValue(panel,root);
                type.GetMethod("RebuildCategoryTabs",flags).Invoke(panel,null);
                bool found=false;
                foreach(var text in root.GetComponentsInChildren<UnityEngine.UI.Text>()) if(text.text=="Premium Shop") found=true;
                Assert.IsTrue(found,"New categories must not require a BuildCategory enum value.");
                config.Set("version",3101);
                config["body"]["catalog"]["configuration"]["categories"].Items[0].Set("active",false);
                ServerCatalog.FromConfig(config).ApplyFurniture(content.Pack);
                type.GetMethod("RebuildCategoryTabs",flags).Invoke(panel,null);
                found=false;
                foreach(var text in root.GetComponentsInChildren<UnityEngine.UI.Text>()) if(text.text=="Premium Shop") found=true;
                Assert.IsFalse(found,"Retirement must remove the category from the active Shop tabs.");
            }
            finally {UnityEngine.Object.DestroyImmediate(host);Loc.ApplyCatalog(JsonValue.Object());Loc.SetLanguage(before);}
        }

        [Test]
        public void ManagedRecipeArtworkAndCategoryNamesReuseSupportedContentAndLocalize()
        {
            var content = ContentLoader.Load("malaysia");
            var config = JsonValue.Parse(@"{""rulesetId"":""malaysia"",""version"":3002,""body"":{
              ""catalog"":{""furniture"":[],""configuration"":{""categories"":[{""code"":""Premium"",""active"":true,""sortOrder"":1,
                ""names"":{""en"":""Premium"",""ms"":""Istimewa"",""zh"":""高级""}}]}},
              ""economy"":{""dishes"":[{""id"":""premium_roti"",""assetKey"":""roti_canai"",""name"":""Premium Roti"",
                ""category"":""starter"",""basePrice"":20,""cookSeconds"":8,""eatSeconds"":6,""unlockLevel"":2,""enabled"":true,
                ""ingredients"":[""flour"",""egg""],""localizedNames"":{""en"":""Premium Roti"",""ms"":""Roti Istimewa"",""zh"":""高级印度煎饼""}}]}}}");
            ServerCatalog.FromConfig(config).ApplyFurniture(content.Pack);
            Assert.AreEqual(content.IconForDish("roti_canai"),content.IconForDish("premium_roti"));
            Assert.AreEqual(content.ServingForDish("roti_canai"),content.ServingForDish("premium_roti"));
            Assert.AreEqual(8,content.Pack.GetDish("premium_roti").CookSeconds);
            string before=Loc.Language;
            try
            {
                Loc.ApplyCatalog(config);
                foreach(var pair in new[] {new[] {"en","Premium","Premium Roti"},new[] {"ms","Istimewa","Roti Istimewa"},new[] {"zh","高级","高级印度煎饼"}})
                {
                    Loc.SetLanguage(pair[0]);
                    Assert.AreEqual(pair[1],Loc.Get("catalog.category.Premium"));
                    Assert.AreEqual(pair[2],Loc.Get(content.Pack.GetDish("premium_roti").NameKey));
                }
            }
            finally {Loc.ApplyCatalog(JsonValue.Object());Loc.SetLanguage(before);}
        }

        private static JsonValue Config() => JsonValue.Parse(
            "{\"rulesetId\":\"malaysia\",\"version\":3001,\"body\":{\"catalog\":{\"furniture\":[" +
            "{\"id\":\"premium_stove\",\"assetKey\":\"gas_stove\",\"kind\":\"Stove\",\"placement\":\"floor\",\"sizeX\":1,\"sizeY\":1,\"price\":250," +
            "\"localizedNames\":{\"en\":\"Premium Stove\",\"ms\":\"Dapur Premium\",\"zh\":\"高级炉具\"}}]}}}");

        [Test]
        public void PublishedVariantReusesTheActualClientFurnitureArtwork()
        {
            var content = ContentLoader.Load("malaysia");
            ServerCatalog.FromConfig(Config()).ApplyFurniture(content.Pack);
            Assert.IsTrue(content.Pack.HasFurniture("premium_stove"));
            Assert.AreSame(content.ArtForFurniture("gas_stove"), content.ArtForFurniture("premium_stove"));
            Assert.AreEqual(250, content.Pack.GetFurniture("premium_stove").Price);
        }

        [Test]
        public void PublishedNamesFollowTheSelectedGameLanguage()
        {
            string before = Loc.Language;
            try
            {
                Loc.ApplyCatalog(Config());
                foreach (var pair in new[] { new[] { "en", "Premium Stove" }, new[] { "ms", "Dapur Premium" }, new[] { "zh", "高级炉具" } })
                {
                    Loc.SetLanguage(pair[0]);
                    Assert.AreEqual(pair[1], Loc.Get("catalog.furniture.premium_stove"));
                }
            }
            finally { Loc.ApplyCatalog(JsonValue.Object()); Loc.SetLanguage(before); }
        }
    }
}

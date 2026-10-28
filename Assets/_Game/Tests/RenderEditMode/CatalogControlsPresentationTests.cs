using DemoRestaurant.Game.Content;
using DemoRestaurant.Logic.Online;
using NUnit.Framework;

namespace DemoRestaurant.Render.Tests
{
    public sealed class CatalogControlsPresentationTests
    {
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

using System.Collections.Generic;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace DemoRestaurant.Game.UI
{
    /// <summary>
    /// The recipe book: every dish with the ingredients it needs. Learning a dish or upgrading it uses one
    /// ingredient set per level (no experimenting; the list shows exactly what each recipe takes).
    /// </summary>
    public sealed class RecipePanel : SlidePanel
    {
        private readonly List<BookEntry> _book = new List<BookEntry>();
        private Text _hint, _bookLabel;

        protected override string TitleKey => "ui.recipes.title";

        protected override void BuildContent(RectTransform content)
        {
            _hint = UiKit.Label(content, "Hint", 0, 0, 780, 56, 22, TextAnchor.UpperLeft, UiKit.SoftText, wrap: true);
            _bookLabel = UiKit.Label(content, "BookLabel", 0, 60, 780, 32, 24, TextAnchor.MiddleLeft, UiKit.DarkText);
            var dishes = Ui.Session.Recipes.AllDishes;
            var viewport = UiKit.Place(UiKit.NewRect("RecipeBookViewport", content), 0, 96, 780, 720);
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = new Color(1,1,1,.01f);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            var book = UiKit.NewRect("RecipeBookRows", viewport);
            book.anchorMin = book.anchorMax = book.pivot = new Vector2(0,1);
            book.sizeDelta = new Vector2(780, ((dishes.Count + 1) / 2) * 220);
            scroll.viewport = viewport; scroll.content = book; scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            for (int i = 0; i < dishes.Count; i++)
                _book.Add(BuildBookEntry(book, dishes[i], i % 2 * 390, i / 2 * 220, 380, 210));

            Ui.Session.Recipes.Discovered += _ => Refresh();
        }

        protected override void Refresh()
        {
            if (_hint == null)
                return;
            _hint.text = Loc.Get("ui.recipes.hint");
            _bookLabel.text = $"{Loc.Get("ui.recipes.book")}  {Ui.Session.Recipes.Known.Count}/{Ui.Session.Recipes.AllDishes.Count}";

            foreach (var entry in _book)
            {
                bool known = Ui.Session.Recipes.IsKnown(entry.Dish);
                entry.Icon.enabled = entry.Icon.sprite != null;
                entry.Name.text = Loc.Get(entry.Dish.NameKey);
                int level = Ui.Session.Recipes.LevelOf(entry.Dish);
                var amounts = new List<string>();
                foreach (var a in entry.Dish.Recipe) amounts.Add(Loc.Get(a.Ingredient.NameKey) + " " +
                    Ui.Session.Inventory.Count(a.Ingredient) + "/" + (a.Count * Mathf.Max(1,Ui.Session.Settings.ingredientSetsPerUpgrade)));
                entry.Info.text = Loc.Format("recipe.level", level) + " · " + Ui.Session.PriceForDish(entry.Dish) + "\n" + string.Join("  ", amounts);
                bool max = level >= Ui.Session.Settings.maxDishLevel;
                entry.TrainLabel.text = Loc.Get(max ? "recipe.max" : known ? "recipe.upgrade" : "recipe.learn");
                entry.Train.interactable = !max && entry.Dish.UnlockLevel <= Ui.Session.Progress.Level;
                entry.Fill.color = known ? UiKit.GoldFill : UiKit.CardFill;
            }
        }

        private void Train(DishDef dish)
        {
            var result = Ui.Session.TrainRecipe(dish);
            string key = result == RecipeTrainResult.Learned ? "recipe.learned" :
                result == RecipeTrainResult.Upgraded ? "recipe.upgraded" :
                result == RecipeTrainResult.NotEnoughIngredients ? "ui.recipes.missing" :
                result == RecipeTrainResult.MaxLevel ? "recipe.max" : "ui.recipes.level_too_low";
            Ui.Toast(Loc.Format(key, Loc.Get(dish.NameKey), Ui.Session.Recipes.LevelOf(dish)));
            Refresh();
        }

        private BookEntry BuildBookEntry(Transform parent, DishDef dish, float x, float y, float w, float h)
        {
            var entry = new BookEntry { Dish = dish };
            var rect = UiKit.Box(parent, dish.Id, UiKit.CardRim, UiKit.CardFill, 3f, out _, out entry.Fill);
            UiKit.Place(rect, x, y, w, h);
            entry.Icon = UiKit.AddImage(UiKit.Place(UiKit.NewRect("Icon", rect), 8, 8, 74, 74), Ui.IconFor(dish));
            entry.Name = UiKit.Label(rect, "Name", 90, 6, w - 100, 40, 21, TextAnchor.MiddleLeft, UiKit.DarkText, wrap: true);
            entry.Info = UiKit.Label(rect, "Info", 90, 50, w - 100, 100, 18, TextAnchor.UpperLeft, UiKit.SoftText, wrap: true);
            entry.Train = UiKit.TextButton(rect, "TrainRecipe", UiKit.Green, 23, () => Train(dish), out entry.TrainLabel);
            UiKit.Place((RectTransform)entry.Train.transform, 12, 156, w - 24, 44);
            return entry;
        }

        private sealed class BookEntry
        {
            public DishDef Dish;
            public Image Icon, Fill;
            public Text Name, Info;
            public Button Train;
            public Text TrainLabel;
        }
    }
}

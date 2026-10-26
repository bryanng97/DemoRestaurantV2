using System.Collections.Generic;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Menu;
using UnityEngine;
using UnityEngine.UI;

namespace DemoRestaurant.Game.UI
{
    /// <summary>
    /// Pick which known dishes to serve today (limited slots). Rules live in <see cref="RestaurantMenu"/>;
    /// this only shows them and forwards taps. New dishes appear here once discovered in the recipe lab.
    /// </summary>
    public sealed class MenuPanel : SlidePanel
    {
        private readonly List<Card> _cards = new List<Card>();
        private RectTransform _grid;
        private Text _count, _hint;

        protected override string TitleKey => "ui.menu.title";
        private RestaurantMenu Menu => Ui.Session.Menu;

        protected override void BuildContent(RectTransform content)
        {
            _count = UiKit.Label(content, "Count", 0, -86, Width - 170, 50, 32, TextAnchor.UpperRight, UiKit.DarkText);
            _hint = UiKit.Label(content, "Hint", 6, 0, Width - 72, 64, 24, TextAnchor.UpperLeft, UiKit.SoftText, wrap: true);

            _grid = UiKit.NewRect("Dishes", content);
            UiKit.Stretch(_grid, 0, 74, 0, 0);
            var layout = _grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(240f, 236f);
            layout.spacing = new Vector2(20f, 20f);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 3;
            layout.childAlignment = TextAnchor.UpperCenter;

            Menu.Changed += Refresh;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            // Session can be gone already (scripts recompiled during Play mode).
            if (Ui != null && Ui.Session != null) Menu.Changed -= Refresh;
        }

        protected override void Refresh()
        {
            if (_grid == null)
                return;
            while (_cards.Count < Menu.Available.Count)
                _cards.Add(BuildCard(Menu.Available[_cards.Count]));

            _count.text = Loc.Format("ui.menu.count", Menu.Active.Count, Menu.Capacity);
            _hint.text = SlotSummary();
            foreach (var card in _cards)
            {
                bool on = Menu.IsActive(card.Dish);
                card.Rim.color = on ? UiKit.Gold : UiKit.CardRim;
                card.Fill.color = on ? UiKit.GoldFill : UiKit.CardFill;
                card.Badge.SetActive(on);
                card.BadgeText.text = Loc.Get("ui.menu.on");
                card.Name.text = Loc.Get(card.Dish.NameKey);
                card.Prep.text = Loc.Format("ui.menu.prep_time", card.Dish.CookSeconds.ToString("0.#"));
                card.Price.text = Ui.Session.PriceForDish(card.Dish).ToString();
                // Unselected dishes look disabled while their category is full (still tappable, to explain why).
                card.Group.alpha = !on && !Menu.HasRoomFor(card.Dish) ? 0.55f : 1f;
            }
        }

        /// <summary>"Starters 1/1 · Mains 1/1 · Desserts 0/1": only categories open at this level.</summary>
        private string SlotSummary()
        {
            if (!Menu.HasCategories)
                return Loc.Format("ui.menu.hint", Menu.Capacity);
            var parts = new System.Collections.Generic.List<string>();
            foreach (var c in RestaurantMenu.Categories)
            {
                int slots = Menu.SlotsFor(c);
                if (slots > 0)
                    parts.Add(Loc.Format("ui.menu.slots", Loc.Get("ui.menu.cat." + c), Menu.ActiveCount(c), slots));
            }
            return string.Join("  ·  ", parts);
        }

        private void OnCardTapped(DishDef dish)
        {
            switch (Menu.Toggle(dish))
            {
                case MenuChange.MenuFull:
                    Ui.Toast(Menu.SlotsFor(dish.Category) == 0
                        ? Loc.Get("ui.menu.category_locked")
                        : Loc.Format("ui.menu.full", Loc.Get("ui.menu.cat." + dish.Category)));
                    break;
                case MenuChange.LastDish: Ui.Toast(Loc.Get("ui.menu.last")); break;
            }
        }

        private Card BuildCard(DishDef dish)
        {
            var card = new Card { Dish = dish };
            var rect = UiKit.Box(_grid, dish.Id, UiKit.CardRim, UiKit.CardFill, 5f, out card.Rim, out card.Fill);
            card.Group = rect.gameObject.AddComponent<CanvasGroup>();
            UiKit.AddButton(rect, () => OnCardTapped(dish));

            UiKit.AddImage(UiKit.Place(UiKit.NewRect("Icon", rect), 55, 14, 130, 96), Ui.IconFor(dish));
            card.Name = UiKit.Label(rect, "Name", 10, 114, 220, 40, 26, TextAnchor.MiddleCenter, UiKit.DarkText);
            card.Name.resizeTextForBestFit = true;
            card.Name.resizeTextMinSize = 16;
            card.Name.resizeTextMaxSize = 26;
            UiKit.Label(rect, "Stars", 10, 150, 220, 26, 22, TextAnchor.MiddleCenter, UiKit.Gold).text = Stars(dish.Stars);

            UiKit.AddImage(UiKit.Place(UiKit.NewRect("Coin", rect), 18, 184, 34, 34), Ui.Coin);
            card.Price = UiKit.Label(rect, "Price", 56, 184, 70, 34, 26, TextAnchor.MiddleLeft, UiKit.DarkText);
            card.Price.text = Ui.Session.PriceForDish(dish).ToString();
            card.Prep = UiKit.Label(rect, "Prep", 108, 184, 116, 34, 21, TextAnchor.MiddleRight, UiKit.SoftText);

            var badge = UiKit.Box(rect, "OnMenu", UiKit.Green, UiKit.Green, 0f);
            badge.anchorMin = badge.anchorMax = badge.pivot = new Vector2(1f, 1f);
            badge.anchoredPosition = new Vector2(6f, 8f);
            badge.sizeDelta = new Vector2(130f, 40f);
            card.Badge = badge.gameObject;
            card.BadgeText = UiKit.AddText(UiKit.NewRect("Label", badge), 20, TextAnchor.MiddleCenter, Color.white);
            UiKit.Stretch(card.BadgeText.rectTransform, 4, 0, 4, 0);
            return card;
        }

        /// <summary>Star rating as text ("★★☆"); falls back to the OS font for the glyph.</summary>
        public static string Stars(int stars)
        {
            var s = new System.Text.StringBuilder();
            for (int i = 0; i < 5; i++)
                s.Append(i < stars ? '★' : '☆');
            return s.ToString();
        }

        private sealed class Card
        {
            public DishDef Dish;
            public Image Rim, Fill;
            public CanvasGroup Group;
            public Text Name, Prep, Price, BadgeText;
            public GameObject Badge;
        }
    }
}

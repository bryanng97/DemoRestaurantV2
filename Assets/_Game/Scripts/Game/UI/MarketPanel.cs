using System.Collections.Generic;
using System.Text;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Logic.Content;
using UnityEngine;
using UnityEngine.UI;

namespace DemoRestaurant.Game.UI
{
    /// <summary>
    /// Market: free ingredient baskets that restock on a timer. Collect them all with one tap.
    /// Shows the ingredient stock below.
    /// </summary>
    public sealed class MarketPanel : SlidePanel
    {
        private readonly List<Image> _baskets = new List<Image>();
        private readonly List<Text> _basketLabels = new List<Text>();
        private IngredientGrid _grid;
        private Button _collect;
        private Text _collectLabel, _timer, _last, _inventoryLabel;

        protected override string TitleKey => "ui.market.title";

        protected override void BuildContent(RectTransform content)
        {
            var market = Ui.Session.Market;
            int baskets = market != null ? market.Capacity : 0;
            for (int i = 0; i < baskets; i++)
            {
                var rect = UiKit.Box(content, "Basket" + i, UiKit.WoodLight, UiKit.CardFill, 4f, out _, out var fill);
                UiKit.Place(rect, i * 130, 0, 118, 118);
                _baskets.Add(fill);
                _basketLabels.Add(UiKit.Label(rect, "Label", 4, 4, 110, 110, 22, TextAnchor.MiddleCenter, UiKit.SoftText, wrap: true));
            }

            _collect = UiKit.TextButton(content, "Collect", UiKit.Green, 30, Collect, out _collectLabel);
            UiKit.Place((RectTransform)_collect.transform, 540, 0, 240, 70);
            _timer = UiKit.Label(content, "Timer", 540, 78, 240, 40, 21, TextAnchor.MiddleCenter, UiKit.SoftText, wrap: true);

            _last = UiKit.Label(content, "Last", 0, 134, 780, 64, 22, TextAnchor.UpperLeft, UiKit.DarkText, wrap: true);
            _inventoryLabel = UiKit.Label(content, "InventoryLabel", 0, 206, 780, 32, 24, TextAnchor.MiddleLeft, UiKit.DarkText);
            _grid = new IngredientGrid(content, 0, 244, 780, Ui.Session.Content.Ingredients, Ui.Content, null, columns: 6, cellHeight: 120f);

            if (market != null)
                market.Changed += Refresh;
        }

        protected override void Refresh()
        {
            if (_grid == null)
                return;
            var market = Ui.Session.Market;
            _inventoryLabel.text = Loc.Get("ui.market.inventory");
            _grid.Refresh(Ui.Session.Inventory);
            if (market == null)
                return;

            for (int i = 0; i < _baskets.Count; i++)
            {
                bool ready = i < market.ReadyBaskets;
                _baskets[i].color = ready ? UiKit.GoldFill : new Color(0.9f, 0.88f, 0.85f);
                _basketLabels[i].text = ready ? "?" : Loc.Get("ui.market.empty_basket");
                _basketLabels[i].fontSize = ready ? 56 : 22;
                _basketLabels[i].color = ready ? UiKit.Gold : UiKit.Disabled;
            }

            bool any = market.ReadyBaskets > 0;
            _collect.interactable = any;
            UiKit.SetButtonColor(_collect, any ? UiKit.Green : UiKit.Disabled);
            _collectLabel.text = Loc.Format("ui.market.collect", market.ReadyBaskets);
            _timer.text = market.ReadyBaskets >= market.Capacity
                ? Loc.Get("ui.market.full")
                : Loc.Format("ui.market.next", Mathf.CeilToInt(market.NextRestockIn));
        }

        private void Collect()
        {
            var got = Ui.Session.CollectMarket();
            if (got.Count > 0)
                _last.text = Loc.Format("ui.market.got", Describe(got));
            Refresh();
        }

        /// <summary>"Rice x2, Chili" style summary.</summary>
        public static string Describe(IReadOnlyList<IngredientDef> items)
        {
            var counts = new Dictionary<IngredientDef, int>();
            var order = new List<IngredientDef>();
            foreach (var i in items)
            {
                if (!counts.ContainsKey(i)) { counts[i] = 0; order.Add(i); }
                counts[i]++;
            }
            var sb = new StringBuilder();
            foreach (var i in order)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(Loc.Get(i.NameKey));
                if (counts[i] > 1) sb.Append(" x").Append(counts[i]);
            }
            return sb.ToString();
        }
    }
}

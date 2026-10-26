using System;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Logic.Economy;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Session;
using UnityEngine;
using UnityEngine.UI;

namespace DemoRestaurant.Game.UI
{
    /// <summary>
    /// Opened from the for-sale sign in front of the restaurant: the next terrace size, the level it needs and its
    /// price, with Buy. The server checks everything again; the enlarged layout is saved before coins are taken.
    /// </summary>
    public sealed class TerraceSaleDialog
    {
        private readonly GameObject _root;
        private readonly Text _title, _size, _need, _price, _buyLabel;
        private readonly Button _buy;
        private readonly RestaurantSession _session;
        private readonly Func<LayoutSnapshot, long, bool> _persist;
        private readonly Action<string> _toast;

        public TerraceSaleDialog(RectTransform canvas, RestaurantSession session, Sprite coin,
            Func<LayoutSnapshot, long, bool> persist, Action<string> toast)
        {
            _session = session; _persist = persist; _toast = toast;
            var card = UiKit.Modal(canvas, "TerraceSale", new Vector2(640f, 470f), out _root);
            _title = UiKit.Label(card, "Title", 0, 26, 640, 56, 40, TextAnchor.MiddleCenter, UiKit.DarkText);
            _size = UiKit.Label(card, "Size", 40, 96, 560, 50, 32, TextAnchor.MiddleCenter, UiKit.DarkText);
            _need = UiKit.Label(card, "Need", 40, 150, 560, 40, 24, TextAnchor.MiddleCenter, UiKit.SoftText);
            var priceBox = UiKit.Box(card, "Price", UiKit.Gold, UiKit.GoldFill, 4f);
            UiKit.Place(priceBox, 150, 206, 340, 80);
            UiKit.AddImage(UiKit.Place(UiKit.NewRect("Coin", priceBox), 18, 12, 56, 56), coin);
            _price = UiKit.Label(priceBox, "Amount", 84, 0, 240, 80, 38, TextAnchor.MiddleLeft, UiKit.DarkText);
            _buy = UiKit.TextButton(card, "Buy", UiKit.Green, 32, Buy, out _buyLabel);
            UiKit.Place((RectTransform)_buy.transform, 170, 330, 300, 80);
            UiKit.CloseButton(card, Hide);
        }

        public bool IsShowing => _root.activeSelf;

        public void Show()
        {
            var next = _session.NextTerrace;
            if (next == null) return;
            _title.text = Loc.Get("ui.terrace.title");
            _size.text = Loc.Format("ui.terrace.size", next.width, next.depth);
            bool levelOk = _session.Progress.Level >= next.level;
            _need.text = levelOk ? Loc.Get("ui.terrace.hint") : Loc.Format("ui.terrace.need_level", next.level);
            _need.color = levelOk ? UiKit.SoftText : UiKit.Red;
            _price.text = next.price.ToString("N0");
            _buyLabel.text = Loc.Get("ui.terrace.buy");
            _buy.interactable = levelOk;
            _root.transform.SetAsLastSibling();
            _root.SetActive(true);
        }

        public void Hide() => _root.SetActive(false);

        private void Buy()
        {
            var result = _session.BuyNextTerrace(_persist);
            _toast(Loc.Get(result == LandPurchaseResult.Success ? "ui.terrace.bought"
                : result == LandPurchaseResult.InsufficientCoins ? "ui.terrace.no_coins"
                : result == LandPurchaseResult.SaveFailed ? "ui.build.save_failed" : "ui.terrace.unavailable"));
            if (result == LandPurchaseResult.Success) Hide();
        }
    }
}

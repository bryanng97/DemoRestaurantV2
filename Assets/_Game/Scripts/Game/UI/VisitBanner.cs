using System;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Logic.Online;
using UnityEngine;
using UnityEngine.UI;

namespace DemoRestaurant.Game.UI
{
    /// <summary>
    /// Shown while visiting another restaurant (the reference game's visit banner): whose restaurant this is, their
    /// level and popularity, and a button home. It replaces the HUD, so nothing here can change the visited room.
    /// </summary>
    public sealed class VisitBanner : MonoBehaviour
    {
        private Text _title, _info, _homeLabel, _message;
        private float _messageTime;
        private StreetRestaurant _shop;

        public void Bind(StreetRestaurant shop, Action goHome)
        {
            _shop = shop;
            var canvas = UiKit.CreateCanvas(transform, "VisitCanvas", 40);
            var safe = SafeArea.Create(canvas);

            var card = UiKit.Box(safe, "VisitCard", UiKit.CardRim, UiKit.Cream, 4);
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(.5f, 1);
            card.anchoredPosition = new Vector2(0, -20); card.sizeDelta = new Vector2(760, 132);
            _title = UiKit.Label(card, "Title", 20, 10, 720, 66, 40, TextAnchor.MiddleCenter, UiKit.DarkText);
            _title.resizeTextForBestFit = true; _title.resizeTextMinSize = 24; _title.resizeTextMaxSize = 40;
            _info = UiKit.Label(card, "Info", 20, 76, 720, 46, 26, TextAnchor.MiddleCenter, UiKit.SoftText);
            _info.resizeTextForBestFit = true; _info.resizeTextMinSize = 18; _info.resizeTextMaxSize = 26;

            var home = UiKit.TextButton(safe, "GoHome", UiKit.Green, 34, goHome, out _homeLabel);
            var rect = (RectTransform)home.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 0);
            rect.anchoredPosition = new Vector2(-30, 30); rect.sizeDelta = new Vector2(260, 110);

            _message = UiKit.Label(safe, "Message", 0, 0, 980, 84, 30, TextAnchor.MiddleCenter, Color.white, true);
            var m = _message.rectTransform;
            m.anchorMin = m.anchorMax = m.pivot = new Vector2(.5f, 0); m.anchoredPosition = new Vector2(0, 160);
            _message.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, .7f);
            _message.text = "";

            Loc.LanguageChanged += Refresh;
            Refresh();
        }

        /// <summary>A short message over the room (the HUD and its toasts are hidden while visiting).</summary>
        public void ShowMessage(string text, float seconds = 3f)
        {
            _message.text = text;
            _messageTime = seconds;
        }

        private void Update()
        {
            if (_messageTime <= 0f) return;
            _messageTime -= Time.unscaledDeltaTime;
            if (_messageTime <= 0f) _message.text = "";
        }

        private void OnDestroy() => Loc.LanguageChanged -= Refresh;

        private void Refresh()
        {
            if (_title == null) return;
            _title.text = Loc.Format("ui.visit.title", _shop.Name);
            _info.text = Loc.Format("ui.street.row_info", _shop.OwnerName, _shop.Level, _shop.Popularity.ToString("0.0"));
            _homeLabel.text = Loc.Get("ui.visit.home");
        }
    }
}

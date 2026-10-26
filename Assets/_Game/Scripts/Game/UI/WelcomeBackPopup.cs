using System;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Logic.Save;
using UnityEngine;
using UnityEngine.UI;

namespace DemoRestaurant.Game.UI
{
    /// <summary>
    /// "Welcome back!" summary of idle progress: time away, customers the staff served, coins, popularity and XP,
    /// or why nothing happened (e.g. no chef). Modal: dims the screen until the player taps Collect.
    /// </summary>
    public sealed class WelcomeBackPopup
    {
        private readonly GameObject _root;
        private readonly Text _title, _away, _served, _coins, _popularity, _xp, _note, _collect;
        private readonly GameObject _statsRow;

        public WelcomeBackPopup(RectTransform canvas, Sprite coin)
        {
            var dim = UiKit.NewRect("WelcomeBack", canvas);
            UiKit.Stretch(dim);
            var dimImage = dim.gameObject.AddComponent<Image>();
            dimImage.color = new Color(0f, 0f, 0f, 0.55f); // also blocks taps on the restaurant behind
            _root = dim.gameObject;

            var card = UiKit.ConceptCard(dim, "Card");
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(760f, 560f);

            _title = UiKit.Label(card, "Title", 0, 28, 760, 60, 46, TextAnchor.MiddleCenter, UiKit.DarkText);
            _away = UiKit.Label(card, "Away", 40, 92, 680, 40, 26, TextAnchor.MiddleCenter, UiKit.SoftText);
            _served = UiKit.Label(card, "Served", 40, 142, 680, 44, 30, TextAnchor.MiddleCenter, UiKit.DarkText, wrap: true);

            // Coins (big), then popularity and XP side by side.
            var row = UiKit.Place(UiKit.NewRect("Stats", card), 40, 196, 680, 170);
            _statsRow = row.gameObject;
            var coinBox = UiKit.Box(row, "Coins", UiKit.Gold, UiKit.GoldFill, 4f);
            UiKit.Place(coinBox, 140, 0, 400, 84);
            UiKit.AddImage(UiKit.Place(UiKit.NewRect("Coin", coinBox), 20, 12, 60, 60), coin);
            _coins = UiKit.Label(coinBox, "Amount", 90, 0, 290, 84, 44, TextAnchor.MiddleLeft, UiKit.DarkText);
            _popularity = UiKit.Label(row, "Popularity", 40, 100, 300, 56, 28, TextAnchor.MiddleCenter, new Color32(214, 84, 120, 255));
            _xp = UiKit.Label(row, "Xp", 340, 100, 300, 56, 28, TextAnchor.MiddleCenter, new Color32(60, 130, 190, 255));

            _note = UiKit.Label(card, "Note", 50, 372, 660, 80, 22, TextAnchor.MiddleCenter, UiKit.SoftText, wrap: true);

            var collect = UiKit.TextButton(card, "Collect", UiKit.Green, 34, Hide, out _collect);
            UiKit.Place((RectTransform)collect.transform, 230, 462, 300, 76);

            _root.SetActive(false);
        }

        public bool IsShowing => _root.activeSelf;
        public event Action Closed;

        public void Show(OfflineReport report, int marketBasketsReady, float maxOfflineHours)
        {
            _title.text = Loc.Get("ui.welcome.title");
            _away.text = Loc.Format("ui.welcome.away", Duration(report.AwaySeconds));
            _collect.text = Loc.Get("ui.welcome.collect");

            bool earned = report.CustomersServed > 0;
            _statsRow.SetActive(earned);
            _served.text = earned
                ? Loc.Format("ui.welcome.served", report.CustomersServed)
                : Loc.Get(report.BlockedReason != null ? "ui.welcome." + report.BlockedReason : "ui.welcome.nothing");
            _coins.text = "+" + report.Coins.ToString("N0");
            _popularity.gameObject.SetActive(false); // popularity was replaced by the star rating, which idle play does not change
            _xp.text = Loc.Format("ui.welcome.xp", report.Xp);

            string note = report.AwaySeconds > report.CountedSeconds + 1
                ? Loc.Format("ui.welcome.capped", maxOfflineHours.ToString("0"))
                : "";
            if (report.ExhaustedStaff > 0)
                note = (note.Length > 0 ? note + "\n" : "") + Loc.Format("ui.welcome.tired", report.ExhaustedStaff);
            if (marketBasketsReady > 0)
                note = (note.Length > 0 ? note + "\n" : "") + Loc.Format("ui.welcome.market", marketBasketsReady);
            _note.text = note;

            _root.transform.SetAsLastSibling();
            _root.SetActive(true);
        }

        public void Hide()
        {
            if (!_root.activeSelf)
                return;
            _root.SetActive(false);
            Closed?.Invoke();
        }

        private static string Duration(double seconds)
        {
            var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
            if (t.TotalHours >= 1)
                return Loc.Format("ui.time.hm", (int)t.TotalHours, t.Minutes);
            return Loc.Format("ui.time.m", Math.Max(1, (int)Math.Round(t.TotalMinutes)));
        }
    }
}

using System;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Logic.Progression;
using DemoRestaurant.Logic.Session;
using UnityEngine;
using UnityEngine.UI;

namespace DemoRestaurant.Game.UI
{
    /// <summary>
    /// The farm's nine plots, laid out like the field (3 x 3). Each card shows the plot's state, its crop and
    /// time left, and one action: buy a seed, water, or harvest. Locked plots show the level that opens them.
    /// Opened from the toolbar or by tapping the farm behind the restaurant.
    /// </summary>
    public sealed class GardenPanel : SlidePanel
    {
        private const float CardSize = 252f;
        private const float Gap = 12f;

        private sealed class Card
        {
            public Image Rim, Fill;
            public Text Title, State, Timer, ActionLabel;
            public Button Action;
        }

        private readonly Card[] _cards = new Card[Garden.PlotCount];
        private Text _hint;
        private int _highlight = -1;

        protected override string TitleKey => "ui.garden.title";

        protected override void BuildContent(RectTransform content)
        {
            _hint = UiKit.Label(content, "Hint", 0, 0, 780, 56, 21, TextAnchor.UpperLeft, UiKit.SoftText, wrap: true);
            for (int i = 0; i < Garden.PlotCount; i++)
            {
                int plot = i;
                float x = (i % Garden.PlotsPerRow) * (CardSize + Gap);
                float y = 64f + (i / Garden.PlotsPerRow) * (CardSize + Gap);
                var rect = UiKit.Box(content, "Plot" + i, UiKit.CardRim, UiKit.CardFill, 4f, out var rim, out var fill);
                UiKit.Place(rect, x, y, CardSize, CardSize);
                var card = new Card { Rim = rim, Fill = fill };
                card.Title = UiKit.Label(rect, "Title", 10, 8, CardSize - 20, 36, 24, TextAnchor.MiddleCenter, UiKit.DarkText);
                card.State = UiKit.Label(rect, "State", 10, 48, CardSize - 20, 64, 20, TextAnchor.UpperCenter, UiKit.SoftText, wrap: true);
                card.Timer = UiKit.Label(rect, "Timer", 10, 114, CardSize - 20, 56, 19, TextAnchor.UpperCenter, UiKit.SoftText, wrap: true);
                card.Action = UiKit.TextButton(rect, "Action", UiKit.Green, 21, () => Act(plot), out card.ActionLabel);
                UiKit.Place((RectTransform)card.Action.transform, 14, CardSize - 72, CardSize - 28, 58);
                card.ActionLabel.resizeTextForBestFit = true;
                card.ActionLabel.resizeTextMinSize = 12;
                card.ActionLabel.resizeTextMaxSize = 21;
                _cards[i] = card;
            }
        }

        /// <summary>Opens the panel with one plot highlighted (tapping the farm).</summary>
        public void Show(int plot)
        {
            _highlight = plot;
            if (!IsOpen) Toggle();
            Refresh();
        }

        protected override void Refresh()
        {
            if (_hint == null) return;
            var s = Ui.Session;
            var garden = s.Garden;
            var now = s.UtcNow;
            int level = s.Progress.Level;
            _hint.text = Loc.Format("ui.garden.hint", garden.SeedCost, garden.WateringSeconds / 3600.0, garden.HarvestAmount);
            for (int i = 0; i < Garden.PlotCount; i++)
            {
                var c = _cards[i];
                var state = garden.StateOf(i, level, now);
                var crop = garden.CropOf(i);
                c.Rim.color = i == _highlight ? UiKit.Gold : UiKit.CardRim;
                c.Fill.color = state == PlotState.Locked ? new Color(0.9f, 0.88f, 0.85f)
                    : state == PlotState.Ready ? UiKit.GoldFill : UiKit.CardFill;
                c.Title.text = crop != null && state != PlotState.Empty ? Loc.Get(crop.NameKey) : Loc.Format("ui.garden.plot", i + 1);
                c.State.text = Loc.Get("ui.garden.state." + state);
                switch (state)
                {
                    case PlotState.Locked:
                        c.Timer.text = "";
                        SetAction(c, Loc.Format("ui.garden.unlocks_at", garden.UnlockLevel(i)), UiKit.Disabled);
                        break;
                    case PlotState.Empty:
                        c.Timer.text = "";
                        SetAction(c, Loc.Format("ui.garden.buy_seed", garden.SeedCost),
                            s.Wallet.DisplayCoins >= garden.SeedCost ? UiKit.Green : UiKit.Disabled);
                        break;
                    case PlotState.Growing:
                        c.Timer.text = Loc.Format("ui.garden.timer", Duration(garden.TimeToHarvest(i, now)), Duration(garden.WaterLeft(i, now)));
                        SetAction(c, Loc.Get("ui.garden.water"), UiKit.WoodLight);
                        break;
                    case PlotState.Dry:
                        c.Timer.text = Loc.Format("ui.garden.timer_dry", Duration(garden.TimeToHarvest(i, now)));
                        SetAction(c, Loc.Get("ui.garden.water"), UiKit.Green);
                        break;
                    case PlotState.Ready:
                        c.Timer.text = "";
                        SetAction(c, Loc.Format("ui.garden.harvest", garden.HarvestAmount), UiKit.Gold);
                        break;
                }
            }
        }

        private static void SetAction(Card c, string label, Color color)
        {
            c.ActionLabel.text = label;
            UiKit.SetButtonColor(c.Action, color);
        }

        private void Act(int plot)
        {
            var s = Ui.Session;
            var state = s.Garden.StateOf(plot, s.Progress.Level, s.UtcNow);
            RestaurantSession.GardenResult result;
            switch (state)
            {
                case PlotState.Locked:
                    Ui.Toast(Loc.Format("ui.garden.unlocks_at", s.Garden.UnlockLevel(plot)), 1.6f);
                    return;
                case PlotState.Empty:
                    result = s.BuySeed(plot);
                    if (result == RestaurantSession.GardenResult.NotEnoughCoins) Ui.Toast(Loc.Get("ui.staff.no_coins"));
                    else if (result == RestaurantSession.GardenResult.Done)
                        Ui.Toast(Loc.Format("ui.garden.planted", Loc.Get(s.Garden.CropOf(plot).NameKey)), 2f);
                    break;
                case PlotState.Ready:
                    var crop = s.Garden.CropOf(plot);
                    if (s.HarvestPlot(plot) == RestaurantSession.GardenResult.Done)
                        Ui.Toast(Loc.Format("ui.garden.harvested", s.Garden.HarvestAmount, Loc.Get(crop.NameKey)), 2f);
                    break;
                default:
                    if (s.WaterPlot(plot) == RestaurantSession.GardenResult.Done) Ui.Toast(Loc.Get("ui.garden.watered"), 1.4f);
                    break;
            }
            _highlight = plot;
            Refresh();
        }

        private static string Duration(TimeSpan t)
        {
            if (t.TotalHours >= 1)
                return Loc.Format("ui.time.hm", (int)t.TotalHours, t.Minutes);
            return Loc.Format("ui.time.m", Math.Max(1, (int)Math.Ceiling(t.TotalMinutes)));
        }
    }
}

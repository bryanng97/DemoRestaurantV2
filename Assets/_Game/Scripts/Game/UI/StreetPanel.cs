using System.Collections.Generic;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Logic.Online;
using UnityEngine;
using UnityEngine.UI;

namespace DemoRestaurant.Game.UI
{
    /// <summary>
    /// Phase 5, as in the reference game: the random street lists other players' restaurants (recently active
    /// owners first). Visiting one records the visit (the first visit ever gives an ingredient) and shows what the
    /// visitor can help with: picking up its litter and watering its planted plots. Every reward comes from the
    /// server; the game's coins, XP and ingredients catch up on the next sync. Needs a signed-in account.
    /// </summary>
    public sealed class StreetPanel : SlidePanel
    {
        private const int MaxRows = 20;
        private const int MaxPlots = 9;
        private const float RowHeight = 88f;

        private readonly List<StreetRow> _rows = new List<StreetRow>();
        private readonly List<(Button button, Text label)> _plotButtons = new List<(Button, Text)>();
        private List<StreetRestaurant> _street = new List<StreetRestaurant>();
        private StreetRestaurant _visiting;
        private VisitState _state;
        private bool _busy;

        private RectTransform _streetView, _visitView, _listContent;
        private Text _streetStatus, _visitTitle, _visitInfo, _trashLabel, _coinsLabel, _gardenLabel;
        private Button _trash;

        protected override string TitleKey => "ui.street.title";

        protected override void BuildContent(RectTransform content)
        {
            _streetView = UiKit.Place(UiKit.NewRect("Street", content), 0, 0, Width - 60, Height - 130);
            var refresh = UiKit.TextButton(_streetView, "Refresh", UiKit.Green, 24, () => LoadStreet(), out var refreshLabel);
            refreshLabel.text = Loc.Get("ui.street.refresh");
            UiKit.Place((RectTransform)refresh.transform, 560, 0, 220, 60);
            _streetStatus = UiKit.Label(_streetView, "Status", 0, 0, 550, 60, 20, TextAnchor.MiddleLeft, UiKit.SoftText, wrap: true);
            BuildList(_streetView);

            _visitView = UiKit.Place(UiKit.NewRect("Visit", content), 0, 0, Width - 60, Height - 130);
            var back = UiKit.TextButton(_visitView, "Back", UiKit.WoodLight, 22, ShowStreet, out var backLabel);
            backLabel.text = Loc.Get("ui.street.back");
            UiKit.Place((RectTransform)back.transform, 0, 0, 180, 56);
            _visitTitle = UiKit.Label(_visitView, "Title", 190, 0, 590, 56, 28, TextAnchor.MiddleLeft, UiKit.DarkText);
            _visitTitle.resizeTextForBestFit = true; _visitTitle.resizeTextMinSize = 16; _visitTitle.resizeTextMaxSize = 28;
            _visitInfo = UiKit.Label(_visitView, "Info", 0, 64, 780, 64, 20, TextAnchor.UpperLeft, UiKit.SoftText, wrap: true);
            _trash = UiKit.TextButton(_visitView, "Trash", UiKit.Gold, 24, PickUpLitter, out _trashLabel);
            UiKit.Place((RectTransform)_trash.transform, 0, 140, 380, 70);
            _coinsLabel = UiKit.Label(_visitView, "Coins", 400, 140, 380, 70, 20, TextAnchor.MiddleLeft, UiKit.SoftText, wrap: true);
            _gardenLabel = UiKit.Label(_visitView, "Garden", 0, 226, 780, 40, 24, TextAnchor.MiddleLeft, UiKit.DarkText);
            for (int i = 0; i < MaxPlots; i++)
            {
                int index = i;
                var button = UiKit.TextButton(_visitView, "Plot" + i, UiKit.WoodLight, 18, () => Water(index), out var label);
                UiKit.Place((RectTransform)button.transform, (i % 3) * 262, 274 + (i / 3) * 96, 250, 86);
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.resizeTextForBestFit = true; label.resizeTextMinSize = 12; label.resizeTextMaxSize = 18;
                _plotButtons.Add((button, label));
            }
            ShowStreet();
            // Every opening shows a fresh street, as the reference game's random street does.
            Opened += _ => { ShowStreet(); LoadStreet(); };
        }

        private void BuildList(RectTransform parent)
        {
            var viewport = UiKit.Place(UiKit.NewRect("List", parent), 0, 72, Width - 60, Height - 210);
            viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            viewport.gameObject.AddComponent<RectMask2D>();
            _listContent = UiKit.NewRect("Rows", viewport);
            _listContent.anchorMin = new Vector2(0f, 1f); _listContent.anchorMax = new Vector2(1f, 1f);
            _listContent.pivot = new Vector2(0.5f, 1f); _listContent.anchoredPosition = Vector2.zero;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = _listContent; scroll.viewport = viewport;
            scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 30f;
            for (int i = 0; i < MaxRows; i++)
            {
                var row = new StreetRow();
                var rect = UiKit.Box(_listContent, "Row" + i, UiKit.CardRim, UiKit.CardFill, 4f);
                UiKit.Place(rect, 0, i * (RowHeight + 6), Width - 60, RowHeight);
                row.Root = rect.gameObject;
                row.Name = UiKit.Label(rect, "Name", 16, 6, 520, 40, 24, TextAnchor.MiddleLeft, UiKit.DarkText);
                row.Name.resizeTextForBestFit = true; row.Name.resizeTextMinSize = 14; row.Name.resizeTextMaxSize = 24;
                row.Info = UiKit.Label(rect, "Info", 16, 46, 520, 34, 18, TextAnchor.MiddleLeft, UiKit.SoftText);
                row.Info.resizeTextForBestFit = true; row.Info.resizeTextMinSize = 11; row.Info.resizeTextMaxSize = 18;
                int index = i;
                row.Visit = UiKit.TextButton(rect, "Visit", UiKit.Green, 24, () => Visit(index), out var label);
                label.text = Loc.Get("ui.street.visit");
                UiKit.Place((RectTransform)row.Visit.transform, 580, 14, 180, 60);
                _rows.Add(row);
            }
        }

        protected override void Refresh()
        {
            if (_listContent == null) return;
            if (_visiting == null) RefreshStreet(); else RefreshVisit();
        }

        private void ShowStreet()
        {
            _visiting = null;
            _state = null;
            if (_streetView == null) return;
            _streetView.gameObject.SetActive(true);
            _visitView.gameObject.SetActive(false);
            RefreshStreet();
        }

        private void RefreshStreet()
        {
            int shown = 0;
            foreach (var r in _street)
            {
                if (shown >= _rows.Count) break;
                var row = _rows[shown++];
                row.Root.SetActive(true);
                row.Name.text = r.Name;
                row.Info.text = Loc.Format("ui.street.row_info", r.OwnerName, r.Level, r.Popularity.ToString("0.0"))
                    + (r.ActiveRecently ? " · " + Loc.Get("ui.street.active") : "");
            }
            for (int i = shown; i < _rows.Count; i++) _rows[i].Root.SetActive(false);
            _listContent.sizeDelta = new Vector2(0f, shown * (RowHeight + 6));
        }

        private async void LoadStreet()
        {
            var online = Ui.Online;
            if (online == null || !online.Auth.IsSignedIn)
            {
                _street.Clear();
                _streetStatus.text = Loc.Get("ui.street.sign_in");
                RefreshStreet();
                return;
            }
            if (_busy) return;
            _busy = true;
            _streetStatus.text = Loc.Get("ui.street.loading");
            var result = await online.Api.RandomStreet();
            _busy = false;
            if (!result.Ok)
            {
                _streetStatus.text = Loc.Get("ui.street.failed");
                return;
            }
            _street = StreetRestaurant.ParseStreet(result.Data);
            _streetStatus.text = _street.Count == 0 ? Loc.Get("ui.street.empty") : Loc.Format("ui.street.count", _street.Count);
            if (_visiting == null) RefreshStreet();
        }

        private async void Visit(int index)
        {
            if (_busy || index >= _street.Count || Ui.Online == null) return;
            _busy = true;
            var target = _street[index];
            var api = Ui.Online.Api;
            var visit = await api.Visit(target.RestaurantId);
            if (!visit.Ok)
            {
                _busy = false;
                Ui.Toast(Loc.Get("ui.street.failed"));
                return;
            }
            var state = await api.VisitState(target.RestaurantId);
            _busy = false;
            if (!state.Ok)
            {
                Ui.Toast(Loc.Get("ui.street.failed"));
                return;
            }
            _visiting = target;
            _state = VisitState.Parse(state.Data);
            _streetView.gameObject.SetActive(false);
            _visitView.gameObject.SetActive(true);
            var reward = FirstVisitReward.Parse(visit.Data);
            if (reward != null)
                Ui.Toast(Loc.Format("ui.street.first_visit", reward.Quantity, IngredientName(reward.IngredientId)), 3f);
            RefreshVisit();
        }

        private void RefreshVisit()
        {
            if (_visiting == null || _state == null) return;
            _visitTitle.text = _visiting.Name;
            _visitInfo.text = Loc.Format("ui.street.visit_info", _visiting.OwnerName, _visiting.Level, _visiting.Popularity.ToString("0.0"));
            _trashLabel.text = Loc.Format("ui.street.pick_litter", _state.Trash);
            UiKit.SetButtonColor(_trash, _state.Trash > 0 ? UiKit.Gold : UiKit.Disabled);
            _coinsLabel.text = Loc.Format("ui.street.rewards", _state.TrashCoinsMin, _state.TrashCoinsMax, _state.WaterCoins)
                + "\n" + (_state.DailyCoinCap.HasValue
                    ? Loc.Format("ui.street.coins_today_cap", _state.CoinsToday, _state.DailyCoinCap.Value)
                    : Loc.Format("ui.street.coins_today", _state.CoinsToday));
            _gardenLabel.text = Loc.Get("ui.street.garden");
            for (int i = 0; i < _plotButtons.Count; i++)
            {
                var (button, label) = _plotButtons[i];
                var plot = i < _state.Plots.Count ? _state.Plots[i] : null;
                button.gameObject.SetActive(plot != null);
                if (plot == null) continue;
                label.text = !plot.Open ? Loc.Get("ui.street.plot_locked")
                    : !plot.Planted ? Loc.Get("ui.street.plot_empty")
                    : plot.Ready ? Loc.Format("ui.street.plot_ready", IngredientName(plot.IngredientId))
                    : Loc.Format(plot.Watered ? "ui.street.plot_watered" : "ui.street.plot_dry", IngredientName(plot.IngredientId));
                UiKit.SetButtonColor(button, plot.CanWater ? (plot.Watered ? UiKit.WoodLight : UiKit.Green) : UiKit.Disabled);
            }
        }

        private async void PickUpLitter()
        {
            if (_busy || _visiting == null || _state == null || _state.Trash <= 0) return;
            _busy = true;
            var id = _visiting.RestaurantId;
            var result = await Ui.Online.Api.HelpTrash(id);
            _busy = false;
            if (_visiting == null || _visiting.RestaurantId != id) return;
            if (!result.Ok)
            {
                Ui.Toast(Loc.Get(result.Code == "TRASH_NOT_READY" ? "ui.street.no_litter" : "ui.street.failed"));
                if (result.Code == "TRASH_NOT_READY") { _state.Trash = 0; RefreshVisit(); }
                return;
            }
            var help = HelpResult.Parse(result.Data);
            _state.Apply(help, result.Data);
            Ui.Toast(help.Reward > 0 ? Loc.Format("ui.street.earned", help.Reward) : Loc.Get(help.Capped ? "ui.street.capped" : "ui.street.thanks"));
            RefreshVisit();
            _ = Ui.Online.SyncNow();
        }

        private async void Water(int index)
        {
            if (_busy || _visiting == null || _state == null || index >= _state.Plots.Count || !_state.Plots[index].CanWater) return;
            _busy = true;
            var id = _visiting.RestaurantId;
            var result = await Ui.Online.Api.HelpWater(id, _state.Plots[index].Index);
            _busy = false;
            if (_visiting == null || _visiting.RestaurantId != id) return;
            if (!result.Ok)
            {
                Ui.Toast(Loc.Get("ui.street.failed"));
                return;
            }
            var help = HelpResult.Parse(result.Data);
            _state.Apply(help, result.Data);
            Ui.Toast(help.Reward > 0 ? Loc.Format("ui.street.earned", help.Reward) : Loc.Get(help.Capped ? "ui.street.capped" : "ui.street.watered"));
            RefreshVisit();
            _ = Ui.Online.SyncNow();
        }

        private string IngredientName(string id)
        {
            var pack = Ui.Session.Content;
            return id != null && pack.HasIngredient(id) ? Loc.Get(pack.GetIngredient(id).NameKey) : id ?? "";
        }

        private sealed class StreetRow
        {
            public GameObject Root;
            public Text Name, Info;
            public Button Visit;
        }
    }
}

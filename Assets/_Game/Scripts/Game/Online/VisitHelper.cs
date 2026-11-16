using DemoRestaurant.Game.Content;
using DemoRestaurant.Game.UI;
using DemoRestaurant.Logic.Online;
using DemoRestaurant.Logic.Session;
using DemoRestaurant.Logic.Simulation;
using UnityEngine;

namespace DemoRestaurant.Game.Online
{
    /// <summary>
    /// Phase 5, as in the reference game: helping in a visited restaurant. The server's visit state puts its litter
    /// on the visited floor and its crops in the visited farm; picking up a piece or watering a planted plot is sent
    /// to the server (help/trash, help/water), which pays the visitor. The visited room never simulates, so nothing
    /// appears that the server didn't send.
    /// </summary>
    public sealed class VisitHelper : MonoBehaviour
    {
        private GameApiClient _api;
        private StreetRestaurant _shop;
        private RestaurantSession _session;
        private VisitBanner _banner;
        private VisitState _state;
        private bool _watering;

        public void Bind(GameApiClient api, StreetRestaurant shop, RestaurantSession session, VisitBanner banner)
        {
            _api = api; _shop = shop; _session = session; _banner = banner;
            _session.Sim.TrashCleaned += OnLitterPicked;
            Load();
        }

        private void OnDestroy()
        {
            if (_session != null) _session.Sim.TrashCleaned -= OnLitterPicked;
        }

        private async void Load()
        {
            var result = await _api.VisitState(_shop.RestaurantId);
            if (this == null) return;
            if (!result.Ok) { _banner.ShowMessage(Loc.Get("ui.street.failed")); return; }
            _state = VisitState.Parse(result.Data);
            // As many pieces as the server holds, on the floor's usual litter cells.
            _session.Sim.RestoreTrash(null, 0);
            float spawn = _session.Content.Rules.Trash.spawnSeconds;
            for (int i = 0; i < _state.Trash && spawn > 0; i++) _session.Sim.TickTrash(spawn);
            ShowGarden();
            Refresh();
        }

        private void ShowGarden() => _session.Garden.Restore(_state.GardenSaves(_session.UtcNow));

        private void Refresh()
        {
            if (_state == null) return;
            _banner.SetHelp(Loc.Format("ui.visit.help", _session.Sim.TrashCount,
                _state.DailyCoinCap.HasValue
                    ? Loc.Format("ui.street.coins_today_cap", _state.CoinsToday, _state.DailyCoinCap.Value)
                    : Loc.Format("ui.street.coins_today", _state.CoinsToday)));
        }

        /// <summary>A litter piece tapped on the visited floor (it leaves the floor at once).</summary>
        private async void OnLitterPicked(TrashPiece piece)
        {
            Refresh();
            if (_state == null) return;
            var result = await _api.HelpTrash(_shop.RestaurantId);
            if (this == null) return;
            if (!result.Ok)
            {
                bool none = result.Code == "TRASH_NOT_READY";
                if (none) _session.Sim.RestoreTrash(null, 0); // the server has no more: clear the floor
                _banner.ShowMessage(Loc.Get(none ? "ui.street.no_litter" : "ui.street.failed"));
                Refresh();
                return;
            }
            var help = HelpResult.Parse(result.Data);
            _state.Apply(help, result.Data);
            _banner.ShowMessage(help.Reward > 0 ? Loc.Format("ui.street.earned", help.Reward)
                : Loc.Get(help.Capped ? "ui.street.capped" : "ui.street.thanks"));
            Refresh();
        }

        /// <summary>A farm plot tapped: a planted, growing plot is watered for its owner.</summary>
        public async void Water(int plot)
        {
            if (_state == null || _watering) return;
            var target = _state.Plots.Find(p => p.Index == plot);
            if (target == null || !target.CanWater)
            {
                if (target != null && target.Ready) _banner.ShowMessage(Loc.Format("ui.street.plot_ready", CropName(target.IngredientId)));
                return;
            }
            _watering = true;
            var result = await _api.HelpWater(_shop.RestaurantId, plot);
            _watering = false;
            if (this == null) return;
            if (!result.Ok) { _banner.ShowMessage(Loc.Get("ui.street.failed")); return; }
            var help = HelpResult.Parse(result.Data);
            _state.Apply(help, result.Data);
            ShowGarden();
            _banner.ShowMessage(help.Reward > 0 ? Loc.Format("ui.street.earned", help.Reward)
                : Loc.Get(help.Capped ? "ui.street.capped" : "ui.street.watered"));
            Refresh();
        }

        private string CropName(string id) =>
            id != null && _session.Content.HasIngredient(id) ? Loc.Get(_session.Content.GetIngredient(id).NameKey) : id ?? "";
    }
}

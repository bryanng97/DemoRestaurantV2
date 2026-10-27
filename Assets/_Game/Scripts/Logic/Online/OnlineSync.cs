using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Session;

namespace DemoRestaurant.Logic.Online
{
    /// <summary>Device-side sync bookkeeping saved next to the game save (per account).</summary>
    [Serializable]
    public sealed class OnlineSyncSave
    {
        public string playerId;
        public string restaurantId;
        public ServerActionList actions = new ServerActionList();
        public InstanceIdBook.List instances = new InstanceIdBook.List();
        public ExteriorIdRecord[] exteriorIds = new ExteriorIdRecord[0];
    }

    [Serializable]
    public sealed class ExteriorIdRecord
    {
        public int instance;
        public string instanceId;
    }

    /// <summary>What a sign-in means for the restaurant already on this device.</summary>
    public enum AccountChange
    {
        /// <summary>The device's restaurant is this account's: carry on.</summary>
        SameAccount,
        /// <summary>A brand-new account on a device with guest play: ask whether to keep this device's restaurant.</summary>
        AskKeepDevice,
        /// <summary>An account with progress on a device with an unsent renovation: ask whether to keep this device's
        /// layout (its other unsent play is dropped either way).</summary>
        AskKeepDeviceLayout,
        /// <summary>The account already has a restaurant (or the device has nothing): drop the device's queued
        /// actions and load the account's.</summary>
        LoadAccount,
    }

    /// <summary>What one sync round decided.</summary>
    public sealed class SyncRound
    {
        public LedgerOutcome Outcome;
        /// <summary>The server's restaurant differs in a way that can't be patched in place (layout, staff):
        /// the game should adopt <see cref="OnlineSync.LastState"/> with <see cref="RestaurantSession.SaveFromServer"/>.</summary>
        public string AdoptReason;
        public bool NeedsAdoption => AdoptReason != null;
    }

    /// <summary>
    /// Plain-C# heart of the online mode: builds ledger batches from the session's <see cref="ServerActionLog"/>
    /// (filling deferred layout, menu and facade bodies from the live state), applies responses, and decides when
    /// the local restaurant must be replaced by the server's. The Unity layer only moves the JSON.
    /// </summary>
    public sealed class OnlineSync
    {
        private readonly RestaurantSession _session;
        private readonly Func<string> _newId;
        private readonly Dictionary<int, string> _exteriorIds = new Dictionary<int, string>();

        public ServerIdMap Ids { get; }
        public InstanceIdBook Instances { get; } = new InstanceIdBook();
        public ServerCatalog Catalog { get; set; }
        /// <summary>The newest server state (bootstrap, ledger or <c>/state</c>).</summary>
        public JsonValue LastState { get; private set; }
        /// <summary>The theme's own wallpaper (what an unpapered wall shows), sent as that wallpaper.</summary>
        public string DefaultWallpaper { get; set; }
        /// <summary>Local pieces the server's catalog does not have (kept local), from the last layout built.</summary>
        public List<string> LocalOnlyPieces { get; } = new List<string>();

        public OnlineSync(RestaurantSession session, ServerIdMap ids = null, Func<string> newId = null)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            Ids = ids ?? new ServerIdMap();
            _newId = newId ?? (() => Guid.NewGuid().ToString());
            _session.ServerInstanceOf = InstanceOf;
        }

        public ServerActionLog Log => _session.ServerLog;

        // ---------------------------------------------------------------- persistence

        public OnlineSyncSave Capture(string playerId)
        {
            var ext = new List<ExteriorIdRecord>();
            foreach (var pair in _exteriorIds) ext.Add(new ExteriorIdRecord { instance = pair.Key, instanceId = pair.Value });
            return new OnlineSyncSave
            {
                playerId = playerId,
                restaurantId = _session.ServerRestaurantId,
                actions = Log.ToList(),
                instances = Instances.ToList(),
                exteriorIds = ext.ToArray(),
            };
        }

        public void Restore(OnlineSyncSave save)
        {
            if (save == null) return;
            if (!string.IsNullOrEmpty(save.restaurantId)) _session.ServerRestaurantId = save.restaurantId;
            Log.Restore(save.actions);
            Instances.Restore(save.instances);
            _exteriorIds.Clear();
            if (save.exteriorIds != null)
                foreach (var r in save.exteriorIds)
                    if (r != null && !string.IsNullOrEmpty(r.instanceId)) _exteriorIds[r.instance] = r.instanceId;
        }

        // ---------------------------------------------------------------- sign-in

        /// <summary>
        /// Who wins when <paramref name="user"/> signs in on a device last synced by <paramref name="deviceOwner"/>
        /// (null: never synced, guest play). A brand-new account may take over the device's guest restaurant. An account
        /// that already has progress keeps its own, except that the player may keep an unsent guest renovation as the
        /// account's layout. Another account's device play never moves to this one.
        /// </summary>
        public static AccountChange OnSignIn(string deviceOwner, string user, JsonValue state, bool deviceHasPlay,
            bool deviceHasRenovation = false)
        {
            if (deviceOwner == user) return AccountChange.SameAccount;
            if (!string.IsNullOrEmpty(deviceOwner)) return AccountChange.LoadAccount;
            if (deviceHasPlay && IsNewAccount(state)) return AccountChange.AskKeepDevice;
            return deviceHasRenovation ? AccountChange.AskKeepDeviceLayout : AccountChange.LoadAccount;
        }

        /// <summary>The player saved a renovation that has not reached the server.</summary>
        public bool DeviceHasRenovation => Log.HasPending("layout");

        /// <summary>Nothing played on the server yet: no XP and the starter layout.</summary>
        public static bool IsNewAccount(JsonValue state) =>
            state["xp"].AsLong() == 0 && state["layoutRevision"].AsInt(1) == 1;

        /// <summary>The device has played: queued actions, served guests or progress.</summary>
        public bool DeviceHasPlay =>
            Log.Count > 0 || _session.TotalCustomersServed > 0 || _session.Progress.Level > 1 || _session.Progress.Xp > 0;

        /// <summary>Drops the device's queued actions and ids; the next sync loads the account's restaurant.</summary>
        public void ForgetDevice()
        {
            Restore(new OnlineSyncSave());
            _session.ServerRestaurantId = null;
        }

        /// <summary>
        /// Keeps the device's restaurant for a new account: its layout and menu go up with the queued actions.
        /// <paramref name="state"/> is the account's state, so the starter furniture it owns isn't bought again.
        /// </summary>
        public void KeepDevice(JsonValue state = null)
        {
            Seen(state);
            _session.RecordLayoutSaved(0);
            _session.RecordMenuChanged();
        }

        /// <summary>
        /// Keeps only this device's layout for an account with progress: the rest of the unsent play is dropped and
        /// the layout goes up as the account's renovation (the server checks it and charges the account for pieces
        /// it doesn't own). <paramref name="state"/> is the account's state, so ownership is counted from it.
        /// </summary>
        public void KeepDeviceLayout(JsonValue state)
        {
            ForgetDevice();
            Seen(state);
            _session.RecordLayoutSaved(0);
        }

        private void Seen(JsonValue state)
        {
            if (state != null && state.Kind == JsonKind.Object) LastState = state;
        }

        // ---------------------------------------------------------------- state

        /// <summary>Takes a server state (bootstrap or <c>/state</c>) without a ledger round.</summary>
        public SyncRound AcceptState(JsonValue state) => Fold(state, new LedgerOutcome());

        /// <summary>The next ledger request, or null when nothing waits and <paramref name="evenIfEmpty"/> is false.</summary>
        public JsonValue NextRequest(out List<ServerAction> batch, bool evenIfEmpty = false)
        {
            FillRestaurantIds();
            batch = Log.NextBatch(Build);
            if (batch.Count == 0 && !evenIfEmpty) return null;
            return ServerActionLog.BatchBody(batch);
        }

        /// <summary>Applies a ledger response (<c>{ state, settlement, results }</c>).</summary>
        public SyncRound ApplyResponse(JsonValue data) => Fold(data["state"], Log.Apply(data["results"]));

        private SyncRound Fold(JsonValue state, LedgerOutcome outcome)
        {
            var round = new SyncRound { Outcome = outcome };
            if (state == null || state.Kind != JsonKind.Object) return round;
            LastState = state;
            _session.ApplyServerState(state, Ids);
            round.AdoptReason = AdoptionReason(state);
            return round;
        }

        /// <summary>Why the local restaurant can't simply continue (null when it matches the server).</summary>
        public string AdoptionReason(JsonValue state)
        {
            if (Catalog != null && state["layout"].Kind == JsonKind.Object && !Log.HasPending("layout", "terrace") &&
                LocalFingerprint() != ServerLayoutCodec.Fingerprint(state["layout"]))
                return "layout";
            if (!Log.HasPending("hire", "dismiss", "owner_role") && _session.StaffDiffersFrom(state)) return "staff";
            return null;
        }

        /// <summary>The fingerprint of the local layout as the server would see it (null before the config loads).</summary>
        public string LocalFingerprint()
        {
            if (Catalog == null) return null;
            var scratch = new InstanceIdBook();
            scratch.Restore(Instances.ToList());
            return ServerLayoutCodec.Fingerprint(ServerLayoutCodec.Encode(_session.Layout, Catalog, Ids, scratch, _newId, null, DefaultWallpaper));
        }

        /// <summary>After the game adopted the server state: the instance ids are the server's.</summary>
        public void Adopted(JsonValue state)
        {
            if (state["layout"].Kind == JsonKind.Object) Instances.Load(state["layout"], Ids);
        }

        // ---------------------------------------------------------------- building deferred bodies

        private JsonValue Build(ServerAction action)
        {
            switch (action.type)
            {
                case "layout": return BuildLayout();
                case "menu": return BuildMenu();
                case "exterior": return BuildExterior();
                case "wardrobe_look": return BuildLook();
                default: return null;
            }
        }

        private JsonValue BuildLayout()
        {
            if (Catalog == null || string.IsNullOrEmpty(_session.ServerRestaurantId)) return null;
            LocalOnlyPieces.Clear();
            var layout = ServerLayoutCodec.Encode(_session.Layout, Catalog, Ids, Instances, _newId, LocalOnlyPieces, DefaultWallpaper);
            return ServerLayoutCodec.SavePayload(layout, _session.ServerRestaurantId, ServerOwned(), Catalog);
        }

        /// <summary>What the server owns once every layout already sent (but not yet answered) is applied.</summary>
        private Dictionary<string, int> ServerOwned()
        {
            var owned = LastState == null ? new Dictionary<string, int>() : LastState["inventory"]["furniture"].AsCounts();
            foreach (var a in Log.Items)
            {
                if (a.type != "layout" || !a.sent || !JsonValue.TryParse(a.payload, out var body)) continue;
                foreach (var pair in body["buy"].AsCounts())
                    owned[pair.Key] = (owned.TryGetValue(pair.Key, out int n) ? n : 0) + pair.Value;
            }
            return owned;
        }

        /// <summary>The current look, limited to parts the server sells (others stay local).</summary>
        private JsonValue BuildLook()
        {
            if (Catalog == null || !Catalog.HasWardrobe) return null;
            var look = JsonValue.Object();
            foreach (var pair in WardrobeIds.Look(_session.Profile.Avatar).Fields)
                if (Catalog.HasWardrobePart(pair.Key, pair.Value.AsString())) look.Set(pair.Key, pair.Value);
            return look.Count == 0 ? null : JsonValue.Object().Set("look", look);
        }

        private JsonValue BuildMenu()
        {
            var body = JsonValue.Object();
            var lists = new Dictionary<string, JsonValue>();
            foreach (var category in new[] { "starter", "main", "dessert", "drink" })
            {
                lists[category] = JsonValue.Array();
                body.Set(category, lists[category]);
            }
            foreach (var dish in _session.Menu.Active)
            {
                string category = dish.Category.ToString().ToLowerInvariant();
                if (lists.TryGetValue(category, out var list)) list.Add(JsonValue.Of(dish.Id));
            }
            return body;
        }

        private JsonValue BuildExterior()
        {
            var facade = _session.CaptureExterior();
            if (facade == null || Catalog == null) return null;
            var pieces = JsonValue.Array();
            var counts = new Dictionary<string, int>();
            foreach (var p in facade.pieces)
            {
                string id = Ids.ToServer(p.itemId);
                if (!Catalog.HasExterior(id)) return null; // facade content the server can't check yet: keep it local
                if (!_exteriorIds.TryGetValue(p.instance, out var instance))
                    _exteriorIds[p.instance] = instance = _newId().ToLowerInvariant();
                pieces.Add(JsonValue.Object().Set("instanceId", instance).Set("id", id)
                    .Set("x", (long)Math.Round(p.x)).Set("y", (long)Math.Round(p.y)).Set("flip", p.flipped));
                counts[id] = (counts.TryGetValue(id, out int n) ? n : 0) + 1;
            }
            var owned = LastState?["exterior"]["owned"].AsCounts() ?? new Dictionary<string, int>();
            var buy = new Dictionary<string, int>();
            foreach (var pair in counts)
            {
                int have = owned.TryGetValue(pair.Key, out int n) ? n : 0;
                if (pair.Value > have) buy[pair.Key] = pair.Value - have;
            }
            return JsonValue.Object().Set("pieces", pieces).Set("buy", JsonValue.FromCounts(buy));
        }

        // ---------------------------------------------------------------- instance ids for functional uses

        /// <summary>
        /// The server id of a placed piece. A piece placed since the last layout was sent has none yet: the pending
        /// layout is built now, so the use that follows it in the log refers to an id the server will know.
        /// </summary>
        public string InstanceOf(PlacedFurniture piece)
        {
            string id = Instances.Find(piece);
            if (id != null) return id;
            var pending = Log.PendingDeferred("layout");
            if (pending != null && Log.Freeze(pending, a => BuildLayout()))
                return Instances.Find(piece);
            return null;
        }

        // Actions recorded before the restaurant id was known (offline before the first sign-in).
        private void FillRestaurantIds()
        {
            string id = _session.ServerRestaurantId;
            if (string.IsNullOrEmpty(id)) return;
            foreach (var a in Log.Items)
            {
                if (a.sent || a.IsDeferred || (a.type != "rename" && a.type != "terrace")) continue;
                if (!JsonValue.TryParse(a.payload, out var body) || !string.IsNullOrEmpty(body["restaurantId"].AsString())) continue;
                a.payload = body.Set("restaurantId", id).ToString();
            }
        }
    }
}

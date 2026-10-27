using System;
using System.Collections.Generic;

namespace DemoRestaurant.Logic.Online
{
    /// <summary>
    /// What the server's ruleset (<c>GET /config</c>) sells, by server id. The client only sends pieces the server
    /// knows; anything else stays local (prototype content the backend has not been given yet).
    /// </summary>
    public sealed class ServerCatalog
    {
        public sealed class Piece
        {
            public string Id;
            /// <summary><c>floor</c>, <c>wall</c>, <c>floorTile</c> or <c>wallpaper</c>.</summary>
            public string Placement;
            public int Price;
            public bool Available;
            public bool OutdoorOnly;
        }

        private readonly Dictionary<string, Piece> _furniture = new Dictionary<string, Piece>();
        private readonly HashSet<string> _exterior = new HashSet<string>();
        private readonly HashSet<string> _items = new HashSet<string>();
        private readonly HashSet<string> _streetCells = new HashSet<string>();
        private readonly HashSet<string> _wardrobe = new HashSet<string>();

        public int RulesetVersion { get; private set; }
        public string RulesetId { get; private set; }
        public string ETag { get; private set; }
        /// <summary>The ruleset sells avatar parts (<c>catalog.wardrobe</c>, v3).</summary>
        public bool HasWardrobe { get; private set; }
        /// <summary>The ruleset pays for cleaned litter (<c>rules.trash</c>, v3).</summary>
        public bool HasTrash { get; private set; }

        /// <summary>Reads a <c>/config</c> body (<c>{ rulesetId, version, etag, body: { catalog, rules } }</c>).</summary>
        public static ServerCatalog FromConfig(JsonValue config)
        {
            var catalog = new ServerCatalog
            {
                RulesetId = config["rulesetId"].AsString(),
                RulesetVersion = config["version"].AsInt(),
                ETag = config["etag"].AsString(),
            };
            var body = config["body"];
            foreach (var f in body["catalog"]["furniture"].Items)
            {
                string id = f["id"].AsString();
                if (string.IsNullOrEmpty(id)) continue;
                catalog._furniture[id] = new Piece
                {
                    Id = id,
                    Placement = f["placement"].AsString() ?? "floor",
                    Price = f["price"].AsInt(),
                    Available = f["available"].AsBool(true),
                    OutdoorOnly = f["outdoorOnly"].AsBool(),
                };
            }
            foreach (var e in body["catalog"]["exterior"].Items)
                if (e["id"].AsString() != null) catalog._exterior.Add(e["id"].AsString());
            foreach (var i in body["catalog"]["items"].Items)
                if (i["id"].AsString() != null) catalog._items.Add(i["id"].AsString());
            catalog.HasWardrobe = body["catalog"]["wardrobe"].Kind == JsonKind.Array;
            foreach (var w in body["catalog"]["wardrobe"].Items)
                catalog._wardrobe.Add(w["slot"].AsString() + ":" + w["id"].AsString());
            catalog.HasTrash = body["rules"]["trash"].Kind == JsonKind.Object;
            foreach (var c in body["rules"]["street"]["cells"].Items)
                catalog._streetCells.Add(Cell(c["x"].AsInt(), c["y"].AsInt()));
            return catalog;
        }

        public Piece Furniture(string id) => id != null && _furniture.TryGetValue(id, out var p) ? p : null;
        public bool HasFurniture(string id, string placement) => Furniture(id)?.Placement == placement;
        public bool HasExterior(string id) => id != null && _exterior.Contains(id);
        public bool HasItem(string id) => id != null && _items.Contains(id);
        public bool IsStreetCell(int x, int y) => _streetCells.Contains(Cell(x, y));
        /// <summary>The server knows this avatar part (server slot id, e.g. <c>hat</c>).</summary>
        public bool HasWardrobePart(string slot, string id) => _wardrobe.Contains(slot + ":" + id);

        /// <summary>The first free indoor floor tile (the fallback for floors the server does not know).</summary>
        public string DefaultFloor
        {
            get
            {
                foreach (var p in _furniture.Values)
                    if (p.Placement == "floorTile" && p.Price == 0 && !p.OutdoorOnly) return p.Id;
                return null;
            }
        }

        /// <summary>The first free terrace tile, or any free floor tile.</summary>
        public string DefaultTerraceFloor
        {
            get
            {
                foreach (var p in _furniture.Values)
                    if (p.Placement == "floorTile" && p.Price == 0 && p.OutdoorOnly) return p.Id;
                return DefaultFloor;
            }
        }

        private static string Cell(int x, int y) => x + "," + y;
    }

    /// <summary>
    /// Local content id ↔ server id, for the few ids that differ (the theme's floors and wallpapers). Data-driven
    /// (<c>Content/&lt;pack&gt;/server_ids.json</c>); unlisted ids are the same on both sides. A local id mapped to an
    /// empty server id means "none" (the theme's default wallpaper is the server's bare wall).
    /// </summary>
    public sealed class ServerIdMap
    {
        [Serializable]
        public sealed class Entry
        {
            public string local;
            public string server;
        }

        [Serializable]
        public sealed class File
        {
            public Entry[] ids = new Entry[0];
        }

        private readonly Dictionary<string, string> _toServer = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _toLocal = new Dictionary<string, string>();

        public ServerIdMap(IEnumerable<Entry> entries = null)
        {
            if (entries == null) return;
            foreach (var e in entries)
            {
                if (e == null || string.IsNullOrEmpty(e.local)) continue;
                if (_toServer.ContainsKey(e.local))
                    throw new Content.ContentException($"server_ids: '{e.local}' is listed twice");
                _toServer[e.local] = e.server ?? "";
                if (!string.IsNullOrEmpty(e.server) && !_toLocal.ContainsKey(e.server)) _toLocal[e.server] = e.local;
            }
        }

        /// <summary>The server id, or null for "none".</summary>
        public string ToServer(string localId)
        {
            if (string.IsNullOrEmpty(localId)) return null;
            if (!_toServer.TryGetValue(localId, out var s)) return localId;
            return s.Length == 0 ? null : s;
        }

        public string ToLocal(string serverId)
        {
            if (string.IsNullOrEmpty(serverId)) return null;
            return _toLocal.TryGetValue(serverId, out var l) ? l : serverId;
        }
    }
}

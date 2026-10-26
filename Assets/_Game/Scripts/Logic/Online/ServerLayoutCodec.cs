using System;
using System.Collections.Generic;
using System.Text;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;

namespace DemoRestaurant.Logic.Online
{
    /// <summary>
    /// Stable server instance ids (UUIDs) for the pieces of a local layout. The server keeps wear per instance, so a
    /// piece keeps its id when it moves: ids are matched by the local piece object first, then by item and position,
    /// then by item alone (a moved copy); only new copies get new ids.
    /// </summary>
    public sealed class InstanceIdBook
    {
        [Serializable]
        public sealed class Record
        {
            public string instanceId;
            public string itemId;
            /// <summary>Floor piece: cell and facing. Wall piece: side (in x) and index (in y), facing -1.</summary>
            public int x, y, facing;
            [NonSerialized] public int runtimeId = -1;
        }

        [Serializable]
        public sealed class List
        {
            public Record[] items = new Record[0];
        }

        private readonly System.Collections.Generic.List<Record> _records = new System.Collections.Generic.List<Record>();

        public IReadOnlyList<Record> Records => _records;

        public List ToList() => new List { items = _records.ToArray() };

        public void Restore(List list)
        {
            _records.Clear();
            if (list?.items != null)
                foreach (var r in list.items)
                    if (r != null && !string.IsNullOrEmpty(r.instanceId)) { r.runtimeId = -1; _records.Add(r); }
        }

        /// <summary>Replaces the book with a server layout's ids (after adopting it).</summary>
        public void Load(JsonValue layout, ServerIdMap ids)
        {
            _records.Clear();
            foreach (var f in layout["furniture"].Items)
                _records.Add(new Record { instanceId = f["instanceId"].AsString(), itemId = ids.ToLocal(f["id"].AsString()),
                    x = f["x"].AsInt(), y = f["y"].AsInt(), facing = f["facing"].AsInt() });
            foreach (var w in layout["walls"].Items)
                _records.Add(new Record { instanceId = w["instanceId"].AsString(), itemId = ids.ToLocal(w["id"].AsString()),
                    x = w["side"].AsInt(), y = w["index"].AsInt(), facing = -1 });
        }

        /// <summary>The id the server knows for a placed piece (as of the last layout sent or adopted), or null.</summary>
        public string Find(PlacedFurniture piece)
        {
            if (piece == null) return null;
            foreach (var r in _records)
                if (r.facing >= 0 && r.runtimeId == piece.InstanceId && r.itemId == piece.Def.Id) return r.instanceId;
            foreach (var r in _records)
                if (r.facing >= 0 && r.itemId == piece.Def.Id && r.x == piece.Origin.X && r.y == piece.Origin.Y && r.facing == (int)piece.Facing)
                    return r.instanceId;
            return null;
        }

        /// <summary>Assigns ids to every piece of <paramref name="layout"/> and makes that the book's content.</summary>
        public Dictionary<object, string> Assign(RestaurantLayout layout, Func<string> newId)
        {
            newId ??= () => Guid.NewGuid().ToString();
            var free = new System.Collections.Generic.List<Record>(_records);
            var result = new Dictionary<object, string>();
            var next = new System.Collections.Generic.List<Record>();

            void Take(object piece, Record r, string itemId, int x, int y, int facing, int runtime)
            {
                if (r != null) free.Remove(r);
                string id = r?.instanceId ?? newId().ToLowerInvariant();
                result[piece] = id;
                next.Add(new Record { instanceId = id, itemId = itemId, x = x, y = y, facing = facing, runtimeId = runtime });
            }

            var floor = new System.Collections.Generic.List<PlacedFurniture>(layout.Furniture);
            var walls = new System.Collections.Generic.List<PlacedWallItem>(layout.WallItems);
            var pending = new System.Collections.Generic.List<PlacedFurniture>();
            foreach (var f in floor)
            {
                var r = free.Find(c => c.facing >= 0 && c.runtimeId == f.InstanceId && c.itemId == f.Def.Id) ??
                        free.Find(c => c.facing >= 0 && c.itemId == f.Def.Id && c.x == f.Origin.X && c.y == f.Origin.Y && c.facing == (int)f.Facing);
                if (r != null) Take(f, r, f.Def.Id, f.Origin.X, f.Origin.Y, (int)f.Facing, f.InstanceId);
                else pending.Add(f);
            }
            foreach (var f in pending)
                Take(f, free.Find(c => c.facing >= 0 && c.itemId == f.Def.Id), f.Def.Id, f.Origin.X, f.Origin.Y, (int)f.Facing, f.InstanceId);

            var pendingWalls = new System.Collections.Generic.List<PlacedWallItem>();
            foreach (var w in walls)
            {
                var r = free.Find(c => c.facing < 0 && c.runtimeId == w.InstanceId && c.itemId == w.Def.Id) ??
                        free.Find(c => c.facing < 0 && c.itemId == w.Def.Id && c.x == (int)w.Side && c.y == w.Index);
                if (r != null) Take(w, r, w.Def.Id, (int)w.Side, w.Index, -1, w.InstanceId);
                else pendingWalls.Add(w);
            }
            foreach (var w in pendingWalls)
                Take(w, free.Find(c => c.facing < 0 && c.itemId == w.Def.Id), w.Def.Id, (int)w.Side, w.Index, -1, w.InstanceId);

            _records.Clear();
            _records.AddRange(next);
            return result;
        }
    }

    /// <summary>
    /// Local restaurant layout ↔ the v2 layout document (<c>GET/PUT /restaurants/{id}/layout</c>, ledger <c>layout</c>).
    /// Both sides use the client grid: (0,0) the back corner, +X down-right, +Y down-left; facings 0-3 as
    /// <see cref="Facing"/>; wall side 0 = back-left. Pieces the server's catalog does not have stay local and are
    /// left out; floors and wallpapers it does not have become its defaults.
    /// </summary>
    public static class ServerLayoutCodec
    {
        /// <summary>The layout fields of a save (no revision, buy or storage).</summary>
        /// <param name="defaultWallpaper">The local wallpaper an unpapered wall shows (the theme's own).</param>
        public static JsonValue Encode(RestaurantLayout layout, ServerCatalog catalog, ServerIdMap ids, InstanceIdBook book,
            Func<string> newId, List<string> leftOut = null, string defaultWallpaper = null)
        {
            ids ??= new ServerIdMap();
            var instance = book.Assign(layout, newId);
            var doc = JsonValue.Object();
            doc.Set("room", JsonValue.Object().Set("width", layout.RoomWidth).Set("height", layout.RoomHeight));
            doc.Set("terrace", JsonValue.Object().Set("width", layout.HasTerrace ? layout.TerraceWidth : 0)
                .Set("depth", layout.HasTerrace ? layout.TerraceDepth : 0));
            var door = layout.Entrance;
            bool leftWall = door.X == 0;
            doc.Set("entrance", JsonValue.Object().Set("side", leftWall ? 0 : 1).Set("index", leftWall ? door.Y : door.X));

            // Floors: one default per area, paint where a cell differs.
            string Floor(string local, string fallback, bool indoor)
            {
                var tile = catalog.Furniture(ids.ToServer(local));
                return tile != null && tile.Placement == "floorTile" && !(indoor && tile.OutdoorOnly) ? tile.Id : fallback;
            }
            string defaultFloor = catalog.DefaultFloor;
            var roomFloors = new List<KeyValuePair<GridPos, string>>();
            var terraceFloors = new List<KeyValuePair<GridPos, string>>();
            for (int y = 0; y < layout.Grid.Height; y++)
            for (int x = 0; x < layout.Grid.Width; x++)
            {
                var cell = new GridPos(x, y);
                if (layout.IsRoomCell(cell)) roomFloors.Add(new KeyValuePair<GridPos, string>(cell, Floor(layout.FloorAt(cell), defaultFloor, true)));
                else if (layout.HasTerrace && layout.IsTerraceCell(cell))
                    terraceFloors.Add(new KeyValuePair<GridPos, string>(cell, Floor(layout.FloorAt(cell), catalog.DefaultTerraceFloor, false)));
            }
            string roomDefault = MostCommon(roomFloors) ?? defaultFloor;
            string terraceDefault = layout.HasTerrace ? MostCommon(terraceFloors) ?? catalog.DefaultTerraceFloor : null;
            doc.Set("floor", roomDefault);
            doc.Set("terraceFloor", terraceDefault);
            var overrides = JsonValue.Array();
            foreach (var pair in roomFloors)
                if (pair.Value != roomDefault) overrides.Add(Paint(pair.Key, pair.Value));
            foreach (var pair in terraceFloors)
                if (pair.Value != terraceDefault) overrides.Add(Paint(pair.Key, pair.Value));
            doc.Set("floorOverrides", overrides);
            doc.Set("wallpaperLeft", Wallpaper(layout, WallSide.Left, catalog, ids, defaultWallpaper));
            doc.Set("wallpaperRight", Wallpaper(layout, WallSide.Right, catalog, ids, defaultWallpaper));

            // Floor pieces: surfaces before what stands on them, so stackedOn can name the base's index.
            var furniture = JsonValue.Array();
            var index = new Dictionary<PlacedFurniture, int>();
            var ordered = new List<PlacedFurniture>();
            foreach (var f in layout.Furniture) if (f.StackedOn == null) ordered.Add(f);
            foreach (var f in layout.Furniture) if (f.StackedOn != null) ordered.Add(f);
            foreach (var f in ordered)
            {
                string id = ids.ToServer(f.Def.Id);
                bool known = catalog.HasFurniture(id, "floor");
                bool placeable = f.StackedOn == null
                    ? layout.IsFloorCell(f.Origin) || catalog.IsStreetCell(f.Origin.X, f.Origin.Y)
                    : index.ContainsKey(f.StackedOn);
                if (!known || !placeable)
                {
                    leftOut?.Add(f.Def.Id);
                    continue;
                }
                var piece = JsonValue.Object().Set("instanceId", instance[f]).Set("id", id)
                    .Set("x", f.Origin.X).Set("y", f.Origin.Y).Set("facing", (int)f.Facing);
                if (f.StackedOn != null) piece.Set("stackedOn", index[f.StackedOn]);
                index[f] = furniture.Count;
                furniture.Add(piece);
            }
            doc.Set("furniture", furniture);

            var walls = JsonValue.Array();
            foreach (var w in layout.WallItems)
            {
                string id = ids.ToServer(w.Def.Id);
                if (!catalog.HasFurniture(id, "wall"))
                {
                    leftOut?.Add(w.Def.Id);
                    continue;
                }
                walls.Add(JsonValue.Object().Set("instanceId", instance[w]).Set("id", id)
                    .Set("side", (int)w.Side).Set("index", w.Index));
            }
            doc.Set("walls", walls);
            return doc;
        }

        /// <summary>
        /// The ledger <c>layout</c> payload: the layout plus <c>buy</c> (copies beyond what the server says is owned,
        /// including priced paint in use) and <c>storage</c> (owned copies not placed). No revision: the server applies
        /// a ledger layout on top of its current one.
        /// </summary>
        public static JsonValue SavePayload(JsonValue layout, string restaurantId, IReadOnlyDictionary<string, int> serverOwned,
            ServerCatalog catalog)
        {
            var placed = PlacedCounts(layout, catalog);
            var buy = new Dictionary<string, int>();
            var storage = new Dictionary<string, int>();
            int Owned(string id) => serverOwned != null && serverOwned.TryGetValue(id, out int n) ? n : 0;
            foreach (var pair in placed)
                if (pair.Value > Owned(pair.Key)) buy[pair.Key] = pair.Value - Owned(pair.Key);
            if (serverOwned != null)
                foreach (var pair in serverOwned)
                {
                    var piece = catalog.Furniture(pair.Key);
                    if (piece == null || piece.Placement == "floorTile" || piece.Placement == "wallpaper") continue;
                    int left = pair.Value - (placed.TryGetValue(pair.Key, out int p) ? p : 0);
                    if (left > 0) storage[pair.Key] = left;
                }
            var payload = JsonValue.Object().Set("restaurantId", restaurantId);
            foreach (var f in layout.Fields) payload.Set(f.Key, f.Value);
            payload.Set("storage", JsonValue.FromCounts(storage));
            payload.Set("buy", JsonValue.FromCounts(buy));
            return payload;
        }

        /// <summary>Copies the layout counts for ownership: one per placed piece; priced paint counts once per id.</summary>
        public static Dictionary<string, int> PlacedCounts(JsonValue layout, ServerCatalog catalog)
        {
            var counts = new Dictionary<string, int>();
            void Add(string id) => counts[id] = (counts.TryGetValue(id, out int n) ? n : 0) + 1;
            foreach (var f in layout["furniture"].Items) Add(f["id"].AsString());
            foreach (var w in layout["walls"].Items) Add(w["id"].AsString());
            var paints = new HashSet<string>();
            foreach (var key in new[] { "floor", "terraceFloor", "wallpaperLeft", "wallpaperRight" })
                if (layout[key].AsString() != null) paints.Add(layout[key].AsString());
            foreach (var o in layout["floorOverrides"].Items) paints.Add(o["id"].AsString());
            foreach (var id in paints)
                if ((catalog.Furniture(id)?.Price ?? 0) > 0) counts[id] = 1;
            return counts;
        }

        /// <summary>
        /// The server layout as a local layout save (for adopting it). <paramref name="isLocalFloor"/> and
        /// <paramref name="isLocalWallpaper"/> say which ids the client can draw; others become the theme default.
        /// </summary>
        public static LayoutSnapshot Decode(JsonValue layout, ServerIdMap ids, Func<string, bool> isLocalFloor,
            Func<string, bool> isLocalWallpaper, LayoutSnapshot localForWaiting = null)
        {
            ids ??= new ServerIdMap();
            int width = layout["room"]["width"].AsInt(), height = layout["room"]["height"].AsInt();
            int tw = layout["terrace"]["width"].AsInt(), td = layout["terrace"]["depth"].AsInt();
            bool terrace = tw > 0 && td > 0;
            int gridWidth = Math.Max(width, terrace ? tw : 0), gridHeight = height + (terrace ? td : 0);
            int side = layout["entrance"]["side"].AsInt(), at = layout["entrance"]["index"].AsInt();
            var snap = new LayoutSnapshot
            {
                entertainmentAxesVersion = 1,
                width = width, height = height,
                terraceWidth = terrace ? tw : 0, terraceDepth = terrace ? td : 0,
                entranceX = side == 0 ? 0 : at, entranceY = side == 0 ? at : 0,
                waiting = new LayoutSnapshot.CellRecord[0],
            };

            string LocalFloor(string serverId)
            {
                string local = ids.ToLocal(serverId);
                return local != null && isLocalFloor != null && isLocalFloor(local) ? local : null;
            }
            snap.floors = new string[gridWidth * gridHeight];
            string roomFloor = LocalFloor(layout["floor"].AsString()), terraceFloor = LocalFloor(layout["terraceFloor"].AsString());
            for (int y = 0; y < gridHeight; y++)
            for (int x = 0; x < gridWidth; x++)
            {
                bool room = x < width && y < height;
                bool onTerrace = terrace && x < tw && y >= height;
                snap.floors[y * gridWidth + x] = room ? roomFloor : onTerrace ? terraceFloor : null;
            }
            foreach (var o in layout["floorOverrides"].Items)
            {
                int x = o["x"].AsInt(), y = o["y"].AsInt();
                if (x >= 0 && y >= 0 && x < gridWidth && y < gridHeight) snap.floors[y * gridWidth + x] = LocalFloor(o["id"].AsString());
            }

            string LocalWallpaper(string serverId)
            {
                string local = ids.ToLocal(serverId);
                return local != null && isLocalWallpaper != null && isLocalWallpaper(local) ? local : null;
            }
            snap.wallpaperLeft = Fill(height, LocalWallpaper(layout["wallpaperLeft"].AsString()));
            snap.wallpaperRight = Fill(width, LocalWallpaper(layout["wallpaperRight"].AsString()));

            // Bases first, so restoring places a stacked piece onto its surface.
            var records = new List<LayoutSnapshot.FurnitureRecord>();
            var stacked = new List<LayoutSnapshot.FurnitureRecord>();
            foreach (var f in layout["furniture"].Items)
            {
                var r = new LayoutSnapshot.FurnitureRecord
                {
                    id = ids.ToLocal(f["id"].AsString()), x = f["x"].AsInt(), y = f["y"].AsInt(), facing = f["facing"].AsInt(),
                    usageCount = layout["wear"][f["instanceId"].AsString() ?? ""]["uses"].AsInt(),
                };
                (f.Has("stackedOn") ? stacked : records).Add(r);
            }
            records.AddRange(stacked);
            snap.furniture = records.ToArray();
            var walls = new List<LayoutSnapshot.WallRecord>();
            foreach (var w in layout["walls"].Items)
                walls.Add(new LayoutSnapshot.WallRecord { id = ids.ToLocal(w["id"].AsString()), side = w["side"].AsInt(), index = w["index"].AsInt() });
            snap.wall = walls.ToArray();
            return snap;
        }

        /// <summary>Canonical text of a layout's content (ids, cells, facings, paint), ignoring instance ids, order,
        /// revision, wear and storage: two layouts that look the same compare equal.</summary>
        public static string Fingerprint(JsonValue layout)
        {
            var parts = new List<string>();
            foreach (var f in layout["furniture"].Items)
                parts.Add("f:" + f["id"].AsString() + "@" + f["x"].AsInt() + "," + f["y"].AsInt() + "/" + f["facing"].AsInt() +
                          (f.Has("stackedOn") ? "s" : ""));
            foreach (var w in layout["walls"].Items)
                parts.Add("w:" + w["id"].AsString() + "@" + w["side"].AsInt() + "," + w["index"].AsInt());
            foreach (var o in layout["floorOverrides"].Items)
                parts.Add("p:" + o["id"].AsString() + "@" + o["x"].AsInt() + "," + o["y"].AsInt());
            parts.Sort(string.CompareOrdinal);
            var sb = new StringBuilder();
            sb.Append(layout["room"]["width"].AsInt()).Append('x').Append(layout["room"]["height"].AsInt())
                .Append('|').Append(layout["terrace"]["width"].AsInt()).Append('x').Append(layout["terrace"]["depth"].AsInt())
                .Append('|').Append(layout["entrance"]["side"].AsInt()).Append(':').Append(layout["entrance"]["index"].AsInt())
                .Append('|').Append(layout["floor"].AsString()).Append('|').Append(layout["terraceFloor"].AsString())
                .Append('|').Append(layout["wallpaperLeft"].AsString()).Append('|').Append(layout["wallpaperRight"].AsString());
            foreach (var p in parts) sb.Append('|').Append(p);
            return sb.ToString();
        }

        /// <summary>What two fingerprints disagree on, for the log: "room 14x13 → 12x11", "+f:sink@2,3/0", "-w:…".</summary>
        public static string Difference(string local, string server, int max = 4)
        {
            if (local == server) return "";
            var a = (local ?? "").Split('|');
            var b = (server ?? "").Split('|');
            var parts = new List<string>();
            string[] heads = { "room", "terrace", "entrance", "floor", "terrace floor", "left wallpaper", "right wallpaper" };
            for (int i = 0; i < heads.Length; i++)
            {
                string x = i < a.Length ? a[i] : "", y = i < b.Length ? b[i] : "";
                if (x != y) parts.Add($"{heads[i]} {x} → {y}");
            }
            var serverPieces = new List<string>();
            for (int i = heads.Length; i < b.Length; i++) serverPieces.Add(b[i]);
            for (int i = heads.Length; i < a.Length; i++)
                if (!serverPieces.Remove(a[i])) parts.Add("+" + a[i]);
            foreach (var p in serverPieces) parts.Add("-" + p);
            int extra = parts.Count - max;
            if (extra > 0) parts.RemoveRange(max, extra);
            return string.Join(", ", parts) + (extra > 0 ? $" (+{extra} more)" : "");
        }

        private static JsonValue Paint(GridPos cell, string id) =>
            JsonValue.Object().Set("x", cell.X).Set("y", cell.Y).Set("id", id);

        private static JsonValue Wallpaper(RestaurantLayout layout, WallSide side, ServerCatalog catalog, ServerIdMap ids,
            string defaultWallpaper)
        {
            var cells = new List<KeyValuePair<GridPos, string>>();
            for (int i = 0; i < layout.WallLength(side); i++)
            {
                string id = ids.ToServer(layout.WallpaperAt(side, i) ?? defaultWallpaper);
                if (id != null && catalog.HasFurniture(id, "wallpaper")) cells.Add(new KeyValuePair<GridPos, string>(new GridPos(i, 0), id));
            }
            return JsonValue.Of(MostCommon(cells));
        }

        private static string MostCommon(List<KeyValuePair<GridPos, string>> cells)
        {
            var counts = new Dictionary<string, int>();
            string best = null;
            int bestCount = 0;
            foreach (var pair in cells)
            {
                if (pair.Value == null) continue;
                counts.TryGetValue(pair.Value, out int n);
                counts[pair.Value] = ++n;
                if (n > bestCount || (n == bestCount && string.CompareOrdinal(pair.Value, best) < 0))
                {
                    best = pair.Value;
                    bestCount = n;
                }
            }
            return best;
        }

        private static string[] Fill(int length, string value)
        {
            var cells = new string[Math.Max(0, length)];
            for (int i = 0; i < cells.Length; i++) cells[i] = value;
            return cells;
        }
    }
}

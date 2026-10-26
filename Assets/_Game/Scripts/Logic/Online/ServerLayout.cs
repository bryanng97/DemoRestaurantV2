using System;
using System.Collections.Generic;
using System.Text;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;

namespace DemoRestaurant.Logic.Online
{
    /// <summary>The backend's layout body (PUT /restaurants/{id}/layout). Field names match the API exactly.</summary>
    [Serializable]
    public sealed class ServerLayout
    {
        public const int SchemaVersion = 1;
        public const int GridSize = 20;
        public const int MaxEntries = 200;

        public int schemaVersion = SchemaVersion;
        public int gridWidth = GridSize;
        public int gridHeight = GridSize;
        public string floorId;
        public string wallId;
        public ServerPlacement[] furniture = new ServerPlacement[0];
    }

    [Serializable]
    public sealed class ServerPlacement
    {
        public string instanceId;
        public string itemId;
        public int x, y;
        /// <summary>Clockwise degrees: 0, 90, 180 or 270.</summary>
        public int rotation;
    }

    /// <summary>
    /// Converts between the local isometric layout and the server layout.
    /// Assumption until layout-contract.md arrives: server cells use our grid axes and the anchor is the
    /// rotated footprint's minimum corner; rotation 0 is our SouthWest art facing, turning clockwise on screen
    /// (SouthWest, NorthWest, NorthEast, SouthEast), so 90/270 are exactly our rotated (swapped) footprints.
    /// </summary>
    public static class ServerLayoutMapper
    {
        public static int ToRotation(Facing facing)
        {
            switch (facing)
            {
                case Facing.SouthWest: return 0;
                case Facing.NorthWest: return 90;
                case Facing.NorthEast: return 180;
                default: return 270;
            }
        }

        public static bool TryFromRotation(int rotation, out Facing facing)
        {
            switch (rotation)
            {
                case 0: facing = Facing.SouthWest; return true;
                case 90: facing = Facing.NorthWest; return true;
                case 180: facing = Facing.NorthEast; return true;
                case 270: facing = Facing.SouthEast; return true;
                default: facing = Facing.SouthWest; return false;
            }
        }

        /// <summary>
        /// Builds the server body. Instance ids are reused from <paramref name="previous"/> for pieces that did not
        /// move, so saves stay stable; new pieces get fresh UUIDs. Returns null and an error when the layout
        /// can't be expressed on the server (too big, unmapped item, too many pieces).
        /// </summary>
        public static ServerLayout ToServer(LayoutSnapshot local, ItemIdMap ids, string wallId, ServerLayout previous,
            Func<string> newId, out string error)
        {
            error = null;
            newId = newId ?? (() => Guid.NewGuid().ToString());
            if (local.width > ServerLayout.GridSize || local.height > ServerLayout.GridSize)
            {
                error = "ROOM_TOO_LARGE";
                return null;
            }
            var records = local.furniture ?? new LayoutSnapshot.FurnitureRecord[0];
            if (records.Length > ServerLayout.MaxEntries)
            {
                error = "TOO_MANY_PIECES";
                return null;
            }

            var reusable = new List<ServerPlacement>(previous?.furniture ?? new ServerPlacement[0]);
            var result = new List<ServerPlacement>(records.Length);
            foreach (var r in records)
            {
                string itemId = ids.ToServer(r.id);
                if (itemId == null)
                {
                    error = "UNMAPPED_ITEM:" + r.id;
                    return null;
                }
                int rotation = ToRotation((Facing)r.facing);
                int match = reusable.FindIndex(p => p.itemId == itemId && p.x == r.x && p.y == r.y && p.rotation == rotation);
                string instance = match >= 0 ? reusable[match].instanceId : newId();
                if (match >= 0) reusable.RemoveAt(match);
                result.Add(new ServerPlacement { instanceId = instance, itemId = itemId, x = r.x, y = r.y, rotation = rotation });
            }

            return new ServerLayout
            {
                floorId = ids.ToServer(MostCommonFloor(local.floors)) ?? previous?.floorId,
                wallId = wallId ?? previous?.wallId,
                furniture = result.ToArray()
            };
        }

        /// <summary>Pieces as local records (unknown items and rotations are skipped and reported).</summary>
        public static List<LayoutSnapshot.FurnitureRecord> ToLocalFurniture(ServerLayout server, ItemIdMap ids,
            List<string> skipped)
        {
            var list = new List<LayoutSnapshot.FurnitureRecord>();
            if (server?.furniture == null) return list;
            foreach (var p in server.furniture)
            {
                string localId = p == null ? null : ids.ToLocal(p.itemId);
                if (localId == null || !TryFromRotation(p.rotation, out var facing))
                {
                    skipped?.Add(p?.itemId);
                    continue;
                }
                list.Add(new LayoutSnapshot.FurnitureRecord { id = localId, x = p.x, y = p.y, facing = (int)facing });
            }
            return list;
        }

        /// <summary>Owned counts per local id from the server inventory (quantity includes placed pieces).</summary>
        public static Dictionary<string, int> ToLocalOwned(IEnumerable<KeyValuePair<string, int>> serverQuantities, ItemIdMap ids)
        {
            var owned = new Dictionary<string, int>();
            foreach (var pair in serverQuantities)
            {
                string local = ids.ToLocal(pair.Key);
                if (local == null || pair.Value <= 0) continue;
                owned.TryGetValue(local, out int n);
                owned[local] = n + pair.Value;
            }
            return owned;
        }

        /// <summary>
        /// Canonical text for comparing layouts after a lost response: furniture sorted by lowercase instance id.
        /// </summary>
        public static string Normalize(ServerLayout layout)
        {
            if (layout == null) return "";
            var pieces = new List<ServerPlacement>(layout.furniture ?? new ServerPlacement[0]);
            pieces.Sort((a, b) => string.CompareOrdinal(a.instanceId?.ToLowerInvariant(), b.instanceId?.ToLowerInvariant()));
            var sb = new StringBuilder();
            sb.Append(layout.schemaVersion).Append('|').Append(layout.gridWidth).Append('x').Append(layout.gridHeight)
                .Append('|').Append(layout.floorId).Append('|').Append(layout.wallId);
            foreach (var p in pieces)
                sb.Append('|').Append(p.instanceId?.ToLowerInvariant()).Append(',').Append(p.itemId).Append(',')
                    .Append(p.x).Append(',').Append(p.y).Append(',').Append(p.rotation);
            return sb.ToString();
        }

        private static string MostCommonFloor(string[] floors)
        {
            if (floors == null) return null;
            var counts = new Dictionary<string, int>();
            string best = null;
            int bestCount = 0;
            foreach (var f in floors)
            {
                if (string.IsNullOrEmpty(f)) continue;
                counts.TryGetValue(f, out int n);
                counts[f] = ++n;
                if (n > bestCount || (n == bestCount && string.CompareOrdinal(f, best) < 0))
                {
                    best = f;
                    bestCount = n;
                }
            }
            return best;
        }
    }

    /// <summary>
    /// Local content id ↔ server catalog item id. Data-driven (item_map.json); ids not listed map to themselves.
    /// </summary>
    public sealed class ItemIdMap
    {
        private readonly Dictionary<string, string> _toServer = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _toLocal = new Dictionary<string, string>();

        [Serializable]
        public sealed class Entry
        {
            public string local;
            public string server;
        }

        [Serializable]
        public sealed class File
        {
            public Entry[] items = new Entry[0];
        }

        public ItemIdMap(IEnumerable<Entry> entries = null)
        {
            if (entries == null) return;
            foreach (var e in entries)
            {
                if (e == null || string.IsNullOrEmpty(e.local) || string.IsNullOrEmpty(e.server)) continue;
                if (_toServer.ContainsKey(e.local) || _toLocal.ContainsKey(e.server))
                    throw new ContentException($"item map: duplicate mapping for '{e.local}' / '{e.server}'");
                _toServer[e.local] = e.server;
                _toLocal[e.server] = e.local;
            }
        }

        public string ToServer(string localId)
        {
            if (string.IsNullOrEmpty(localId)) return null;
            return _toServer.TryGetValue(localId, out var s) ? s : localId;
        }

        public string ToLocal(string serverId)
        {
            if (string.IsNullOrEmpty(serverId)) return null;
            return _toLocal.TryGetValue(serverId, out var l) ? l : serverId;
        }
    }
}

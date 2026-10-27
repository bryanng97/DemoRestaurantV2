using System;
using System.Collections.Generic;

namespace DemoRestaurant.Logic.Online
{
    /// <summary>
    /// One player action waiting for the server: a v2 ledger entry (<c>POST /sync/ledger</c>) with its own
    /// Idempotency-Key. Online and offline actions use the same log, so an action is applied exactly once
    /// however often its batch is resent.
    /// </summary>
    [Serializable]
    public sealed class ServerAction
    {
        public string key;
        /// <summary>Ledger type (<c>sale</c>, <c>hire</c>, <c>layout</c>, ...).</summary>
        public string type;
        /// <summary>Payload JSON. Empty for a deferred entry (layout, menu, exterior) until it is first sent.</summary>
        public string payload;
        public string[] dependsOn;
        /// <summary>What the client already applied locally for this action (coins, XP, demand), so a server state
        /// that does not include it yet can be shown with it added on top.</summary>
        public long coins;
        public int xp;
        public float demand;
        /// <summary>Sent at least once: the payload is frozen and every retry resends it unchanged.</summary>
        public bool sent;
        public string createdUtc;

        public bool IsDeferred => string.IsNullOrEmpty(payload);
    }

    [Serializable]
    public sealed class ServerActionList
    {
        public ServerAction[] items = new ServerAction[0];
        public int dropped;
    }

    /// <summary>What one ledger response did to the log.</summary>
    public sealed class LedgerOutcome
    {
        public readonly List<ServerAction> Accepted = new List<ServerAction>();
        /// <summary>Rejected actions with the server's reason code.</summary>
        public readonly List<KeyValuePair<ServerAction, string>> Rejected = new List<KeyValuePair<ServerAction, string>>();
        public readonly List<ServerAction> Retry = new List<ServerAction>();
        /// <summary>Accepted results by key (e.g. a layout save's new revision).</summary>
        public readonly Dictionary<string, JsonValue> Results = new Dictionary<string, JsonValue>();
    }

    /// <summary>
    /// The keyed action outbox. Actions are recorded as they happen (and applied locally at once, offline-first);
    /// <see cref="NextBatch"/> takes them in order for <c>POST /sync/ledger</c>, and <see cref="Apply"/> drops what the
    /// server accepted or rejected. Layout, menu and exterior saves are "deferred": only the newest unsent one is
    /// kept, and its body is built from the current state when it is first sent.
    /// </summary>
    public sealed class ServerActionLog
    {
        public const int MaxBatch = 200;
        public const int MaxEntries = 5000;
        public const int MaxDemandEventsPerEntry = 100;

        public static readonly string[] DeferredTypes = { "layout", "menu", "exterior", "wardrobe_look" };

        private readonly List<ServerAction> _items = new List<ServerAction>();
        private readonly Func<string> _newKey;
        private readonly Func<DateTime> _utcNow;

        public ServerActionLog(Func<string> newKey = null, Func<DateTime> utcNow = null)
        {
            _newKey = newKey ?? (() => Guid.NewGuid().ToString());
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        /// <summary>Raised after every change, so the owner can persist <see cref="ToList"/>.</summary>
        public event Action Changed;

        public IReadOnlyList<ServerAction> Items => _items;
        public int Count => _items.Count;
        /// <summary>Unsent actions dropped because the log was full (the server state corrects the totals).</summary>
        public int Dropped { get; private set; }

        /// <summary>Records an action with its payload. Returns its key.</summary>
        public string Record(string type, JsonValue payload, long coins = 0, int xp = 0, float demand = 0f,
            IEnumerable<string> dependsOn = null)
        {
            if (string.IsNullOrEmpty(type)) throw new ArgumentException("No type", nameof(type));
            if (payload == null || payload.Kind != JsonKind.Object) throw new ArgumentException("Payload must be an object", nameof(payload));
            var action = Add(type, payload.ToString(), dependsOn);
            action.coins = coins;
            action.xp = xp;
            action.demand = demand;
            Changed?.Invoke();
            return action.key;
        }

        /// <summary>
        /// Marks a deferred save (layout, menu or exterior) as needed. An unsent one of the same type is replaced
        /// by a new entry at the end, so the save follows every action before it and its effects carry over.
        /// </summary>
        public string RecordDeferred(string type, long coins = 0)
        {
            if (Array.IndexOf(DeferredTypes, type) < 0) throw new ArgumentException("Not a deferred type: " + type, nameof(type));
            int previous = _items.FindIndex(a => a.type == type && !a.sent);
            if (previous >= 0)
            {
                coins += _items[previous].coins;
                _items.RemoveAt(previous);
            }
            var action = Add(type, "", null);
            action.coins = coins;
            Changed?.Invoke();
            return action.key;
        }

        /// <summary>
        /// One unhappy guest (or several) for the server's demand. Consecutive unsent events share one entry.
        /// </summary>
        public string RecordDemandEvent(string eventType, float demand)
        {
            var last = _items.Count > 0 ? _items[_items.Count - 1] : null;
            if (last != null && last.type == "demand_events" && !last.sent && JsonValue.TryParse(last.payload, out var body) &&
                body["events"].Count < MaxDemandEventsPerEntry)
            {
                body["events"].Add(JsonValue.Object().Set("type", eventType));
                last.payload = body.ToString();
                last.demand += demand;
                Changed?.Invoke();
                return last.key;
            }
            var events = JsonValue.Array().Add(JsonValue.Object().Set("type", eventType));
            return Record("demand_events", JsonValue.Object().Set("events", events), demand: demand);
        }

        /// <summary>Adds a side effect the client applied for an action (e.g. a level-up reward after a sale).</summary>
        public void AddEffect(string key, long coins, int xp = 0, float demand = 0f)
        {
            var action = Find(key);
            if (action == null) return;
            action.coins += coins;
            action.xp += xp;
            action.demand += demand;
            Changed?.Invoke();
        }

        public ServerAction Find(string key)
        {
            if (key == null) return null;
            foreach (var a in _items)
                if (a.key == key) return a;
            return null;
        }

        /// <summary>The newest unsent deferred entry of a type, or null.</summary>
        public ServerAction PendingDeferred(string type) => _items.FindLast(a => a.type == type && !a.sent);

        /// <summary>
        /// Builds a deferred entry's body now (for example before an action that refers to the new layout), so later
        /// edits start a new entry. Returns false and drops the entry when <paramref name="build"/> returns null.
        /// </summary>
        public bool Freeze(ServerAction action, Func<ServerAction, JsonValue> build)
        {
            if (action == null || !action.IsDeferred) return action != null;
            var body = build?.Invoke(action);
            if (body == null || body.Kind != JsonKind.Object)
            {
                _items.Remove(action);
                Changed?.Invoke();
                return false;
            }
            action.payload = body.ToString();
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// The next actions to send, oldest first (at most <paramref name="max"/>). Deferred entries get their body
        /// from <paramref name="build"/>; one that can't be built (null) is dropped. Everything returned is marked sent.
        /// </summary>
        public List<ServerAction> NextBatch(Func<ServerAction, JsonValue> build, int max = MaxBatch)
        {
            var batch = new List<ServerAction>();
            foreach (var action in new List<ServerAction>(_items))
            {
                if (batch.Count >= Math.Min(max, MaxBatch)) break;
                if (action.IsDeferred && !Freeze(action, build)) continue;
                action.sent = true;
                batch.Add(action);
            }
            if (batch.Count > 0) Changed?.Invoke();
            return batch;
        }

        /// <summary>The ledger request body for a batch.</summary>
        public static JsonValue BatchBody(IEnumerable<ServerAction> batch)
        {
            var entries = JsonValue.Array();
            if (batch != null)
                foreach (var a in batch)
                {
                    var entry = JsonValue.Object().Set("key", a.key).Set("type", a.type);
                    if (!string.IsNullOrEmpty(a.createdUtc)) entry.Set("clientTime", a.createdUtc);
                    if (a.dependsOn != null && a.dependsOn.Length > 0)
                    {
                        var deps = JsonValue.Array();
                        foreach (var d in a.dependsOn) deps.Add(JsonValue.Of(d));
                        entry.Set("dependsOn", deps);
                    }
                    entry.Set("payload", JsonValue.Parse(a.payload));
                    entries.Add(entry);
                }
            return JsonValue.Object().Set("entries", entries);
        }

        /// <summary>
        /// Applies a ledger response's <c>results</c>: accepted and rejected actions leave the log (the server
        /// stored both); retry actions stay, unchanged, for the next batch.
        /// </summary>
        public LedgerOutcome Apply(JsonValue results)
        {
            var outcome = new LedgerOutcome();
            foreach (var r in results.Items)
            {
                var action = Find(r["key"].AsString()?.ToLowerInvariant()) ?? Find(r["key"].AsString());
                if (action == null) continue;
                string kind = r["outcome"].AsString() ?? (r["accepted"].AsBool() ? "accepted" : "retry");
                switch (kind)
                {
                    case "accepted":
                        _items.Remove(action);
                        outcome.Accepted.Add(action);
                        outcome.Results[action.key] = r["result"];
                        break;
                    case "rejected":
                        _items.Remove(action);
                        outcome.Rejected.Add(new KeyValuePair<ServerAction, string>(action, r["reason"].AsString() ?? "REJECTED"));
                        break;
                    default:
                        outcome.Retry.Add(action);
                        break;
                }
            }
            if (outcome.Accepted.Count + outcome.Rejected.Count > 0) Changed?.Invoke();
            return outcome;
        }

        // ---------------------------------------------------------------- predictions not yet in a server state

        public long PendingCoins { get { long n = 0; foreach (var a in _items) n += a.coins; return n; } }
        public int PendingXp { get { int n = 0; foreach (var a in _items) n += a.xp; return n; } }
        public float PendingDemand { get { float n = 0; foreach (var a in _items) n += a.demand; return n; } }

        /// <summary>True if any action of these types still waits for the server.</summary>
        public bool HasPending(params string[] types)
        {
            foreach (var a in _items)
                if (Array.IndexOf(types, a.type) >= 0) return true;
            return false;
        }

        // ---------------------------------------------------------------- persistence

        public ServerActionList ToList() => new ServerActionList { items = _items.ToArray(), dropped = Dropped };

        public void Restore(ServerActionList list)
        {
            _items.Clear();
            Dropped = Math.Max(0, list?.dropped ?? 0);
            if (list?.items != null)
                foreach (var a in list.items)
                    if (a != null && !string.IsNullOrEmpty(a.key) && !string.IsNullOrEmpty(a.type)) _items.Add(a);
        }

        private ServerAction Add(string type, string payload, IEnumerable<string> dependsOn)
        {
            var deps = dependsOn == null ? null : new List<string>(dependsOn).FindAll(d => !string.IsNullOrEmpty(d)).ToArray();
            var action = new ServerAction
            {
                key = _newKey().ToLowerInvariant(),
                type = type,
                payload = payload,
                dependsOn = deps != null && deps.Length > 0 ? deps : null,
                createdUtc = _utcNow().ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture),
            };
            _items.Add(action);
            TrimToCapacity();
            return action;
        }

        // A very long offline stretch: drop the oldest unsent sales first (each is one guest's coins; the server
        // state that follows corrects the balance), never structural actions.
        private void TrimToCapacity()
        {
            while (_items.Count > MaxEntries)
            {
                int i = _items.FindIndex(a => !a.sent && (a.type == "sale" || a.type == "demand_events"));
                if (i < 0) i = _items.FindIndex(a => !a.sent);
                if (i < 0) return;
                _items.RemoveAt(i);
                Dropped++;
            }
        }
    }
}

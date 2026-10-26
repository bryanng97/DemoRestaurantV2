using System;
using System.Collections.Generic;

namespace DemoRestaurant.Logic.Online
{
    /// <summary>
    /// A keyed request (create restaurant, purchase, daily claim) that was persisted before sending.
    /// Retries reuse the same key and body, even after an app restart, so the server applies it once.
    /// </summary>
    [Serializable]
    public sealed class PendingOperation
    {
        public string key;
        public string method;
        public string path;
        public string body;
        public string createdUtc;
    }

    /// <summary>Serializable form of <see cref="PendingOperationStore"/> for the device save.</summary>
    [Serializable]
    public sealed class PendingOperationList
    {
        public PendingOperation[] items = new PendingOperation[0];
    }

    public sealed class PendingOperationStore
    {
        private readonly List<PendingOperation> _items = new List<PendingOperation>();
        private readonly Func<string> _newKey;

        /// <summary>Raised after every change, so the owner can persist <see cref="ToList"/>.</summary>
        public event Action Changed;

        public PendingOperationStore(Func<string> newKey = null)
        {
            _newKey = newKey ?? (() => Guid.NewGuid().ToString());
        }

        public IReadOnlyList<PendingOperation> Items => _items;

        /// <summary>
        /// Returns the unfinished operation for the same request, or records a new one with a fresh key.
        /// Never makes a new key for a request that may already have reached the server.
        /// </summary>
        public PendingOperation Begin(string method, string path, string body, DateTime nowUtc)
        {
            foreach (var op in _items)
                if (op.method == method && op.path == path && op.body == body)
                    return op;
            var created = new PendingOperation
            {
                key = _newKey(), method = method, path = path, body = body, createdUtc = nowUtc.ToString("o")
            };
            _items.Add(created);
            Changed?.Invoke();
            return created;
        }

        /// <summary>The server answered definitively (success or a non-retryable error).</summary>
        public void Complete(string key)
        {
            if (_items.RemoveAll(op => op.key == key) > 0)
                Changed?.Invoke();
        }

        public PendingOperationList ToList() => new PendingOperationList { items = _items.ToArray() };

        public void Restore(PendingOperationList list)
        {
            _items.Clear();
            if (list?.items != null)
                foreach (var op in list.items)
                    if (op != null && !string.IsNullOrEmpty(op.key)) _items.Add(op);
        }
    }
}

using System;
using DemoRestaurant.Logic.Online;

namespace DemoRestaurant.Game.Online
{
    // Response and request shapes of the game API (mobile-integration.md). Field names match the JSON exactly.
    // Request bodies only contain the fields the server accepts: it rejects unknown properties.

    [Serializable] public sealed class Envelope<T> { public T data; public string requestId; }

    [Serializable] public sealed class Page<T> { public T[] items; public string nextCursor; }

    [Serializable] public sealed class ApiProfile { public string id; public string displayName; public string createdAt; }

    [Serializable] public sealed class ApiWallet { public long softCurrency; public string updatedAt; }

    [Serializable] public sealed class ApiBootstrap { public ApiProfile profile; public ApiWallet wallet; }

    [Serializable]
    public sealed class ApiRestaurant
    {
        public string id, ownerId, name, visibility, createdAt, updatedAt;
        public int level;
    }

    [Serializable] public sealed class ApiFootprint { public int width, height; }

    [Serializable]
    public sealed class ApiCatalogItem
    {
        public string itemId, name, category, assetKey;
        /// <summary>0 for floor/wall cosmetics (null in JSON).</summary>
        public int price;
        public ApiFootprint footprint;
        public int[] allowedRotations;
        public bool isAvailable;
    }

    [Serializable]
    public sealed class ApiInventoryEntry { public string itemId; public int quantity, placedQuantity, availableQuantity; }

    [Serializable]
    public sealed class ApiLayoutSnapshot { public int version; public ServerLayout layout; public string updatedAt; }

    [Serializable]
    public sealed class ApiPurchase
    {
        public string receiptId, itemId, createdAt;
        public int quantity, unitPrice, chargedAmount, ownedQuantity;
        public ApiWallet wallet;
    }

    [Serializable]
    public sealed class ApiDailyEligibility { public int amount; public bool canClaim; public string nextEligibleAt, serverTime; }

    [Serializable]
    public sealed class ApiDailyClaim
    {
        public string receiptId, claimedAt, nextEligibleAt;
        public int grantedAmount;
        public ApiWallet wallet;
    }

    [Serializable]
    public sealed class ApiVisit { public string restaurantId, visitId, visitedAt, visitDate; }

    [Serializable] public sealed class ApiErrorDetails { public int currentVersion; public string nextEligibleAt; }

    [Serializable] public sealed class ApiErrorBody { public string code, message; public ApiErrorDetails details; }

    [Serializable] public sealed class ApiErrorEnvelope { public ApiErrorBody error; public string requestId; }

    // ---- request bodies

    [Serializable] internal sealed class CreateRestaurantBody { public string name; }
    [Serializable] internal sealed class RenameBody { public string name; }
    [Serializable] internal sealed class VisibilityBody { public string visibility; }
    [Serializable] internal sealed class RenameAndVisibilityBody { public string name; public string visibility; }
    [Serializable] internal sealed class PurchaseBody { public string itemId; }
    [Serializable] internal sealed class SaveLayoutBody { public int expectedVersion; public ServerLayout layout; }

    /// <summary>Result of one game API call: either <see cref="Data"/> or an error with the action to take.</summary>
    public sealed class ApiResult<T>
    {
        public bool Ok;
        public T Data;
        public int Status;
        public string Code;
        public ApiErrorDetails Details;
        public string RequestId;
        public ApiAction Action;

        /// <summary>The outcome is unknown (network/5xx after all retries). Keyed operations stay pending.</summary>
        public bool Uncertain => !Ok && Action == ApiAction.Backoff;
    }
}

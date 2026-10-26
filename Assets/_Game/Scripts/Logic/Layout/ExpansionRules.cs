using System;

namespace DemoRestaurant.Logic.Layout
{
    /// <summary>Shared catalog policy. The authority computes costs from owned dimensions, never a client price.</summary>
    [Serializable]
    public sealed class ExpansionRules
    {
        public int baseWidth, baseHeight, maxWidth, maxHeight, step;
        public int firstPrice, priceIncrement;

        public bool ValidSize(int width, int height) =>
            step > 0 && baseWidth > 0 && baseHeight > 0 && maxWidth <= 128 && maxHeight <= 128 &&
            width >= baseWidth && height >= baseHeight && width <= maxWidth && height <= maxHeight &&
            (width - baseWidth) % step == 0 && (height - baseHeight) % step == 0;

        public bool TryQuote(int ownedWidth, int ownedHeight, int width, int height, out long price)
        {
            price = 0;
            if (!ValidSize(ownedWidth, ownedHeight) || !ValidSize(width, height) ||
                width < ownedWidth || height < ownedHeight || firstPrice <= 0 || priceIncrement < 0) return false;
            int owned = (ownedWidth - baseWidth + ownedHeight - baseHeight) / step;
            int extra = (width - ownedWidth + height - ownedHeight) / step;
            price = (long)extra * firstPrice + (long)priceIncrement * extra * (2L * owned + extra - 1) / 2;
            return true;
        }
    }
}

using System;

namespace DemoRestaurant.Logic.Layout
{
    /// <summary>
    /// One size of the terrace (outside area) bought in front of the room, as in the reference game: the steps are
    /// bought in order from a "for sale" sign, each from its restaurant level, at the server's price.
    /// </summary>
    [Serializable]
    public sealed class TerraceStep
    {
        public int width, depth, level;
        public long price;

        public bool IsValid => width > 0 && depth > 0 && width <= 64 && depth <= 64 && level >= 1 && price >= 0;
    }
}

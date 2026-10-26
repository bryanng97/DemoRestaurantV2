using System;
using System.Collections.Generic;

namespace DemoRestaurant.Logic.Iso
{
    /// <summary>
    /// One drawable thing on the floor: its footprint in grid space plus its bounds on screen.
    /// Screen bounds come from the renderer and are only used to skip pairs that cannot overlap.
    /// </summary>
    public struct DepthItem
    {
        /// <summary>Footprint in grid space (1 unit = 1 cell). Characters use a small box around their feet.</summary>
        public float MinX, MinY, MaxX, MaxY;

        /// <summary>Base height above the floor, e.g. a customer sitting on a chair draws above the chair.</summary>
        public float Z;

        /// <summary>Screen-space bounds in any consistent unit (world units are fine).</summary>
        public float ScreenMinX, ScreenMinY, ScreenMaxX, ScreenMaxY;

        public float CentreSum => (MinX + MaxX + MinY + MaxY) * 0.5f;

        public static DepthItem FromFootprint(float minX, float minY, float maxX, float maxY, float z = 0f)
        {
            return new DepthItem
            {
                MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY, Z = z,
                ScreenMinX = float.NegativeInfinity, ScreenMinY = float.NegativeInfinity,
                ScreenMaxX = float.PositiveInfinity, ScreenMaxY = float.PositiveInfinity,
            };
        }
    }

    /// <summary>
    /// Sorts isometric objects back-to-front so that tall multi-cell objects (shelves, stoves, counters)
    /// and characters walking around them always draw in the correct order.
    ///
    /// Grid convention: +X runs down-right on screen, +Y runs down-left, so the viewer looks from +X+Y.
    /// For two footprints that do not overlap, the one on the lower side of a separating axis is behind.
    /// Pairs are compared only if their screen bounds overlap, then a topological sort produces the order.
    /// A simple "sort by x+y" breaks for footprints larger than one cell, which is why this exists.
    /// </summary>
    public sealed class IsoDepthSorter
    {
        private const float Epsilon = 1e-4f;

        private List<int>[] _edges = Array.Empty<List<int>>();
        private int[] _inDegree = Array.Empty<int>();
        private bool[] _emitted = Array.Empty<bool>();
        private readonly List<int> _ready = new List<int>();

        /// <summary>
        /// Writes item indices into <paramref name="order"/>, back (first) to front (last).
        /// Runs in O(n^2) over pairs, which costs well under a millisecond for a few hundred items.
        /// </summary>
        public void Sort(IReadOnlyList<DepthItem> items, List<int> order)
        {
            int n = items.Count;
            order.Clear();
            EnsureCapacity(n);

            for (int i = 0; i < n; i++)
            {
                _edges[i].Clear();
                _inDegree[i] = 0;
                _emitted[i] = false;
            }

            for (int i = 0; i < n; i++)
            {
                DepthItem a = items[i];
                for (int j = i + 1; j < n; j++)
                {
                    DepthItem b = items[j];
                    if (!ScreenOverlap(a, b))
                        continue;
                    int cmp = Compare(a, b);
                    if (cmp < 0) AddEdge(i, j);
                    else if (cmp > 0) AddEdge(j, i);
                }
            }

            // Kahn's algorithm. Among ready items, emit the one furthest back first so unrelated
            // items still come out in a stable, sensible order.
            _ready.Clear();
            for (int i = 0; i < n; i++)
            {
                if (_inDegree[i] == 0)
                    _ready.Add(i);
            }

            while (order.Count < n)
            {
                int pick = _ready.Count > 0 ? TakeBackmost(items, _ready) : BreakCycle(items);
                if (_emitted[pick])
                    continue;
                _emitted[pick] = true;
                order.Add(pick);
                foreach (int next in _edges[pick])
                {
                    if (--_inDegree[next] == 0 && !_emitted[next])
                        _ready.Add(next);
                }
            }
        }

        /// <summary>Negative when a is behind b, positive when a is in front of b, 0 when unrelated.</summary>
        public static int Compare(in DepthItem a, in DepthItem b)
        {
            bool aBehindOnX = a.MaxX <= b.MinX + Epsilon;
            bool bBehindOnX = b.MaxX <= a.MinX + Epsilon;
            bool aBehindOnY = a.MaxY <= b.MinY + Epsilon;
            bool bBehindOnY = b.MaxY <= a.MinY + Epsilon;

            bool aBehind = aBehindOnX || aBehindOnY;
            bool bBehind = bBehindOnX || bBehindOnY;

            if (aBehind && !bBehind) return -1;
            if (bBehind && !aBehind) return 1;

            if (!aBehind && !bBehind)
            {
                // Footprints overlap, e.g. a customer sitting on a chair. Higher things draw on top.
                if (Math.Abs(a.Z - b.Z) > Epsilon)
                    return a.Z < b.Z ? -1 : 1;
            }

            // Separated diagonally (or overlapping at the same height): fall back to distance from viewer.
            float sa = a.CentreSum, sb = b.CentreSum;
            if (Math.Abs(sa - sb) <= Epsilon)
                return 0;
            return sa < sb ? -1 : 1;
        }

        private static bool ScreenOverlap(in DepthItem a, in DepthItem b)
        {
            return a.ScreenMinX < b.ScreenMaxX && b.ScreenMinX < a.ScreenMaxX
                && a.ScreenMinY < b.ScreenMaxY && b.ScreenMinY < a.ScreenMaxY;
        }

        private void AddEdge(int from, int to)
        {
            _edges[from].Add(to);
            _inDegree[to]++;
        }

        private static int TakeBackmost(IReadOnlyList<DepthItem> items, List<int> ready)
        {
            int best = 0;
            float bestSum = items[ready[0]].CentreSum;
            for (int k = 1; k < ready.Count; k++)
            {
                float s = items[ready[k]].CentreSum;
                if (s < bestSum || (s == bestSum && ready[k] < ready[best]))
                {
                    best = k;
                    bestSum = s;
                }
            }
            int pick = ready[best];
            ready[best] = ready[ready.Count - 1];
            ready.RemoveAt(ready.Count - 1);
            return pick;
        }

        /// <summary>Cycles should not happen for valid layouts; if one does, release the backmost remaining item.</summary>
        private int BreakCycle(IReadOnlyList<DepthItem> items)
        {
            int pick = -1;
            float bestSum = float.PositiveInfinity;
            for (int i = 0; i < items.Count; i++)
            {
                if (_emitted[i])
                    continue;
                float s = items[i].CentreSum;
                if (s < bestSum)
                {
                    bestSum = s;
                    pick = i;
                }
            }
            return pick;
        }

        private void EnsureCapacity(int n)
        {
            if (_edges.Length >= n)
                return;
            int size = Math.Max(n, _edges.Length * 2);
            var edges = new List<int>[size];
            for (int i = 0; i < size; i++)
                edges[i] = i < _edges.Length ? _edges[i] : new List<int>(8);
            _edges = edges;
            _inDegree = new int[size];
            _emitted = new bool[size];
        }
    }
}

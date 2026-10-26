using System.Collections.Generic;

namespace DemoRestaurant.Logic.Grid
{
    /// <summary>
    /// 4-directional A* over a <see cref="FloorGrid"/>. Movement follows the isometric axes,
    /// which reads naturally on screen. Reuses its buffers, so keep one instance per grid
    /// and call it from one thread only.
    /// </summary>
    public sealed class Pathfinder
    {
        private readonly FloorGrid _grid;
        private int[] _gScore;
        private int[] _cameFrom;
        private int[] _visitStamp;
        private bool[] _closed;
        private int _bufferWidth;
        private readonly MinHeap _open;
        private int _stamp;

        public Pathfinder(FloorGrid grid)
        {
            _grid = grid;
            _bufferWidth = grid.Width;
            int n = grid.Width * grid.Height;
            _gScore = new int[n];
            _cameFrom = new int[n];
            _visitStamp = new int[n];
            _closed = new bool[n];
            _open = new MinHeap(n);
        }

        /// <summary>
        /// Finds a path from start to goal. Blocked goals require explicit permission (for entering
        /// a reserved chair only). A blocked start may be left. The list excludes start and includes goal.
        /// Returns false when no path exists.
        /// </summary>
        public bool TryFindPath(GridPos start, GridPos goal, List<GridPos> result, bool allowBlockedGoal = false)
        {
            result.Clear();
            int cells = _grid.Width * _grid.Height;
            if (_bufferWidth != _grid.Width || _gScore.Length != cells)
            {
                _gScore = new int[cells]; _cameFrom = new int[cells];
                _visitStamp = new int[cells]; _closed = new bool[cells];
                _bufferWidth = _grid.Width; _stamp = 0;
            }
            if (!_grid.InBounds(start) || !_grid.InBounds(goal))
                return false;
            if (!allowBlockedGoal && !_grid.IsWalkable(goal))
                return false;
            if (start == goal)
                return true;

            unchecked { _stamp++; }
            int width = _grid.Width;
            int startIdx = start.Y * width + start.X;
            int goalIdx = goal.Y * width + goal.X;

            _open.Clear();
            Touch(startIdx);
            _gScore[startIdx] = 0;
            _cameFrom[startIdx] = -1;
            _open.Push(startIdx, start.ManhattanDistance(goal));

            while (_open.Count > 0)
            {
                int current = _open.Pop();
                if (_closed[current])
                    continue;
                if (current == goalIdx)
                {
                    Reconstruct(goalIdx, startIdx, result);
                    return true;
                }
                _closed[current] = true;

                var cp = new GridPos(current % width, current / width);
                foreach (var d in GridPos.Neighbours4)
                {
                    var np = cp + d;
                    if (!_grid.InBounds(np))
                        continue;
                    int ni = np.Y * width + np.X;
                    if (!_grid.IsWalkable(np) && !(allowBlockedGoal && ni == goalIdx))
                        continue;

                    bool seen = _visitStamp[ni] == _stamp;
                    if (seen && _closed[ni])
                        continue;
                    int tentative = _gScore[current] + 1;
                    if (seen && tentative >= _gScore[ni])
                        continue;

                    Touch(ni);
                    _gScore[ni] = tentative;
                    _cameFrom[ni] = current;
                    _open.Push(ni, tentative + np.ManhattanDistance(goal));
                }
            }
            return false;
        }

        private void Touch(int idx)
        {
            if (_visitStamp[idx] != _stamp)
            {
                _visitStamp[idx] = _stamp;
                _closed[idx] = false;
            }
        }

        private void Reconstruct(int goalIdx, int startIdx, List<GridPos> result)
        {
            int width = _grid.Width;
            for (int i = goalIdx; i != startIdx; i = _cameFrom[i])
                result.Add(new GridPos(i % width, i / width));
            result.Reverse();
        }

        /// <summary>Binary min-heap of (index, priority) with lazy deletion.</summary>
        private sealed class MinHeap
        {
            private (int item, int priority)[] _items;
            public int Count { get; private set; }

            public MinHeap(int capacity) => _items = new (int, int)[capacity > 0 ? capacity : 4];

            public void Clear() => Count = 0;

            public void Push(int item, int priority)
            {
                if (Count == _items.Length)
                    System.Array.Resize(ref _items, _items.Length * 2);
                int i = Count++;
                _items[i] = (item, priority);
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (_items[parent].priority <= _items[i].priority)
                        break;
                    (_items[parent], _items[i]) = (_items[i], _items[parent]);
                    i = parent;
                }
            }

            public int Pop()
            {
                int top = _items[0].item;
                _items[0] = _items[--Count];
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1, r = l + 1, smallest = i;
                    if (l < Count && _items[l].priority < _items[smallest].priority) smallest = l;
                    if (r < Count && _items[r].priority < _items[smallest].priority) smallest = r;
                    if (smallest == i)
                        break;
                    (_items[smallest], _items[i]) = (_items[i], _items[smallest]);
                    i = smallest;
                }
                return top;
            }
        }
    }
}

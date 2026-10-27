using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Grid;

namespace DemoRestaurant.Logic.Simulation
{
    [Serializable]
    public sealed class TrashPiece
    {
        public int id, x, y, variant;
        public GridPos Cell => new GridPos(x, y);
    }

    public sealed partial class RestaurantSim
    {
        private readonly List<TrashPiece> _trash = new List<TrashPiece>();
        private readonly HashSet<int> _claimedTrash = new HashSet<int>();
        private readonly List<GridPos> _trashCells = new List<GridPos>();
        private readonly List<TrashPiece> _invalidTrash = new List<TrashPiece>();
        private int _nextTrashId = 1;
        public IReadOnlyList<TrashPiece> Trash => _trash;
        public int TrashCount => _trash.Count;
        public float TrashSpawnTimer { get; private set; }
        public float TrashTurnAwayChance => Math.Min(1f, TrashCount * Math.Max(0f, _settings.Trash.turnAwayChancePerPiece));
        public event Action<TrashPiece> TrashCleaned;

        private bool ValidTrashCell(GridPos cell) => cell.X < _layout.RoomWidth && cell.Y < _layout.RoomHeight &&
            cell != _layout.Entrance && DoorRegion().Contains(cell);

        /// <summary>Timer driven litter, independent of guests. Spawn checks never block the walkability grid.</summary>
        public void TickTrash(float dt)
        {
            if (dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            _invalidTrash.Clear();
            foreach (var piece in _trash) if (!ValidTrashCell(piece.Cell)) _invalidTrash.Add(piece);
            foreach (var piece in _invalidTrash) RemoveTrash(piece, reward: false);
            var rules = _settings.Trash;
            if (rules.spawnSeconds <= 0 || rules.maxPieces <= 0) return;
            double elapsed = TrashSpawnTimer + (double)dt;
            int count = (int)Math.Min(rules.maxPieces, Math.Floor(elapsed / rules.spawnSeconds));
            TrashSpawnTimer = (float)(elapsed % rules.spawnSeconds);
            // Bound the work even after months away; no backlog is released after reaching the cap.
            for (int i = 0; i < count && _trash.Count < rules.maxPieces; i++)
                if (!SpawnTrash()) break;
        }

        private bool SpawnTrash()
        {
            _trashCells.Clear();
            foreach (var cell in DoorRegion())
            {
                if (!ValidTrashCell(cell)) continue;
                bool occupied = false;
                foreach (var piece in _trash) if (piece.Cell == cell) { occupied = true; break; }
                if (!occupied) _trashCells.Add(cell);
            }
            if (_trashCells.Count == 0) return false;
            var chosen = _trashCells[_random.Next(_trashCells.Count)];
            _trash.Add(new TrashPiece { id = _nextTrashId++, x = chosen.X, y = chosen.Y, variant = _random.Next(5) });
            return true;
        }

        /// <summary>Both the player's tap and a cleaner complete through this single, idempotent removal.</summary>
        public bool CleanTrash(int id)
        {
            foreach (var piece in _trash)
                if (piece.id == id) { RemoveTrash(piece, reward: true); return true; }
            return false;
        }

        private void RemoveTrash(TrashPiece piece, bool reward)
        {
            if (!_trash.Remove(piece)) return;
            _claimedTrash.Remove(piece.id);
            foreach (var staff in _staff)
                if (staff.TrashTarget == piece) { ReleaseTrash(staff); GoHome(staff); }
            if (reward) TrashCleaned?.Invoke(piece);
        }

        public void RestoreTrash(IEnumerable<TrashPiece> pieces, float timer)
        {
            foreach (var s in _staff) if (s.TrashTarget != null) { ReleaseTrash(s); GoHome(s); }
            _trash.Clear(); _claimedTrash.Clear(); _nextTrashId = 1;
            var rules = _settings.Trash;
            TrashSpawnTimer = rules.spawnSeconds > 0 && !float.IsNaN(timer) && !float.IsInfinity(timer)
                ? Math.Max(0, timer) % rules.spawnSeconds : 0;
            if (pieces == null || rules.spawnSeconds <= 0) return;
            var cells = new HashSet<GridPos>();
            foreach (var piece in pieces)
            {
                if (piece == null || !ValidTrashCell(piece.Cell) || !cells.Add(piece.Cell)) continue;
                if (_trash.Count >= Math.Max(0, rules.maxPieces)) break;
                _trash.Add(new TrashPiece { id = _nextTrashId++, x = piece.x, y = piece.y, variant = Math.Abs(piece.variant % 5) });
            }
        }

        private bool TryStartTrashCleaning(Staff s)
        {
            TrashPiece best = null; int distance = int.MaxValue;
            foreach (var piece in _trash)
            {
                int d = s.Position.Cell.ManhattanDistance(piece.Cell);
                if (_claimedTrash.Contains(piece.id) || d >= distance) continue;
                // Test every candidate: an unreachable nearest piece must not starve the rest.
                if (!StartPath(s, piece.Cell)) continue;
                best = piece; distance = d;
            }
            if (best == null) return false;
            StartPath(s, best.Cell);
            _claimedTrash.Add(best.id); s.TrashTarget = best;
            s.State = StaffState.WalkingToTrash; s.CookProgress = 0;
            return true;
        }

        private void UpdateTrashCleaning(Staff s, float dt)
        {
            var piece = s.TrashTarget;
            if (piece == null || !_trash.Contains(piece)) { ReleaseTrash(s); GoHome(s); return; }
            if (s.State == StaffState.WalkingToTrash)
            {
                if (FollowPath(s, dt)) { s.State = StaffState.PickingTrash; s.Timer = Math.Max(.1f, _settings.Trash.cleanSeconds); }
                return;
            }
            s.Timer -= dt * s.EffectiveCookSpeed;
            s.CookProgress = Math.Min(1, 1 - s.Timer / Math.Max(.1f, _settings.Trash.cleanSeconds));
            if (s.Timer <= 0) CleanTrash(piece.id);
        }

        private void ReleaseTrash(Staff s)
        {
            if (s.TrashTarget != null) _claimedTrash.Remove(s.TrashTarget.id);
            s.TrashTarget = null; s.CookProgress = 0;
        }
    }
}

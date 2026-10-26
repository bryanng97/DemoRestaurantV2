using System.Collections.Generic;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Iso;
using DemoRestaurant.Logic.Progression;
using DemoRestaurant.Logic.Session;
using UnityEngine;

namespace DemoRestaurant.Game.Render
{
    /// <summary>
    /// Draws the farm behind the back-right wall: nine 2 x 2 plots in a fenced 6 x 6 field, from the skin's field and plot
    /// sprites when it has them, otherwise placeholders (grass while locked, soil once open; pale when
    /// the water ran out) with a round plant per cell in the ingredient's colour that grows with the plot's progress.
    /// Placeholder art until the designers deliver farm sprites. Everything sorts behind the walls.
    /// </summary>
    public sealed class FarmView : MonoBehaviour
    {
        private const float RefreshSeconds = 0.5f;
        private const int Steps = 5; // plant sizes drawn as the crop grows

        private static readonly Color Grass = new Color(0.55f, 0.74f, 0.45f), GrassDark = new Color(0.50f, 0.69f, 0.41f);
        private static readonly Color Soil = new Color(0.52f, 0.36f, 0.24f), SoilDark = new Color(0.46f, 0.31f, 0.20f);
        private static readonly Color DrySoil = new Color(0.72f, 0.60f, 0.46f), DrySoilDark = new Color(0.66f, 0.55f, 0.42f);

        private sealed class PlotView
        {
            public SpriteRenderer Ground;
            public readonly List<SpriteRenderer> Crops = new List<SpriteRenderer>();
            public PlotState State = (PlotState)(-1);
            public int Step = -1;
        }

        private readonly PlotView[] _plots = new PlotView[Garden.PlotCount];
        private readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>();
        private System.Func<bool> _tapsBlocked;
        private System.Action<int> _plotTapped;
        private Vector2 _pressAt;
        private float _pressTime;
        private bool _pressed;
        private RestaurantSession _session;
        private LoadedContentPack _content;
        private Transform _root;
        private float _timer;
        private ArtLibrary _art;
        private float _artPixelsPerCell; // the skin's floor-cell width in pixels (0 = no skin art)

        /// <param name="tapsBlocked">True while taps on the world mean something else (the build editor).</param>
        /// <param name="plotTapped">Called with the plot index when the player taps the farm.</param>
        public void Bind(RestaurantSession session, LoadedContentPack content, System.Func<bool> tapsBlocked = null,
            System.Action<int> plotTapped = null)
        {
            _session = session;
            _tapsBlocked = tapsBlocked;
            _plotTapped = plotTapped;
            _content = content;
            _art = ArtLibrary.For(content.Theme);
            _artPixelsPerCell = _art.SkinOf("garden_field")?.tilePixelWidth ?? 0f;
            session.Layout.BoundsChanged += Build; // the field sits at the room's outer end: follow expansions
            Build();
        }

        private void OnDestroy()
        {
            if (_session != null) _session.Layout.BoundsChanged -= Build;
        }

        private void Build()
        {
            if (_root != null) Destroy(_root.gameObject);
            _root = new GameObject("Farm").transform;
            _root.SetParent(transform, false);
            int width = _session.Layout.RoomWidth;
            // A wooden fence: a one-cell border under the field.
            var field = Garden.FieldOrigin(width);
            var fieldArt = _art.GetOptional("garden_field");
            if (fieldArt != null && _artPixelsPerCell > 0)
                PlaceCentred(NewRenderer("Field", Vector2.zero, RestaurantView.OutsideGroundOrder), fieldArt,
                    IsoProjection.GridToWorld(field.X + Garden.FieldSize * 0.5f, field.Y + Garden.FieldSize * 0.5f));
            else
                NewRenderer("Fence", IsoProjection.GridToWorld(new GridPos(field.X - 1, field.Y - 1)), RestaurantView.OutsideGroundOrder)
                    .sprite = Ground("fence", new Color(0.62f, 0.44f, 0.28f), new Color(0.55f, 0.38f, 0.24f), Garden.FieldSize + 2);
            for (int i = 0; i < Garden.PlotCount; i++)
            {
                var origin = Garden.PlotOrigin(i, width);
                var view = new PlotView { Ground = NewRenderer($"Plot{i}", IsoProjection.GridToWorld(origin), RestaurantView.OutsideGroundOrder + 1) };
                for (int y = 0; y < Garden.PlotSize; y++)
                for (int x = 0; x < Garden.PlotSize; x++)
                {
                    var cell = new GridPos(origin.X + x, origin.Y + y);
                    // Back rows first: a crop further from the camera never covers one in front of it.
                    int depth = cell.X + cell.Y - (Garden.FieldOrigin(width).X + Garden.FieldOrigin(width).Y);
                    var crop = NewRenderer($"Crop{i}_{x}_{y}", (Vector2)IsoProjection.GridToWorld(cell.X + 0.5f, cell.Y + 0.5f) + new Vector2(0f, 0.08f),
                        RestaurantView.OutsideGroundOrder + 3 + depth);
                    crop.sprite = UI.UiArt.Circle();
                    crop.enabled = false;
                    view.Crops.Add(crop);
                }
                _plots[i] = view;
            }
            _timer = 0f;
            Refresh();
        }

        private void Update()
        {
            if (_session == null) return;
            _timer -= Time.unscaledDeltaTime;
            if (_timer <= 0f) Refresh();
            HandleTap();
        }

        private void Refresh()
        {
            _timer = RefreshSeconds;
            var garden = _session.Garden;
            var now = _session.UtcNow;
            int level = _session.Progress.Level;
            for (int i = 0; i < Garden.PlotCount; i++)
            {
                var view = _plots[i];
                var state = garden.StateOf(i, level, now);
                int step = state == PlotState.Ready ? Steps : (int)(garden.Progress(i, now) * (Steps - 1)) + 1;
                if (state == view.State && step == view.Step) continue;
                view.State = state;
                view.Step = step;

                var skinPlot = PlotArt(state, garden.WaterLeft(i, now).TotalSeconds / System.Math.Max(1.0, garden.WateringSeconds));
                if (skinPlot != null)
                {
                    var origin = Garden.PlotOrigin(i, _session.Layout.RoomWidth);
                    PlaceCentred(view.Ground, skinPlot, IsoProjection.GridToWorld(origin.X + Garden.PlotSize * 0.5f, origin.Y + Garden.PlotSize * 0.5f));
                }
                else
                    view.Ground.sprite = state == PlotState.Locked ? Ground("grass", Grass, GrassDark)
                        : state == PlotState.Dry ? Ground("dry", DrySoil, DrySoilDark)
                        : Ground("soil", Soil, SoilDark);
                bool planted = state == PlotState.Growing || state == PlotState.Dry || state == PlotState.Ready;
                var crop = garden.CropOf(i);
                foreach (var sr in view.Crops)
                {
                    sr.enabled = planted && crop != null;
                    if (!sr.enabled) continue;
                    var color = _content.ColorForIngredient(crop.Id);
                    if (state == PlotState.Dry) color = Color.Lerp(color, Color.gray, 0.5f);
                    sr.color = color;
                    // A seedling to a full round plant (about a third of a cell wide when ready).
                    float size = 0.10f + 0.24f * step / Steps;
                    float scale = size / Mathf.Max(0.001f, sr.sprite.bounds.size.x);
                    sr.transform.localScale = new Vector3(scale, scale * 0.85f, 1f);
                }
            }
        }

        /// <summary>
        /// The skin's plot sprite for a state: locked, bare, or soil from dry to soaked by the water left (null = no skin art).
        /// </summary>
        private Sprite PlotArt(PlotState state, double waterLeft)
        {
            if (_artPixelsPerCell <= 0) return null;
            if (state == PlotState.Locked) return _art.GetOptional("garden_plot_locked");
            if (state == PlotState.Empty || state == PlotState.Ready) return _art.GetOptional("garden_plot_empty");
            if (state == PlotState.Dry) return _art.GetOptional("garden_plot_wet_00");
            int wet = 1 + Mathf.Clamp(Mathf.CeilToInt((float)waterLeft * 3f) - 1, 0, 2); // frames 01..03
            return _art.GetOptional($"garden_plot_wet_{wet:D2}") ?? _art.GetOptional("garden_plot_empty");
        }

        /// <summary>Draws a skin sprite at its native size (bottom-centre pivot) with its middle on <paramref name="centre"/>.</summary>
        private void PlaceCentred(SpriteRenderer sr, Sprite sprite, Vector2 centre)
        {
            sr.sprite = sprite;
            float scale = 2f * IsoProjection.HalfTileWidth / _artPixelsPerCell * sprite.pixelsPerUnit;
            sr.transform.localScale = new Vector3(scale, scale, 1f);
            sr.transform.localPosition = new Vector3(centre.x, centre.y - sprite.bounds.size.y * scale * 0.5f, 0f);
        }

        /// <summary>A short press and release without dragging (so panning the camera never opens the farm).</summary>
        private void HandleTap()
        {
            var pointer = UnityEngine.InputSystem.Pointer.current;
            if (pointer == null || _plotTapped == null) return;
            var pos = pointer.position.ReadValue();
            if (pointer.press.wasPressedThisFrame)
            {
                _pressed = !UI.UiInput.IsPointerOverUi(pos) && (_tapsBlocked == null || !_tapsBlocked());
                _pressAt = pos;
                _pressTime = Time.unscaledTime;
            }
            if (!pointer.press.wasReleasedThisFrame || !_pressed) return;
            _pressed = false;
            if ((pos - _pressAt).sqrMagnitude > 20f * 20f || Time.unscaledTime - _pressTime > 0.5f || Camera.main == null) return;
            var world = transform.InverseTransformPoint(Camera.main.ScreenToWorldPoint(pos));
            var g = IsoProjection.WorldToGrid(world);
            int plot = Garden.PlotAt(new GridPos(Mathf.FloorToInt(g.X), Mathf.FloorToInt(g.Y)), _session.Layout.RoomWidth);
            if (plot >= 0) _plotTapped(plot);
        }

        private SpriteRenderer NewRenderer(string name, Vector2 position, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.transform.localPosition = position;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = order;
            return sr;
        }

        private Sprite Ground(string key, Color a, Color b, int size = Garden.PlotSize)
        {
            if (!_sprites.TryGetValue(key, out var sprite))
                _sprites[key] = sprite = PlaceholderArt.Floor(size, size, a, b);
            return sprite;
        }

    }
}

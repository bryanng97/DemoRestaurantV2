using System.Collections.Generic;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Simulation;
using UnityEngine;

namespace DemoRestaurant.Game.Render
{
    /// <summary>Procedural tap cues, queued-task progress and completion sparkles; no economy state.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class TableCleaningFeedback : MonoBehaviour
    {
        private sealed class Cue
        {
            public Transform Root;
            public SpriteRenderer Halo, Back, Progress;
        }
        private sealed class Spark
        {
            public SpriteRenderer Renderer;
            public Vector3 Origin, Velocity;
            public float Age;
        }
        private RestaurantSim _sim;
        private RestaurantView _view;
        private Sprite _halo, _pixel, _star;
        private readonly Dictionary<PlacedFurniture, Cue> _cues = new Dictionary<PlacedFurniture, Cue>();
        private readonly List<PlacedFurniture> _remove = new List<PlacedFurniture>();
        private readonly List<Spark> _sparks = new List<Spark>();

        public void Bind(RestaurantSim sim, RestaurantView view)
        {
            _sim = sim; _view = view;
            _halo = TextureSprite("CleaningHalo", 96, 48, (x, y) =>
            {
                float r = Mathf.Sqrt(x * x + y * y);
                return new Color(1, 1, 1, Mathf.Clamp01(1 - Mathf.Abs(r - .73f) / .25f));
            });
            _pixel = TextureSprite("CleaningProgress", 1, 1, (x, y) => Color.white);
            _star = TextureSprite("CleaningSpark", 32, 32, (x, y) =>
                new Color(1, 1, 1, Mathf.Clamp01((1 - Mathf.Sqrt(Mathf.Abs(x)) - Mathf.Sqrt(Mathf.Abs(y))) * 5)));
            sim.SeatCleaned += OnCleaned;
        }

        private static Sprite TextureSprite(string name, int w, int h, System.Func<float, float, Color> color)
        {
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = name };
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                pixels[y * w + x] = color((x + .5f) / w * 2 - 1, (y + .5f) / h * 2 - 1);
            texture.SetPixels(pixels); texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(.5f, .5f), 100);
        }

        private SpriteRenderer Renderer(Transform parent, string name, Sprite sprite)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var renderer = go.AddComponent<SpriteRenderer>(); renderer.sprite = sprite;
            return renderer;
        }

        private void LateUpdate()
        {
            if (_sim == null) return;
            foreach (var seat in _sim.DirtySeats)
            {
                var plate = _view.DirtyPlateRenderer(seat);
                if (plate == null) continue;
                if (!_cues.TryGetValue(seat, out var cue))
                {
                    var root = new GameObject("DirtyTableCue").transform; root.SetParent(transform, false);
                    cue = new Cue { Root = root };
                    cue.Halo = Renderer(root, "TapGlow", _halo);
                    cue.Back = Renderer(root, "TaskTrack", _pixel);
                    cue.Progress = Renderer(root, "TaskProgress", _pixel);
                    _cues.Add(seat, cue);
                }
                cue.Root.gameObject.SetActive(plate.gameObject.activeInHierarchy && plate.enabled);
                cue.Root.position = plate.bounds.center;
                float width = Mathf.Max(.24f, plate.bounds.size.x * 1.5f);
                float progress = _sim.PlayerCleaningProgress(seat);
                cue.Halo.transform.localScale = new Vector3(width / _halo.bounds.size.x, width * .5f / _halo.bounds.size.y, 1);
                cue.Halo.color = progress >= 0 ? new Color(1, .94f, .65f, .45f) : new Color(1, .94f, .7f, .2f + .12f * Mathf.Sin(Time.unscaledTime * 3));
                cue.Halo.sortingOrder = plate.sortingOrder + 1;
                cue.Back.enabled = cue.Progress.enabled = progress >= 0;
                cue.Back.transform.localPosition = new Vector3(0, .2f, 0);
                cue.Back.transform.localScale = new Vector3(.36f / _pixel.bounds.size.x, .045f / _pixel.bounds.size.y, 1);
                cue.Back.color = new Color(.16f, .12f, .08f, .85f); cue.Back.sortingOrder = plate.sortingOrder + 2;
                float fill = .36f * Mathf.Max(.025f, progress);
                cue.Progress.transform.localPosition = new Vector3((fill - .36f) * .5f, .2f, 0);
                cue.Progress.transform.localScale = new Vector3(fill / _pixel.bounds.size.x, .025f / _pixel.bounds.size.y, 1);
                cue.Progress.color = new Color(.4f, 1, .65f); cue.Progress.sortingOrder = plate.sortingOrder + 3;
            }
            _remove.Clear();
            foreach (var pair in _cues) if (!_sim.IsSeatDirty(pair.Key)) _remove.Add(pair.Key);
            foreach (var seat in _remove) { Destroy(_cues[seat].Root.gameObject); _cues.Remove(seat); }
            for (int i = _sparks.Count - 1; i >= 0; i--)
            {
                var spark = _sparks[i]; spark.Age += Time.deltaTime;
                if (spark.Age >= .65f) { Destroy(spark.Renderer.gameObject); _sparks.RemoveAt(i); continue; }
                spark.Renderer.transform.position = spark.Origin + spark.Velocity * spark.Age;
                spark.Renderer.color = new Color(1, .92f, .5f, 1 - spark.Age / .65f);
            }
        }

        private void OnCleaned(PlacedFurniture seat)
        {
            var plate = _view.DirtyPlateRenderer(seat);
            if (plate == null || !plate.enabled || !plate.gameObject.activeInHierarchy) return;
            for (int i = 0; i < 6; i++)
            {
                float angle = i * Mathf.PI / 3;
                var renderer = Renderer(transform, "CleanSpark", _star);
                renderer.transform.localScale = Vector3.one * .24f;
                renderer.sortingOrder = 29000;
                _sparks.Add(new Spark { Renderer = renderer, Origin = plate.bounds.center,
                    Velocity = new Vector3(Mathf.Cos(angle) * .25f, .3f + Mathf.Sin(angle) * .15f) });
            }
        }

        private void OnDestroy()
        {
            if (_sim != null) _sim.SeatCleaned -= OnCleaned;
            foreach (var sprite in new[] { _halo, _pixel, _star })
                if (sprite != null) { Destroy(sprite.texture); Destroy(sprite); }
        }
    }
}

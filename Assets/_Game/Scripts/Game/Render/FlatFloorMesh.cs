using System.Collections.Generic;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Logic.Layout;
using UnityEngine;

namespace DemoRestaurant.Game.Render
{
    /// <summary>One flat mesh for the room. Samples only each tile's top face, excluding the illustrated sides.</summary>
    public sealed class FlatFloorMesh : MonoBehaviour
    {
        private sealed class Surface
        {
            public Vector2[] Uv;
            public int Material;
        }

        private RestaurantLayout _layout;
        private LoadedContentPack _content;
        private ArtLibrary _art;
        private Mesh _mesh;
        private MeshRenderer _renderer;
        private Sprite _fallback;
        private readonly Dictionary<string, Surface> _surfaces = new Dictionary<string, Surface>();
        private readonly List<Material> _materials = new List<Material>();
        private readonly List<List<int>> _triangles = new List<List<int>>();
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector2> _uv = new List<Vector2>();
        private readonly List<Color32> _colors = new List<Color32>();

        public void Bind(RestaurantLayout layout, LoadedContentPack content, ArtLibrary art, int order)
        {
            _layout = layout; _content = content; _art = art;
            _mesh = new Mesh { name = "Flat restaurant floor" };
            _mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.sortingOrder = order;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            Refresh();
        }

        public void Refresh()
        {
            _vertices.Clear(); _uv.Clear(); _colors.Clear();
            foreach (var triangles in _triangles) triangles.Clear();
            for (int y = 0; y < _layout.Grid.Height; y++)
            for (int x = 0; x < _layout.Grid.Width; x++)
            {
                var cell = new Logic.Grid.GridPos(x, y);
                if (!_layout.IsFloorCell(cell)) continue; // beside a narrower terrace: lawn
                // The terrace is paved with its own outdoor tile unless the player laid another.
                var surface = GetSurface(_layout.FloorAt(cell) ?? (_layout.IsTerraceCell(cell) ? _content.Theme.terraceFloor : null));
                int first = _vertices.Count;
                _vertices.Add(IsoProjection.GridToWorld(x, y));
                _vertices.Add(IsoProjection.GridToWorld(x + 1, y));
                _vertices.Add(IsoProjection.GridToWorld(x + 1, y + 1));
                _vertices.Add(IsoProjection.GridToWorld(x, y + 1));
                for (int i = 0; i < 4; i++) { _uv.Add(surface.Uv[i]); _colors.Add(new Color32(255, 255, 255, 255)); }
                var triangles = _triangles[surface.Material];
                triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
                triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
            }
            _mesh.Clear();
            _mesh.SetVertices(_vertices); _mesh.SetUVs(0, _uv); _mesh.SetColors(_colors);
            _mesh.subMeshCount = _materials.Count;
            for (int i = 0; i < _triangles.Count; i++) _mesh.SetTriangles(_triangles[i], i);
            _mesh.RecalculateBounds();
            _renderer.sharedMaterials = _materials.ToArray();
        }

        private Surface GetSurface(string id)
        {
            string spriteName = _content.Theme.floorTile;
            FloorArt option = null;
            foreach (var floor in _content.Theme.floors)
                if (floor.id == id) { option = floor; spriteName = floor.sprite; break; }
            if (option == null)
                foreach (var floor in _content.Theme.floors)
                    if (floor.sprite == spriteName) { option = floor; break; }
            string key = spriteName ?? "default";
            if (_surfaces.TryGetValue(key, out var existing)) return existing;
            var sprite = _art.Get(spriteName);
            // A skin brings its own tile shape; content corners describe the base art only.
            Vector2[] corners = _art.SkinOf(spriteName) != null ? _art.HintsFor(spriteName)?.surfaceCorners : option?.surfaceCorners;
            if (sprite == null)
            {
                if (_fallback == null) _fallback = PlaceholderArt.Floor(1, 1,
                    new Color(.86f, .76f, .62f), new Color(.80f, .69f, .55f));
                sprite = _fallback;
                corners = new[] { new Vector2(.5f, .94f), new Vector2(.97f, .5f),
                    new Vector2(.5f, .06f), new Vector2(.03f, .5f) };
            }
            if (corners == null || corners.Length != 4)
                corners = new[] { new Vector2(.5f, 1), new Vector2(1, .5f), new Vector2(.5f, 0), new Vector2(0, .5f) };
            int materialIndex = _materials.FindIndex(m => m.mainTexture == sprite.texture);
            if (materialIndex < 0)
            {
                materialIndex = _materials.Count;
                var material = new Material(Shader.Find("Sprites/Default")) { name = "Flat floor", mainTexture = sprite.texture };
                _materials.Add(material); _triangles.Add(new List<int>());
            }
            var surface = new Surface { Material = materialIndex, Uv = new Vector2[4] };
            for (int i = 0; i < 4; i++) surface.Uv[i] = SpriteUv(sprite, corners[i]);
            _surfaces.Add(key, surface);
            return surface;
        }

        // Derive an affine UV transform from sprite vertices. Works with tight-packed/rotated atlases,
        // without readable textures or textureRect (which is unavailable for tight-packed sprites).
        private static Vector2 SpriteUv(Sprite sprite, Vector2 normalizedPixel)
        {
            var vertices = sprite.vertices; var uv = sprite.uv;
            var point = new Vector2(normalizedPixel.x * sprite.rect.width - sprite.pivot.x,
                normalizedPixel.y * sprite.rect.height - sprite.pivot.y) / sprite.pixelsPerUnit;
            for (int i = 1; i < vertices.Length; i++)
            for (int j = i + 1; j < vertices.Length; j++)
            {
                Vector2 a = vertices[i] - vertices[0], b = vertices[j] - vertices[0];
                float determinant = a.x * b.y - a.y * b.x;
                if (Mathf.Abs(determinant) < .000001f) continue;
                Vector2 p = point - vertices[0];
                float s = (p.x * b.y - p.y * b.x) / determinant;
                float t = (a.x * p.y - a.y * p.x) / determinant;
                return uv[0] + s * (uv[i] - uv[0]) + t * (uv[j] - uv[0]);
            }
            return uv[0];
        }

        private void OnDestroy()
        {
            Release(_mesh);
            foreach (var material in _materials) Release(material);
            if (_fallback != null) { Release(_fallback.texture); Release(_fallback); }
        }

        private static void Release(Object asset)
        { if (asset == null) return; if (Application.isPlaying) Destroy(asset); else DestroyImmediate(asset); }
    }
}

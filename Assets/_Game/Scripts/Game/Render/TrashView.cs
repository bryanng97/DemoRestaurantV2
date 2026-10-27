using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Session;
using DemoRestaurant.Logic.Simulation;
using UnityEngine;

namespace DemoRestaurant.Game.Render
{
    /// <summary>Ground litter. Input is routed through WallTapHandler's existing UI/drag/pinch guards.</summary>
    public sealed class TrashView : MonoBehaviour
    {
        private static readonly string[] Keys = { "trash_banana_peel", "trash_pizza_slice", "trash_soda_can", "trash_chicken_bone", "trash_apple_core" };
        private readonly Dictionary<int, SpriteRenderer> _renderers = new Dictionary<int, SpriteRenderer>();
        private readonly List<int> _remove = new List<int>();
        private readonly List<Sprite> _generated = new List<Sprite>();
        private readonly Sprite[] _sprites = new Sprite[5];
        private readonly float[] _nativePixelsPerCell = new float[5];
        private RestaurantSession _session;
        private Func<bool> _hidden;

        public void Bind(RestaurantSession session, ArtLibrary art, Func<bool> hidden)
        {
            _session = session; _hidden = hidden;
            for (int i = 0; i < _sprites.Length; i++)
            {
                _sprites[i] = art.GetOptional(Keys[i]);
                _nativePixelsPerCell[i] = art.SkinOf(Keys[i])?.tilePixelWidth ?? 0;
                if (_sprites[i] == null) { _sprites[i] = DrawLitter(i); _generated.Add(_sprites[i]); }
            }
        }

        private void LateUpdate()
        {
            if (_session == null) return;
            _remove.Clear(); _remove.AddRange(_renderers.Keys);
            foreach (var piece in _session.Sim.Trash)
            {
                if (!_renderers.TryGetValue(piece.id, out var renderer))
                {
                    var go = new GameObject("Trash_" + piece.id); go.transform.SetParent(transform, false);
                    renderer = go.AddComponent<SpriteRenderer>(); renderer.sprite = _sprites[piece.variant];
                    // Above ground and shadows, below furniture and people.
                    renderer.sortingOrder = -28950;
                    go.transform.localScale = Vector3.one * (_nativePixelsPerCell[piece.variant] > 0
                        ? renderer.sprite.pixelsPerUnit / _nativePixelsPerCell[piece.variant] : .34f / renderer.sprite.bounds.size.x);
                    _renderers.Add(piece.id, renderer);
                }
                renderer.sprite = _sprites[piece.variant];
                renderer.transform.position = IsoProjection.GridToWorld(piece.x + .5f, piece.y + .5f);
                renderer.enabled = !(_hidden?.Invoke() ?? false);
                _remove.Remove(piece.id);
            }
            foreach (int id in _remove) { _renderers[id].enabled = false; Destroy(_renderers[id].gameObject); _renderers.Remove(id); }
        }

        public bool TryTap(Vector2 world)
        {
            if (_session == null || (_hidden?.Invoke() ?? false)) return false;
            foreach (var pair in _renderers)
                if (pair.Value.enabled && pair.Value.bounds.Contains(new Vector3(world.x, world.y, pair.Value.bounds.center.z)))
                    return _session.Sim.CleanTrash(pair.Key);
            return false;
        }

        private static Sprite DrawLitter(int kind)
        {
            const int w = 64, h = 40;
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                float u = (x - 32f) / 28f, v = (y - 20f) / 16f;
                Color c = Color.clear;
                // Small, distinct silhouettes until the art team supplies the optional sprite keys above.
                if (kind == 0 && ((Mathf.Abs(v + .4f - u * u * .9f) < .17f && Mathf.Abs(u) < .8f) || (Mathf.Abs(u + v * .45f) < .13f && v > -.45f && v < .8f))) c = new Color(1,.82f,.18f);
                if (kind == 1 && u > -.8f && u < .8f && v > -.65f && v < .65f - Mathf.Abs(u) * 1.5f) c = v < -.4f ? new Color(.67f,.35f,.14f) : new Color(1,.75f,.3f);
                if (kind == 2 && Mathf.Abs(u + v * .2f) < .36f && Mathf.Abs(v) < .75f) c = Mathf.Abs(v) > .58f ? new Color(.8f,.84f,.86f) : new Color(.78f,.15f,.12f);
                if (kind == 3 && ((Mathf.Abs(v - u * .35f) < .15f && Mathf.Abs(u) < .75f) || (Mathf.Abs(u) > .52f && Mathf.Abs(u) < .85f && Mathf.Abs(v - u * .35f) < .34f))) c = new Color(.96f,.9f,.72f);
                if (kind == 4 && Mathf.Abs(u) < .36f + .19f * Mathf.Abs(v) && Mathf.Abs(v) < .75f) c = Mathf.Abs(v) > .43f ? new Color(.8f,.18f,.1f) : new Color(.95f,.9f,.64f);
                pixels[y*w+x] = c;
            }
            var texture = new Texture2D(w,h,TextureFormat.RGBA32,false) { name = Keys[kind], filterMode = FilterMode.Bilinear };
            texture.SetPixels(pixels); texture.Apply(false,true);
            return Sprite.Create(texture,new Rect(0,0,w,h),new Vector2(.5f,.5f),100);
        }

        private void OnDestroy()
        {
            foreach (var sprite in _generated) { Destroy(sprite.texture); Destroy(sprite); }
        }
    }
}

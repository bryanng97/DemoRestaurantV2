using System;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Session;
using UnityEngine;

namespace DemoRestaurant.Game.Render
{
    /// <summary>
    /// The for-sale sign on the lawn in front of the restaurant's street-side corner (beyond the terrace, if any), as
    /// in the reference game. Shown while another terrace size is for sale; a short tap opens the sale dialog.
    /// </summary>
    public sealed class TerraceSignView : MonoBehaviour
    {
        private RestaurantSession _session;
        private Func<bool> _blocked;
        private Action _tapped;
        private SpriteRenderer _sign;
        private Vector2 _pressAt;
        private float _pressTime;
        private bool _pressed;

        /// <param name="blocked">True while taps mean something else (decorate mode).</param>
        public void Bind(RestaurantSession session, LoadedContentPack content, Func<bool> blocked, Action tapped)
        {
            _session = session; _blocked = blocked; _tapped = tapped;
            var art = ArtLibrary.For(content.Theme);
            _sign = new GameObject("ForSaleSign").AddComponent<SpriteRenderer>();
            _sign.transform.SetParent(transform, false);
            _sign.sortingOrder = RestaurantView.OutsideGroundOrder + 4;
            var sprite = art.GetOptional("sale_sign");
            var skin = art.SkinOf("sale_sign");
            if (sprite != null && skin != null && skin.tilePixelWidth > 0)
            {
                _sign.sprite = sprite;
                float s = 2f * IsoProjection.HalfTileWidth / skin.tilePixelWidth * sprite.pixelsPerUnit;
                _sign.transform.localScale = new Vector3(s, s, 1f);
            }
            else _sign.sprite = PlaceholderArt.Prism(1, 1, 1.2f, new Color(0.85f, 0.65f, 0.35f));
            session.Layout.BoundsChanged += Place;
            Place();
        }

        private void OnDestroy()
        {
            if (_session != null) _session.Layout.BoundsChanged -= Place;
        }

        /// <summary>One cell beyond the front of the room (or terrace), next to the street.</summary>
        private void Place()
        {
            var layout = _session.Layout;
            var cell = new GridPos(1, layout.Grid.Height + 1);
            _sign.transform.localPosition = IsoProjection.GridToWorld(cell.X + 0.5f, cell.Y + 0.5f) + new Vector2(0f, -IsoProjection.HalfTileHeight);
        }

        private void Update()
        {
            if (_session == null) return;
            bool show = _session.NextTerrace != null && (_blocked == null || !_blocked());
            if (_sign.enabled != show) _sign.enabled = show;
            if (!show) return;
            var pointer = UnityEngine.InputSystem.Pointer.current;
            if (pointer == null) return;
            var pos = pointer.position.ReadValue();
            if (pointer.press.wasPressedThisFrame)
            {
                _pressed = !UI.UiInput.IsPointerOverUi(pos);
                _pressAt = pos; _pressTime = Time.unscaledTime;
            }
            if (!pointer.press.wasReleasedThisFrame || !_pressed) return;
            _pressed = false;
            if ((pos - _pressAt).sqrMagnitude > 20f * 20f || Time.unscaledTime - _pressTime > 0.5f || Camera.main == null) return;
            var world = Camera.main.ScreenToWorldPoint(pos);
            var bounds = _sign.bounds;
            bounds.Expand(new Vector3(0.2f, 0.2f, 100f));
            if (bounds.Contains(new Vector3(world.x, world.y, bounds.center.z))) _tapped?.Invoke();
        }
    }
}

using UnityEngine;

namespace DemoRestaurant.Game.Render
{
    /// <summary>The back-view stove mat follows the body/ghost's visibility without becoming a walk obstacle.</summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class FurnitureWorkMat : MonoBehaviour
    {
        public SpriteRenderer Body;
        private SpriteRenderer _mat;
        public void Refresh()
        {
            if (_mat == null) _mat = GetComponent<SpriteRenderer>();
            _mat.enabled = Body != null && Body.enabled;
            if (Body != null) _mat.color = Body.color;
        }
        private void LateUpdate() => Refresh();
    }
}

using DemoRestaurant.Logic.Content;
using UnityEngine;

namespace DemoRestaurant.Game.Render
{
    /// <summary>Native serving proportions stay unchanged across kitchen, hands, eating and empty containers.</summary>
    public static class DishPresentation
    {
        public const float PlateWidth = 0.32f;
        public const float DrinkWidth = 0.20f;
        public static float WidthFor(DishDef dish, Sprite sprite = null, float tilePixelWidth = 0) =>
            sprite != null && tilePixelWidth > 0 ? sprite.rect.width / tilePixelWidth :
            dish != null && dish.Category == DishCategory.Drink ? DrinkWidth : PlateWidth;
        public static void Fit(SpriteRenderer renderer, Sprite sprite, float width)
        {
            renderer.sprite = sprite;
            if (sprite == null) return;
            float scale = width / sprite.bounds.size.x;
            renderer.transform.localScale = new Vector3(scale,scale,1);
        }
        /// <summary>Align the saucer or bowl's base to its surface; shared animation bounds prevent jumping.</summary>
        public static float PivotOffset(Sprite sprite, float width, float anchorY = -1f) => sprite == null ? 0 :
            (sprite.pivot.y - (anchorY >= 0 ? sprite.rect.height * anchorY : sprite.rect.width / 4f)) / sprite.rect.width * width;
    }
}

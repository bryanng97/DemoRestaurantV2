using DemoRestaurant.Game.Content;
using UnityEngine;

namespace DemoRestaurant.Game.Render
{
    /// <summary>Fits the illustrated base, rather than the whole image rectangle, inside its blocked cells.</summary>
    public static class FurnitureGrounding
    {
        public static Vector2[] BaseCorners(Sprite sprite, FurnitureArt art, bool mirrored)
        {
            if (art.GroundCorners == null || art.GroundCorners.Length != 3) return null;
            var points = new Vector2[4];
            for (int i = 0; i < 3; i++)
            {
                var pixel = art.GroundCorners[i];
                points[i] = new Vector2(pixel.x * sprite.rect.width - sprite.pivot.x,
                    pixel.y * sprite.rect.height - sprite.pivot.y) / sprite.pixelsPerUnit;
                if (mirrored) points[i].x = -points[i].x;
            }
            points[3] = points[0] + points[2] - points[1];
            return points;
        }

        public static bool Apply(SpriteRenderer renderer, FurnitureArt art, Vector2 centre, int sx, int sy)
        {
            var corners = BaseCorners(renderer.sprite, art, renderer.flipX);
            if (corners == null) return false;
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = -min;
            foreach (var point in corners)
            {
                var grid = IsoProjection.WorldToGrid(point);
                min = Vector2.Min(min, new Vector2(grid.X, grid.Y));
                max = Vector2.Max(max, new Vector2(grid.X, grid.Y));
            }
            var size = max - min;
            if (size.x <= 0 || size.y <= 0) return false;
            const float inset = .04f;
            float scale = Mathf.Min((sx - 2 * inset) / size.x, (sy - 2 * inset) / size.y) * Mathf.Min(1, art.Scale);
            var baseCentre = IsoProjection.GridToWorld((min.x + max.x) * .5f, (min.y + max.y) * .5f);
            renderer.transform.localPosition = centre - baseCentre * scale;
            renderer.transform.localScale = new Vector3(scale, scale, 1);
            return true;
        }
    }
}

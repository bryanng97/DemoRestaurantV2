using System;
using System.Collections.Generic;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace DemoRestaurant.Game.UI
{
    /// <summary>Grid of every ingredient with the owned count; tapping one calls back (or does nothing).</summary>
    public sealed class IngredientGrid
    {
        private readonly List<Cell> _cells = new List<Cell>();

        public IngredientGrid(Transform parent, float x, float y, float width, IReadOnlyList<IngredientDef> ingredients,
            LoadedContentPack content, Action<IngredientDef> onTap, int columns = 6, float cellHeight = 118f)
        {
            var root = UiKit.Place(UiKit.NewRect("Ingredients", parent), x, y, width, cellHeight * Mathf.Ceil(ingredients.Count / (float)columns));
            float cellWidth = width / columns;
            for (int i = 0; i < ingredients.Count; i++)
            {
                var ingredient = ingredients[i];
                var cell = new Cell { Ingredient = ingredient };
                var slot = UiKit.Place(UiKit.NewRect(ingredient.Id, root), (i % columns) * cellWidth, (i / columns) * cellHeight, cellWidth, cellHeight);
                cell.Group = slot.gameObject.AddComponent<CanvasGroup>();

                var chip = UiKit.Chip(slot, "Chip", content.ColorForIngredient(ingredient.Id), "", 84f, out cell.Name);
                chip.anchorMin = chip.anchorMax = new Vector2(0.5f, 1f);
                chip.pivot = new Vector2(0.5f, 1f);
                chip.anchoredPosition = new Vector2(0, -4f);
                if (onTap != null)
                    UiKit.AddButton(chip, () => onTap(ingredient));

                var badge = UiKit.Box(chip, "Count", UiKit.Wood, UiKit.Wood, 0f);
                badge.anchorMin = badge.anchorMax = badge.pivot = new Vector2(1f, 0f);
                badge.anchoredPosition = new Vector2(10f, -6f);
                badge.sizeDelta = new Vector2(48f, 32f);
                cell.Count = UiKit.AddText(UiKit.NewRect("N", badge), 20, TextAnchor.MiddleCenter, Color.white);
                UiKit.Stretch(cell.Count.rectTransform);

                _cells.Add(cell);
            }
        }

        /// <param name="reserved">Units already placed elsewhere (e.g. in the recipe pot), shown as used.</param>
        public void Refresh(Inventory inventory, IReadOnlyDictionary<IngredientDef, int> reserved = null)
        {
            foreach (var cell in _cells)
            {
                int owned = inventory.Count(cell.Ingredient);
                int used = reserved != null && reserved.TryGetValue(cell.Ingredient, out int r) ? r : 0;
                int left = owned - used;
                cell.Name.text = Loc.Get(cell.Ingredient.NameKey);
                cell.Count.text = left.ToString();
                cell.Group.alpha = left > 0 ? 1f : 0.4f;
            }
        }

        private sealed class Cell
        {
            public IngredientDef Ingredient;
            public CanvasGroup Group;
            public Text Name, Count;
        }
    }
}

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ScrapYardKing.UI
{
    /// <summary>
    /// Shrinks a fixed-column <see cref="GridLayoutGroup"/>'s cell width so its columns always fit the available width
    /// (tall phones have a narrower canvas). Cells never grow past <see cref="maxCellWidth"/>.
    /// </summary>
    [RequireComponent(typeof(GridLayoutGroup))]
    public sealed class GridColumnsFit : UIBehaviour
    {
        [SerializeField, Min(1f)] float maxCellWidth = 310f;

        GridLayoutGroup grid;

        protected override void OnEnable()
        {
            base.OnEnable();
            Fit();
        }

        protected override void OnRectTransformDimensionsChange() => Fit();

        void Fit()
        {
            if (grid == null) grid = GetComponent<GridLayoutGroup>();
            if (grid == null || grid.constraint != GridLayoutGroup.Constraint.FixedColumnCount) return;

            int columns = Mathf.Max(1, grid.constraintCount);
            float width = ((RectTransform)transform).rect.width - grid.padding.horizontal - grid.spacing.x * (columns - 1);
            if (width <= 0f) return;
            float cell = Mathf.Min(maxCellWidth, Mathf.Floor(width / columns));
            if (!Mathf.Approximately(grid.cellSize.x, cell)) grid.cellSize = new Vector2(cell, grid.cellSize.y);
        }
    }
}

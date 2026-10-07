using UnityEngine;

namespace ScrapYardKing.Economy
{
    /// <summary>
    /// Where a change of balance came from, so the HUD can fly coins or diamonds from it: a world position (a cash pile,
    /// a truck), a screen point (a reward popup's icon) or nowhere (a purchase, a load).
    /// </summary>
    public readonly struct CurrencyOrigin
    {
        public readonly Vector3? World;
        public readonly Vector2? Screen;

        CurrencyOrigin(Vector3? world, Vector2? screen)
        {
            World = world;
            Screen = screen;
        }

        public bool HasValue => World.HasValue || Screen.HasValue;

        public static CurrencyOrigin FromWorld(Vector3 position) => new(position, null);
        public static CurrencyOrigin FromScreen(Vector2 point) => new(null, point);

        /// <summary>Screen point of a UI element on a Screen Space Overlay canvas.</summary>
        public static CurrencyOrigin FromUI(RectTransform element) =>
            element == null ? default : new CurrencyOrigin(null, RectTransformUtility.WorldToScreenPoint(null, element.position));
    }
}

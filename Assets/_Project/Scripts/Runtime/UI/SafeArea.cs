using UnityEngine;

namespace ScrapYardKing.UI
{
    /// <summary>
    /// Fits this RectTransform to <see cref="Screen.safeArea"/> (notches, rounded corners, gesture bars). Put HUD
    /// elements under it; full-screen dimmers stay outside so they still cover the whole screen. Runs in Play mode
    /// only, so the scene file keeps plain full-screen anchors.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeArea : MonoBehaviour
    {
        /// <summary>
        /// Dev tools only: extra insets in pixels (left, right, top, bottom) on top of <see cref="Screen.safeArea"/>, to
        /// check a notch / gesture-bar layout in the Editor (AgentScripts/Tools/UiAudit.cs). Zero in the game.
        /// </summary>
        public static Vector4 Simulated;

        Rect applied;
        Vector2Int size;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Simulated = Vector4.zero;

        static Rect Area
        {
            get
            {
                var r = Screen.safeArea;
                if (Simulated == Vector4.zero) return r;
                return Rect.MinMaxRect(r.xMin + Simulated.x, r.yMin + Simulated.w, r.xMax - Simulated.y, r.yMax - Simulated.z);
            }
        }

        void OnEnable() => Fit();

        void Update()
        {
            if (Area != applied || size.x != Screen.width || size.y != Screen.height) Fit();
        }

        void Fit()
        {
            var rt = (RectTransform)transform;
            applied = Area;
            size = new Vector2Int(Screen.width, Screen.height);
            if (size.x <= 0 || size.y <= 0) return;
            rt.anchorMin = new Vector2(applied.xMin / size.x, applied.yMin / size.y);
            rt.anchorMax = new Vector2(applied.xMax / size.x, applied.yMax / size.y);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}

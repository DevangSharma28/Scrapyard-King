using ScrapYardKing.Core;
using UnityEngine;

namespace ScrapYardKing.Harvest
{
    /// <summary>
    /// Marks this object's collider footprint as a place where loose items must not come to rest (a scrap mound against a
    /// wall, a container stack). Items that land on it hop back toward open ground, so the player, the Porters and the
    /// guide never chase a piece that is buried where nobody can stand.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class DropBlocker : MonoBehaviour
    {
        [Tooltip("Extra metres around the collider (covers the gap between a pile and the wall behind it).")]
        [SerializeField, Min(0f)] float margin = 0.35f;

        HarvestManager harvest;
        Bounds area;
        bool registered;

        void Start() => Register();

        void OnEnable()
        {
            // Start has run before when the object is re-enabled.
            if (harvest != null) Register();
        }

        void OnDisable()
        {
            if (!registered || harvest == null) return;
            harvest.RemoveNoRestArea(area);
            registered = false;
        }

        void Register()
        {
            if (registered || (harvest == null && !Services.TryGet(out harvest))) return;
            area = GetComponent<Collider>().bounds;
            area.Expand(new Vector3(margin * 2f, 0f, margin * 2f));
            harvest.AddNoRestArea(area);
            registered = true;
        }
    }
}

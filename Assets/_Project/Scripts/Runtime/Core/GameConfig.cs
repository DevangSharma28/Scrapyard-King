using ScrapYardKing.Feedback;
using UnityEngine;

namespace ScrapYardKing.Core
{
    /// <summary>Root configuration asset. Global (non per-entity) tuning hangs off this.</summary>
    [CreateAssetMenu(fileName = "GameConfig", menuName = "Scrap Yard King/Game Config")]
    public sealed class GameConfig : ScriptableObject
    {
        [SerializeField] FeedbackConfig feedback;

        public FeedbackConfig Feedback => feedback;
    }
}

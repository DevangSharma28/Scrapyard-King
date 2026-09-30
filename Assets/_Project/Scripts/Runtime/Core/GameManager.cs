using DG.Tweening;
using UnityEngine;

namespace ScrapYardKing.Core
{
    /// <summary>Composition root: owns the global config and app-level settings. Runs before every other system.</summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GameManager : ServiceBehaviour<GameManager>
    {
        [SerializeField] GameConfig config;
        [SerializeField, Min(30)] int targetFrameRate = 60;
        [Tooltip("Pre-allocated tween slots. Items, cash and UI all tween, so this avoids growth spikes mid-play.")]
        [SerializeField, Min(50)] int tweenCapacity = 600;

        public GameConfig Config => config;

        protected override void Awake()
        {
            base.Awake();
            if (config == null) Debug.LogError("[GameManager] GameConfig is not assigned.", this);
            Application.targetFrameRate = targetFrameRate;
            DOTween.Init(recycleAllByDefault: false, useSafeMode: true, logBehaviour: LogBehaviour.ErrorsOnly)
                .SetCapacity(tweenCapacity, tweenCapacity / 4);
        }
    }
}

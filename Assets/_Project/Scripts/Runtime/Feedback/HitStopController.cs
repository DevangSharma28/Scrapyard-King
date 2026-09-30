using ScrapYardKing.Core;
using UnityEngine;

namespace ScrapYardKing.Feedback
{
    /// <summary>Owns short time-scale freezes ("hit-stop"). Overlapping requests extend rather than stack.</summary>
    [DefaultExecutionOrder(-500)]
    public sealed class HitStopController : ServiceBehaviour<HitStopController>
    {
        [SerializeField] bool hitStopEnabled = true;

        float endTime;
        bool isStopped;

        public bool IsStopped => isStopped;

        public void Trigger(float duration, float timeScale)
        {
            if (!hitStopEnabled || duration <= 0f) return;

            endTime = Mathf.Max(isStopped ? endTime : 0f, Time.unscaledTime + duration);
            Time.timeScale = isStopped ? Mathf.Min(Time.timeScale, Mathf.Clamp01(timeScale)) : Mathf.Clamp01(timeScale);
            isStopped = true;
        }

        void Update()
        {
            if (isStopped && Time.unscaledTime >= endTime) Restore();
        }

        void OnDisable()
        {
            if (isStopped) Restore();
        }

        void Restore()
        {
            isStopped = false;
            Time.timeScale = 1f;
        }
    }
}

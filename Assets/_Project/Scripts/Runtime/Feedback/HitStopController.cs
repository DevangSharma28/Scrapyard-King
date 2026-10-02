using ScrapYardKing.Core;
using UnityEngine;

namespace ScrapYardKing.Feedback
{
    /// <summary>
    /// Owns short time-scale freezes ("hit-stop"). Overlapping requests extend rather than stack, and the time scale
    /// that was active before the freeze is restored (so a sped-up play test or slow-motion moment survives hits).
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public sealed class HitStopController : ServiceBehaviour<HitStopController>
    {
        [SerializeField] bool hitStopEnabled = true;

        float endTime, restoreScale = 1f;
        bool isStopped;

        public bool IsStopped => isStopped;

        public void Trigger(float duration, float timeScale)
        {
            if (!hitStopEnabled || duration <= 0f) return;

            endTime = Mathf.Max(isStopped ? endTime : 0f, Time.unscaledTime + duration);
            if (!isStopped) restoreScale = Time.timeScale;
            float frozen = restoreScale * Mathf.Clamp01(timeScale);
            Time.timeScale = isStopped ? Mathf.Min(Time.timeScale, frozen) : frozen;
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
            Time.timeScale = restoreScale;
        }
    }
}

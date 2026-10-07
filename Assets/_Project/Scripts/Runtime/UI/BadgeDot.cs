using TMPro;
using UnityEngine;

namespace ScrapYardKing.UI
{
    /// <summary>A red dot with a count on a navigation button; pops when the count goes up.</summary>
    public sealed class BadgeDot : MonoBehaviour
    {
        [SerializeField] string key;
        [SerializeField] RectTransform dot;
        [SerializeField] TMP_Text count;

        int shown = -1;
        float next;

        void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.4f;
            int n = Badges.Count(key);
            if (n == shown) return;
            bool grew = n > shown && shown >= 0;
            shown = n;
            if (dot != null) dot.gameObject.SetActive(n > 0);
            if (count != null) count.SetText(n > 9 ? "9+" : n.ToString());
            if (grew && dot != null) UIAnim.RewardPop(dot);
        }
    }
}

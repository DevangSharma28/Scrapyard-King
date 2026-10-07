using ScrapYardKing.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ScrapYardKing.UI
{
    /// <summary>
    /// The bottom bar: SHOP, MISSIONS, DAILY. Hidden in the first minutes (only cash, level and the task show); the
    /// three buttons pop in one after another with the first diamond. The game is played in the world, so there is no
    /// HOME tab, and settings stay on the gear.
    /// </summary>
    public sealed class NavBar : MonoBehaviour
    {
        [SerializeField] RectTransform[] buttons;
        [SerializeField] Button shop;
        [SerializeField] Button missions;
        [SerializeField] Button daily;
        [SerializeField] ShopPanel shopPanel;
        [SerializeField] MissionsPanel missionsPanel;
        [SerializeField] DailyPanel dailyPanel;
        UpgradePanel upgradePanel;
        bool shown, aside;
        float next;

        void Awake()
        {
            foreach (var b in buttons) b.gameObject.SetActive(false);
            shop.onClick.AddListener(() => shopPanel.Open(Badges.Count("shop") > 0 ? ShopTab.Free : ShopTab.Diamonds));
            missions.onClick.AddListener(() => missionsPanel.Open());
            daily.onClick.AddListener(() => dailyPanel.Open());
        }

        void Update()
        {
            // the bar steps aside while the upgrade panel is open (it covered the lowest row of cards)
            if (shown && (upgradePanel != null || Services.TryGet(out upgradePanel))) StepAside(upgradePanel.IsOpen);
            if (shown || Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.5f;
            if (!Badges.ExtrasUnlocked) return;
            shown = true;
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].gameObject.SetActive(true);
                UIAnim.UnlockReveal(buttons[i], 0.15f * i + (Time.unscaledTime > 5f ? 0.6f : 0f));
            }
        }

        void StepAside(bool on)
        {
            if (on == aside) return;
            aside = on;
            foreach (var b in buttons)
            {
                var button = b;
                if (on) UIAnim.Hide(button, null, () => button.gameObject.SetActive(false), 0.15f);
                else
                {
                    button.gameObject.SetActive(true);
                    UIAnim.Show(button, null, 0.25f);
                }
            }
        }
    }
}

using ScrapYardKing.Boosts;
using ScrapYardKing.Core;
using ScrapYardKing.Factory;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ScrapYardKing.UI
{
    /// <summary>
    /// HUD chips for what is running: a machine's Overdrive (first, from the boost pads) and the timed boosts from
    /// <see cref="BoostManager"/>. Each chip shows the icon, the time left as a draining bar and as a clock. In the last
    /// seconds a chip breathes gently (no flashing) so the player sees it is about to end.
    /// </summary>
    public sealed class BoostBar : MonoBehaviour
    {
        [System.Serializable]
        public struct Chip
        {
            public GameObject root;
            public Image icon;
            public Image fill;
            public TMP_Text time;
        }

        [SerializeField] Chip[] chips;
        [Header("Overdrive")]
        [SerializeField] OverdriveConfig overdrive;
        [SerializeField] Sprite overdriveIcon;
        [SerializeField] Color overdriveColor = new(1f, 0.55f, 0.15f);
        [Tooltip("A chip starts breathing this many seconds before it ends.")]
        [SerializeField, Min(0f)] float warnSeconds = 10f;

        BoostManager boosts;
        float overdriveUntil;
        readonly System.Collections.Generic.Dictionary<BoostDefinition, string> prefixes = new();

        void Awake()
        {
            foreach (var chip in chips)
                if (chip.root != null) chip.root.SetActive(false);
        }

        void OnEnable() => GameEvents.MachineOverdrive += OnOverdrive;
        void OnDisable() => GameEvents.MachineOverdrive -= OnOverdrive;

        void OnOverdrive(string machineId)
        {
            bool wasOff = Time.time >= overdriveUntil;
            overdriveUntil = Time.time + 0.1f;
            if (wasOff && chips.Length > 0 && chips[0].root != null) UIAnim.UnlockReveal(chips[0].root.transform);
        }

        void Update()
        {
            if (boosts == null) Services.TryGet(out boosts);
            int slot = 0;

            // the pad running longest (a video makes it two minutes)
            Tiles.OverdriveTile longest = null;
            foreach (var t in Tiles.OverdriveTile.Running)
                if (longest == null || t.Remaining > longest.Remaining) longest = t;
            float left = longest != null ? longest.Remaining : 0f;
            if (left > 0f && slot < chips.Length)
            {
                overdriveUntil = Time.time + left;
                string mult = overdrive != null ? $"{overdrive.SpeedMultiplier:0.#}X " : "";
                Set(chips[slot++], overdriveIcon, left / Mathf.Max(1f, longest.RunLength), overdriveColor, left, mult);
            }

            if (boosts != null)
                foreach (var boost in boosts.Active)
                {
                    if (slot >= chips.Length) break;
                    Set(chips[slot++], boost.Definition.Icon, boost.Remaining / Mathf.Max(1f, boost.Definition.Duration), boost.Definition.Color, boost.Remaining,
                        Prefix(boost.Definition));
                }

            for (; slot < chips.Length; slot++)
                if (chips[slot].root != null && chips[slot].root.activeSelf) chips[slot].root.SetActive(false);
        }

        /// <summary>"2X " in front of the timer, so a chip says what it does and not only how long (an icon alone did not).</summary>
        string Prefix(BoostDefinition definition)
        {
            if (!prefixes.TryGetValue(definition, out string p))
                prefixes[definition] = p = definition.Multiplier > 1.001f ? $"{definition.Multiplier:0.#}X " : "";
            return p;
        }

        void Set(Chip chip, Sprite sprite, float fill01, Color color, float remaining, string prefix)
        {
            if (chip.root == null) return;
            if (!chip.root.activeSelf) chip.root.SetActive(true);
            if (chip.icon != null) chip.icon.sprite = sprite;
            if (chip.fill != null)
            {
                chip.fill.fillAmount = Mathf.Clamp01(fill01);
                chip.fill.color = color;
            }

            if (chip.time != null)
            {
                int seconds = Mathf.CeilToInt(remaining);
                chip.time.SetText(prefix + "{0}:{1:00}", seconds / 60, seconds % 60);
            }

            // a gentle breath in the last seconds; steady otherwise
            float breath = remaining <= warnSeconds ? 1f + 0.05f * Mathf.Max(0f, Mathf.Sin(Time.unscaledTime * 6f)) : 1f;
            chip.root.transform.localScale = Vector3.one * breath;
        }
    }
}

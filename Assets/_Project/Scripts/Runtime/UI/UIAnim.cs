using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ScrapYardKing.UI
{
    /// <summary>
    /// The UI's shared motion vocabulary, so every panel moves the same way and no screen hand-rolls its own tweens.
    /// Everything runs on unscaled time (panels work while the game is paused) and is linked to its target so a
    /// destroyed element never leaves a tween behind. Normal interactions stay within 0.15–0.45 s; rewards may breathe.
    /// </summary>
    public static class UIAnim
    {
        public const float Fast = 0.15f;
        public const float Normal = 0.3f;

        static readonly Color ErrorRed = new(1f, 0.35f, 0.3f);

        /// <summary>Panel entry: pops from 0.7 with a soft overshoot while it fades in.</summary>
        public static Tween Show(RectTransform card, CanvasGroup group = null, float duration = 0.35f)
        {
            if (card == null) return null;
            card.DOKill();
            card.gameObject.SetActive(true);
            card.localScale = Vector3.one * 0.7f;
            var s = DOTween.Sequence().SetUpdate(true).SetLink(card.gameObject);
            s.Append(card.DOScale(1f, duration).SetEase(Ease.OutBack, 1.6f));
            if (group != null)
            {
                group.alpha = 0f;
                s.Join(group.DOFade(1f, duration * 0.6f));
            }

            return s;
        }

        /// <summary>Panel exit: shrinks a little and fades, then runs <paramref name="done"/>.</summary>
        public static Tween Hide(RectTransform card, CanvasGroup group = null, Action done = null, float duration = 0.2f)
        {
            if (card == null)
            {
                done?.Invoke();
                return null;
            }

            card.DOKill();
            var s = DOTween.Sequence().SetUpdate(true).SetLink(card.gameObject);
            s.Append(card.DOScale(0.88f, duration).SetEase(Ease.InBack));
            if (group != null) s.Join(group.DOFade(0f, duration));
            s.OnComplete(() => done?.Invoke());
            return s;
        }

        /// <summary>Fades a full-screen dimmer to <paramref name="alpha"/>.</summary>
        public static Tween Dim(Graphic dimmer, float alpha, float duration = 0.2f)
        {
            if (dimmer == null) return null;
            dimmer.DOKill();
            return dimmer.DOFade(alpha, duration).SetUpdate(true).SetLink(dimmer.gameObject);
        }

        /// <summary>A short scale punch (a counter that changed, a badge that got news).</summary>
        public static Tween Punch(Transform t, float strength = 0.18f, float duration = 0.25f)
        {
            if (t == null) return null;
            t.DOKill(true);
            return t.DOPunchScale(Vector3.one * strength, duration, 8, 0.6f).SetUpdate(true).SetLink(t.gameObject);
        }

        /// <summary>Sideways shake ("no"), on a unit-scale RectTransform.</summary>
        public static Tween Shake(RectTransform t, float strength = 16f)
        {
            if (t == null) return null;
            t.DOComplete();
            return t.DOShakeAnchorPos(0.3f, new Vector2(strength, 0f), 30, 0f, false, true).SetUpdate(true).SetLink(t.gameObject);
        }

        /// <summary>Not allowed / not enough: shake plus a red pulse on <paramref name="tint"/>.</summary>
        public static void Error(RectTransform t, Graphic tint = null)
        {
            Shake(t);
            if (tint == null) return;
            var original = tint.color;
            tint.DOKill(true);
            DOTween.Sequence().SetUpdate(true).SetLink(tint.gameObject)
                .Append(tint.DOColor(ErrorRed * original, 0.08f))
                .Append(tint.DOColor(original, 0.25f));
        }

        /// <summary>A reward icon arriving: from nothing to oversized and back, with a little wobble.</summary>
        public static Tween RewardPop(Transform t, float delay = 0f)
        {
            if (t == null) return null;
            t.DOKill();
            t.localScale = Vector3.zero;
            t.localRotation = Quaternion.Euler(0f, 0f, -14f);
            return DOTween.Sequence().SetUpdate(true).SetLink(t.gameObject).SetDelay(delay)
                .Append(t.DOScale(1.25f, 0.28f).SetEase(Ease.OutBack))
                .Join(t.DOLocalRotate(Vector3.zero, 0.45f).SetEase(Ease.OutElastic))
                .Append(t.DOScale(1f, 0.18f).SetEase(Ease.InOutSine));
        }

        /// <summary>A coin-like turn around the vertical axis (diamonds, medals).</summary>
        public static Tween Spin(Transform t, float duration = 0.7f)
        {
            if (t == null) return null;
            return t.DOLocalRotate(new Vector3(0f, 360f, 0f), duration, RotateMode.FastBeyond360)
                .SetEase(Ease.OutCubic).SetUpdate(true).SetLink(t.gameObject);
        }

        /// <summary>A slow breathing loop for the one thing on screen that wants a tap.</summary>
        public static Tween Breathe(Transform t, float scale = 1.05f, float period = 0.55f)
        {
            if (t == null) return null;
            t.DOKill();
            t.localScale = Vector3.one;
            return t.DOScale(scale, period).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetUpdate(true).SetLink(t.gameObject);
        }

        /// <summary>A filled image running to <paramref name="to"/> (0..1).</summary>
        public static Tween ProgressFill(Image fill, float to, float duration = 0.35f)
        {
            if (fill == null) return null;
            fill.DOKill();
            return fill.DOFillAmount(Mathf.Clamp01(to), duration).SetEase(Ease.OutCubic).SetUpdate(true).SetLink(fill.gameObject);
        }

        /// <summary>A number rolling from <paramref name="from"/> to <paramref name="to"/>, formatted each frame.</summary>
        public static Tween CountTo(TMP_Text label, double from, double to, float duration, Func<double, string> format)
        {
            if (label == null) return null;
            double shown = from;
            label.SetText(format(from));
            return DOTween.To(() => shown, x =>
            {
                shown = x;
                label.SetText(format(x));
            }, to, duration).SetEase(Ease.OutCubic).SetUpdate(true).SetLink(label.gameObject);
        }

        public static Tween ButtonPress(Transform t)
        {
            if (t == null) return null;
            t.DOKill();
            return t.DOScale(0.94f, 0.07f).SetEase(Ease.OutQuad).SetUpdate(true).SetLink(t.gameObject);
        }

        public static Tween ButtonRelease(Transform t)
        {
            if (t == null) return null;
            t.DOKill();
            return t.DOScale(1f, 0.25f).SetEase(Ease.OutBack, 3f).SetUpdate(true).SetLink(t.gameObject);
        }

        /// <summary>Something new: grows out of nothing past full size with a twist, then settles.</summary>
        public static Tween UnlockReveal(Transform t, float delay = 0f)
        {
            if (t == null) return null;
            t.DOKill();
            t.localScale = Vector3.zero;
            t.localRotation = Quaternion.Euler(0f, 0f, 10f);
            return DOTween.Sequence().SetUpdate(true).SetLink(t.gameObject).SetDelay(delay)
                .Append(t.DOScale(1.15f, 0.35f).SetEase(Ease.OutBack, 2f))
                .Join(t.DOLocalRotate(Vector3.zero, 0.5f).SetEase(Ease.OutBack))
                .Append(t.DOScale(1f, 0.2f));
        }

        /// <summary>Claim pressed: the button sinks, flashes and comes back.</summary>
        public static Tween Claim(Transform t)
        {
            if (t == null) return null;
            t.DOKill();
            return DOTween.Sequence().SetUpdate(true).SetLink(t.gameObject)
                .Append(t.DOScale(0.9f, 0.06f))
                .Append(t.DOScale(1.08f, 0.12f).SetEase(Ease.OutQuad))
                .Append(t.DOScale(1f, 0.12f));
        }

        /// <summary>Bought / levelled up: a green flash on <paramref name="tint"/> and a punch.</summary>
        public static void Upgrade(Transform t, Graphic tint = null)
        {
            Punch(t, 0.22f, 0.35f);
            if (tint == null) return;
            var original = tint.color;
            tint.DOKill(true);
            DOTween.Sequence().SetUpdate(true).SetLink(tint.gameObject)
                .Append(tint.DOColor(new Color(0.55f, 1f, 0.45f), 0.08f))
                .Append(tint.DOColor(original, 0.3f));
        }
    }
}

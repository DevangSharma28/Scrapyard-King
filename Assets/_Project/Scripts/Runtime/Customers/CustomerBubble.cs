using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ScrapYardKing.Customers
{
    /// <summary>
    /// Speech bubble over a customer: what they want and how many are still missing, a ring that fills while the desk
    /// gets to them, and a happy face when the order is complete. Billboards to the camera.
    /// </summary>
    public sealed class CustomerBubble : MonoBehaviour
    {
        [SerializeField] Transform root;
        [SerializeField] Image icon;
        [SerializeField] TMP_Text count;
        [SerializeField] Image waitRing;
        [SerializeField] GameObject orderGroup;
        [SerializeField] GameObject happyGroup;

        Transform cameraTransform;
        Vector3 rootScale = Vector3.one;
        int shown = -1;

        void Awake()
        {
            if (root != null) rootScale = root.localScale;
            HideImmediate();
        }

        void OnDestroy()
        {
            if (root != null) root.DOKill();
        }

        public void ShowOrder(Sprite sprite, int remaining)
        {
            if (icon != null)
            {
                icon.sprite = sprite;
                icon.enabled = sprite != null;
            }

            if (orderGroup != null) orderGroup.SetActive(true);
            if (happyGroup != null) happyGroup.SetActive(false);
            shown = -1;
            SetRemaining(remaining);
            SetWait(-1f);
            Pop();
        }

        public void SetRemaining(int remaining)
        {
            if (count != null) count.text = remaining.ToString();
            if (shown >= 0 && remaining != shown && root != null)
            {
                root.DOKill(true);
                root.localScale = rootScale;
                root.DOPunchScale(rootScale * 0.18f, 0.15f, 5, 0.5f);
            }

            shown = remaining;
        }

        /// <summary>0..1 fills the ring; negative hides it.</summary>
        public void SetWait(float progress01)
        {
            if (waitRing == null) return;
            bool on = progress01 >= 0f;
            if (waitRing.gameObject.activeSelf != on) waitRing.gameObject.SetActive(on);
            if (on) waitRing.fillAmount = Mathf.Clamp01(progress01);
        }

        public void ShowHappy(float hideAfter)
        {
            if (orderGroup != null) orderGroup.SetActive(false);
            if (happyGroup != null) happyGroup.SetActive(true);
            SetWait(-1f);
            Pop();
            if (root == null) return;
            DOTween.Sequence()
                .AppendInterval(hideAfter)
                .Append(root.DOScale(0f, 0.2f).SetEase(Ease.InBack))
                .OnComplete(HideImmediate)
                .SetTarget(root);
        }

        public void HideImmediate()
        {
            if (root == null) return;
            root.DOKill();
            root.gameObject.SetActive(false);
            root.localScale = rootScale;
        }

        void Pop()
        {
            if (root == null) return;
            root.DOKill();
            root.gameObject.SetActive(true);
            root.localScale = Vector3.zero;
            root.DOScale(rootScale, 0.3f).SetEase(Ease.OutBack);
        }

        void LateUpdate()
        {
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
            if (cameraTransform != null) transform.rotation = cameraTransform.rotation;
        }
    }
}

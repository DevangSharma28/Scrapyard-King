using DG.Tweening;
using ScrapYardKing.Harvest;
using UnityEngine;

namespace ScrapYardKing.Player
{
    /// <summary>
    /// Drives the character Animator (locomotion blend by speed, chainsaw arm layer while cutting) and adds
    /// procedural juice on top: chainsaw rev vibration, recoil on each hit and a squash when a hit breaks scrap.
    /// </summary>
    public sealed class PlayerVisuals : MonoBehaviour
    {
        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int CuttingId = Animator.StringToHash("Cutting");

        [SerializeField] PlayerController controller;
        [SerializeField] HarvestTool harvestTool;
        [SerializeField] Animator animator;
        [Tooltip("Model root (squash/stretch).")]
        [SerializeField] Transform body;
        [Tooltip("Chainsaw root parented to the hand bone.")]
        [SerializeField] Transform tool;
        [SerializeField] ParticleSystem toolSmoke;

        [SerializeField, Min(0f)] float speedDampTime = 0.08f;
        [SerializeField, Min(0f)] float revJitter = 0.012f;
        [SerializeField] float hitRecoilDegrees = 18f;

        Vector3 toolPosition, bodyScale;
        Quaternion toolRotation;

        void Awake()
        {
            if (tool != null)
            {
                toolPosition = tool.localPosition;
                toolRotation = tool.localRotation;
            }

            if (body != null) bodyScale = body.localScale;
        }

        void OnEnable()
        {
            if (harvestTool != null) harvestTool.Hit += OnHit;
        }

        void OnDisable()
        {
            if (harvestTool != null) harvestTool.Hit -= OnHit;
        }

        void OnHit(ScrapObject target, ScrapHit hit)
        {
            if (tool != null)
            {
                tool.DOKill(true);
                tool.localRotation = toolRotation;
                tool.DOPunchRotation(new Vector3(-hitRecoilDegrees, 0f, 0f), 0.15f, 6, 0.5f);
            }

            if (body != null && !target.IsTargetable)
            {
                body.DOKill(true);
                body.localScale = bodyScale;
                body.DOPunchScale(new Vector3(0.12f, -0.12f, 0.12f), 0.25f, 6, 0.6f);
            }
        }

        void LateUpdate()
        {
            bool cutting = harvestTool != null && harvestTool.IsCutting;
            if (animator != null)
            {
                animator.SetFloat(SpeedId, controller != null ? controller.Speed01 : 0f, speedDampTime, Time.deltaTime);
                animator.SetBool(CuttingId, cutting);
            }

            if (toolSmoke != null)
            {
                if (cutting && !toolSmoke.isEmitting) toolSmoke.Play();
                else if (!cutting && toolSmoke.isEmitting) toolSmoke.Stop();
            }

            if (tool == null || DOTween.IsTweening(tool)) return;
            tool.localPosition = cutting ? toolPosition + Random.insideUnitSphere * revJitter : toolPosition;
        }
    }
}

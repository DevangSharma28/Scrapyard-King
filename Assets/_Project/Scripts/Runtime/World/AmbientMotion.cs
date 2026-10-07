using UnityEngine;

namespace ScrapYardKing.World
{
    /// <summary>
    /// Presentation only: idle motion that keeps the yard alive (crane jibs swinging, fans spinning, flags flapping, birds
    /// circling, lamps bobbing). Cheap per-frame transform math; no gameplay effect.
    /// </summary>
    public sealed class AmbientMotion : MonoBehaviour
    {
        public enum Mode { Spin, Swing, Bob }

        [SerializeField] Mode mode = Mode.Spin;
        [Tooltip("Local axis for Spin / Swing; direction for Bob.")]
        [SerializeField] Vector3 axis = Vector3.up;
        [Tooltip("Spin: degrees per second. Swing: degrees of amplitude. Bob: metres of amplitude.")]
        [SerializeField] float amount = 30f;
        [Tooltip("Swing / Bob cycles per second.")]
        [SerializeField, Min(0f)] float frequency = 0.2f;
        [Tooltip("Random start phase so copies don't move in lockstep.")]
        [SerializeField] bool randomPhase = true;

        Quaternion restRotation;
        Vector3 restPosition;
        float phase;

        void Awake()
        {
            restRotation = transform.localRotation;
            restPosition = transform.localPosition;
            phase = randomPhase ? Random.value * 100f : 0f;
        }

        void Update()
        {
            float t = Time.time + phase;
            switch (mode)
            {
                case Mode.Spin:
                    transform.localRotation = restRotation * Quaternion.AngleAxis(amount * t, axis);
                    break;
                case Mode.Swing:
                    transform.localRotation = restRotation * Quaternion.AngleAxis(amount * Mathf.Sin(t * frequency * Mathf.PI * 2f), axis);
                    break;
                case Mode.Bob:
                    transform.localPosition = restPosition + axis.normalized * (amount * Mathf.Sin(t * frequency * Mathf.PI * 2f));
                    break;
            }
        }
    }
}

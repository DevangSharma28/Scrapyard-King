using UnityEngine;

namespace ScrapYardKing.UI
{
    /// <summary>Keeps a world-space label or icon facing the camera (copies the camera rotation, so text never skews).</summary>
    public sealed class Billboard : MonoBehaviour
    {
        Transform cameraTransform;

        void LateUpdate()
        {
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
            if (cameraTransform != null) transform.rotation = cameraTransform.rotation;
        }
    }
}

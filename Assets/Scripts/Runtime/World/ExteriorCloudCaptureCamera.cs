using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Marks a non-gameplay camera that should see the camera-relative cloud
    /// shell. The City fountain cubemap is the only current user; preview and
    /// UI cameras deliberately carry no marker.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class ExteriorCloudCaptureCamera : MonoBehaviour
    {
    }
}

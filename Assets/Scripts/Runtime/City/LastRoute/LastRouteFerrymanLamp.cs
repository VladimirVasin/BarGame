using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Carries the Ferryman's one lamp with him.
    ///
    /// It used to be a fixed point beside the bonnet, which was right for as
    /// long as he never left the bonnet. He does now - he drops onto the
    /// lot, walks the length of his own car and gets in - and a man in the
    /// darkest coat in the game walking out of the only light on an unlit
    /// island is a man who simply disappears for four seconds.
    ///
    /// The offset is CAPTURED rather than authored a second time: the
    /// factory places the lamp exactly where it always placed it, and this
    /// reads the vector from him to it once and keeps writing it. So the
    /// perch looks identical to the frame before this component existed,
    /// and everything after it is free.
    ///
    /// It stays a child of the runtime root and never of the art. The
    /// staged prefab is validated passive - no light anywhere inside it -
    /// and one PlayMode test holds it to that.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(320)]
    public sealed class LastRouteFerrymanLamp : MonoBehaviour
    {
        private Transform bearer;
        private Vector3 localOffset;

        public bool IsInitialized { get; private set; }

        public void Initialize(Transform ferrymanTransform)
        {
            if (ferrymanTransform == null)
            {
                throw new ArgumentNullException(nameof(ferrymanTransform));
            }

            bearer = ferrymanTransform;
            localOffset = Quaternion.Inverse(bearer.rotation) *
                          (transform.position - bearer.position);
            IsInitialized = true;
        }

        /// <summary>
        /// After the presentation, so the lamp is never a frame behind the
        /// face it is lighting.
        /// </summary>
        private void LateUpdate()
        {
            if (!IsInitialized || bearer == null)
            {
                return;
            }

            transform.position =
                bearer.position + (bearer.rotation * localOffset);
        }
    }
}

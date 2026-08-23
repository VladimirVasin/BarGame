using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// The line between the rod tip and the float, and the float itself.
    ///
    /// It exists because a rod without one is a stick. It is built by the
    /// runtime rather than drawn into the model for a reason the source
    /// records: the only thing that knows how far it is from the rod tip
    /// down to the water is the coast plan, whose frame carries the
    /// sea's own top.
    ///
    /// The float's XZ is set once and stays — a bobber that slides
    /// around with a breathing man's hands reads as a bug. Its Y rides
    /// the swell: the sea visibly heaves now, and a bobber pinned to
    /// the datum would hang in the air over every trough. It asks
    /// <see cref="CityWaterWaveModel"/> — the shader's CPU mirror —
    /// where the drawn surface actually is, so the ride and the water
    /// can never disagree. The line is re-struck from the live rod tip
    /// every frame, so the tip lift authored into the loop actually
    /// pulls on it.
    /// </summary>
    [DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    public sealed class SeacoastFishermanLine : MonoBehaviour
    {
        public const float LineThicknessMeters = 0.012f;

        /// <summary>A bobber floats about half under. Its body is
        /// centred on the waterline for exactly that reason.</summary>
        public const float FloatHeightMeters = 0.100f;
        public const float FloatWidthMeters = 0.048f;

        /// <summary>A line shorter than this means the tip has ended up
        /// at or below the waterline, which can only happen if the plan
        /// and the pose disagree. Better to keep a visible stub than to
        /// scale a mesh to zero.</summary>
        public const float MinimumLengthMeters = 0.05f;

        private static readonly Color LineColor =
            new Color(0.680f, 0.665f, 0.600f, 1f);
        private static readonly Color FloatTopColor =
            new Color(0.520f, 0.110f, 0.075f, 1f);
        private static readonly Color FloatBodyColor =
            new Color(0.720f, 0.700f, 0.650f, 1f);

        private Transform rodTip;
        private Transform line;
        private Transform floatBody;
        private Vector3 floatPoint;
        private float restWaterTopY;
        private CityWaterWaveProfile waveProfile;

        public bool IsInitialized { get; private set; }

        /// <summary>Where the float sits: the waterline XZ it was cast
        /// to, at the swell's current height there.</summary>
        public Vector3 FloatPosition => floatPoint;

        /// <summary>Current strung length, in metres.</summary>
        public float LineLength { get; private set; }

        public void Initialize(
            Transform configuredRodTip,
            float waterTopY,
            in CityWaterWaveProfile swellProfile)
        {
            rodTip = configuredRodTip != null
                ? configuredRodTip
                : throw new ArgumentNullException(nameof(configuredRodTip));
            restWaterTopY = waterTopY;
            waveProfile = swellProfile;

            Vector3 tip = rodTip.position;
            floatPoint = new Vector3(tip.x, waterTopY, tip.z);

            line = RuntimePrimitiveFactory.CreateBox(
                "Fishing Line",
                transform,
                Vector3.zero,
                new Vector3(
                    LineThicknessMeters,
                    1f,
                    LineThicknessMeters),
                LineColor,
                false).transform;
            line.gameObject.GetComponent<Renderer>().shadowCastingMode =
                ShadowCastingMode.Off;

            // floatPoint is a world position; the factory takes a local
            // one, and the two only coincide while the host hierarchy
            // sits at identity - convert instead of relying on that.
            GameObject body = RuntimePrimitiveFactory.CreateBox(
                "Float",
                transform,
                transform.InverseTransformPoint(floatPoint),
                new Vector3(
                    FloatWidthMeters,
                    FloatHeightMeters,
                    FloatWidthMeters),
                FloatBodyColor,
                false);
            floatBody = body.transform;
            // The painted cap, in the float's own unit-cube space: the
            // one red mark on a precinct whose whole palette is wet grey.
            RuntimePrimitiveFactory.CreateBox(
                "Float Cap",
                body.transform,
                new Vector3(0f, 0.36f, 0f),
                new Vector3(0.86f, 0.30f, 0.86f),
                FloatTopColor,
                false);

            IsInitialized = true;
            Strike();
        }

        private void LateUpdate()
        {
            if (!IsInitialized)
            {
                return;
            }

            if (floatBody != null)
            {
                // The same wave the shader is drawing this frame, from
                // its CPU mirror. Near the shore the fade has taken
                // most of the height, so the ride is a bob and not a
                // heave — which is what a bobber in the shallows does.
                floatPoint.y = restWaterTopY +
                    CityWaterWaveModel.SampleHeight(
                        waveProfile,
                        floatPoint.x,
                        floatPoint.z,
                        Time.timeSinceLevelLoad);
                floatBody.position = floatPoint;
            }

            Strike();
        }

        private void Strike()
        {
            if (rodTip == null || line == null)
            {
                return;
            }

            Vector3 tip = rodTip.position;
            Vector3 span = floatPoint - tip;
            float length = span.magnitude;
            if (length < MinimumLengthMeters)
            {
                length = MinimumLengthMeters;
                span = Vector3.down * length;
            }

            LineLength = length;
            line.position = tip + span * 0.5f;
            line.rotation = Quaternion.FromToRotation(
                Vector3.up,
                span / length);
            line.localScale = new Vector3(
                LineThicknessMeters,
                length,
                LineThicknessMeters);
        }
    }
}

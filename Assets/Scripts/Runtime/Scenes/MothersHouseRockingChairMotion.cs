using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Rolls the chair and its sitter on the authored runner contact edges.
    ///
    /// ONE ANGLE MOVES BOTH. The chair's two meshes and the woman are driven
    /// from a single angle; neither carries a sway of its own. The
    /// alternative - a rock authored into her clip and a matching one written
    /// here - gives the same motion two owners, and two owners of one motion
    /// drift apart the first time either is retuned. Her clip is therefore
    /// only breathing, which is the one thing a rocking chair cannot do by
    /// itself.
    ///
    /// IT DRIVES WORLD POSES AND REPARENTS NOTHING. The chair is two
    /// renderers inside the room's imported model, and that model is not a
    /// neutral place to cut: it carries its own unit factor and axis
    /// conversion, and the room's PlayMode test counts every renderer under
    /// the asset registry and would find two missing. So each rider's rest
    /// pose is recorded once and re-placed each frame; the imported hierarchy
    /// is left exactly as it was imported.
    ///
    /// The generator approximates its parabola with chamfered straight
    /// sections. The actual FBX lower hull has a short flat at its centre;
    /// up to +/-3.02869 degrees it rolls about one end of that flat. These
    /// measured edges, not the parabola's centre of curvature, are the
    /// instantaneous pivots. Both give the same pose at zero tilt. The rug
    /// under the contacts is 0.032 m high, above the floor boards.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(300)]
    public sealed class MothersHouseRockingChairMotion : MonoBehaviour
    {
        /// <summary>Centre of the runners' bottom flat in room space.</summary>
        public const float ContactZ = 1.55f;

        /// <summary>Measured underside, including thickness and chamfers.</summary>
        public const float ContactY = 0.01781656f;

        public const float ContactHalfSpan = 0.0069744f;
        public const float SupportSurfaceY = 0.032f;

        /// <summary>
        /// The quiet authored lean stays inside the central contact edges'
        /// supported angle range and the chair's static fixture blocker.
        /// </summary>
        public const float AmplitudeDegrees = 2.5f;

        /// <summary>
        /// Seconds for one full quiet, even cycle, unchanged by arrivals.
        /// </summary>
        public const float PeriodSeconds = 3.2f;

        /// <summary>
        /// The same bound every looping presentation in the game uses: a
        /// hitch advances the rock by a step instead of teleporting it
        /// through half a swing.
        /// </summary>
        public const float MaximumStepSeconds = 0.1f;

        private readonly List<Rider> riders = new List<Rider>();
        private Transform roomRoot;
        private float phaseSeconds;
        private bool initialized;

        public bool IsInitialized => initialized;

        public int RiderCount => riders.Count;

        /// <summary>The chair's current lean, in degrees.</summary>
        public float AngleDegrees { get; private set; }

        /// <summary>
        /// Rest-space edge in contact with the rug at this lean. The two
        /// edges exchange support through the flat neutral pose.
        /// </summary>
        public static Vector3 GetRunnerContact(float angleDegrees)
        {
            float offset = angleDegrees > 0f ? ContactHalfSpan :
                angleDegrees < 0f ? -ContactHalfSpan : 0f;
            return new Vector3(0f, ContactY, ContactZ + offset);
        }

        public void Initialize(
            Transform configuredRoomRoot,
            float initialPhaseSeconds,
            params Transform[] carried)
        {
            roomRoot = configuredRoomRoot != null
                ? configuredRoomRoot
                : throw new System.ArgumentNullException(
                    nameof(configuredRoomRoot));

            riders.Clear();
            if (carried != null)
            {
                for (int index = 0; index < carried.Length; index++)
                {
                    Carry(carried[index]);
                }
            }

            phaseSeconds = initialPhaseSeconds;
            initialized = true;
            Apply();
        }

        /// <summary>
        /// Adds one more rider, so the chair can be assembled before its
        /// sitter exists. The rest pose is taken NOW, once: reading it every
        /// frame would compound the rock into itself and walk the chair
        /// across the room.
        /// </summary>
        public void Carry(Transform rider)
        {
            if (rider == null)
            {
                return;
            }

            riders.Add(new Rider(rider, roomRoot));
            if (initialized)
            {
                Apply();
            }
        }

        private void LateUpdate()
        {
            if (!initialized)
            {
                return;
            }

            phaseSeconds += Mathf.Min(Time.deltaTime, MaximumStepSeconds);
            if (phaseSeconds >= PeriodSeconds)
            {
                phaseSeconds -= PeriodSeconds;
            }

            Apply();
        }

        private void Apply()
        {
            AngleDegrees = AmplitudeDegrees * Mathf.Sin(
                phaseSeconds / PeriodSeconds * 2f * Mathf.PI);
            Vector3 contact = GetRunnerContact(AngleDegrees);
            Vector3 lift = Vector3.up * (SupportSurfaceY - ContactY);
            Quaternion rock = Quaternion.AngleAxis(
                AngleDegrees, Vector3.right);
            for (int index = 0; index < riders.Count; index++)
            {
                Rider rider = riders[index];
                if (rider.Transform == null)
                {
                    continue;
                }

                rider.Transform.SetPositionAndRotation(
                    roomRoot.TransformPoint(contact + lift +
                        rock * (rider.Position - contact)),
                    roomRoot.rotation * rock * rider.Rotation);
            }
        }

        private readonly struct Rider
        {
            public Rider(Transform transform, Transform room)
            {
                Transform = transform;
                Position = room.InverseTransformPoint(transform.position);
                Rotation = Quaternion.Inverse(room.rotation) * transform.rotation;
            }

            public Transform Transform { get; }
            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
        }
    }
}

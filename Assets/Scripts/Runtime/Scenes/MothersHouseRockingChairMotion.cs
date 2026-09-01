using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Rocks the chair, and whoever is sitting in it, about the arc its own
    /// runners were drawn on.
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
    /// THE PIVOT IS DERIVED, NOT CHOSEN. The runners are a parabola in the
    /// room's own coordinates: `rocker_rail` samples `y = 0.055 + 0.10 t^2`
    /// at `z = 1.55 + 0.63 t`, which in z is `y = 0.055 + 0.2520 dz^2`. A
    /// parabola `y = a z^2` has radius of curvature `1 / 2a` at its vertex,
    /// so these runners roll on a circle of `1.984 m` centred `2.039 m` above
    /// the floor. Turning about the X axis through that point rolls the
    /// runners along the boards WITHOUT SLIDING, which is what a rocking
    /// chair does and what an arbitrary pivot would not.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(300)]
    public sealed class MothersHouseRockingChairMotion : MonoBehaviour
    {
        /// <summary>Where the runners touch the floor, in room space.
        /// </summary>
        public const float ContactZ = 1.55f;

        /// <summary>The lowest point of the runner arc.</summary>
        public const float ContactY = 0.055f;

        /// <summary>
        /// The runners' own curvature, `1 / (2 * 0.2520)`. Not a tuned
        /// number: change the generator's parabola and this must follow it.
        /// </summary>
        public const float RunnerRadius = 1.9845f;

        /// <summary>
        /// How far the chair leans either way. Two and a half degrees moves
        /// the contact point about nine centimetres along the floor, which is
        /// a real rocking chair's travel and stays well inside the fixture's
        /// own collider - so the blocker never has to move.
        /// </summary>
        public const float AmplitudeDegrees = 2.5f;

        /// <summary>
        /// Seconds for one full rock. A rocking chair's period is set by its
        /// runner radius and gravity, and for `1.98 m` a pendulum comes out
        /// near `2.8 s`; this is a little slower, because she is old and
        /// barely pushing.
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
        /// Where the pivot stands, given the room root. Exposed so a test can
        /// assert the derivation rather than re-type the number.
        /// </summary>
        public static Vector3 GetRockCenter(Transform roomRoot)
        {
            var local = new Vector3(0f, ContactY + RunnerRadius, ContactZ);
            return roomRoot == null ? local : roomRoot.TransformPoint(local);
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

            riders.Add(new Rider(rider));
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
            Vector3 center = GetRockCenter(roomRoot);
            Quaternion rock = Quaternion.AngleAxis(
                AngleDegrees, roomRoot.right);
            for (int index = 0; index < riders.Count; index++)
            {
                Rider rider = riders[index];
                if (rider.Transform == null)
                {
                    continue;
                }

                rider.Transform.SetPositionAndRotation(
                    center + rock * (rider.Position - center),
                    rock * rider.Rotation);
            }
        }

        private readonly struct Rider
        {
            public Rider(Transform transform)
            {
                Transform = transform;
                Position = transform.position;
                Rotation = transform.rotation;
            }

            public Transform Transform { get; }
            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
        }
    }
}

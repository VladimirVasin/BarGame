using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The bodies whose steps the hero can hear.
    ///
    /// Rigs register their root as they come alive - the pooled walker
    /// prefab, the six authored residents the port and the cannery also
    /// wear, the bartender, the cafe cast - and the director on the hero
    /// listens to whichever of them move within earshot. Explicit
    /// registration rather than a scene scan: nothing else that moves in
    /// the world is a pair of feet, and a crane's trolley over a stone quay
    /// must never sound like one.
    /// </summary>
    public static class NpcFootstepSources
    {
        private static readonly List<Transform> Roots = new List<Transform>();

        public static int Count => Roots.Count;

        public static void Register(Transform root)
        {
            if (root == null || Roots.Contains(root))
            {
                return;
            }

            Roots.Add(root);
        }

        public static void Unregister(Transform root)
        {
            if (root != null)
            {
                Roots.Remove(root);
            }
        }

        /// <summary>Drops destroyed roots and returns the live list.</summary>
        public static IReadOnlyList<Transform> Prune()
        {
            for (int index = Roots.Count - 1; index >= 0; index--)
            {
                if (Roots[index] == null)
                {
                    Roots.RemoveAt(index);
                }
            }

            return Roots;
        }

        internal static void ResetForTests()
        {
            Roots.Clear();
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            Roots.Clear();
        }
    }

    /// <summary>
    /// Counts a walker's stride from achieved movement, the way the hero's
    /// motor does: a step falls every stride of planar travel, a crawl
    /// counts for nothing, and a jump of more than a stride in one frame is
    /// a pool reset or a teleport, never a step.
    /// </summary>
    public sealed class NpcStrideTracker
    {
        public const float TeleportDistance = 1.5f;
        public const float RunSpeed = 3.2f;

        private Vector3 lastPosition;
        private bool hasPosition;
        private float distance;

        public float Distance => distance;

        public void Reset()
        {
            hasPosition = false;
            distance = 0f;
        }

        /// <summary>
        /// Advances by the walker's position this frame. True when a step
        /// falls in this frame.
        /// </summary>
        public bool Advance(Vector3 position, float deltaTime)
        {
            if (!hasPosition || deltaTime <= 0f)
            {
                lastPosition = position;
                hasPosition = true;
                return false;
            }

            Vector3 delta = position - lastPosition;
            delta.y = 0f;
            lastPosition = position;
            float travelled = delta.magnitude;
            if (travelled > TeleportDistance)
            {
                distance = 0f;
                return false;
            }

            float speed = travelled / deltaTime;
            float stride = speed > RunSpeed
                ? PlayerMotor.RunFootstepStride
                : PlayerMotor.FootstepStride;
            if (speed * speed < PlayerMotor.FootstepMinimumSpeedSquared)
            {
                distance = Mathf.Min(distance, stride * 0.35f);
                return false;
            }

            distance += travelled;
            if (distance < stride)
            {
                return false;
            }

            distance %= stride;
            return true;
        }
    }

    /// <summary>
    /// Plays the steps of every registered body moving within earshot of the
    /// hero, on the floor under its own feet.
    ///
    /// A step needs KNOWN ground: the same stamped colliders and overlays the
    /// hero's own step reads. That is also what keeps a rider quiet - a bus
    /// floor, a car, a cableway cabin and a boat deck carry no stamp, so a
    /// body carried across the city makes no sound until it stands on a
    /// pavement again. Quieter than the hero, and never more than one step
    /// every few frames across all of them, so a crowd cannot empty the
    /// world pool.
    /// </summary>
    public sealed class NpcFootstepDirector : MonoBehaviour
    {
        public const float HearingRadius = 14f;
        public const float VolumeScale = 0.55f;
        public const float GlobalMinimumInterval = 0.09f;

        private sealed class Walker
        {
            public readonly NpcStrideTracker Stride = new NpcStrideTracker();
            public bool Silent;
            public int LastSeenTick;
        }

        private readonly Dictionary<Transform, Walker> walkers =
            new Dictionary<Transform, Walker>();
        private readonly List<Transform> stale = new List<Transform>();
        private Transform hero;
        private double nextStepDspTime;
        private int tick;

        /// <summary>Steps whose ground was known, audio or not.</summary>
        public int StepsResolved { get; private set; }
        public FootstepGroundKind LastKind { get; private set; }

        public void Initialize(Transform heroRoot)
        {
            hero = heroRoot;
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        internal void Tick(float deltaTime)
        {
            if (hero == null || deltaTime <= 0f)
            {
                return;
            }

            tick++;
            Vector3 centre = hero.position;
            IReadOnlyList<Transform> roots = NpcFootstepSources.Prune();
            for (int index = 0; index < roots.Count; index++)
            {
                Transform root = roots[index];
                if (!root.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Vector3 position = root.position;
                Vector3 offset = position - centre;
                offset.y = 0f;
                if (offset.sqrMagnitude > HearingRadius * HearingRadius)
                {
                    walkers.Remove(root);
                    continue;
                }

                if (!walkers.TryGetValue(root, out Walker walker))
                {
                    walker = new Walker { Silent = IsSilent(root) };
                    walkers.Add(root, walker);
                }

                walker.LastSeenTick = tick;
                if (!walker.Stride.Advance(position, deltaTime) ||
                    walker.Silent)
                {
                    continue;
                }

                TryStep(root, position);
            }

            foreach (KeyValuePair<Transform, Walker> pair in walkers)
            {
                if (pair.Key == null || pair.Value.LastSeenTick != tick)
                {
                    stale.Add(pair.Key);
                }
            }

            for (int index = 0; index < stale.Count; index++)
            {
                walkers.Remove(stale[index]);
            }

            stale.Clear();
        }

        /// <summary>
        /// The hero's own rig, should it ever register, has the motor's
        /// footstep; a body on a wheelchair rolls.
        /// </summary>
        private bool IsSilent(Transform root)
        {
            return (hero != null && root.IsChildOf(hero)) ||
                   root.GetComponentInParent<CityWheelchairNpcAssetRegistry>() !=
                   null;
        }

        private void TryStep(Transform root, Vector3 feet)
        {
            double now = AudioSettings.dspTime;
            if (now < nextStepDspTime)
            {
                return;
            }

            if (!HeroFootstepGround.TryResolve(
                    feet,
                    root,
                    out FootstepGroundKind kind,
                    out Vector3 contact))
            {
                return;
            }

            StepsResolved++;
            LastKind = kind;
            if (RetroAudio.PlayAt(
                    HeroFootstepGround.ToSfx(kind),
                    contact,
                    VolumeScale))
            {
                nextStepDspTime = now + GlobalMinimumInterval;
            }
        }
    }
}

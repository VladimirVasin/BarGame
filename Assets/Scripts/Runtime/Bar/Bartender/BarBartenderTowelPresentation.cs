using UnityEngine;

namespace BarPromenade
{
    public enum BarBartenderTowelState { Wiping, Stowing, Stowed, Drawing }

    /// <summary>Moves the one mounted towel with the worker's real hand before lending it to service.</summary>
    public sealed class BarBartenderTowelPresentation
    {
        public const float StowDurationSeconds = .9f;
        public const float DrawDurationSeconds = .9f;
        private const float ContactTolerance = .003f;
        private const float DrawContactProgress = .35f;
        private const float ReleaseDurationSeconds = .45f;
        private readonly BarBartenderPresentation presentation;
        private readonly BarBartenderAssetRegistry registry;
        private readonly Transform towelParent;
        private readonly Vector3 towelLocalPosition;
        private readonly Quaternion towelLocalRotation;
        private readonly Vector3 towelLocalScale;
        private bool serviceRequested;
        private bool drawingHasTowel;
        private Pose startHand;
        private Pose lastHand;
        private Pose lastTarget;
        private Vector3 releaseSide;
        private float releaseElapsed = ReleaseDurationSeconds;

        public BarBartenderTowelState State { get; private set; }
        public float Progress { get; private set; }
        public bool IsTransitioning => State == BarBartenderTowelState.Stowing || State == BarBartenderTowelState.Drawing;
        public bool ServiceReady => State == BarBartenderTowelState.Stowed && releaseElapsed >= ReleaseDurationSeconds;
        public bool WipeReady => State == BarBartenderTowelState.Wiping;
        public bool IsTowelVisible => Towel.IsVisible;
        public CityPedestrianHandPropRegistry Towel => registry.ServiceTowel;
        public Vector3 PocketWorldPosition => registry.TowelPocketWorldPosition;
        public Bounds TowelWorldBounds => registry.TowelWorldBounds;
        public float LastGripError { get; private set; }
        public float WipeContactError => registry.TowelWipeContactError;
        public bool HandBehindBack => registry.IsTowelHandBehindBack;
        public Vector3 HandWorldPosition => registry.LeftHand.position;
        public Vector3 TargetHandWorldPosition { get; private set; }

        internal BarBartenderTowelPresentation(BarBartenderPresentation owner, BarBartenderAssetRegistry assetRegistry)
        {
            presentation = owner;
            registry = assetRegistry;
            Transform towel = Towel.transform;
            towelParent = towel.parent;
            towelLocalPosition = towel.localPosition;
            towelLocalRotation = towel.localRotation;
            towelLocalScale = towel.localScale;
            State = BarBartenderTowelState.Wiping;
            lastHand = HandPose();
            lastTarget = lastHand;
        }

        public void RequestService(bool requested)
        {
            serviceRequested = requested;
            if (requested && State == BarBartenderTowelState.Wiping)
                Begin(BarBartenderTowelState.Stowing);
            else if (!requested && State == BarBartenderTowelState.Stowed)
                Begin(BarBartenderTowelState.Drawing);
        }

        private void Begin(BarBartenderTowelState state)
        {
            State = state;
            Progress = 0f;
            startHand = HandPose();
            drawingHasTowel = false;
            LastGripError = 0f;
        }

        internal void Advance(float deltaTime, Pose wipeHand)
        {
            float delta = GameTimeScaleRuntime.IsPaused ? 0f : Mathf.Max(0f, deltaTime);
            if (State == BarBartenderTowelState.Wiping)
            {
                registry.SetServiceTowelVisible(true);
                lastHand = HandPose();
                return;
            }
            if (State == BarBartenderTowelState.Stowed)
            {
                registry.SetServiceTowelVisible(false);
                // The released arm returns continuously to the next action's sampled pose.
                // The service clock starts only after the towel itself is concealed.
                if (releaseElapsed < ReleaseDurationSeconds)
                {
                    releaseElapsed = Mathf.Min(ReleaseDurationSeconds, releaseElapsed + delta);
                    float amount = releaseElapsed / ReleaseDurationSeconds;
                    Pose sampled = HandPose();
                    Vector3 position = amount < .45f
                        ? Vector3.Lerp(lastHand.position, releaseSide, Smooth(amount / .45f))
                        : Vector3.Lerp(releaseSide, sampled.position, Smooth((amount - .45f) / .55f));
                    Solve(new Pose(position, Quaternion.Slerp(lastHand.rotation, sampled.rotation, Smooth(amount))));
                }
                return;
            }

            float duration = State == BarBartenderTowelState.Stowing ? StowDurationSeconds : DrawDurationSeconds;
            float next = Mathf.Min(1f, Progress + delta / duration);
            if (State == BarBartenderTowelState.Drawing && !drawingHasTowel)
                next = Mathf.Min(next, DrawContactProgress);
            Progress = next;
            Pose pocket = registry.ResolveTowelPocketHandPose();
            if (State == BarBartenderTowelState.Stowing)
            {
                Solve(StowPath(startHand, pocket, Progress));
                registry.SetServiceTowelVisible(true);
                if (Progress >= 1f && LastGripError <= ContactTolerance && HandBehindBack)
                {
                    // The real hand has carried this same mesh behind the
                    // back; its stored pose stays on the moving pelvis.
                    Towel.transform.SetParent(registry.Pelvis, true);
                    registry.SetServiceTowelVisible(false);
                    lastHand = HandPose();
                    releaseSide = SideWaypoint(pocket, .14f);
                    releaseElapsed = 0f;
                    State = BarBartenderTowelState.Stowed;
                    if (!serviceRequested) Begin(BarBartenderTowelState.Drawing);
                }
                return;
            }

            if (!drawingHasTowel)
            {
                Solve(DrawApproach(startHand, pocket, Progress / DrawContactProgress));
                registry.SetServiceTowelVisible(false);
                if (Progress >= DrawContactProgress && LastGripError <= ContactTolerance && HandBehindBack)
                {
                    Transform towel = Towel.transform;
                    towel.SetParent(towelParent, false);
                    towel.localPosition = towelLocalPosition;
                    towel.localRotation = towelLocalRotation;
                    towel.localScale = towelLocalScale;
                    // The same cloth reappears at the same real hand behind the back.
                    drawingHasTowel = true;
                    registry.SetServiceTowelVisible(true);
                }
                return;
            }
            Solve(DrawPath(pocket, wipeHand, Mathf.InverseLerp(DrawContactProgress, 1f, Progress)));
            registry.SetServiceTowelVisible(true);
            if (Progress >= 1f && LastGripError <= ContactTolerance && WipeContactError <= ContactTolerance)
            {
                State = BarBartenderTowelState.Wiping;
                lastHand = HandPose();
                if (serviceRequested) Begin(BarBartenderTowelState.Stowing);
            }
        }

        private Pose StowPath(Pose from, Pose pocket, float amount)
        {
            Vector3 up = registry.transform.up;
            Vector3 lifted = from.position + up * .10f;
            Vector3 side = SideWaypoint(pocket, .14f);
            Vector3 entry = pocket.position + up * .07f - registry.transform.forward * .035f;
            Vector3 position = amount < .2f ? Vector3.Lerp(from.position, lifted, Smooth(amount / .2f))
                : amount < .5f ? Vector3.Lerp(lifted, side, Smooth((amount - .2f) / .3f))
                : amount < .8f ? Vector3.Lerp(side, entry, Smooth((amount - .5f) / .3f))
                : Vector3.Lerp(entry, pocket.position, Smooth((amount - .8f) / .2f));
            return new Pose(position, Quaternion.Slerp(from.rotation, pocket.rotation,
                Smooth(Mathf.InverseLerp(.2f, .8f, amount))));
        }

        private Pose DrawApproach(Pose from, Pose pocket, float amount)
        {
            Vector3 side = SideWaypoint(pocket, .08f);
            Vector3 position = amount < .5f
                ? Vector3.Lerp(from.position, side, Smooth(amount * 2f))
                : Vector3.Lerp(side, pocket.position, Smooth((amount - .5f) * 2f));
            return new Pose(position, Quaternion.Slerp(from.rotation, pocket.rotation, Smooth(amount)));
        }

        private Pose DrawPath(Pose pocket, Pose wipe, float amount)
        {
            Vector3 up = registry.transform.up;
            Vector3 raisedPocket = pocket.position + up * .08f - registry.transform.forward * .035f;
            Vector3 side = SideWaypoint(pocket, .18f);
            Vector3 liftedWipe = wipe.position + up * .10f;
            Vector3 position = amount < .2f ? Vector3.Lerp(pocket.position, raisedPocket, Smooth(amount / .2f))
                : amount < .5f ? Vector3.Lerp(raisedPocket, side, Smooth((amount - .2f) / .3f))
                : amount < .8f ? Vector3.Lerp(side, liftedWipe, Smooth((amount - .5f) / .3f))
                : Vector3.Lerp(liftedWipe, wipe.position, Smooth((amount - .8f) / .2f));
            return new Pose(position, Quaternion.Slerp(pocket.rotation, wipe.rotation,
                Smooth(Mathf.InverseLerp(.2f, .8f, amount))));
        }

        private Vector3 SideWaypoint(Pose pocket, float lift)
        {
            Transform actor = registry.transform;
            Vector3 shoulder = registry.LeftUpperArm.position;
            Vector3 side = shoulder - actor.right * .13f;
            side += actor.up * Vector3.Dot(pocket.position + actor.up * lift - side, actor.up);
            return side;
        }

        private void Solve(Pose target)
        {
            TargetHandWorldPosition = target.position;
            lastTarget = target;
            presentation.SolveTowelHand(target);
            LastGripError = Vector3.Distance(registry.LeftHand.position, target.position);
        }

        internal void HoldDisabledPose()
        {
            if (IsTransitioning) presentation.SolveTowelHand(lastTarget);
        }

        private Pose HandPose() => new Pose(registry.LeftHand.position, registry.LeftHand.rotation);
        private static float Smooth(float amount) => Mathf.SmoothStep(0f, 1f, amount);
    }
}

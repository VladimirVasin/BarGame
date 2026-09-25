using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Combat weight and hero tension. The duel advances it; pose/contact samples only read it.</summary>
    internal sealed class CombatBodyMotion
    {
        private readonly Transform frame;
        private readonly Transform[] bones = new Transform[4];
        private readonly Quaternion[] baseRotations = new Quaternion[4];
        private SecondOrderFilter forwardSpeed = new SecondOrderFilter(12f, 1f);
        private SecondOrderFilter sideSpeed = new SecondOrderFilter(12f, 1f);
        private SecondOrderFilter pitch = new SecondOrderFilter(15f, .85f);
        private SecondOrderFilter roll = new SecondOrderFilter(15f, .85f);
        private SecondOrderFilter turn = new SecondOrderFilter(14f, 1f);
        private SecondOrderFilter headPitch = new SecondOrderFilter(11f, .85f);
        private SecondOrderFilter headRoll = new SecondOrderFilter(11f, .85f);
        private SecondOrderFilter fear = new SecondOrderFilter(7f, 1f);
        private SecondOrderFilter exhaustion = new SecondOrderFilter(4f, 1f);
        private SecondOrderFilter effort = new SecondOrderFilter(9f, 1f);
        private SecondOrderFilter flinch = new SecondOrderFilter(25f, 1f);
        private float fearTarget, exhaustionTarget, effortTarget, threatTarget;
        private float breathPhase, irregularPhase, tremorPhase;
        private float flinchClock = FlinchSeconds, flinchPeak;
        private bool threatObserved;
        private Vector3 previousForward;
        private Vector3 shoveAxis;
        private float shoveReach;
        private bool applied;
        private const float FlinchSeconds = .36f;

        public float FearAmount => Mathf.Clamp01(fear.Value);
        public float EffortAmount => Mathf.Clamp01(effort.Value);
        public float FlinchAmount => FearAmount * Mathf.Clamp01(flinch.Value);
        // Degrees of chest breathing, independent of the existing HP response.
        public float BreathAmplitude => FearAmount * (1.05f + .9f * Mathf.Clamp01(exhaustion.Value) + .25f * EffortAmount);
        public float BreathAmount => BreathAmplitude *
            (.8f * Mathf.Sin(breathPhase) + .2f * Mathf.Sin(2f * breathPhase + irregularPhase));

        public CombatBodyMotion(Transform root, Transform actorFrame)
        {
            frame = actorFrame;
            string[] names = { "spine", "chest", "neck", "head" };
            foreach (Transform bone in root.GetComponentsInChildren<Transform>(true))
                for (int i = 0; i < names.Length; i++)
                    if (bone.name == names[i]) bones[i] = bone;
            foreach (Transform bone in bones)
                if (bone == null) throw new InvalidOperationException("Combat motion needs the original torso and head chain.");
            Reset();
        }

        /// <summary>
        /// Observations only: the caller supplies a nearby, visible opponent's
        /// actual windup, never an AI intent or a prediction of a future hit.
        /// Setting targets cannot move a bone or advance the emotional response.
        /// </summary>
        public void SetEmotionTargets(bool frightenedHero, float stamina01, float visibleWindupThreat01,
            float effort01, bool active = true)
        {
            fearTarget = frightenedHero && active ? 1f : 0f;
            exhaustionTarget = fearTarget * (1f - Unit(stamina01));
            effortTarget = fearTarget * Unit(effort01);
            threatTarget = fearTarget * Unit(visibleWindupThreat01);
        }

        /// <summary>The planted body drives the open palm on the same externally sampled shove clock.</summary>
        public void SetShovePose(bool active, Vector3 direction, float elapsed, float contactSeconds, float duration)
        {
            shoveReach = 0f;
            if (!active || !float.IsFinite(elapsed) || !float.IsFinite(contactSeconds) || !float.IsFinite(duration) ||
                contactSeconds <= 0f || duration <= contactSeconds) return;
            Vector3 forward = Vector3.ProjectOnPlane(direction, frame.up);
            if (!float.IsFinite(forward.x) || !float.IsFinite(forward.y) || !float.IsFinite(forward.z)) return;
            if (forward.sqrMagnitude < .0001f) forward = frame.forward;
            shoveAxis = Vector3.Cross(frame.up, forward.normalized).normalized;
            shoveReach = CombatSupportGrip.ShoveReach(Mathf.Clamp(elapsed, 0f, duration), contactSeconds, duration);
        }

        public void Advance(float seconds, Vector3 velocity)
        {
            if (seconds <= 0f) return;
            Vector3 local = frame.InverseTransformDirection(velocity);
            local.y = 0f;
            local = Vector3.ClampMagnitude(local, 5f);
            // The difference from the lagging centre of mass changes sign when braking.
            float forward = local.z - forwardSpeed.Value;
            float sideways = local.x - sideSpeed.Value;
            forwardSpeed.Advance(local.z, seconds);
            sideSpeed.Advance(local.x, seconds);
            float yaw = Mathf.Clamp(Vector3.SignedAngle(previousForward, frame.forward, Vector3.up) / seconds, -240f, 240f);
            previousForward = frame.forward;
            pitch.Advance(Mathf.Clamp(forward * 3.5f + local.z * .6f, -6f, 6f), seconds);
            roll.Advance(Mathf.Clamp(-sideways * 4f - local.x * .7f, -6f, 6f), seconds);
            turn.Advance(-yaw * .022f, seconds);
            headPitch.Advance(-pitch.Value * .45f, seconds);
            headRoll.Advance(-roll.Value * .45f, seconds);
            AdvanceEmotion(seconds);
        }

        private void AdvanceEmotion(float seconds)
        {
            float remaining = Mathf.Min(seconds, SecondOrderFilter.MaximumAdvanceSeconds);
            while (remaining > 0f)
            {
                float step = Mathf.Min(remaining, SecondOrderFilter.MaximumSubStepSeconds);
                remaining -= step;
                fear.Advance(fearTarget, step);
                exhaustion.Advance(exhaustionTarget, step);
                effort.Advance(effortTarget, step);

                // One short protective contraction per observed tell. Holding
                // a charge does not trap the hero in a permanent recoil pose.
                if (threatTarget <= .01f) threatObserved = false;
                else if (!threatObserved && threatTarget > .05f)
                {
                    threatObserved = true;
                    flinchClock = 0f;
                    flinchPeak = threatTarget;
                }
                float contraction = 0f;
                if (flinchClock < FlinchSeconds)
                {
                    flinchPeak = Mathf.Max(flinchPeak, threatTarget);
                    contraction = flinchPeak * (1f - Mathf.SmoothStep(0f, 1f,
                        (flinchClock - .07f) / (FlinchSeconds - .07f)));
                    flinchClock = Mathf.Min(FlinchSeconds, flinchClock + step);
                }
                flinch.Advance(contraction, step);

                // The slow modulation makes successive breaths unequal without
                // random samples, wall time, or a discontinuity at charge release.
                irregularPhase = Mathf.Repeat(irregularPhase + step * .87f, Mathf.PI * 2f);
                float breathRate = 3.2f + 1.7f * Mathf.Clamp01(exhaustion.Value) + .45f * EffortAmount;
                breathPhase = Mathf.Repeat(breathPhase + step * breathRate *
                    (1f + .13f * Mathf.Sin(irregularPhase)), Mathf.PI * 2f);
                tremorPhase = Mathf.Repeat(tremorPhase + step * 48f, Mathf.PI * 2f);
            }
        }

        public void Apply()
        {
            Restore();
            for (int i = 0; i < bones.Length; i++) baseRotations[i] = bones[i].localRotation;
            applied = true;
            // Leave pelvis/soles on their authored contacts. The hands inherit
            // the chest, then the existing support solver closes the left palm.
            Rotate(0, frame.right, pitch.Value * .35f);
            Rotate(1, frame.right, pitch.Value * .65f);
            Rotate(0, frame.forward, roll.Value * .35f);
            Rotate(1, frame.forward, roll.Value * .65f);
            Rotate(1, Vector3.up, turn.Value);
            Rotate(2, frame.right, headPitch.Value * .35f);
            Rotate(3, frame.right, headPitch.Value * .65f);
            Rotate(3, frame.forward, headRoll.Value);

            float breath = BreathAmount;
            float tense = FearAmount;
            float shrink = FlinchAmount;
            float brace = tense * (1f - .75f * EffortAmount) * (1f - shoveReach);
            float tremor = tense * (.1f + .12f * EffortAmount) *
                Mathf.Sin(tremorPhase) * (.65f + .35f * Mathf.Sin(irregularPhase));
            // Authored hero clips own the frightened stance and awkward strikes.
            // These small rotations keep it alive: at most 2.2 degrees of breath,
            // .22 degrees of tremor and a brief chin tuck. Both hands inherit the
            // chest together; no wrist, foot, gameplay root or timing is changed.
            // He keeps his chest away from the threat, then has to overcome
            // that defensive brace to put his body behind the weapon.
            Rotate(0, frame.right, breath * -.2f + shrink * .65f - brace * 2f);
            Rotate(1, frame.right, -breath + shrink * 1.55f - brace * 2.5f);
            Rotate(1, frame.forward, tremor);
            Rotate(1, Vector3.up, tremor * .55f);
            Rotate(2, frame.right, tense * .5f + shrink * .9f + breath * .12f);
            Rotate(3, frame.right, tense * .85f + shrink * 2.3f + EffortAmount * .4f);
            Rotate(3, frame.forward, shrink * -.8f);
            // Reach comes from leaning over the planted stance, never extending
            // an arm bone. Concentrating the drive at the spine carries both
            // shoulders forward before weapon clearance and left-palm IK run.
            if (shoveReach > 0f)
            {
                Rotate(0, shoveAxis, 25f * shoveReach);
                Rotate(1, shoveAxis, 5f * shoveReach);
                Rotate(2, shoveAxis, -14f * shoveReach);
                Rotate(3, shoveAxis, -8f * shoveReach);
            }
        }

        public void Restore()
        {
            if (!applied) return;
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null) bones[i].localRotation = baseRotations[i];
            applied = false;
        }

        public void Forget() => applied = false;

        public void Reset()
        {
            Restore();
            forwardSpeed.Reset(); sideSpeed.Reset(); pitch.Reset(); roll.Reset(); turn.Reset();
            headPitch.Reset(); headRoll.Reset();
            fear.Reset(); exhaustion.Reset(); effort.Reset(); flinch.Reset();
            fearTarget = exhaustionTarget = effortTarget = threatTarget = 0f;
            breathPhase = irregularPhase = tremorPhase = flinchPeak = 0f;
            flinchClock = FlinchSeconds;
            threatObserved = false;
            shoveReach = 0f; shoveAxis = Vector3.zero;
            previousForward = frame.forward;
        }

        private static float Unit(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp01(value);

        private void Rotate(int index, Vector3 axis, float degrees) =>
            bones[index].rotation = Quaternion.AngleAxis(degrees, axis) * bones[index].rotation;
    }
}

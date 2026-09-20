using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Bounded, reversible damage presentation for the two combat rigs. The duel
    /// owns its clock; sampling a pose never advances a spring or changes health.
    /// </summary>
    public sealed class CombatDamagePose : IDisposable
    {
        private Transform actorFrame;
        private Transform[] bones = Array.Empty<Transform>();
        private Quaternion[] baseRotations = Array.Empty<Quaternion>();
        private bool baseCaptured;
        private Vector3 impulse;
        private float breathPhase;
        private SecondOrderFilter injury = new SecondOrderFilter(6f, 1f);
        private SecondOrderFilter pitch = new SecondOrderFilter(14f, .58f);
        private SecondOrderFilter roll = new SecondOrderFilter(14f, .58f);
        private SecondOrderFilter turn = new SecondOrderFilter(14f, .58f);
        private SecondOrderFilter headPitch = new SecondOrderFilter(10f, .65f);
        private SecondOrderFilter headRoll = new SecondOrderFilter(10f, .65f);
        private SecondOrderFilter shoulder = new SecondOrderFilter(11f, .65f);

        public bool IsInitialized => actorFrame != null && bones.Length == 6;
        public float InjuryAmount => Mathf.Clamp01(injury.Value);
        public float BreathAmount => InjuryAmount * InjuryAmount * Mathf.Sin(breathPhase);
        public float AppliedWeight { get; private set; }
        public bool HasOffsets => baseCaptured;
        public float ReactionAmount => Mathf.Clamp01(
            Mathf.Max(Mathf.Abs(pitch.Value), Mathf.Abs(roll.Value)) / 20f);

        /// <param name="rigRoot">The imported model root, including its anatomical bones.</param>
        /// <param name="frame">The gameplay actor, whose forward defines the strike direction.</param>
        public void Initialize(Transform rigRoot, Transform frame = null)
        {
            if (rigRoot == null) throw new ArgumentNullException(nameof(rigRoot));
            Reset();
            var found = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform candidate in rigRoot.GetComponentsInChildren<Transform>(true))
                if (!found.ContainsKey(candidate.name)) found.Add(candidate.name, candidate);
            string[] names = { "spine", "chest", "neck", "head", "upper_arm.L", "forearm.L" };
            var resolved = new Transform[names.Length];
            for (int i = 0; i < names.Length; i++)
                if (!found.TryGetValue(names[i], out resolved[i]))
                    throw new InvalidOperationException("Combat damage pose needs joint " + names[i]);
            bones = resolved;
            baseRotations = new Quaternion[bones.Length];
            actorFrame = frame != null ? frame : rigRoot;
        }

        public void Advance(float seconds, float health01)
        {
            if (!Finite(seconds) || seconds < 0f) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (!Finite(health01)) throw new ArgumentOutOfRangeException(nameof(health01));
            // A smooth continuum: the silhouette is already affected at 75%,
            // visibly folded at 50%, and fully protective by 25% health.
            float target = Mathf.SmoothStep(0f, 1f, (1f - Mathf.Clamp01(health01)) / .75f);
            float remaining = Mathf.Min(seconds, SecondOrderFilter.MaximumAdvanceSeconds);
            while (remaining > 0f)
            {
                float step = Mathf.Min(remaining, SecondOrderFilter.MaximumSubStepSeconds);
                remaining -= step;
                injury.Advance(target, step);
                // Breath grows through the damped injury envelope. Its clock
                // only advances with the duel, so pause and resampling freeze it.
                if (InjuryAmount > 0f)
                    breathPhase = Mathf.Repeat(breathPhase +
                        Mathf.Lerp(2.1f, 3.6f, InjuryAmount) * step, Mathf.PI * 2f);
                pitch.Advance(impulse.x, step);
                roll.Advance(impulse.y, step);
                turn.Advance(impulse.z, step);
                // The head and free shoulder follow the chest with their own
                // inertia; repeated hits add energy without resetting the pose.
                headPitch.Advance(pitch.Value * .65f, step);
                headRoll.Advance(roll.Value * .6f, step);
                shoulder.Advance(ReactionAmount, step);
                impulse *= Mathf.Exp(-15f * step);
            }
            if (impulse.sqrMagnitude < .000001f) impulse = Vector3.zero;
        }

        /// <summary>The direction is the force travelling into/through the victim, not toward the attacker.</summary>
        public void Hit(Vector3 worldDirection, float severity)
        {
            if (!IsInitialized || !Finite(severity) || severity <= 0f || !Finite(worldDirection)) return;
            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude < .000001f) worldDirection = -Forward();
            worldDirection.Normalize();
            Vector3 forward = Forward();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            float front = Vector3.Dot(worldDirection, forward);
            float side = Vector3.Dot(worldDirection, right);
            impulse += new Vector3(front * 52f, -side * 46f, side * 18f) * Mathf.Clamp(severity, 0f, 1.5f);
            impulse = Vector3.ClampMagnitude(impulse, 70f);
        }

        /// <summary>
        /// The caller masks this to zero during committed weapon/guard/step
        /// actions. It must restore before every animation or contact sample.
        /// </summary>
        public void Apply(float combatWeight)
        {
            Restore();
            if (!IsInitialized || !Finite(combatWeight)) return;
            float weight = Mathf.Clamp01(combatWeight);
            if (weight <= 0f || (InjuryAmount <= .0001f && ReactionAmount <= .0001f &&
                Mathf.Abs(headPitch.Value) + Mathf.Abs(headRoll.Value) + Mathf.Abs(shoulder.Value) <= .0001f)) return;
            for (int i = 0; i < bones.Length; i++) baseRotations[i] = bones[i].localRotation;
            baseCaptured = true;
            AppliedWeight = weight;
            Vector3 forward = Forward();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            float hurt = InjuryAmount;
            float breath = BreathAmount;
            float shoulderBreath = hurt * hurt * Mathf.Sin(breathPhase - .3f);
            float bodyPitch = Mathf.Clamp(pitch.Value, -24f, 24f);
            float bodyRoll = Mathf.Clamp(roll.Value, -22f, 22f);
            // Rotate in the actor's frame. Imported bone-local axes differ
            // across the hero/NPC, and imported metre units have a 100x root.
            Rotate(0, right, (hurt * 7f + bodyPitch * .3f) * weight);
            Rotate(0, forward, bodyRoll * .25f * weight);
            Rotate(1, right, (hurt * 11f + bodyPitch * .7f - breath * 1.5f) * weight);
            Rotate(1, forward, (hurt * -2f + bodyRoll * .75f) * weight);
            Rotate(1, Vector3.up, Mathf.Clamp(turn.Value, -9f, 9f) * weight);
            Rotate(2, right, (hurt * -3f + headPitch.Value * .4f + breath * .35f) * weight);
            Rotate(3, right, (hurt * 7f + headPitch.Value * .6f) * weight);
            Rotate(3, forward, headRoll.Value * weight);
            Rotate(4, right, (hurt * 8f + shoulder.Value * 10f + shoulderBreath * .8f) * weight);
            Rotate(4, forward, (hurt * 8f + shoulder.Value * 7f) * weight);
            Rotate(5, right, hurt * 10f * weight);
            // Pelvis, feet and the weapon hand remain on their authored pose.
            // The free hand closes toward the torso without inventing a contact.
        }

        public void Restore()
        {
            if (baseCaptured)
                for (int i = 0; i < bones.Length; i++)
                    if (bones[i] != null) bones[i].localRotation = baseRotations[i];
            ForgetBase();
        }

        /// <summary>Hands the currently visible bones to physics without restoring the clip underneath.</summary>
        public void ForgetBase()
        {
            baseCaptured = false;
            AppliedWeight = 0f;
        }

        public void Reset()
        {
            Restore();
            impulse = Vector3.zero;
            breathPhase = 0f;
            injury.Reset(); pitch.Reset(); roll.Reset(); turn.Reset();
            headPitch.Reset(); headRoll.Reset(); shoulder.Reset();
        }

        public void Dispose()
        {
            Reset();
            actorFrame = null;
            bones = Array.Empty<Transform>();
            baseRotations = Array.Empty<Quaternion>();
        }

        private Vector3 Forward()
        {
            Vector3 forward = actorFrame.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > .000001f ? forward.normalized : Vector3.forward;
        }

        private void Rotate(int index, Vector3 axis, float degrees) =>
            bones[index].rotation = Quaternion.AngleAxis(degrees, axis) * bones[index].rotation;

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}

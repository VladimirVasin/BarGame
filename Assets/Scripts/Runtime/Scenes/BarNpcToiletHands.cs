using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Relaxed live hands on the visitor's own rig, after its authored seated sample.</summary>
    internal sealed class BarNpcToiletHands
    {
        private sealed class Arm
        {
            public Transform Upper, Forearm, Hand, Knee;
            public AnatomicalHandFrame Anatomy;
        }

        private readonly Transform actor, pelvis;
        private readonly NpcHandPose fingers;
        private readonly Arm[] arms = new Arm[2];
        public Vector3 LeftPalm => Palm(arms[0]);
        public Vector3 RightPalm => Palm(arms[1]);
        public float ContactError { get; private set; }

        public BarNpcToiletHands(BarPatron patron)
        {
            CityPedestrianAssetRegistry registry = patron?.Registry;
            if (registry == null) throw new ArgumentException("Seated hands require the actual visitor.", nameof(patron));
            actor = registry.transform;
            pelvis = registry.PelvisAnchor;
            fingers = registry.GetComponentInChildren<NpcHandPose>();
            if (pelvis == null || fingers == null)
                throw new InvalidOperationException("Seated hands require the registered pelvis and hand anatomy.");
            for (int side = 0; side < arms.Length; side++)
            {
                string suffix = side == 0 ? ".L" : ".R";
                var arm = new Arm
                {
                    Upper = CityPedestrianHandProps.FindSocket(registry.ModelRoot, "upper_arm" + suffix),
                    Forearm = CityPedestrianHandProps.FindSocket(registry.ModelRoot, "forearm" + suffix),
                    Hand = CityPedestrianHandProps.FindSocket(registry.ModelRoot, "hand" + suffix),
                    Knee = CityPedestrianHandProps.FindSocket(registry.ModelRoot, "shin" + suffix),
                    Anatomy = fingers.GetAnatomicalFrame(side == 0)
                };
                if (arm.Upper == null || arm.Forearm == null || arm.Hand == null || arm.Knee == null)
                    throw new InvalidOperationException("Seated hands require the production visitor's arm and knee bones.");
                arms[side] = arm;
            }
        }

        /// <param name="normalizedPhase">The authored three-second loop's phase, never wall-clock time.</param>
        public void Apply(float normalizedPhase, float seatedWeight)
        {
            float weight = Mathf.Clamp01(seatedWeight);
            ContactError = 0f;
            if (weight <= 0f) { Clear(); return; }
            float phase = normalizedPhase * (2f * Mathf.PI);
            for (int index = 0; index < arms.Length; index++)
            {
                Arm arm = arms[index];
                float side = index == 0 ? -1f : 1f;
                float breath = Mathf.Sin(phase + index * 1.1f);
                float wrist = Mathf.Sin(phase * 2f + index * .7f);
                // Upper thighs support the palms. The real knees determine
                // forward placement, while the pelvis keeps both hands low
                // across the existing visitor body proportions.
                Vector3 target = Vector3.Lerp(arm.Knee.position, pelvis.position, .55f) +
                    actor.up * (.10f + .006f * breath) + actor.right * (side * .025f + .003f * wrist) +
                    actor.forward * (.006f * Mathf.Sin(phase + index * .7f));
                Vector3 distal = actor.forward + actor.right * (-side * .12f + .035f * wrist);
                Vector3 normal = -actor.up + actor.right * (.045f * breath);
                Quaternion rotation = arm.Anatomy.ResolveHandRotation(normal, distal);
                Vector3 wristTarget = arm.Anatomy.ResolveHandPosition(target, rotation);
                float reach = (Vector3.Distance(arm.Upper.position, arm.Forearm.position) +
                    Vector3.Distance(arm.Forearm.position, arm.Hand.position)) * .985f;
                Vector3 offset = wristTarget - arm.Upper.position;
                if (offset.sqrMagnitude > reach * reach) wristTarget = arm.Upper.position + offset.normalized * reach;
                LimbTwoBoneIk.Solve(arm.Upper, arm.Forearm, arm.Hand, wristTarget, rotation,
                    arm.Upper.position + actor.right * (side * .30f) + actor.forward * .10f - actor.up * .25f,
                    weight, .995f, true);
                fingers.SetGrip(index == 0, weight * (.07f + .015f * breath));
                if (weight >= .99f) ContactError = Mathf.Max(ContactError, Vector3.Distance(Palm(arm), target));
            }
        }

        public void Clear()
        {
            fingers.SetGrip(true, 0f);
            fingers.SetGrip(false, 0f);
            ContactError = 0f;
        }

        private static Vector3 Palm(Arm arm) => arm.Hand.position + arm.Hand.rotation * arm.Anatomy.PalmCentreInHand;
    }
}

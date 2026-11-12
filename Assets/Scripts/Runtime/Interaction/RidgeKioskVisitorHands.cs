using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal sealed class RidgeKioskVisitorHands
    {
        private readonly CityPedestrianActor actor;
        private readonly CityPedestrianPresentation presentation;
        private readonly Transform shoulder, upper, lower, hand, socket, leftShoulder, leftUpper, leftLower, leftSocket;
        private readonly Transform spine, chest, head, mouth;
        private readonly NpcHandPose fingers;
        private readonly NpcHeroUpperBodyPose.Binding upperBody;
        private readonly Animator animator;
        private AnimatorCullingMode previousCulling;
        private bool ownsContactCulling;

        internal RidgeKioskVisitorHands(CityPedestrianActor actor)
        {
            this.actor = actor;
            presentation = actor.Presentation;
            animator = presentation.Registry.Animator;
            Transform model = presentation.Registry.ModelRoot;
            shoulder = Find(BarPatronWorldBuilder.RightClavicleBoneName);
            upper = Find(BarPatronWorldBuilder.RightUpperArmBoneName);
            lower = Find(BarPatronWorldBuilder.RightForearmBoneName);
            hand = Find(BarPatronWorldBuilder.RightHandBoneName);
            socket = Find(BarPatronWorldBuilder.BottleSocketName);
            leftShoulder = Find(BarPatronWorldBuilder.LeftClavicleBoneName);
            leftUpper = Find(BarPatronWorldBuilder.LeftUpperArmBoneName);
            leftLower = Find(BarPatronWorldBuilder.LeftForearmBoneName);
            leftSocket = Find(BarPatronWorldBuilder.LeftHandSocketName);
            spine = Find(BarPatronWorldBuilder.SpineBoneName);
            chest = Find(BarPatronWorldBuilder.ChestBoneName);
            head = Find(BarPatronWorldBuilder.HeadBoneName);
            mouth = Find(BarPatronWorldBuilder.MouthSocketName);
            fingers = hand.GetComponentInParent<NpcHandPose>();
            upperBody = new NpcHeroUpperBodyPose.Binding(model);
            Transform Find(string name) => CityPedestrianHandProps.FindSocket(model, name);
        }

        internal BarPatronDrinkingArmPose BindTable(BarPatronDrinkProp drink, Vector3 top, int place, BarPatronDrinkTimeline timeline)
        {
            RestoreContactCulling();
            AttachToHand(drink);
            presentation.Advance(0f, false, true);
            Vector3 outward = RidgeKioskCourtPlan.PlaceDirection(place);
            Vector3 right = Vector3.Cross(Vector3.up, -outward);
            Quaternion upright = CarryRotation();
            Vector3 vesselBase = ResolveCarryGrip() + upright * (drink.BaseLocalPosition - drink.GripLocalPosition);
            Vector3 support = top + outward * .56f - right * .20f;
            var pose = presentation.gameObject.AddComponent<BarPatronDrinkingArmPose>();
            pose.InitializeTable(timeline, presentation, actor.transform,
                shoulder, upper, lower, hand, socket, leftShoulder, leftUpper, leftLower, leftSocket,
                spine, chest, head, mouth, drink.Root, drink.Mouth, drink.GripToLipDistance,
                drink.GripToBaseDistance, vesselBase, support, drinkProp: drink, keepDrinkInHand: true);
            return pose;
        }

        internal void Receive(BarPatronDrinkProp drink, Vector3 at, float reach, float carryBlend, bool pickedUp)
        {
            KeepContactAnimated();
            Quaternion rotation = CarryRotation();
            AnatomicalHandFrame anatomy = fingers.GetAnatomicalFrame(false, drink.GripKind);
            Vector3 onCounter = at + rotation * drink.GripLocalPosition;
            SolveGrip(drink, anatomy, rotation, Vector3.Lerp(onCounter, ResolveCarryGrip(), carryBlend), reach);
            if (pickedUp) PlaceInHand(drink, anatomy, rotation);
            else drink.Root.SetPositionAndRotation(at, rotation);
        }

        internal void Carry(BarPatronDrinkProp drink)
        {
            KeepContactAnimated();
            Quaternion rotation = CarryRotation();
            AnatomicalHandFrame anatomy = fingers.GetAnatomicalFrame(false, drink.GripKind);
            SolveGrip(drink, anatomy, rotation, ResolveCarryGrip(), 1f);
            PlaceInHand(drink, anatomy, rotation);
        }

        private Vector3 ResolveCarryGrip()
        {
            Transform body = actor.transform;
            return upper.position - body.up * .29f + body.forward * .22f + body.right * .055f;
        }

        private Quaternion CarryRotation() => Quaternion.LookRotation(actor.transform.right, actor.transform.up);

        private void SolveGrip(BarPatronDrinkProp drink, AnatomicalHandFrame anatomy, Quaternion rotation, Vector3 grip, float weight)
        {
            Transform body = actor.transform;
            Pose wrist = ResolveWrist(drink, anatomy, rotation, grip);
            Vector3 elbow = upper.position - body.up * .26f + body.right * .13f;
            LimbTwoBoneIk.Solve(upper, lower, hand, wrist.position, wrist.rotation, elbow, weight, .98f, true);
            fingers.SetGrip(false, weight, drink.GripKind);
        }

        private static Pose ResolveWrist(BarPatronDrinkProp drink, AnatomicalHandFrame anatomy,
            Quaternion rotation, Vector3 grip) =>
            anatomy.ResolveCylinderPose(grip, rotation * drink.OpeningLocalAxis, -(rotation * drink.HandleLocalAxis));

        private Vector3 GripCentre(AnatomicalHandFrame anatomy) =>
            hand.position + hand.rotation * anatomy.CylinderCentreInHand;

        private void PlaceInHand(BarPatronDrinkProp drink, AnatomicalHandFrame anatomy, Quaternion rotation)
        {
            AttachToHand(drink);
            drink.Root.SetPositionAndRotation(drink.RootPositionAtGrip(GripCentre(anatomy), rotation), rotation);
        }

        private void AttachToHand(BarPatronDrinkProp drink)
        {
            // Keep the vessel on the same transform that drives the visible
            // skin until the throw hands it to world physics.
            if (drink.Root.parent != hand) drink.Root.SetParent(hand, true);
        }

        internal void ThrowPose(NpcHeroUpperBodyPose source, AnimationClip clip, float progress, BarPatronDrinkProp drink)
        {
            KeepContactAnimated();
            const float blendProgress = .18f;
            upperBody.Restore();
            if (drink != null && progress < blendProgress) Carry(drink);
            float weight = Mathf.SmoothStep(0f, 1f,
                Mathf.Min(progress, 1f - progress) / blendProgress);
            source.Apply(upperBody, clip, progress * clip.length, weight, additive: false);
            if (drink == null) return;

            // Match the hero's final throw contact. The clip drives the
            // elbow and swing; the live wrist solves to the upright vessel.
            AnatomicalHandFrame anatomy = fingers.GetAnatomicalFrame(false, drink.GripKind);
            Quaternion rotation = CarryRotation();
            Pose wrist = ResolveWrist(drink, anatomy, rotation, GripCentre(anatomy));
            LimbTwoBoneIk.Solve(upper, lower, hand, wrist.position, wrist.rotation, lower.position);
            upperBody.IncludeContactCorrections();
            PlaceInHand(drink, anatomy, rotation);
            fingers.SetGrip(false, 1f, drink.GripKind);
        }

        private void KeepContactAnimated()
        {
            if (ownsContactCulling) return;
            previousCulling = animator.cullingMode;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            ownsContactCulling = true;
        }

        internal void RestoreContactCulling()
        {
            if (!ownsContactCulling) return;
            if (animator) animator.cullingMode = previousCulling;
            ownsContactCulling = false;
        }

        internal void EndThrow()
        {
            upperBody.Restore();
            RestoreContactCulling();
        }

        internal void ReleaseGrip() => fingers?.ResetGrip();
    }

    /// <summary>Fits the hero's authored right hand to the court regulars' existing rigs and outfits.</summary>
    internal sealed class RidgeKioskHeroHand : IDisposable
    {
        private readonly NpcHandPose.HandBinding source;
        private readonly Dictionary<string, SkinnedMeshRenderer> parts = new(StringComparer.Ordinal);
        private readonly Dictionary<Mesh, (Mesh Mesh, string[] Bones)> fitted = new();

        internal RidgeKioskHeroHand()
        {
            var hero = Player3DResources.LoadPrefab().GetComponent<NpcHandPose>();
            foreach (var binding in hero.Hands)
                if (!binding.IsLeft) source = binding;
            foreach (var renderer in source.Renderers) parts.Add(renderer.name, renderer);
        }

        internal void Fit(CityPedestrianPresentation presentation)
        {
            Transform model = presentation.Registry.ModelRoot;
            // City fits each body while it is inactive in the presentation pool.
            var pose = model.GetComponentInParent<NpcHandPose>(includeInactive: true);
            if (pose == null)
                throw new InvalidOperationException($"Court visitor '{presentation.Registry.DesignId}' has no hand rig.");

            foreach (var binding in pose.Hands)
            {
                if (binding.IsLeft) continue;
                foreach (var renderer in binding.Renderers)
                {
                    SkinnedMeshRenderer donor = parts[HeroPartName(renderer.name)];
                    Mesh npcMesh = renderer.sharedMesh;
                    if (!fitted.TryGetValue(npcMesh, out var part))
                    {
                        part = FitMesh(donor, npcMesh);
                        fitted.Add(npcMesh, part);
                    }

                    var bones = new Transform[part.Bones.Length];
                    for (int i = 0; i < bones.Length; i++)
                    {
                        // The grip resolves the wrist bind pose by transform identity.
                        // An unused slot must not masquerade as the actual wrist.
                        bones[i] = part.Bones[i] == null ? model : Require(model, part.Bones[i]);
                    }
                    renderer.sharedMesh = part.Mesh;
                    renderer.bones = bones;
                }

                CopyAnchor(binding, source.CentreAnchor);
                CopyAnchor(binding, source.AxisAnchor);
                CopyAnchor(binding, source.PalmAnchor);
                foreach (string marker in BowlAnchors) CopyAnchor(binding, Require(source.Hand, marker));
            }

            pose.Configure(new[] { pose.Hands[0], pose.Hands[1] }, pose.ShapeName, pose.CylinderRadius);
        }

        private static (Mesh Mesh, string[] Bones) FitMesh(SkinnedMeshRenderer donor, Mesh npcMesh)
        {
            if (!PlayerVodkaGripCatalog.TryGetEntry(donor.sharedMesh, out var grip))
                throw new InvalidOperationException("The hero's bottle grip is missing on " + donor.name);

            Transform[] donorBones = donor.bones;
            var bones = new string[donorBones.Length];
            using (var weights = grip.Extended.GetAllBoneWeights())
                foreach (var weight in weights)
                    if (weight.weight > 0f) bones[weight.boneIndex] = donorBones[weight.boneIndex].name;

            // Keep the stock skin/glove palette and renderer ownership.
            var uv = new Vector2[grip.Extended.vertexCount];
            Array.Fill(uv, npcMesh.uv[0]);
            Mesh mesh = UnityEngine.Object.Instantiate(grip.Extended);
            mesh.uv = uv;
            return (mesh, bones);
        }

        private static readonly string[] BowlAnchors = { "ANCHOR_WineBowlGripCentre.R", "ANCHOR_WineBowlGripAxis.R",
            "ANCHOR_WineBowlGripPalm.R", "ANCHOR_SnifterBowlGripCentre.R", "ANCHOR_SnifterBowlGripAxis.R", "ANCHOR_SnifterBowlGripPalm.R" };

        private void CopyAnchor(NpcHandPose.HandBinding binding, Transform marker)
        {
            Transform target = Require(binding.Hand, marker.name);
            target.position = binding.Hand.TransformPoint(source.Hand.InverseTransformPoint(marker.position));
        }

        private static string HeroPartName(string name)
        {
            if (name.Contains("Palm.")) return "GEO_Hand.R";
            if (name.Contains("Thumb.")) return "GEO_Thumb.R";
            return "GEO_" + name.Substring(name.IndexOf("Finger", StringComparison.Ordinal));
        }

        private static Transform Require(Transform root, string name) => CityPedestrianHandProps.FindSocket(root, name) ??
            throw new InvalidOperationException("A court regular cannot bind the hero hand joint " + name);

        public void Dispose()
        {
            foreach (var part in fitted.Values)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(part.Mesh);
                else UnityEngine.Object.DestroyImmediate(part.Mesh);
            }
            fitted.Clear();
        }
    }
}

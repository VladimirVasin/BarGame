using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
        [UnityTest]
        public IEnumerator Range_PistolRegionalReactionsAndWoundsStayContinuousThroughPauseAndReset()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            Assert.That(CombatTestStartService.TryStart(CombatWeaponId.Pistol), Is.True);
            yield return AwaitSelectedCombatRange();
            var parts = new[] { Player3DAnatomicalPart.Torso, Player3DAnatomicalPart.LowerTorso,
                Player3DAnatomicalPart.LeftUpperArm, Player3DAnatomicalPart.LeftForearm,
                Player3DAnatomicalPart.LeftHand, Player3DAnatomicalPart.LeftThigh,
                Player3DAnatomicalPart.LeftShin, Player3DAnatomicalPart.LeftFoot };

            foreach (bool heroVictim in new[] { false, true })
            {
                CombatActor target = heroVictim ? root.Hero : root.Opponent;
                CombatActor source = heroVictim ? root.Opponent : root.Hero;
                string subject = heroVictim ? "hero" : "opponent";
                Vector3 chestReaction = Vector3.zero;
                foreach (Player3DAnatomicalPart part in parts)
                {
                    root.ResetRound();
                    PlacePair(6f);
                    yield return null;
                    PresentInertiaPose();
                    root.CameraFollow.Snap();
                    string context = subject + " " + part;
                    Transform bone = FindAnatomicalBone(target, ProjectileBoneName(part));
                    Quaternion ready = bone.rotation;
                    Vector3 readyPosition = bone.position;
                    float pelvisBefore = FindAnatomicalBone(target, "pelvis").position.y;
                    FireRegionalProjectile(source, target, part);
                    Assert.That(target.State.IsDefeated, Is.False, context);
                    Assert.That(target.ImpactMotion.ProjectileReactionPart, Is.EqualTo(part), context);
                    Assert.That(target.ImpactMotion.ProjectileReactionActive, Is.True, context);
                    Assert.That(root.HeadEffects.DetachedSectorCountFor(target), Is.Zero,
                        "Regional body shots must not add dismemberment.");
                    AssertRegionalWoundUsesOriginalSurface(target, part);
                    if (part == Player3DAnatomicalPart.LeftHand)
                    {
                        Assert.That(root.BloodEffects.TryGetProjectilePresentation(target, 0, out var material), Is.True);
                        Assert.That(material.Surface, Is.EqualTo(heroVictim ? CombatDamageMarks.ProjectileWoundSurface.Skin :
                            CombatDamageMarks.ProjectileWoundSurface.Glove), context);
                        // The contact frame preserves the incoming ray's clear
                        // view before the reactive wrist folds its entry inward.
                        yield return CaptureRegionalWoundNearView(target, "regional-" + subject + "-hand-wound");
                    }
                    bool capture = part == Player3DAnatomicalPart.Torso ||
                        part == Player3DAnatomicalPart.LeftUpperArm || part == Player3DAnatomicalPart.LeftShin;
                    if (capture) yield return CaptureRegionalGameView(subject, part, "contact");

                    float maximumAngle = 0f, maximumTravel = 0f, minimumPelvis = pelvisBefore;
                    bool capturedPeak = false;
                    using (Player3DFootGroundProbe soles = CreateProjectileSoleProbe(target))
                    {
                        for (int frame = 0; frame < 36 && target.ImpactMotion.ProjectileReactionAge < .17f &&
                            !target.IsRagdollActive; frame++)
                        {
                            root.Tick(1f / 60f);
                            yield return null;
                            PresentInertiaPose();
                            maximumAngle = Mathf.Max(maximumAngle, Quaternion.Angle(ready, bone.rotation));
                            maximumTravel = Mathf.Max(maximumTravel, Vector3.Distance(readyPosition, bone.position));
                            minimumPelvis = Mathf.Min(minimumPelvis, FindAnatomicalBone(target, "pelvis").position.y);
                            AssertProjectileSolesAboveFloor(target, soles, context);
                            if (capture && !capturedPeak && target.ImpactMotion.ProjectileReactionAge >= .065f)
                            {
                                yield return CaptureRegionalGameView(subject, part, "peak");
                                capturedPeak = true;
                            }
                        }
                    }
                    Assert.That(target.ImpactMotion.ProjectileReactionAge >= .17f || target.IsRagdollActive,
                        Is.True, context);
                    Assert.That(maximumAngle > 1f || maximumTravel > .01f || target.IsRagdollActive, Is.True,
                        context + ": the original visible joint must turn or move with the support response.");
                    if (part == Player3DAnatomicalPart.Torso)
                        chestReaction = target.ImpactMotion.ProjectileRotationFor(part);
                    else if (part == Player3DAnatomicalPart.LowerTorso)
                        Assert.That(Vector3.Distance(chestReaction, target.ImpactMotion.ProjectileRotationFor(part)),
                            Is.GreaterThan(.01f), context + ": the abdomen must differ from the chest.");
                    if (part == Player3DAnatomicalPart.LeftForearm || part == Player3DAnatomicalPart.LeftHand)
                        Assert.That(target.ImpactMotion.ProjectileRotationFor(part).magnitude, Is.GreaterThan(.01f),
                            context + ": the exact distal joint needs its own response.");
                    if (part == Player3DAnatomicalPart.LeftHand)
                        AssertRegionalWoundUsesOriginalSurface(target, part, false);
                    if (part == Player3DAnatomicalPart.LeftThigh || part == Player3DAnatomicalPart.LeftShin ||
                        part == Player3DAnatomicalPart.LeftFoot)
                    {
                        Assert.That(target.ImpactMotion.InjuredLegSide, Is.EqualTo(0), context);
                        Assert.That(target.ImpactMotion.InjuredLegAmount, Is.GreaterThan(0f), context);
                        Assert.That(pelvisBefore - minimumPelvis, Is.GreaterThan(.01f),
                            context + ": support loss must visibly lower the pelvis through footwork.");
                    }
                    if (capture && !capturedPeak) yield return CaptureRegionalGameView(subject, part, "peak");
                    for (int frame = 0; frame < 84; frame++)
                    {
                        root.Tick(1f / 60f);
                        yield return null;
                    }
                    PresentInertiaPose();
                    Assert.That(!target.ImpactMotion.ProjectileReactionActive || target.IsRagdollActive, Is.True,
                        context + ": the accent finishes or yields its exact pose to physics.");
                    Assert.That(target.ReceivedImpactCount, Is.EqualTo(1), context + ": pose sampling cannot repeat damage.");
                    if (capture) yield return CaptureRegionalGameView(subject, part, "recovered");
                    Assert.That(root.BloodEffects.TryGetProjectileWound(target, 0, out _, out _), Is.True,
                        context + ": the wound outlives the motion response.");
                }
                yield return VerifyRegionalProjectileLifecycle(source, target, subject);
            }
            root.ResetRound();
            LogAssert.NoUnexpectedReceived();
        }

        private IEnumerator VerifyRegionalProjectileLifecycle(CombatActor source, CombatActor target, string subject)
        {
            root.ResetRound();
            PlacePair(6f);
            yield return null;
            FireRegionalProjectile(source, target, Player3DAnatomicalPart.Torso);
            for (int frame = 0; frame < 18; frame++) { root.Tick(1f / 60f); yield return null; }
            PresentInertiaPose();
            Assert.That(root.BloodEffects.TryGetProjectilePresentation(target, 0, out var first), Is.True);
            Assert.That(first.Surface, Is.EqualTo(CombatDamageMarks.ProjectileWoundSurface.Fabric));
            Assert.That(first.DirectionUv.sqrMagnitude, Is.GreaterThan(.9f));
            Assert.That(first.Stretch, Is.GreaterThanOrEqualTo(1f));
            Assert.That(first.AgeSeconds, Is.GreaterThan(0f));
            Assert.That(first.Wetness, Is.InRange(0f, 1f));
            Assert.That(root.BloodEffects.TryGetProjectileWound(target, 0, out Vector3 woundPoint, out Vector3 normal), Is.True);
            int wounds = root.BloodEffects.ProjectileWoundCountFor(target);
            Transform chest = FindAnatomicalBone(target, "chest");
            Quaternion liveChest = chest.rotation;
            FireProjectileAtSurface(source, target, woundPoint, normal, Player3DAnatomicalPart.Torso);
            PresentInertiaPose();
            Assert.That(Quaternion.Angle(liveChest, chest.rotation), Is.LessThan(30f),
                subject + ": a repeat adds to the current body pose without replaying a neutral hit clip.");
            Assert.That(root.BloodEffects.ProjectileWoundCountFor(target), Is.EqualTo(wounds),
                subject + ": a close repeat must deepen the same opening. Aimed at " + woundPoint +
                ", incoming " + -normal + ", coarse contact " + target.LastImpact.Point);
            Assert.That(root.BloodEffects.TryGetProjectilePresentation(target, 0, out var repeated), Is.True);
            Assert.That(repeated.HitCount, Is.EqualTo(first.HitCount + 1));
            Assert.That(repeated.Spread, Is.GreaterThanOrEqualTo(first.Spread));
            Assert.That(repeated.AgeSeconds, Is.LessThan(first.AgeSeconds));

            float reactionAge = target.ImpactMotion.ProjectileReactionAge;
            float bleedAge = root.BloodEffects.BleedingAgeFor(target);
            liveChest = chest.rotation;
            Assert.That(root.PauseMenu.Open(), Is.True);
            root.Tick(.8f);
            for (int frame = 0; frame < 3; frame++) yield return null;
            Assert.That(target.ImpactMotion.ProjectileReactionAge, Is.EqualTo(reactionAge));
            Assert.That(root.BloodEffects.BleedingAgeFor(target), Is.EqualTo(bleedAge));
            Assert.That(root.BloodEffects.TryGetProjectilePresentation(target, 0, out var paused), Is.True);
            Assert.That(paused.AgeSeconds, Is.EqualTo(repeated.AgeSeconds));
            Assert.That(paused.Wetness, Is.EqualTo(repeated.Wetness));
            Assert.That(Quaternion.Angle(liveChest, chest.rotation), Is.LessThan(.01f));
            Assert.That(root.PauseMenu.Cancel(), Is.True);
            yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Pause did not release the regional reaction.");
            for (int frame = 0; frame < 24; frame++) { root.Tick(1f / 60f); yield return null; }
            Assert.That(root.BloodEffects.TryGetProjectilePresentation(target, 0, out var grown), Is.True);
            Assert.That(grown.AgeSeconds, Is.GreaterThan(paused.AgeSeconds));
            Assert.That(grown.Spread, Is.GreaterThanOrEqualTo(paused.Spread));
            yield return CapturePistolWoundNearView(target, "regional-" + subject + "-torso-repeat-wet");

            FireRegionalProjectile(source, target, Player3DAnatomicalPart.LeftUpperArm);
            Assert.That(root.BloodEffects.ProjectileWoundCountFor(target), Is.GreaterThan(wounds),
                "A distant arm contact must preserve the torso opening and add another wound.");
            for (int shot = 0; shot < 5 && !target.State.IsDefeated; shot++)
                FireRegionalProjectile(source, target, Player3DAnatomicalPart.Torso);
            Assert.That(target.State.IsDefeated && target.IsRagdollActive, Is.True,
                "Body wounds pass their already visible rig to physical falling without extra dismemberment.");
            Assert.That(root.HeadEffects.DetachedSectorCountFor(target), Is.Zero);
            for (int frame = 0; frame < 18; frame++)
            {
                root.Tick(Time.fixedDeltaTime);
                yield return new WaitForFixedUpdate();
            }
            Assert.That(root.BloodEffects.TryGetProjectileWound(target, 0, out Vector3 fallenPoint, out _), Is.True);
            Assert.That(Vector3.Distance(fallenPoint, woundPoint), Is.GreaterThan(.01f),
                "The wound follows its original live skin through the physical fall.");
            yield return CaptureRegionalGameView(subject, Player3DAnatomicalPart.Torso, "physical-fall");
            yield return CapturePistolWoundNearView(target, "regional-" + subject + "-torso-fallen");
            int contacts = target.ReceivedImpactCount;
            FireRegionalProjectile(source, target, Player3DAnatomicalPart.LeftThigh);
            Assert.That(target.ReceivedImpactCount, Is.EqualTo(contacts + 1), "A corpse still receives one physical contact.");
            Assert.That(target.State.Health, Is.Zero);
            Assert.That(root.BloodEffects.ProjectileWoundCountFor(target), Is.GreaterThanOrEqualTo(wounds));
            root.BloodEffects.Tick(CombatBloodEffects.ProjectileBleedLifetimeSeconds);
            Assert.That(root.BloodEffects.BleedingPressureFor(target), Is.Zero);
            Assert.That(root.BloodEffects.TryGetProjectileWound(target, 0, out _, out _), Is.True,
                "Exhausting the finite blood supply leaves the opening attached.");
            root.ResetRound();
            Assert.That(root.BloodEffects.ProjectileWoundCountFor(target), Is.Zero);
            Assert.That(root.BloodEffects.TryGetProjectilePresentation(target, 0, out _), Is.False);
            Assert.That(target.ImpactMotion.ProjectileReactionActive, Is.False);
            Assert.That(target.ImpactMotion.InjuredLegSide, Is.EqualTo(-1));
            Assert.That(target.ImpactMotion.InjuredLegAmount, Is.Zero);
            Assert.That(target.IsRagdollActive, Is.False);
            Assert.That(target.State.Health, Is.EqualTo(target.State.Settings.MaxHealth));
        }

        private void FireRegionalProjectile(CombatActor source, CombatActor target, Player3DAnatomicalPart part)
        {
            target.CaptureContactPose();
            Vector3 center = FindAnatomicalBone(target, ProjectileBoneName(part)).position;
            foreach (var entry in target.Ragdoll.PhysicsController.AnatomicalColliders)
            {
                if (entry.Value != part) continue;
                center = entry.Key switch
                {
                    BoxCollider box => box.transform.TransformPoint(box.center),
                    CapsuleCollider capsule => capsule.transform.TransformPoint(capsule.center),
                    SphereCollider sphere => sphere.transform.TransformPoint(sphere.center),
                    _ => entry.Key.transform.position
                };
                break;
            }
            if (part == Player3DAnatomicalPart.LeftHand)
                // The hand socket is the wrist. Aim inside the distal hand
                // hurtbox at the exposed palm, rather than at its sleeve cuff.
                center += (center - FindAnatomicalBone(target, "forearm.L").position).normalized * .05f;
            foreach (Vector3 direction in new[] { target.transform.forward, -target.transform.forward,
                -target.transform.right, target.transform.right, Vector3.up,
                (target.transform.forward + Vector3.up).normalized, (-target.transform.forward + Vector3.up).normalized })
            {
                Vector3 origin = center + direction * .55f;
                Vector3 end = origin - direction * CombatProjectilePool.MuzzleSpeed * CombatTestRoot.SimulationStep +
                    Vector3.down * (CombatProjectilePool.Gravity * .5f * CombatTestRoot.SimulationStep * CombatTestRoot.SimulationStep);
                if (!target.Hurtboxes.SweepProjectile(origin, end, CombatProjectilePool.Radius, -direction, out var hit) || hit.Part != part)
                    continue;
                FireProjectileAtSurface(source, target, center, direction, part);
                return;
            }
            Assert.Fail("No exposed projectile approach to " + (target.IsHero ? "hero " : "opponent ") + part);
        }

        private void FireProjectileAtSurface(CombatActor source, CombatActor target, Vector3 point, Vector3 outward,
            Player3DAnatomicalPart expectedPart)
        {
            target.CaptureContactPose();
            int impacts = target.ReceivedImpactCount;
            Assert.That(root.Projectiles.TrySpawn(source, point + outward * .55f,
                -outward * CombatProjectilePool.MuzzleSpeed, root.Projectiles.SpawnCount + 1), Is.True);
            root.Projectiles.Advance(CombatTestRoot.SimulationStep, root.Hero, root.Opponent);
            root.Projectiles.ApplyContacts();
            Assert.That(target.ReceivedImpactCount, Is.EqualTo(impacts + 1));
            Assert.That(target.LastImpact.Kind, Is.EqualTo(CombatImpactKind.Projectile));
            Assert.That(target.LastImpact.Part, Is.EqualTo(expectedPart));
            Assert.That(root.Projectiles.ActiveCount, Is.Zero);
        }

        private IEnumerator CaptureRegionalGameView(string subject, Player3DAnatomicalPart part, string stage)
        {
            PresentInertiaPose();
            yield return CaptureFocusGameView("regional-" + subject + "-" + part + "-" + stage);
        }

        private void AssertRegionalWoundUsesOriginalSurface(CombatActor target, Player3DAnatomicalPart part, bool atContact = true)
        {
            string context = (target.IsHero ? "Hero " : "Opponent ") + part;
            int woundCount = root.BloodEffects.ProjectileWoundCountFor(target);
            Assert.That(woundCount, Is.EqualTo(1), context + ": expected one entry, actual count=" + woundCount + ".");
            Assert.That(root.BloodEffects.TryGetProjectileWound(target, 0, out Vector3 point, out _), Is.True,
                context + ": its recorded entry must sample a currently visible original surface.");
            if (atContact)
                Assert.That(Vector3.Distance(point, target.LastImpact.Point), Is.LessThan(.41f),
                    context + ": a real skin entry must remain inside the bounded coarse-contact projection.");
            bool visible = false;
            foreach (SkinnedMeshRenderer overlay in target.DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!overlay.enabled || !overlay.name.StartsWith("Projectile Wounds__", StringComparison.Ordinal)) continue;
                visible = true;
                SkinnedMeshRenderer original = overlay.transform.parent.GetComponent<SkinnedMeshRenderer>();
                Assert.That(original, Is.Not.Null, "The authored overlay is parented to its original visible garment or skin.");
                Assert.That(original.enabled, Is.True);
                CollectionAssert.AreEqual(original.bones, overlay.bones,
                    "The wound must use precisely the source surface's original skeleton.");
                AssertRegionalMorphMatches(original, overlay, point,
                    context + (atContact ? " at contact" : " during reaction"));
                if (part == Player3DAnatomicalPart.LeftHand)
                {
                    bool handSurface = target.IsHero ? original.name.StartsWith("GEO_Hand.", StringComparison.Ordinal) ||
                        original.name.StartsWith("GEO_Finger", StringComparison.Ordinal) ||
                        original.name.StartsWith("GEO_Thumb.", StringComparison.Ordinal) :
                        original.name.StartsWith("CLO_GlovePalm", StringComparison.Ordinal) ||
                        original.name.StartsWith("CLO_GloveFinger", StringComparison.Ordinal) ||
                        original.name.StartsWith("CLO_GloveThumb", StringComparison.Ordinal);
                    Assert.That(handSurface && original.name.EndsWith(".L", StringComparison.Ordinal),
                        Is.True, $"{(target.IsHero ? "Hero" : "Opponent")} {part}: the exposed palm or glove was hit, " +
                        $"but the entry overlay selected {original.name}; contact={target.LastImpact.Point:F3}, wound={point:F3}.");
                }
                else if (part == Player3DAnatomicalPart.LeftFoot)
                    Assert.That(original.name.StartsWith("CLO_Boot", StringComparison.Ordinal) ||
                        original.name.StartsWith("GEO_Foot", StringComparison.Ordinal), Is.True,
                        $"{part}: a foot wound belongs to its shoe surface, but selected {original.name}.");
                else Assert.That(original.name.StartsWith("CLO_", StringComparison.Ordinal), Is.True,
                    $"{part}: clothed contacts must damage the original garment, but selected {original.name}.");
            }
            Assert.That(visible, Is.True, "The real rendered rig must show its persistent entry overlay.");
        }

        private static void AssertRegionalMorphMatches(SkinnedMeshRenderer original, SkinnedMeshRenderer overlay,
            Vector3 wound, string context)
        {
            Mesh sourceMesh = original.sharedMesh, overlayMesh = overlay.sharedMesh;
            context += " source=" + original.name;
            Assert.That(overlayMesh.blendShapeCount, Is.EqualTo(sourceMesh.blendShapeCount),
                context + ": the entry surface must retain every original surface morph.");
            for (int shape = 0; shape < sourceMesh.blendShapeCount; shape++)
            {
                string name = sourceMesh.GetBlendShapeName(shape);
                Assert.That(overlayMesh.GetBlendShapeName(shape), Is.EqualTo(name), context);
                Assert.That(overlayMesh.GetBlendShapeFrameCount(shape), Is.EqualTo(sourceMesh.GetBlendShapeFrameCount(shape)),
                    context + " morph=" + name);
                Assert.That(overlay.GetBlendShapeWeight(shape), Is.EqualTo(original.GetBlendShapeWeight(shape)).Within(.001f),
                    context + " morph=" + name + ": the rendered entry must use the live grip weight.");
            }
            var scratch = new Mesh { name = "Test deformed hand surface" };
            try
            {
                original.BakeMesh(scratch, true);
                Vector3[] vertices = scratch.vertices;
                for (int index = 0; index < vertices.Length; index++) vertices[index] = original.transform.TransformPoint(vertices[index]);
                int[] triangles = scratch.triangles;
                float gapSquared = float.PositiveInfinity;
                for (int triangle = 0; triangle < triangles.Length; triangle += 3)
                    gapSquared = Mathf.Min(gapSquared, RegionalPointTriangleSquared(wound, vertices[triangles[triangle]],
                        vertices[triangles[triangle + 1]], vertices[triangles[triangle + 2]]));
                Assert.That(Mathf.Sqrt(gapSquared), Is.LessThanOrEqualTo(.006f),
                    context + ": the bleed/entry point must lie on the actual morphed source triangles with only the authored surface lift.");
            }
            finally { UnityEngine.Object.DestroyImmediate(scratch); }
        }

        private static float RegionalPointTriangleSquared(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = point - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return ap.sqrMagnitude;
            Vector3 bp = point - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return bp.sqrMagnitude;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return (point - (a + ab * (d1 / (d1 - d3)))).sqrMagnitude;
            Vector3 cp = point - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return cp.sqrMagnitude;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return (point - (a + ac * (d2 / (d2 - d6)))).sqrMagnitude;
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
                return (point - (b + (c - b) * ((d4 - d3) / (d4 - d3 + d5 - d6)))).sqrMagnitude;
            float sum = va + vb + vc;
            if (Mathf.Abs(sum) < .0000000001f)
                return Mathf.Min(Segment(a, b), Mathf.Min(Segment(b, c), Segment(c, a)));
            return (point - (a + ab * (vb / sum) + ac * (vc / sum))).sqrMagnitude;

            float Segment(Vector3 from, Vector3 to)
            {
                Vector3 delta = to - from;
                return (point - (from + delta * (delta.sqrMagnitude > 0f ?
                    Mathf.Clamp01(Vector3.Dot(point - from, delta) / delta.sqrMagnitude) : 0f))).sqrMagnitude;
            }
        }

        private IEnumerator CaptureRegionalWoundNearView(CombatActor actor, string name)
        {
            Camera camera = root.CameraFollow.Camera;
            Vector3 savedPosition = camera.transform.position;
            Quaternion savedRotation = camera.transform.rotation;
            float savedFov = camera.fieldOfView, savedNear = camera.nearClipPlane;
            bool followEnabled = root.CameraFollow.enabled;
            var scratch = new Mesh { name = "Test wound visibility mesh" };
            try
            {
                root.CameraFollow.enabled = false;
                yield return null;
                PresentInertiaPose();
                Assert.That(root.BloodEffects.TryGetProjectileWound(actor, 0, out Vector3 point, out Vector3 normal), Is.True);
                Vector3 approach = -actor.LastImpact.Direction.normalized;
                Vector3 side = Vector3.Cross(approach, Mathf.Abs(approach.y) > .9f ? Vector3.forward : Vector3.up).normalized;
                Vector3 up = Vector3.Cross(side, approach).normalized;
                // Photograph from the actual incoming side first. Smoothed
                // vertex normals can otherwise place the lens behind a cuff.
                var candidates = new[] { approach, (approach + normal).normalized,
                    (approach + side * .45f).normalized, (approach - side * .45f).normalized,
                    (approach + up * .45f).normalized, (approach - up * .45f).normalized,
                    normal.normalized, (normal + side * .45f).normalized, (normal - side * .45f).normalized,
                    (normal + side * 1.5f).normalized, (normal - side * 1.5f).normalized,
                    (normal + up * 1.5f).normalized, (normal - up * 1.5f).normalized };
                var surfaces = new List<Vector3[]>();
                var triangles = new List<int[]>();
                var surfaceNames = new List<string>();
                var blockers = new HashSet<string>();
                foreach (SkinnedMeshRenderer skin in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!skin.enabled || !skin.gameObject.activeInHierarchy || skin.sharedMesh == null ||
                        skin.name.StartsWith("Projectile Wounds__", StringComparison.Ordinal) ||
                        skin.name.StartsWith("Wound__", StringComparison.Ordinal)) continue;
                    skin.BakeMesh(scratch, true);
                    AddSurface(scratch, skin.transform, skin.name);
                }
                foreach (MeshFilter mesh in actor.GetComponentsInChildren<MeshFilter>(true))
                {
                    Renderer renderer = mesh.GetComponent<Renderer>();
                    // Non-readable analytic shadow quads are not solid view
                    // blockers. Never change their runtime import/readability.
                    if (mesh.sharedMesh != null && mesh.sharedMesh.isReadable && renderer != null &&
                        renderer.enabled && renderer.gameObject.activeInHierarchy)
                        AddSurface(mesh.sharedMesh, mesh.transform, mesh.name);
                }
                Vector3 eye = Vector3.zero;
                bool clear = false;
                foreach (Vector3 direction in candidates)
                {
                    if (direction.sqrMagnitude < .9f || Vector3.Dot(direction, normal) < .02f) continue;
                    // A gripping hand can have a clear few centimetres above
                    // its entry even while the bar/cuff blocks a wider view.
                    foreach (float distance in new[] { .20f, .10f, .06f })
                    {
                        Vector3 candidate = point + direction * distance;
                        Vector3 endpoint = point + direction * .006f;
                        bool blocked = false;
                        for (int surface = 0; surface < surfaces.Count && !blocked; surface++)
                        {
                            Vector3[] vertices = surfaces[surface];
                            int[] indices = triangles[surface];
                            for (int triangle = 0; triangle < indices.Length; triangle += 3)
                                if (PistolHandEdgeCrosses(candidate, endpoint, vertices[indices[triangle]],
                                    vertices[indices[triangle + 1]], vertices[indices[triangle + 2]]))
                                { blocked = true; blockers.Add(surfaceNames[surface]); break; }
                        }
                        if (blocked) continue;
                        eye = candidate; clear = true; break;
                    }
                    if (clear) break;
                }
                Assert.That(clear, Is.True,
                    (actor.IsHero ? "Hero" : "Opponent") + ": a close view needs an unobstructed real entry; blockers=" +
                    string.Join(", ", blockers) + $"; wound={point:F3}, normal={normal:F3}, approach={approach:F3}.");
                Vector3 view = (point - eye).normalized;
                Vector3 cameraUp = Vector3.ProjectOnPlane(Mathf.Abs(view.y) > .9f ? Vector3.forward : Vector3.up, view).normalized;
                camera.fieldOfView = 34f; camera.nearClipPlane = .002f;
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(view, cameraUp));
                string path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Captures", SceneIds.CombatTest, name + ".png");
                LogAssert.Expect(LogType.Log, "Area capture wrote " + path);
                AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.CombatTest, name);

                void AddSurface(Mesh mesh, Transform frame, string sourceName)
                {
                    Vector3[] vertices = mesh.vertices;
                    for (int index = 0; index < vertices.Length; index++) vertices[index] = frame.TransformPoint(vertices[index]);
                    surfaces.Add(vertices); triangles.Add(mesh.triangles); surfaceNames.Add(sourceName);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(scratch);
                root.CameraFollow.enabled = followEnabled;
                camera.fieldOfView = savedFov; camera.nearClipPlane = savedNear;
                camera.transform.SetPositionAndRotation(savedPosition, savedRotation);
            }
        }

        private static string ProjectileBoneName(Player3DAnatomicalPart part) => part switch
        {
            Player3DAnatomicalPart.LowerTorso => "spine",
            Player3DAnatomicalPart.LeftUpperArm => "upper_arm.L",
            Player3DAnatomicalPart.LeftForearm => "forearm.L",
            Player3DAnatomicalPart.LeftHand => "hand.L",
            Player3DAnatomicalPart.LeftThigh => "thigh.L",
            Player3DAnatomicalPart.LeftShin => "shin.L",
            Player3DAnatomicalPart.LeftFoot => "foot.L",
            _ => "chest"
        };

        private static Player3DFootGroundProbe CreateProjectileSoleProbe(CombatActor actor)
        {
            Player3DAssetRegistry registry = actor.DamageRigRoot.GetComponentInParent<Player3DAssetRegistry>();
            if (registry != null) return Player3DFootGroundProbe.CreateForHero(registry, actor.transform);
            var left = new List<SkinnedMeshRenderer>();
            var right = new List<SkinnedMeshRenderer>();
            foreach (SkinnedMeshRenderer skin in actor.DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin.name.EndsWith("Sole.L", StringComparison.Ordinal)) left.Add(skin);
                if (skin.name.EndsWith("Sole.R", StringComparison.Ordinal)) right.Add(skin);
            }
            return Player3DFootGroundProbe.Create(left, right, actor.transform);
        }

        private static void AssertProjectileSolesAboveFloor(CombatActor actor, Player3DFootGroundProbe soles, string context)
        {
            if (actor.IsRagdollActive) return;
            foreach (FootSide side in new[] { FootSide.Left, FootSide.Right })
            {
                Transform ankle = FindAnatomicalBone(actor, side == FootSide.Left ? "foot.L" : "foot.R");
                Assert.That(soles.TryGetSoleHeight(side, out float sole), Is.True, context);
                Assert.That(soles.TryProbeActorGround(ankle.position, out float floor, out _), Is.True, context);
                Assert.That(sole - floor, Is.GreaterThan(-.04f), context + ": the original boot cannot pass through its floor.");
            }
        }
    }
}

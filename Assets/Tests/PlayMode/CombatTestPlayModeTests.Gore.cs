using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
        [UnityTest]
        public IEnumerator Range_HeadFractureBleedingAndConvulsionsFollowContactPauseAndReset()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            yield return EnterRange(false, CombatWeaponId.Pistol);
            var npcMasks = new List<int>();
            foreach (bool heroVictim in new[] { false, true })
            {
                // Three anatomical approaches plus one turned-head contact prove
                // that the damage follows the struck skull, independently of root yaw.
                int approaches = heroVictim ? 1 : 4;
                for (int approach = 0; approach < approaches; approach++)
                {
                    root.ResetRound();
                    PlacePair(4f);
                    Assert.That(root.SetOpponentFocus(false), Is.True);
                    CombatActor target = heroVictim ? root.Hero : root.Opponent;
                    CombatActor source = heroVictim ? root.Opponent : root.Hero;
                    Assert.That(root.HeadEffects.RetainedBrainCountFor(target), Is.EqualTo(8),
                        "The intact skull already contains its authored brain volume before an impact.");
                    Assert.That(CaptureBrainMeshVertices(target, false).Count, Is.EqualTo(8));
                    Assert.That(root.HeadEffects.BrainDeformationFor(target), Is.Zero);
                    Renderer[] originals = target.DamageRigRoot.GetComponentsInChildren<Renderer>(true);
                    var originalVisibility = new bool[originals.Length];
                    for (int i = 0; i < originals.Length; i++) originalVisibility[i] = originals[i].enabled;
                    target.CaptureContactPose();
                    Assert.That(target.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head,
                        out Vector3 centre, out Vector3 forward, out Vector3 up), Is.True);
                    Vector3 right = Vector3.Cross(up, forward).normalized;
                    Vector3 outward = heroVictim || approach == 1 ? right : approach == 2 ? -forward : forward;
                    string view = heroVictim ? "hero-side" : approach == 0 ? "front" :
                        approach == 1 ? "side" : approach == 2 ? "rear" : "turned-head";
                    if (approach == 3)
                    {
                        Transform head = root.HeadEffects.HeadFrameFor(target);
                        Assert.That(head, Is.Not.Null);
                        head.rotation = Quaternion.AngleAxis(180f, up) * head.rotation;
                        target.CaptureContactPose();
                        Assert.That(target.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head,
                            out centre, out Vector3 turnedForward, out _), Is.True);
                        Assert.That(Vector3.Dot(turnedForward, forward), Is.LessThan(-.99f));
                        // Keep the original world approach: it is now behind the face.
                    }

                    Player3DHeadVisibility hiddenHead = heroVictim
                        ? Player3DHeadVisibility.Hide(((Player3DCharacterPresentation)root.Player.Visual).Registry) : null;
                    try
                    {
                        if (hiddenHead != null)
                            Assert.That(CaptureBrainMeshVertices(target, false), Is.Empty,
                                "The first-person head hide also owns the derived brain surfaces.");
                        FireGoreProjectile(source, target, centre, outward);
                    }
                    finally { hiddenHead?.Restore(); }
                    Assert.That(target.State.Health, Is.Zero);
                    Assert.That(target.IsRagdollActive && target.Ragdoll.IsConvulsing, Is.True);
                    Assert.That(target.Ragdoll.BeginTerminalConvulsions(), Is.False,
                        "The lethal contact owns one contraction episode.");
                    Assert.That(root.HeadEffects.DetachedSectorCountFor(target), Is.InRange(3, 5),
                        "One headshot removes a local portion of the skull rather than most of the head.");
                    Assert.That(root.HeadEffects.RetainedSectorCountFor(target), Is.GreaterThan(0),
                        "The first fracture must leave an actual surviving silhouette for later contacts.");
                    Assert.That(root.HeadEffects.BrainFragmentCountFor(target), Is.GreaterThan(0));
                    Assert.That(root.HeadEffects.RetainedBrainCountFor(target), Is.GreaterThanOrEqualTo(5),
                        "A partial skull fracture keeps most brain tissue inside the surviving head.");
                    AssertBrainPieceConservation(target);
                    Assert.That(CaptureBrainMeshVertices(target, false).Count,
                        Is.EqualTo(root.HeadEffects.RetainedBrainCountFor(target)));
                    AssertExposedBrainContact(target);
                    foreach (Rigidbody fragment in root.HeadEffects.GetComponentsInChildren<Rigidbody>(true))
                        if (fragment.gameObject.activeInHierarchy && fragment.name.EndsWith("Fragment", StringComparison.Ordinal))
                            Assert.That(fragment.GetComponent<Collider>().bounds.size.magnitude, Is.LessThan(.8f),
                                "Head fragments need anatomical collision bounds: " + fragment.name + " " +
                                string.Join("; ", Array.ConvertAll(fragment.GetComponentsInChildren<MeshFilter>(),
                                    mesh => mesh.name + " local=" + mesh.sharedMesh.bounds.size.ToString("F5") +
                                    " scale=" + mesh.transform.lossyScale.ToString("F5"))));
                    foreach (SkinnedMeshRenderer skin in target.DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        if (skin.enabled && skin.name.StartsWith("Fracture ", StringComparison.Ordinal))
                        {
                            var baked = new Mesh();
                            skin.BakeMesh(baked, true);
                            Vector3 worldSize = Vector3.Scale(baked.bounds.size, skin.transform.lossyScale);
                            UnityEngine.Object.Destroy(baked);
                            Assert.That(worldSize.magnitude, Is.LessThan(.8f),
                                "Retained skull surfaces need anatomical scale: " + skin.name);
                        }
                    Assert.That(Vector3.Dot(root.HeadEffects.LastEjectionDirection, -outward), Is.GreaterThan(.99f));
                    Assert.That(root.BloodEffects.ActiveDropCount, Is.InRange(31, 80),
                        "The local head impact has a visible, bounded blood burst.");
                    int suppressedCount = 0;
                    foreach (Renderer original in originals)
                        if (CombatHeadDestruction.IsSuppressed(original))
                        {
                            suppressedCount++;
                            Assert.That(original.enabled, Is.False,
                                "Restoring a first-person head hide must not resurrect fractured original surfaces.");
                        }
                    Assert.That(suppressedCount, Is.GreaterThan(0));
                    AssertIntactHeadColliderDisabled(target);
                    if (!heroVictim) npcMasks.Add(root.HeadEffects.DestroyedMaskFor(target));
                    if (approach == 2 || approach == 3)
                        Assert.That(target.LastImpact.Location.Side, Is.EqualTo(MeleeHitSide.Rear));
                    yield return CaptureFocusGameView("head-fracture-" + view + "-contact");
                    CaptureGoreNearView(target, outward, "head-fracture-" + view + "-near");

                    // Pause/expiry/repeat coverage is shared by the full hero and NPC rigs;
                    // the additional NPC approaches above only vary their contact geometry.
                    if (approach == 0)
                        yield return VerifyGoreAftermath(source, target, outward, view);

                    root.ResetRound();
                    Assert.That(root.HeadEffects.DestroyedMaskFor(target), Is.Zero);
                    Assert.That(root.HeadEffects.DetachedSectorCountFor(target), Is.Zero);
                    Assert.That(root.HeadEffects.BrainFragmentCountFor(target), Is.Zero);
                    Assert.That(root.HeadEffects.RetainedBrainCountFor(target), Is.EqualTo(8));
                    Assert.That(root.HeadEffects.BrainDeformationFor(target), Is.Zero);
                    Assert.That(CaptureBrainMeshVertices(target, false).Count, Is.EqualTo(8),
                        "Round reset restores the filled intact skull, including the brain renderers.");
                    Assert.That(root.HeadEffects.DebrisAgeFor(target), Is.Zero);
                    Assert.That(target.Ragdoll.IsConvulsing, Is.False);
                    Assert.That(target.Ragdoll.ConvulsionSeconds, Is.Zero);
                    Assert.That(target.Ragdoll.ConvulsionPulseCount, Is.Zero);
                    Assert.That(target.State.Health, Is.EqualTo(target.State.Settings.MaxHealth));
                    Assert.That(root.BloodEffects.BleedingAgeFor(target), Is.Zero);
                    Assert.That(root.BloodEffects.ProjectileWoundCountFor(target), Is.Zero);
                    target.CaptureContactPose();
                    Assert.That(target.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head, out _, out _, out _), Is.True);
                    for (int i = 0; i < originals.Length; i++)
                    {
                        Assert.That(CombatHeadDestruction.IsSuppressed(originals[i]), Is.False);
                        Assert.That(originals[i].enabled, Is.EqualTo(originalVisibility[i]),
                            "Round reset restores the original renderer state: " + originals[i].name);
                    }
                }
            }
            Assert.That(npcMasks[0], Is.Not.EqualTo(npcMasks[1]), "Front and side contacts must remove different regions.");
            Assert.That(npcMasks[0], Is.Not.EqualTo(npcMasks[2]), "Entry and exit sides must follow bullet direction.");
            Assert.That(npcMasks[2], Is.EqualTo(npcMasks[3]),
                "A turned skull struck from the old world front receives its anatomical rear fracture.");
            VerifyPulsatingBodyWound();
            LogAssert.NoUnexpectedReceived();
        }

        private void FireGoreProjectile(CombatActor source, CombatActor target, Vector3 point, Vector3 outward)
        {
            int impacts = target.ReceivedImpactCount;
            target.CaptureContactPose();
            Assert.That(root.Projectiles.TrySpawn(source, point + outward * .75f,
                -outward * CombatProjectilePool.MuzzleSpeed, root.Projectiles.SpawnCount + 1), Is.True);
            // Use the production swept flight/contact path directly, retaining an
            // explicitly turned bone instead of resampling its idle animation first.
            root.Projectiles.Advance(CombatTestRoot.SimulationStep, root.Hero, root.Opponent);
            root.Projectiles.ApplyContacts();
            Assert.That(target.ReceivedImpactCount, Is.EqualTo(impacts + 1));
            Assert.That(target.LastImpact.Kind, Is.EqualTo(CombatImpactKind.Projectile));
            Assert.That(target.LastImpact.Location.Region, Is.EqualTo(MeleeBodyRegion.Head));
            Assert.That(root.Projectiles.ActiveCount, Is.Zero);
        }

        private IEnumerator VerifyGoreAftermath(CombatActor source, CombatActor target, Vector3 outward, string view)
        {
            int initialMask = root.HeadEffects.DestroyedMaskFor(target);
            int brainFragments = root.HeadEffects.BrainFragmentCountFor(target);
            Assert.That(target.Ragdoll.PhysicsController.IsSimulationSuspended, Is.True);
            float convulsionAge = target.Ragdoll.ConvulsionSeconds;
            float bleedAge = root.BloodEffects.BleedingAgeFor(target);
            float debrisAge = root.HeadEffects.DebrisAgeFor(target);
            float brainDeformation = root.HeadEffects.BrainDeformationFor(target);
            Dictionary<Mesh, Vector3[]> brainVertices = CaptureBrainMeshVertices(target, true);
            Dictionary<Mesh, Vector3[]> retainedBrainVertices = CaptureBrainMeshVertices(target, false);
            var releasedBrainVertices = new Dictionary<Mesh, Vector3[]>(brainVertices);
            foreach (Mesh mesh in retainedBrainVertices.Keys) releasedBrainVertices.Remove(mesh);
            Assert.That(retainedBrainVertices, Is.Not.Empty);
            Assert.That(releasedBrainVertices, Is.Not.Empty);
            Vector3 pelvis = target.Ragdoll.PelvisBody.position;
            root.Tick(CombatTestRoot.SimulationStep * 2f);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.That(target.Ragdoll.ConvulsionSeconds, Is.EqualTo(convulsionAge));
            Assert.That(root.BloodEffects.BleedingAgeFor(target), Is.EqualTo(bleedAge));
            Assert.That(root.HeadEffects.DebrisAgeFor(target), Is.EqualTo(debrisAge));
            Assert.That(root.HeadEffects.BrainDeformationFor(target), Is.EqualTo(brainDeformation));
            AssertBrainMeshesUnchanged(brainVertices, CaptureBrainMeshVertices(target, true), "Hitstop");
            Assert.That(Vector3.Distance(target.Ragdoll.PelvisBody.position, pelvis), Is.LessThan(.0001f));

            root.Tick(.15f);
            for (int frame = 0; frame < 6; frame++)
            {
                root.Tick(Time.fixedDeltaTime);
                yield return new WaitForFixedUpdate();
            }
            Assert.That(root.HeadEffects.BrainDeformationFor(target), Is.GreaterThan(0f),
                "The tissue responds to the impact and moving head with a finite jelly deformation.");
            Assert.That(BrainMeshMovement(retainedBrainVertices, CaptureBrainMeshVertices(target, false)), Is.GreaterThan(.0000001f),
                "The remaining tissue deforms its mesh while following the world head rig.");
            Assert.That(BrainMeshMovement(releasedBrainVertices, CaptureBrainMeshVertices(target, true)), Is.GreaterThan(.0000001f),
                "Flying tissue also changes actual vertices, rather than only a diagnostic value or a rigid transform.");
            AssertExposedBrainContact(target);
            CaptureGoreNearView(target, outward, "head-fracture-" + view + "-airborne", true, true);
            Assert.That(root.PauseMenu.Open(), Is.True);
            var bodies = new List<Rigidbody>(target.Ragdoll.Bodies);
            foreach (Rigidbody body in root.HeadEffects.GetComponentsInChildren<Rigidbody>(true))
                if (body.gameObject.activeInHierarchy && body.name.EndsWith("Fragment", StringComparison.Ordinal)) bodies.Add(body);
            Assert.That(bodies.Count, Is.GreaterThan(target.Ragdoll.BodyCount), "The fragments have their own physical bodies.");
            var positions = new Vector3[bodies.Count];
            var rotations = new Quaternion[bodies.Count];
            for (int i = 0; i < bodies.Count; i++) { positions[i] = bodies[i].position; rotations[i] = bodies[i].rotation; }
            convulsionAge = target.Ragdoll.ConvulsionSeconds;
            bleedAge = root.BloodEffects.BleedingAgeFor(target);
            debrisAge = root.HeadEffects.DebrisAgeFor(target);
            brainDeformation = root.HeadEffects.BrainDeformationFor(target);
            brainVertices = CaptureBrainMeshVertices(target, true);
            int drops = root.BloodEffects.BleedingDropCountFor(target);
            root.Tick(.8f);
            for (int frame = 0; frame < 6; frame++) yield return null;
            for (int i = 0; i < bodies.Count; i++)
            {
                Assert.That(Vector3.Distance(bodies[i].position, positions[i]), Is.LessThan(.0001f));
                Assert.That(Quaternion.Angle(bodies[i].rotation, rotations[i]), Is.LessThan(.01f));
            }
            Assert.That(target.Ragdoll.ConvulsionSeconds, Is.EqualTo(convulsionAge));
            Assert.That(root.BloodEffects.BleedingAgeFor(target), Is.EqualTo(bleedAge));
            Assert.That(root.HeadEffects.DebrisAgeFor(target), Is.EqualTo(debrisAge));
            Assert.That(root.HeadEffects.BrainDeformationFor(target), Is.EqualTo(brainDeformation));
            AssertBrainMeshesUnchanged(brainVertices, CaptureBrainMeshVertices(target, true), "Pause");
            Assert.That(root.BloodEffects.BleedingDropCountFor(target), Is.EqualTo(drops));
            Assert.That(root.PauseMenu.Cancel(), Is.True);
            yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Pause did not release the fractured body.");

            float initialPressure = root.BloodEffects.BleedingPressureFor(target);
            float initialSupply = root.BloodEffects.RemainingBloodFractionFor(target);
            float minimumPulse = 1f, maximumPulse = 0f;
            for (int sample = 0; sample < 16; sample++)
            {
                root.Tick(.05f);
                float pulse = root.BloodEffects.BleedingPulseFor(target);
                minimumPulse = Mathf.Min(minimumPulse, pulse); maximumPulse = Mathf.Max(maximumPulse, pulse);
            }
            Assert.That(maximumPulse - minimumPulse, Is.GreaterThan(.3f), "Emission must share a visible pulse cycle.");
            Assert.That(root.BloodEffects.BleedingDropCountFor(target), Is.GreaterThan(drops));
            Assert.That(root.BloodEffects.RemainingBloodFractionFor(target), Is.LessThan(initialSupply));
            Assert.That(root.BloodEffects.BleedingPressureFor(target), Is.LessThan(initialPressure));
            for (int step = 0; step < 180 && target.Ragdoll.IsConvulsing; step++)
            {
                root.Tick(Time.fixedDeltaTime);
                yield return new WaitForFixedUpdate();
                if (target.Ragdoll.IsConvulsing)
                    Assert.That(target.Ragdoll.IsSettled, Is.False, "Settlement waits for the last physical contraction.");
            }
            Assert.That(target.Ragdoll.IsConvulsing, Is.False, "The physical episode has a finite duration.");
            Assert.That(target.Ragdoll.ConvulsionPulseCount, Is.EqualTo(7));
            Assert.That(root.HeadEffects.DestroyedMaskFor(target), Is.EqualTo(initialMask));
            Assert.That(root.HeadEffects.BrainFragmentCountFor(target), Is.EqualTo(brainFragments),
                "Advancing a corpse cannot replay its fragments.");
            AssertBrainPieceConservation(target);
            for (int step = 0; step < 110 && !target.Ragdoll.IsSettled; step++)
            {
                root.Tick(Time.fixedDeltaTime);
                yield return new WaitForFixedUpdate();
            }
            Assert.That(target.Ragdoll.IsSettled && target.Ragdoll.PhysicsController.IsFrozen, Is.True,
                "The terminal body comes to rest after its contraction episode.");
            yield return CaptureFocusGameView("head-fracture-" + view + "-aftermath");
            CaptureGoreNearView(target, outward, "head-fracture-" + view + "-aftermath-near", true);

            root.Tick(CombatBloodEffects.ProjectileBleedLifetimeSeconds + .2f);
            Assert.That(root.BloodEffects.BleedingAgeFor(target), Is.EqualTo(CombatBloodEffects.ProjectileBleedLifetimeSeconds).Within(.001f));
            Assert.That(root.BloodEffects.BleedingPressureFor(target), Is.Zero);
            drops = root.BloodEffects.BleedingDropCountFor(target);
            root.Tick(.8f);
            Assert.That(root.BloodEffects.BleedingDropCountFor(target), Is.EqualTo(drops), "Bleeding eventually stops.");
            for (int repeat = 0; repeat < 8 && root.HeadEffects.RetainedSectorCountFor(target) > 0; repeat++)
            {
                Assert.That(root.HeadEffects.TryGetRetainedTarget(target, out Vector3 retained), Is.True);
                float supplyBeforeRepeat = root.BloodEffects.RemainingBloodFractionFor(target);
                int detachedBeforeRepeat = root.HeadEffects.DetachedSectorCountFor(target);
                int brainsBeforeRepeat = root.HeadEffects.BrainFragmentCountFor(target);
                Assert.That(TryFindGoreHeadApproach(source, target, retained, out Vector3 repeatOutward), Is.True,
                    "A visible remaining head sector needs an unobstructed anatomical projectile approach.");
                FireGoreProjectile(source, target, retained, repeatOutward);
                Assert.That(root.HeadEffects.DetachedSectorCountFor(target), Is.GreaterThan(detachedBeforeRepeat));
                Assert.That(root.HeadEffects.BrainFragmentCountFor(target), Is.GreaterThanOrEqualTo(brainsBeforeRepeat),
                    "Later shots only release tissue that remains attached, without replaying earlier fragments.");
                AssertBrainPieceConservation(target);
                Assert.That(target.State.Health, Is.Zero);
                Assert.That(target.Ragdoll.IsConvulsing, Is.False, "A later corpse shot cannot restart the terminal episode.");
                Assert.That(root.BloodEffects.RemainingBloodFractionFor(target), Is.LessThanOrEqualTo(supplyBeforeRepeat));
                Assert.That(root.BloodEffects.BleedingAgeFor(target), Is.EqualTo(CombatBloodEffects.ProjectileBleedLifetimeSeconds).Within(.001f));
                AssertIntactHeadColliderDisabled(target);
                target.CaptureContactPose();
                if (root.HeadEffects.RetainedSectorCountFor(target) > 0)
                    Assert.That(target.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head, out _, out _, out _), Is.True,
                        "The surviving head stays targetable until the last local fracture.");
            }
            target.CaptureContactPose();
            Assert.That(root.HeadEffects.RetainedSectorCountFor(target), Is.Zero);
            Assert.That(root.HeadEffects.RetainedBrainCountFor(target), Is.Zero);
            Assert.That(root.HeadEffects.BrainFragmentCountFor(target), Is.EqualTo(8),
                "Complete destruction releases each authored brain piece exactly once.");
            Assert.That(CaptureBrainMeshVertices(target, false), Is.Empty);
            Assert.That(target.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head, out _, out _, out _), Is.False,
                "The fully destroyed head must not leave an invisible projectile target.");
            root.Tick(.2f);
            yield return new WaitForFixedUpdate();
            Assert.That(target.Ragdoll.IsSettled, Is.False, "The new contact wakes the existing settled corpse.");
            Assert.That(target.Ragdoll.ConvulsionPulseCount, Is.EqualTo(7));
            AssertIntactHeadColliderDisabled(target);
        }

        private void AssertBrainPieceConservation(CombatActor target) =>
            Assert.That(root.HeadEffects.RetainedBrainCountFor(target) + root.HeadEffects.BrainFragmentCountFor(target),
                Is.EqualTo(8), "A brain piece is either retained or released, never duplicated or silently lost.");

        private Dictionary<Mesh, Vector3[]> CaptureBrainMeshVertices(CombatActor target, bool includeDetached)
        {
            var result = new Dictionary<Mesh, Vector3[]>();
            Add(target.DamageRigRoot.GetComponentsInChildren<Renderer>(true));
            if (includeDetached) Add(root.HeadEffects.GetComponentsInChildren<Renderer>(true));
            return result;

            void Add(Renderer[] surfaces)
            {
                foreach (Renderer surface in surfaces)
                {
                    if (!surface.enabled || !surface.gameObject.activeInHierarchy ||
                        !surface.name.Contains("Brain")) continue;
                    Mesh mesh = surface is SkinnedMeshRenderer skin ? skin.sharedMesh :
                        surface.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh != null) result[mesh] = mesh.vertices;
                }
            }
        }

        private static float BrainMeshMovement(Dictionary<Mesh, Vector3[]> before, Dictionary<Mesh, Vector3[]> after)
        {
            float maximum = 0f;
            foreach (KeyValuePair<Mesh, Vector3[]> surface in before)
            {
                Assert.That(after.TryGetValue(surface.Key, out Vector3[] vertices), Is.True,
                    "Advancing or freezing tissue must preserve its mesh ownership.");
                Assert.That(vertices.Length, Is.EqualTo(surface.Value.Length));
                for (int i = 0; i < vertices.Length; i++)
                    maximum = Mathf.Max(maximum, Vector3.Distance(vertices[i], surface.Value[i]));
            }
            return maximum;
        }

        private static void AssertBrainMeshesUnchanged(Dictionary<Mesh, Vector3[]> before,
            Dictionary<Mesh, Vector3[]> after, string context)
        {
            Assert.That(after.Count, Is.EqualTo(before.Count), context + " cannot detach or hide a tissue piece.");
            Assert.That(BrainMeshMovement(before, after), Is.Zero, context + " freezes the actual tissue vertices.");
        }

        private static void AssertExposedBrainContact(CombatActor target)
        {
            target.CaptureContactPose();
            List<HeadContactSurface> surfaces = BakeHeadContactSurfaces(target, true);
            var brains = surfaces.FindAll(surface => surface.Name.StartsWith("Fracture Brain", StringComparison.Ordinal));
            Assert.That(brains, Is.Not.Empty);
            foreach (HeadContactSurface brain in brains)
            {
                int stride = Mathf.Max(3, brain.Triangles.Length / 24 / 3 * 3);
                for (int triangle = 0; triangle < brain.Triangles.Length; triangle += stride)
                {
                    Vector3 a = brain.Vertices[brain.Triangles[triangle]];
                    Vector3 b = brain.Vertices[brain.Triangles[triangle + 1]];
                    Vector3 c = brain.Vertices[brain.Triangles[triangle + 2]];
                    Vector3 centre = (a + b + c) / 3f;
                    Vector3 normal = Vector3.Cross((b - a).normalized, (c - a).normalized).normalized;
                    if (normal.sqrMagnitude < .9f) continue;
                    // A normal probe keeps the solver's conservative distance
                    // tolerance from being magnified along a grazing ray.
                    foreach (Vector3 outward in new[] { normal, -normal })
                    {
                        var ray = new Ray(centre + outward * .3f, -outward);
                        if (!IndependentHeadRaycast(surfaces, ray, out Vector3 visible) ||
                            !IndependentHeadRaycast(brains, ray, out Vector3 tissue) ||
                            Vector3.Distance(visible, centre) > .00001f ||
                            Vector3.Distance(tissue, centre) > .00001f) continue;
                        Assert.That(target.Hurtboxes.SweepProjectile(tissue + outward * .015f, tissue - outward * .015f, 0f,
                            ray.direction, out var hit), Is.True, "Exposed brain triangles remain a real projectile target.");
                        Assert.That(hit.Location.Region, Is.EqualTo(MeleeBodyRegion.Head));
                        Assert.That(Vector3.Distance(hit.Point, tissue), Is.LessThan(.0005f),
                            "The contact must follow the rendered, currently deformed brain surface.");
                        return;
                    }
                }
            }
            Assert.Fail("The partial fracture must expose an independently visible brain surface.");
        }

        private static void AssertIntactHeadColliderDisabled(CombatActor target)
        {
            foreach (KeyValuePair<Collider, Player3DAnatomicalPart> shape in target.Ragdoll.PhysicsController.AnatomicalColliders)
                if (shape.Value == Player3DAnatomicalPart.Head)
                    Assert.That(shape.Key.enabled, Is.False, "A fracture cannot retain the intact physical head collider.");
        }

        private static bool TryFindGoreHeadApproach(CombatActor source, CombatActor target, Vector3 point, out Vector3 outward)
        {
            target.CaptureContactPose();
            foreach (Vector3 candidate in new[] { Vector3.up, Vector3.up + Vector3.right,
                Vector3.up - Vector3.right, Vector3.up + Vector3.forward, Vector3.up - Vector3.forward,
                Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
            {
                Vector3 direction = candidate.normalized;
                Vector3 origin = point + direction * .75f;
                float step = CombatTestRoot.SimulationStep;
                Vector3 end = origin - direction * (CombatProjectilePool.MuzzleSpeed * step) +
                    Vector3.down * (.5f * CombatProjectilePool.Gravity * step * step);
                if (!target.Hurtboxes.SweepProjectile(origin, end, CombatProjectilePool.Radius, -direction, out var hit) ||
                    hit.Location.Region != MeleeBodyRegion.Head) continue;
                bool blocked = false;
                foreach (Collider shape in Physics.OverlapSphere(origin, CombatProjectilePool.Radius,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    if (IsWorld(shape)) { blocked = true; break; }
                Vector3 segment = end - origin;
                foreach (RaycastHit contact in Physics.SphereCastAll(origin, CombatProjectilePool.Radius,
                    segment.normalized, segment.magnitude * hit.Fraction,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    if (IsWorld(contact.collider)) { blocked = true; break; }
                if (!blocked) { outward = direction; return true; }
            }
            outward = Vector3.zero;
            return false;

            bool IsWorld(Collider shape) => shape != null && !shape.isTrigger &&
                !shape.transform.IsChildOf(source.transform) && shape.GetComponentInParent<CombatActor>() == null;
        }

        private void VerifyPulsatingBodyWound()
        {
            root.ResetRound();
            PlacePair(4f);
            CombatActor target = root.Opponent;
            target.CaptureContactPose();
            Assert.That(target.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Torso,
                out Vector3 point, out Vector3 forward, out _), Is.True);
            Vector3 outward = Vector3.zero;
            foreach (Vector3 candidate in new[] { -forward, forward, Vector3.right, Vector3.left })
            {
                Vector3 origin = point + candidate * .75f;
                Vector3 end = origin - candidate * CombatProjectilePool.MuzzleSpeed * CombatTestRoot.SimulationStep;
                if (target.Hurtboxes.SweepSphere(origin, end, CombatProjectilePool.Radius, -candidate, out var hit) &&
                    hit.Location.Region == MeleeBodyRegion.Torso)
                { outward = candidate; break; }
            }
            Assert.That(outward.sqrMagnitude, Is.GreaterThan(.9f), "The body wound needs an exposed torso approach.");
            Assert.That(root.Projectiles.TrySpawn(root.Hero, point + outward * .75f,
                -outward * CombatProjectilePool.MuzzleSpeed, 1), Is.True);
            root.Projectiles.Advance(CombatTestRoot.SimulationStep, root.Hero, root.Opponent);
            root.Projectiles.ApplyContacts();
            Assert.That(target.LastImpact.Location.Region, Is.EqualTo(MeleeBodyRegion.Torso));
            Assert.That(target.State.IsDefeated, Is.False);
            Assert.That(target.State.Health, Is.LessThan(target.State.Settings.MaxHealth));
            Assert.That(root.HeadEffects.DetachedSectorCountFor(target), Is.Zero);
            Assert.That(target.Ragdoll.IsConvulsing, Is.False);
            root.Tick(.15f);
            float pressure = root.BloodEffects.BleedingPressureFor(target);
            float supply = root.BloodEffects.RemainingBloodFractionFor(target);
            int drops = root.BloodEffects.BleedingDropCountFor(target);
            float minimum = 1f, maximum = 0f;
            for (int sample = 0; sample < 16; sample++)
            {
                root.Tick(.05f);
                float pulse = root.BloodEffects.BleedingPulseFor(target);
                minimum = Mathf.Min(minimum, pulse); maximum = Mathf.Max(maximum, pulse);
            }
            Assert.That(maximum - minimum, Is.GreaterThan(.3f));
            Assert.That(root.BloodEffects.BleedingDropCountFor(target), Is.GreaterThan(drops));
            Assert.That(root.BloodEffects.BleedingPressureFor(target), Is.LessThan(pressure));
            Assert.That(root.BloodEffects.RemainingBloodFractionFor(target), Is.LessThan(supply));
            root.BloodEffects.Tick(CombatBloodEffects.ProjectileBleedLifetimeSeconds);
            Assert.That(root.BloodEffects.BleedingPressureFor(target), Is.Zero);
            drops = root.BloodEffects.BleedingDropCountFor(target);
            root.BloodEffects.Tick(.8f);
            Assert.That(root.BloodEffects.BleedingDropCountFor(target), Is.EqualTo(drops));
            root.ResetRound();
            Assert.That(root.BloodEffects.BleedingAgeFor(target), Is.Zero);
            Assert.That(root.BloodEffects.ProjectileWoundCountFor(target), Is.Zero);
            Assert.That(root.BloodEffects.RemainingBloodFractionFor(target), Is.EqualTo(1f));
        }

        private void CaptureGoreNearView(CombatActor target, Vector3 outward, string name,
            bool avoidObstruction = false, bool airborne = false)
        {
            Camera camera = root.CameraFollow.Camera;
            Vector3 savedPosition = camera.transform.position;
            Quaternion savedRotation = camera.transform.rotation;
            float savedFieldOfView = camera.fieldOfView, savedNear = camera.nearClipPlane;
            try
            {
                Vector3 subject = root.HeadEffects.HeadFrameFor(target).position + Vector3.up * .07f;
                Vector3 side = Vector3.Cross(outward, Vector3.up).normalized;
                if (side.sqrMagnitude < .1f) side = Vector3.right;
                Vector3 eye = subject + outward * .8f + side * .26f + Vector3.up * .22f;
                if (airborne) subject -= outward * .3f;
                if (avoidObstruction)
                {
                    Rigidbody headBody = root.HeadEffects.HeadFrameFor(target).GetComponent<Rigidbody>();
                    float distance = airborne ? 1.8f : 1f;
                    Vector3[] approaches = { airborne ? side : outward, -outward, -side,
                        Vector3.forward, Vector3.back, Vector3.right, Vector3.left, Vector3.up };
                    foreach (Vector3 approach in approaches)
                    {
                        Vector3 candidate = subject + approach.normalized * distance + Vector3.up * .7f;
                        candidate.y = Mathf.Max(candidate.y, .4f);
                        Vector3 ray = candidate - subject;
                        bool blocked = false;
                        foreach (RaycastHit hit in Physics.RaycastAll(subject, ray.normalized, ray.magnitude + .08f,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                            if (hit.distance > .18f && hit.collider.attachedRigidbody != headBody)
                            { blocked = true; break; }
                        if (blocked) continue;
                        foreach (Collider shape in Physics.OverlapSphere(candidate, .08f,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                            if (shape.attachedRigidbody != headBody) { blocked = true; break; }
                        if (!blocked) { eye = candidate; break; }
                    }
                }
                camera.fieldOfView = airborne ? 54f : 42f; camera.nearClipPlane = .015f;
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(subject - eye, Vector3.up));
                string path = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.CombatTest, name + ".png");
                LogAssert.Expect(LogType.Log, "Area capture wrote " + path);
                AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.CombatTest, name);
            }
            finally
            {
                camera.fieldOfView = savedFieldOfView; camera.nearClipPlane = savedNear;
                camera.transform.SetPositionAndRotation(savedPosition, savedRotation);
            }
        }
    }
}

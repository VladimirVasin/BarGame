using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
        private WeaponMeshClearanceProbe weaponMeshClearance;

        private IEnumerator VerifyMixedWeaponSupport(CombatActor actor, string subject)
        {
            foreach (var sample in new[]
            {
                (Swing: MeleeSwing.Forehand, Power: .25f),
                (Swing: MeleeSwing.Forehand, Power: 1f),
                (Swing: MeleeSwing.Backhand, Power: .25f),
                (Swing: MeleeSwing.Backhand, Power: .5f),
                (Swing: MeleeSwing.Backhand, Power: 1f)
            })
            {
                MeleeSwing swing = sample.Swing;
                PlacePair(4f);
                yield return null;
                yield return null;
                PresentInertiaPose();
                actor.State.ObserveLateralCue(swing == MeleeSwing.Backhand ? 1 : -1);
                Assert.That(actor.RequestCharge(), Is.True);
                float power = sample.Power;
                string context = subject + "/" + swing + " power=" + power + " mixed grip";
                string captureSubject = subject + "-" + swing + "-power" + Mathf.RoundToInt(power * 100f);
                for (int frame = 0; frame < 120 && actor.State.Charge01 < power; frame++)
                {
                    root.Tick(ImpactFrameSeconds);
                    yield return null;
                    PresentInertiaPose();
                    AssertMixedSupportPose(actor, context + " charge=" + actor.State.Charge01);
                }
                Assert.That(actor.State.Charge01, Is.EqualTo(power).Within(.035f));
                Transform[] releaseJoints = { FindAnatomicalBone(actor, "forearm.R"), FindAnatomicalBone(actor, "hand.R"),
                    FindAnatomicalBone(actor, "forearm.L"), FindAnatomicalBone(actor, "hand.L"), actor.Weapon.transform };
                var beforeRelease = new Pose[releaseJoints.Length];
                for (int i = 0; i < releaseJoints.Length; i++)
                    beforeRelease[i] = new Pose(releaseJoints[i].position, releaseJoints[i].rotation);
                Assert.That(actor.ReleaseCharge(), Is.True, context + ": the actual support must permit release.");
                PresentInertiaPose();
                for (int i = 0; i < releaseJoints.Length; i++)
                {
                    Assert.That(Vector3.Distance(beforeRelease[i].position, releaseJoints[i].position), Is.LessThan(.005f),
                        context + ": charge/release must share the same visible pose: " + releaseJoints[i].name);
                    Assert.That(Quaternion.Angle(beforeRelease[i].rotation, releaseJoints[i].rotation), Is.LessThan(1f),
                        context + ": charge/release must retain the joint frame: " + releaseJoints[i].name);
                }
                bool capturedContact = false, capturedReturn = false;
                for (int frame = 0; frame < 150 && actor.State.IsAttacking; frame++)
                {
                    root.Tick(ImpactFrameSeconds);
                    yield return null;
                    PresentInertiaPose();
                    AssertMixedSupportPose(actor, context + " release=" + actor.State.AttackProgress);
                    float seconds = actor.State.AttackProgress * 1.28f;
                    if (!capturedContact && seconds >= .60f)
                    {
                        CaptureImpactRecoveryFrame(actor, captureSubject, "weapon-contact",
                            actor.transform.position, actor.transform.rotation, true);
                        capturedContact = true;
                    }
                    if (!capturedReturn && seconds >= .89f)
                    {
                        CaptureImpactRecoveryFrame(actor, captureSubject, "weapon-return",
                            actor.transform.position, actor.transform.rotation, true);
                        capturedReturn = true;
                    }
                }
                Assert.That(actor.State.Phase, Is.EqualTo(MeleePhase.Ready), context);
                Assert.That(capturedContact && capturedReturn, Is.True, context + ": both changed arcs must be inspected.");
            }
        }

        private void AssertMixedSupportPose(CombatActor actor, string context)
        {
            AssertWeaponClearance(actor, context);
            Assert.That(actor.SupportArmState, Is.EqualTo(CombatArmSupportState.SupportingWeapon), context);
            NpcHandPose hands = actor.GetComponentInChildren<NpcHandPose>();
            Assert.That(Vector3.Distance(hands.CylinderCentre(true), actor.SupportGripWorldPosition), Is.LessThan(.025f), context);
            foreach (bool left in new[] { false, true })
            {
                Transform hand = FindAnatomicalBone(actor, left ? "hand.L" : "hand.R");
                Transform forearm = FindAnatomicalBone(actor, left ? "forearm.L" : "forearm.R");
                Vector3 palm = hands.PalmNormal(left);
                Vector3 fingers = Vector3.ProjectOnPlane(hands.CylinderCentre(left) - hand.position, palm).normalized;
                Vector3 lower = (hand.position - forearm.position).normalized;
                float radial = Mathf.Abs(Mathf.Asin(Mathf.Clamp(Vector3.Dot(lower, Vector3.Cross(palm, fingers).normalized), -1f, 1f)) * Mathf.Rad2Deg);
                float flexion = Mathf.Abs(Mathf.Atan2(Vector3.Dot(lower, palm), Vector3.Dot(lower, fingers)) * Mathf.Rad2Deg);
                Assert.That(radial, Is.LessThanOrEqualTo(25.1f), context + (left ? " L" : " R") + " wrist sideways bend");
                Assert.That(flexion, Is.LessThanOrEqualTo(55.1f), context + (left ? " L" : " R") + " wrist flexion");
            }
        }

        // Invoked by the impact/recovery scenario, so the same scene and both
        // authored rigs cover held, interrupted, recovering and dropped props.
        private IEnumerator VerifyWeaponClearanceStates(CombatActor actor, string subject)
        {
            foreach (string pose in new[] { "Ready", "Block", "Attack" })
            {
                PlacePair(4f);
                yield return null;
                yield return null;
                PresentInertiaPose();
                if (pose == "Block") actor.SetBlock(true);
                if (pose == "Attack") Assert.That(actor.TryAttack(), Is.True, subject + ": start the unobstructed swing.");
                for (int frame = 0; frame < 8; frame++)
                {
                    root.Tick(ImpactFrameSeconds);
                    yield return null;
                    PresentInertiaPose();
                }
                if (pose == "Attack")
                {
                    for (int frame = 0; frame < 45 && actor.State.Phase == MeleePhase.Windup; frame++)
                    {
                        root.Tick(ImpactFrameSeconds);
                        yield return null;
                        PresentInertiaPose();
                    }
                    Assert.That(actor.State.Phase, Is.EqualTo(MeleePhase.Active),
                        subject + ": place the panel in the live arc, after an unobstructed windup.");
                }
                AssertWeaponClearance(actor, subject + "/" + pose + " before wall");

                Transform weapon = actor.Weapon.transform;
                Vector3 gripPosition = weapon.localPosition;
                Quaternion gripRotation = weapon.localRotation;
                Vector3 hook = ClearanceProbe.HookSurface(actor);
                Vector3 outward = Vector3.ProjectOnPlane(hook - actor.transform.position, Vector3.up).normalized;
                if (outward.sqrMagnitude < .5f) outward = actor.transform.forward;
                var panel = new GameObject("Weapon hook overlap fixture");
                panel.transform.SetParent(root.transform, false);
                panel.transform.SetPositionAndRotation(hook, Quaternion.LookRotation(outward));
                BoxCollider wall = panel.AddComponent<BoxCollider>();
                wall.size = new Vector3(.20f, .25f, .035f);
                Physics.SyncTransforms();
                try
                {
                    // Spawn around the already presented hook: a sweep-only
                    // implementation has no previous outside point to rescue it.
                    Assert.That(ClearanceProbe.DepthAgainst(actor, wall), Is.GreaterThan(.012f),
                        subject + "/" + pose + ": the real hook surface must start inside this thin panel.");
                    bool sawRecoil = false;
                    for (int frame = 0; frame < 18; frame++)
                    {
                        root.Tick(ImpactFrameSeconds);
                        yield return null;
                        PresentInertiaPose();
                        sawRecoil |= actor.ActiveClipName != null && actor.ActiveClipName.Contains("Recoil");
                        AssertWeaponClearance(actor, subject + "/" + pose + " wall frame=" + frame);
                        Assert.That(Vector3.Distance(weapon.localPosition, gripPosition), Is.LessThan(.001f),
                            "Clearance must move the arm without sliding the crowbar out of its right-hand grip.");
                        Assert.That(Quaternion.Angle(weapon.localRotation, gripRotation), Is.LessThan(.1f),
                            "Clearance must not rotate the prop independently inside its palm.");
                    }
                    if (pose == "Attack")
                    {
                        Assert.That(actor.State.AttackOutcome, Is.EqualTo(MeleeAttackOutcome.Obstacle),
                            subject + ": correcting the rendered crowbar must not erase its wall contact.");
                        Assert.That(sawRecoil, Is.True,
                            subject + ": the live arc must show its wall recoil after the full prop is stopped.");
                    }
                }
                finally { wall.enabled = false; Object.Destroy(panel); }
            }

            yield return VerifyDroppedWeaponClearance(actor, subject);
            PlacePair(4f);
            yield return null;
            PresentInertiaPose();
            AssertWeaponClearance(actor, subject + "/reset after weapon clearance");
        }

        private IEnumerator VerifyDroppedWeaponClearance(CombatActor actor, string subject)
        {
            PlacePair(4f);
            yield return null;
            PresentInertiaPose();
            MethodInfo drop = typeof(CombatActor).GetMethod("DropWeapon", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(drop, Is.Not.Null, "Exercise the actual prop physics handoff without defeating the fixture's actor.");
            drop.Invoke(actor, null);
            Assert.That(actor.IsWeaponDropped, Is.True);
            Rigidbody body = actor.Weapon.GetComponent<Rigidbody>();
            Assert.That(body, Is.Not.Null);
            Assert.That(body.isKinematic, Is.False);
            Assert.That(body.detectCollisions, Is.True);
            for (int frame = 0; frame < 150; frame++)
            {
                root.Tick(ImpactFrameSeconds);
                yield return null;
                AssertWeaponClearance(actor, subject + "/dropped frame=" + frame);
            }
            Assert.That(ClearanceProbe.LowestWorldPoint(actor).y, Is.LessThan(.12f),
                subject + ": the dropped rendered hook and shaft must actually reach the arena floor.");
            Assert.That(body.linearVelocity.magnitude, Is.LessThan(.5f),
                subject + ": the floor contact must settle instead of leaving the prop falling through it.");

            // A standing arm outside the movement capsule has disabled physical
            // anatomy. The freely dropped prop must still stop at its live skin.
            PlacePair(4f);
            CombatActor target = actor == root.Hero ? root.Opponent : root.Hero;
            target.SetBlock(true);
            for (int frame = 0; frame < 24; frame++)
            {
                root.Tick(ImpactFrameSeconds);
                yield return null;
                PresentInertiaPose();
            }
            CapsuleCollider arm = null;
            Vector3 contact = Vector3.zero, outward = Vector3.zero, armPointLocal = Vector3.zero;
            float furthest = float.NegativeInfinity;
            foreach (var entry in target.Ragdoll.PhysicsController.AnatomicalColliders)
            {
                if ((entry.Value != Player3DAnatomicalPart.LeftForearm && entry.Value != Player3DAnatomicalPart.RightForearm) ||
                    !(entry.Key is CapsuleCollider capsule)) continue;
                Vector3 axis = capsule.direction == 0 ? Vector3.right : capsule.direction == 1 ? Vector3.up : Vector3.forward;
                float half = Mathf.Max(0f, capsule.height * .5f - capsule.radius);
                foreach (float end in new[] { -1f, 1f })
                {
                    Vector3 point = capsule.transform.TransformPoint(capsule.center + axis * (end * half));
                    Vector3 radial = Vector3.ProjectOnPlane(point - target.transform.position, Vector3.up);
                    if (radial.magnitude <= furthest) continue;
                    furthest = radial.magnitude; arm = capsule; contact = point; outward = radial.normalized;
                    armPointLocal = capsule.center + axis * (end * half);
                }
            }
            Assert.That(arm, Is.Not.Null);
            Assert.That(arm.enabled, Is.False, "This regression requires query-only standing anatomy.");
            drop.Invoke(actor, null);
            body = actor.Weapon.GetComponent<Rigidbody>();
            CombatHeldWeaponPhysics droppedPhysics = actor.GetComponent<CombatHeldWeaponPhysics>();
            Assert.That(droppedPhysics, Is.Not.Null);
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.None;
            body.rotation = Quaternion.LookRotation(outward, Vector3.up);
            body.angularVelocity = Vector3.zero;
            Vector3 shaftOffset = Vector3.up * .25f;
            float radius = arm.radius * Mathf.Max(arm.transform.lossyScale.x,
                Mathf.Max(arm.transform.lossyScale.y, arm.transform.lossyScale.z));
            body.position = contact + outward * (radius + .01f) - shaftOffset;
            actor.Weapon.transform.SetPositionAndRotation(body.position, body.rotation);
            Physics.SyncTransforms();
            Assert.That(ClearanceProbe.DepthAgainst(actor, arm), Is.GreaterThan(.005f),
                "The intended path must cross the rendered shaft and this real disabled forearm.");
            Assert.That(ClearanceProbe.DepthAgainst(actor, target.Body), Is.LessThan(.001f),
                "The same contact pose must be outside the central CharacterController: it cannot prove this contract.");
            Assert.That(ClearanceProbe.RadialClearanceFrom(actor, target.Body), Is.GreaterThan(.001f),
                "Every mesh sample must stay outside even the controller's infinite central cylinder.");
            body.position = contact + outward * (radius + .16f) - shaftOffset;
            actor.Weapon.transform.SetPositionAndRotation(body.position, body.rotation);
            Physics.SyncTransforms();
            droppedPhysics.EnableDropped(); // Begin at the controlled, already detached launch pose.
            body.linearVelocity = -outward * 2f;
            int contactsBefore = droppedPhysics.DroppedAnatomyContactCount;
            for (int frame = 0; frame < 14; frame++)
            {
                root.Tick(ImpactFrameSeconds);
                yield return new WaitForFixedUpdate();
                yield return null;
                PresentInertiaPose();
                // Like the authored pose above, explicitly present the production
                // pass because this manually stepped coroutine resumes before LateUpdate.
                droppedPhysics.ResolveDroppedAnatomy();
                AssertWeaponClearance(actor, subject + "/dropped into animated arm frame=" + frame);
            }
            Assert.That(droppedPhysics.DroppedAnatomyContactCount, Is.GreaterThan(contactsBefore),
                "The animated limb, rather than the central capsule, must receive the dropped-prop contact.");

            // Moving anatomy must also be swept when the weapon itself is still.
            body.linearVelocity = body.angularVelocity = Vector3.zero;
            contact = arm.transform.TransformPoint(armPointLocal);
            outward = Vector3.ProjectOnPlane(contact - target.transform.position, Vector3.up).normalized;
            body.rotation = Quaternion.LookRotation(outward, Vector3.up);
            body.position = contact + outward * (radius + .04f) - shaftOffset;
            actor.Weapon.transform.SetPositionAndRotation(body.position, body.rotation);
            Physics.SyncTransforms();
            droppedPhysics.EnableDropped();
            contactsBefore = droppedPhysics.DroppedAnatomyContactCount;
            target.Body.Move(outward * .08f);
            Physics.SyncTransforms();
            droppedPhysics.ResolveDroppedAnatomy();
            AssertWeaponClearance(actor, subject + "/animated arm moves into stationary dropped prop");
            Assert.That(droppedPhysics.DroppedAnatomyContactCount, Is.GreaterThan(contactsBefore));
        }

        private WeaponMeshClearanceProbe ClearanceProbe
        {
            get
            {
                if (weaponMeshClearance == null || !weaponMeshClearance.BelongsTo(root.transform))
                    weaponMeshClearance = new WeaponMeshClearanceProbe(root.transform);
                return weaponMeshClearance;
            }
        }

        private void AssertWeaponClearance(CombatActor actor, string context)
        {
            Physics.SyncTransforms();
            float depth = ClearanceProbe.MaximumDepth(actor, out string obstacle);
            if (depth > .01f)
                CaptureImpactRecoveryFrame(actor, actor.IsHero ? "hero-weapon-clearance" : "opponent-weapon-clearance",
                    "penetration-failure", actor.transform.position, actor.transform.rotation, true);
            Assert.That(depth, Is.LessThanOrEqualTo(.01f), context + ": rendered crowbar enters " + obstacle +
                " by " + depth.ToString("F4") + "m; phase=" + actor.State.Phase + ", clip=" + actor.ActiveClipName +
                ", dropped=" + actor.IsWeaponDropped + ", solver=" + actor.WeaponPenetrationDepth.ToString("F4") +
                "/" + actor.WeaponBlockingShape + ". The oracle samples imported mesh surfaces, not solver capsules.");
        }

        private sealed class WeaponMeshClearanceProbe
        {
            private const float SampleRadius = .001f;
            private readonly Transform owner;
            private readonly CombatTestRoot range;
            private readonly SphereCollider probe;
            private readonly Dictionary<GameObject, Vector3[]> samples = new Dictionary<GameObject, Vector3[]>();
            private readonly Collider[] nearby = new Collider[128];
            private readonly HashSet<Collider> candidates = new HashSet<Collider>();

            internal WeaponMeshClearanceProbe(Transform parent)
            {
                owner = parent;
                range = parent.GetComponent<CombatTestRoot>();
                Assert.That(range, Is.Not.Null);
                var query = new GameObject("Weapon mesh clearance oracle") { hideFlags = HideFlags.HideAndDontSave };
                query.transform.SetParent(parent, false);
                probe = query.AddComponent<SphereCollider>();
                probe.radius = SampleRadius;
                probe.enabled = false;
            }

            internal bool BelongsTo(Transform parent) => owner != null && owner == parent && probe != null;

            internal Vector3 HookSurface(CombatActor actor)
            {
                Vector3 tip = CombatAssetProvider.FindAnchor(actor.Weapon, "StrikeTip").position;
                Vector3 closest = actor.Weapon.transform.position;
                float distance = float.PositiveInfinity;
                foreach (Vector3 local in Samples(actor))
                {
                    Vector3 point = World(actor, local);
                    float candidate = (point - tip).sqrMagnitude;
                    if (candidate < distance) { closest = point; distance = candidate; }
                }
                return closest;
            }

            internal Vector3 LowestWorldPoint(CombatActor actor)
            {
                Vector3 lowest = Vector3.positiveInfinity;
                foreach (Vector3 local in Samples(actor))
                {
                    Vector3 point = World(actor, local);
                    if (point.y < lowest.y) lowest = point;
                }
                return lowest;
            }

            internal float RadialClearanceFrom(CombatActor actor, CharacterController controller)
            {
                Vector3 center = controller.transform.TransformPoint(controller.center);
                float scale = Mathf.Max(Mathf.Abs(controller.transform.lossyScale.x), Mathf.Abs(controller.transform.lossyScale.z));
                float minimum = float.PositiveInfinity;
                foreach (Vector3 local in Samples(actor))
                    minimum = Mathf.Min(minimum, Vector3.ProjectOnPlane(World(actor, local) - center,
                        controller.transform.up).magnitude - controller.radius * scale);
                return minimum;
            }

            internal float MaximumDepth(CombatActor actor, out string obstacle)
            {
                candidates.Clear();
                Bounds bounds = new Bounds(actor.Weapon.transform.position, Vector3.zero);
                foreach (Vector3 local in Samples(actor)) bounds.Encapsulate(World(actor, local));
                int count = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents + Vector3.one * SampleRadius,
                    nearby, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
                Assert.That(count, Is.LessThan(nearby.Length), "The clearance oracle's broadphase must not silently truncate.");
                for (int i = 0; i < count; i++)
                {
                    Collider collider = nearby[i];
                    if (collider == null || collider == probe || collider is CharacterController ||
                        collider.transform.IsChildOf(actor.Weapon.transform) ||
                        collider.GetComponentInParent<CombatActor>() != null) continue;
                    candidates.Add(collider);
                }
                // Disabled live anatomical shapes still represent the posed body.
                // Only the intentional fingers/palm grip is exempt; forearms remain.
                if (!actor.IsWeaponDropped)
                    foreach (var entry in actor.Ragdoll.PhysicsController.AnatomicalColliders)
                        if (entry.Value != Player3DAnatomicalPart.LeftHand && entry.Value != Player3DAnatomicalPart.RightHand)
                            candidates.Add(entry.Key);
                // Opposing anatomy is disabled while animated too. A registered
                // hit must stop the visible steel at the struck body surface.
                CombatActor other = actor == range.Hero ? range.Opponent : range.Hero;
                if (other != null && other.Ragdoll != null)
                    foreach (var entry in other.Ragdoll.PhysicsController.AnatomicalColliders)
                        candidates.Add(entry.Key);

                float maximum = 0f;
                obstacle = "nothing";
                foreach (Collider collider in candidates)
                {
                    float depth = DepthAgainst(actor, collider);
                    if (depth > maximum) { maximum = depth; obstacle = collider.name; }
                }
                // A concave floor mesh has triangle surfaces, not a filled solid.
                // A ray from above also catches a rendered tip that already crossed
                // its surface farther than the tiny sphere's contact thickness.
                Vector3 lowest = LowestWorldPoint(actor);
                foreach (RaycastHit hit in Physics.RaycastAll(lowest + Vector3.up * 2f, Vector3.down, 4f,
                    ~0, QueryTriggerInteraction.Ignore))
                {
                    if (!hit.collider.name.Contains("Collision_Floor") || hit.normal.y < .5f) continue;
                    float depth = hit.point.y - lowest.y;
                    if (depth > maximum) { maximum = depth; obstacle = hit.collider.name; }
                }
                return maximum;
            }

            internal float DepthAgainst(CombatActor actor, Collider collider)
            {
                if (collider == null) return 0f;
                Bounds bounds = ShapeBounds(collider);
                bounds.Expand(SampleRadius * 2f);
                float maximum = 0f;
                Vector3 origin = actor.Weapon.transform.position;
                Quaternion rotation = actor.Weapon.transform.rotation;
                foreach (Vector3 local in Samples(actor))
                {
                    Vector3 point = origin + rotation * local;
                    if (!bounds.Contains(point)) continue;
                    if (Physics.ComputePenetration(probe, point, Quaternion.identity, collider,
                        collider.transform.position, collider.transform.rotation, out _, out float depth))
                        maximum = Mathf.Max(maximum, depth - SampleRadius);
                }
                return maximum;
            }

            private static Bounds ShapeBounds(Collider collider)
            {
                Vector3 center, size;
                if (collider is CapsuleCollider capsule)
                {
                    center = capsule.center;
                    size = Vector3.one * capsule.radius * 2f;
                    size[capsule.direction] = Mathf.Max(capsule.height, size[capsule.direction]);
                }
                else if (collider is BoxCollider box) { center = box.center; size = box.size; }
                else if (collider is SphereCollider sphere) { center = sphere.center; size = Vector3.one * sphere.radius * 2f; }
                else return collider.bounds;
                var result = new Bounds(collider.transform.TransformPoint(center), Vector3.zero);
                for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                        for (int z = -1; z <= 1; z += 2)
                            result.Encapsulate(collider.transform.TransformPoint(center + Vector3.Scale(size, new Vector3(x, y, z)) * .5f));
                return result;
            }

            private Vector3[] Samples(CombatActor actor)
            {
                if (samples.TryGetValue(actor.Weapon, out Vector3[] points)) return points;
                Transform weapon = actor.Weapon.transform;
                var unique = new Dictionary<Vector3Int, Vector3>();
                foreach (MeshFilter filter in actor.Weapon.GetComponentsInChildren<MeshFilter>(true))
                {
                    Mesh mesh = filter.sharedMesh;
                    Assert.That(mesh, Is.Not.Null);
                    Assert.That(mesh.isReadable, Is.True, "The imported prop is the independent clearance reference.");
                    Vector3[] vertices = mesh.vertices;
                    for (int i = 0; i < vertices.Length; i++)
                        vertices[i] = Quaternion.Inverse(weapon.rotation) * (filter.transform.TransformPoint(vertices[i]) - weapon.position);
                    foreach (Vector3 vertex in vertices) Add(vertex);
                    int[] triangles = mesh.triangles;
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                        Edge(a, b); Edge(b, c); Edge(c, a);
                        Add((a + b + c) / 3f);
                    }
                }
                Assert.That(unique.Count, Is.GreaterThan(40), "Clearance must sample the complete visible crowbar.");
                points = new Vector3[unique.Count];
                unique.Values.CopyTo(points, 0);
                samples.Add(actor.Weapon, points);
                return points;

                void Add(Vector3 point)
                {
                    var key = new Vector3Int(Mathf.RoundToInt(point.x * 1000f), Mathf.RoundToInt(point.y * 1000f),
                        Mathf.RoundToInt(point.z * 1000f));
                    if (!unique.ContainsKey(key)) unique.Add(key, point);
                }
                void Edge(Vector3 a, Vector3 b)
                {
                    int intervals = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / .015f));
                    for (int i = 1; i < intervals; i++) Add(Vector3.Lerp(a, b, i / (float)intervals));
                }
            }

            private static Vector3 World(CombatActor actor, Vector3 local) =>
                actor.Weapon.transform.position + actor.Weapon.transform.rotation * local;
        }
    }
}

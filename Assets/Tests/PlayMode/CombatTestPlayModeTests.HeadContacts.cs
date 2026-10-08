using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
        [UnityTest]
        public IEnumerator Range_FreePistolCentreAimHitsVisibleHeadEdgesFromActualMuzzle()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            yield return EnterRange(false, CombatWeaponId.Pistol);
            MethodInfo prepare = typeof(CombatTestRoot).GetMethod("PrepareFreePistolAim",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(prepare, Is.Not.Null, "Exercise the same centre-ray preparation used by RMB input.");

            for (int sample = 0; sample < 4; sample++)
            {
                root.ResetRound();
                PlacePair(sample < 2 ? 4f : 6f);
                root.SendMessage("OnApplicationFocus", true);
                Assert.That(root.SetOpponentFocus(false), Is.True);
                Transform shoulder = root.Hero.Ragdoll.PhysicsController.ChestBody.transform;
                Transform head = FindAnatomicalBone(root.Opponent, "head");
                root.Hero.SetPistolAim(true, head.position);
                // Different ordinary presentation-clock phases include both signs
                // of the idle hand sway; the test never edits the muzzle pose.
                root.Tick(.35f + sample * .25f);
                Assert.That(root.Hero.Pistol.CanFire, Is.True);
                Assert.That(root.CameraFollow.SetFreeAim(root, shoulder), Is.True);
                root.CameraFollow.Snap();
                Camera camera = root.CameraFollow.Camera;
                Vector3 cameraPosition = camera.transform.position;
                Assert.That(Vector3.Distance(cameraPosition, root.Hero.PistolMuzzle.position), Is.GreaterThan(1f),
                    "This must retain the third-person shoulder/muzzle parallax.");
                var surfaces = BakeHeadContactSurfaces(root.Opponent, false);
                Assert.That(root.Opponent.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head,
                    out _, out Vector3 forward, out Vector3 up), Is.True);
                Vector3 side = Vector3.Cross(up, forward).normalized * (sample % 2 == 0 ? 1f : -1f);
                Vector3 cheek = SelectHeadContactFeature(surfaces, head.position, side, up, forward, "cheek");
                Vector3 insideEdge = cheek - side * .01f;
                AimActualShoulderCamera(insideEdge);
                Ray ray = camera.ViewportPointToRay(new Vector3(.5f, .5f, 0f));
                Assert.That(IndependentHeadRaycast(surfaces, ray, out Vector3 expected), Is.True,
                    "The centre pixel must cross independently baked visible flesh, just inside the cheek edge.");
                string context = "cheek " + (sample % 2 == 0 ? "right" : "left") + " phase " + sample;

                if (sample == 0)
                {
                    // A world surface still wins ahead of the rendered anatomy.
                    var wall = new GameObject("Test aiming wall");
                    var collider = wall.AddComponent<BoxCollider>();
                    collider.size = new Vector3(.2f, .2f, .02f);
                    wall.transform.SetPositionAndRotation(Vector3.Lerp(ray.origin, expected, .5f),
                        Quaternion.LookRotation(ray.direction));
                    Physics.SyncTransforms();
                    try
                    {
                        Assert.That(collider.Raycast(ray, out RaycastHit world, CombatProjectilePool.MaximumDistance), Is.True);
                        Vector3 blocked = (Vector3)prepare.Invoke(root, null);
                        Assert.That(Vector3.Distance(blocked, world.point), Is.LessThan(.005f),
                            "A camera-visible head cannot pull the aim target through a nearer wall.");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(wall); Physics.SyncTransforms(); }

                    // This ray is 1 cm outside the entire independently projected
                    // silhouette, rather than a point inside a head bounding box.
                    Vector3 missPoint = OutsideHeadCameraSilhouette(surfaces, camera, expected);
                    AimActualShoulderCamera(missPoint);
                    Ray missRay = camera.ViewportPointToRay(new Vector3(.5f, .5f, 0f));
                    Assert.That(IndependentHeadRaycast(surfaces, missRay, out _), Is.False);
                    Vector3 missed = (Vector3)prepare.Invoke(root, null);
                    Assert.That(Vector3.Distance(missRay.origin, missed),
                        Is.GreaterThan(Vector3.Distance(missRay.origin, expected) + .1f),
                        "A close screen-space miss must continue behind the head, without aim assistance.");
                    AimActualShoulderCamera(insideEdge);
                    ray = camera.ViewportPointToRay(new Vector3(.5f, .5f, 0f));
                    Assert.That(IndependentHeadRaycast(surfaces, ray, out expected), Is.True);
                }

                Vector3 prepared = (Vector3)prepare.Invoke(root, null);
                Assert.That(Vector3.Distance(prepared, expected), Is.LessThan(.015f),
                    context + ": camera aim must use the visible head surface, not the coarse capsule or far wall.");
                root.Hero.SetPistolAim(true, prepared);
                root.Hero.Present();
                Assert.That(root.Hero.PistolAimAligned, Is.True, context);
                Vector3 actualMuzzle = root.Hero.PistolMuzzle.position;
                Assert.That(root.Hero.RequestPistolShot(), Is.True);
                Assert.That(root.Hero.CommitPistolShot(root.Projectiles), Is.True, context);
                Assert.That(root.Projectiles.SpawnCount, Is.EqualTo(1));
                Assert.That(Vector3.Distance(root.Projectiles.LastPosition, actualMuzzle), Is.LessThan(.00001f),
                    "The shot starts at the real gun muzzle, never at the camera or test surface.");
                for (int step = 0; step < 8 && root.Projectiles.ActiveCount > 0; step++)
                {
                    root.Projectiles.Advance(CombatTestRoot.SimulationStep, root.Hero, root.Opponent);
                    root.Projectiles.ApplyContacts();
                }
                Assert.That(root.Opponent.ReceivedImpactCount, Is.EqualTo(1), context);
                Assert.That(root.Opponent.LastImpact.Kind, Is.EqualTo(CombatImpactKind.Projectile), context);
                Assert.That(root.Opponent.LastImpact.Location.Region, Is.EqualTo(MeleeBodyRegion.Head), context);
                Assert.That(Vector3.Distance(root.Opponent.LastImpact.Point, expected), Is.LessThan(.025f),
                    context + ": the physical bullet must land by the crosshair's anatomical surface.");
            }
            LogAssert.NoUnexpectedReceived();
        }

        private void AimActualShoulderCamera(Vector3 point)
        {
            Camera camera = root.CameraFollow.Camera;
            root.CameraFollow.ClearFreeAim(root);
            camera.transform.rotation = Quaternion.LookRotation(point - camera.transform.position, Vector3.up);
            Assert.That(root.CameraFollow.SetFreeAim(root,
                root.Hero.Ragdoll.PhysicsController.ChestBody.transform, preserveCurrentPose: true), Is.True);
        }

        private static Vector3 OutsideHeadCameraSilhouette(List<HeadContactSurface> surfaces,
            Camera camera, Vector3 heightReference)
        {
            Transform view = camera.transform;
            float edge = float.NegativeInfinity;
            foreach (HeadContactSurface surface in surfaces)
                foreach (Vector3 vertex in surface.Vertices)
                {
                    Vector3 local = view.InverseTransformPoint(vertex);
                    Assert.That(local.z, Is.GreaterThan(0f));
                    edge = Mathf.Max(edge, local.x / local.z);
                }
            Vector3 reference = view.InverseTransformPoint(heightReference);
            return view.TransformPoint(new Vector3(edge * reference.z + .01f, reference.y, reference.z));
        }

        // Independent, zero-radius ray/triangle oracle over rendered BakeMesh
        // output. It shares neither hurtbox snapshots nor the runtime sweep solver.
        private static bool IndependentHeadRaycast(List<HeadContactSurface> surfaces, Ray ray, out Vector3 point)
        {
            float nearest = float.PositiveInfinity;
            foreach (HeadContactSurface surface in surfaces)
                for (int i = 0; i < surface.Triangles.Length; i += 3)
                {
                    Vector3 a = surface.Vertices[surface.Triangles[i]];
                    Vector3 ab = surface.Vertices[surface.Triangles[i + 1]] - a;
                    Vector3 ac = surface.Vertices[surface.Triangles[i + 2]] - a;
                    Vector3 cross = Vector3.Cross(ray.direction, ac);
                    float determinant = Vector3.Dot(ab, cross);
                    if (Mathf.Abs(determinant) < .000000001f) continue;
                    float inverse = 1f / determinant;
                    Vector3 relative = ray.origin - a;
                    float u = Vector3.Dot(relative, cross) * inverse;
                    if (u < 0f || u > 1f) continue;
                    Vector3 q = Vector3.Cross(relative, ab);
                    float v = Vector3.Dot(ray.direction, q) * inverse;
                    if (v < 0f || u + v > 1f) continue;
                    float distance = Vector3.Dot(ac, q) * inverse;
                    if (distance >= 0f) nearest = Mathf.Min(nearest, distance);
                }
            point = float.IsPositiveInfinity(nearest) ? default : ray.GetPoint(nearest);
            return !float.IsPositiveInfinity(nearest);
        }

        [UnityTest]
        public IEnumerator Range_ProjectileHeadEdgesMatchVisibleFrozenAndRetainedSurfaces()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            yield return EnterRange(false, CombatWeaponId.Pistol);
            foreach (bool heroVictim in new[] { false, true })
            {
                root.ResetRound();
                PlacePair(4f);
                CombatActor target = heroVictim ? root.Hero : root.Opponent;
                CombatActor source = heroVictim ? root.Opponent : root.Hero;
                string rig = heroVictim ? "hero" : "opponent";
                Transform head = FindAnatomicalBone(target, "head");
                target.CaptureContactPose();
                Assert.That(target.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head,
                    out _, out Vector3 forward, out Vector3 up), Is.True);
                Vector3 right = Vector3.Cross(up, forward).normalized;
                var intact = BakeHeadContactSurfaces(target, false);
                Vector3 ear = SelectHeadContactFeature(intact, head.position, right, up, forward, "ear");
                Vector3 nose = SelectHeadContactFeature(intact, head.position, right, up, forward, "nose");
                Vector3 cheek = SelectHeadContactFeature(intact, head.position, right, up, forward, "cheek");
                AssertVisibleHeadEdge(target, ear, forward, rig + " outer ear");
                AssertVisibleHeadEdge(target, nose, forward, rig + " nose tip");
                AssertVisibleHeadEdge(target, cheek, forward, rig + " cheek edge");

                float furthestRight = float.NegativeInfinity;
                foreach (HeadContactSurface surface in intact)
                    foreach (Vector3 point in surface.Vertices)
                        furthestRight = Mathf.Max(furthestRight, Vector3.Dot(point, right));
                Vector3 nearMiss = ear + right * (furthestRight - Vector3.Dot(ear, right) + CombatProjectilePool.Radius + .003f);
                Assert.That(target.Hurtboxes.SweepProjectile(nearMiss + forward * .22f,
                    nearMiss - forward * .22f, CombatProjectilePool.Radius, -forward, out _), Is.False,
                    rig + ": a bullet just outside the ear's actual silhouette must miss.");

                // A silhouette's empty upper corner lies inside its bounding rectangle.
                // The independent projected-triangle oracle proves that an enlarged
                // head box would invent a target here even though no flesh is present.
                Vector3 corner = EmptyHeadSilhouetteCorner(intact, head.position, right, up);
                Assert.That(ProjectedHeadGap(intact, corner, right, up), Is.GreaterThan(CombatProjectilePool.Radius + .003f));
                Assert.That(target.Hurtboxes.SweepProjectile(corner + forward * .25f,
                    corner - forward * .25f, CombatProjectilePool.Radius, -forward, out _), Is.False,
                    rig + ": an empty corner cannot become an invisible head target.");

                Vector3 savedPosition = head.localPosition;
                Quaternion savedRotation = head.localRotation;
                Assert.That(target.Hurtboxes.SweepProjectile(ear + forward * .22f,
                    ear - forward * .03f, CombatProjectilePool.Radius, -forward, out var beforeTurn), Is.True);
                try
                {
                    head.rotation = Quaternion.AngleAxis(90f, up) * head.rotation;
                    head.position += right * .4f;
                    Assert.That(target.Hurtboxes.SweepProjectile(ear + forward * .22f,
                        ear - forward * .03f, CombatProjectilePool.Radius, -forward, out var frozen), Is.True);
                    Assert.That(Vector3.Distance(beforeTurn.Point, frozen.Point), Is.LessThan(.0001f),
                        "A contact step retains its frozen rendered surface until the next capture.");
                    target.CaptureContactPose();
                    Assert.That(target.Hurtboxes.SweepProjectile(ear + forward * .22f,
                        ear - forward * .03f, CombatProjectilePool.Radius, -forward, out _), Is.False,
                        "Capturing the moved head must retire its previous target.");
                    Assert.That(target.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head,
                        out _, out Vector3 turnedForward, out Vector3 turnedUp), Is.True);
                    Assert.That(Vector3.Angle(turnedForward, forward), Is.GreaterThan(80f));
                    Vector3 turnedRight = Vector3.Cross(turnedUp, turnedForward).normalized;
                    var turned = BakeHeadContactSurfaces(target, false);
                    Vector3 turnedEar = SelectHeadContactFeature(turned, head.position,
                        turnedRight, turnedUp, turnedForward, "ear");
                    AssertVisibleHeadEdge(target, turnedEar, turnedForward, rig + " turned ear");
                }
                finally
                {
                    head.SetLocalPositionAndRotation(savedPosition, savedRotation);
                    target.CaptureContactPose();
                }

                // A real swept bullet, including its gravity and pool world checks,
                // must reach the independently baked ear edge on BOTH production rigs.
                Assert.That(root.Projectiles.TrySpawn(source, ear + forward * .18f,
                    -forward * CombatProjectilePool.MuzzleSpeed, 1), Is.True);
                root.Projectiles.Advance(CombatTestRoot.SimulationStep, root.Hero, root.Opponent);
                root.Projectiles.ApplyContacts();
                Assert.That(target.ReceivedImpactCount, Is.EqualTo(1), rig + " visible ear must receive the actual bullet.");
                Assert.That(target.LastImpact.Kind, Is.EqualTo(CombatImpactKind.Projectile));
                Assert.That(target.LastImpact.Location.Region, Is.EqualTo(MeleeBodyRegion.Head));
                Assert.That(Vector3.Distance(target.LastImpact.Point, ear), Is.LessThan(.025f),
                    "The ear itself owns the edge contact rather than the central head capsule.");
                Assert.That(target.State.IsDefeated, Is.True);
                Assert.That(root.HeadEffects.DetachedSectorCountFor(target), Is.GreaterThan(0));

                target.CaptureContactPose();
                var retained = BakeHeadContactSurfaces(target, true);
                int retainedEarParts = 0;
                foreach (HeadContactSurface surface in retained)
                {
                    if (!surface.Name.Contains("__GEO_Ear.")) continue;
                    retainedEarParts++;
                    Vector3 centroid = Vector3.zero;
                    foreach (Vector3 vertex in surface.Vertices) centroid += vertex;
                    centroid /= surface.Vertices.Length;
                    Vector3 outward = Vector3.Dot(centroid - head.position, right) >= 0f ? right : -right;
                    Vector3 retainedEar = ExtremeHeadContactPoint(surface, outward);
                    AssertVisibleHeadEdge(target, retainedEar, forward, rig + " surviving ear");
                }
                Assert.That(retainedEarParts, Is.GreaterThan(0),
                    "A grazing ear hit leaves the opposite ear available as real retained flesh.");

                Vector3 gap = ear;
                float largestGap = 0f;
                foreach (HeadContactSurface surface in intact)
                    foreach (Vector3 point in surface.Vertices)
                    {
                        float distance = ProjectedHeadGap(retained, point, right, up);
                        if (distance > largestGap) { largestGap = distance; gap = point; }
                    }
                Assert.That(largestGap, Is.GreaterThan(CombatProjectilePool.Radius + .003f),
                    "The actual removed silhouette must contain a measurable open gap.");
                Assert.That(target.Hurtboxes.SweepProjectile(gap + forward * .25f,
                    gap - forward * .25f, CombatProjectilePool.Radius, -forward, out _), Is.False,
                    "Detached original triangles and retained-sector box corners cannot fill the open gap.");
                yield return CaptureFocusGameView("head-edge-" + rig + "-contact");
                CaptureGoreNearView(target, forward, "head-edge-" + rig + "-near", true);
                root.ResetRound();
                PlacePair(4f);
                target.CaptureContactPose();
                Assert.That(target.Hurtboxes.GetRegionFrame(MeleeBodyRegion.Head,
                    out _, out Vector3 resetForward, out Vector3 resetUp), Is.True);
                Vector3 resetEar = SelectHeadContactFeature(BakeHeadContactSurfaces(target, false),
                    head.position, Vector3.Cross(resetUp, resetForward).normalized, resetUp, resetForward, "ear");
                AssertVisibleHeadEdge(target, resetEar, resetForward, rig + " reset ear");
            }
            LogAssert.NoUnexpectedReceived();
        }

        private static void AssertVisibleHeadEdge(CombatActor target, Vector3 point, Vector3 forward, string context)
        {
            Assert.That(target.Hurtboxes.SweepProjectile(point + forward * .22f,
                point - forward * .03f, CombatProjectilePool.Radius, -forward, out var hit), Is.True,
                context + " is a real rendered surface within the 4 mm bullet radius.");
            Assert.That(hit.Location.Region, Is.EqualTo(MeleeBodyRegion.Head), context);
            Assert.That(hit.Part, Is.EqualTo(Player3DAnatomicalPart.Head), context);
            Assert.That(Vector3.Distance(hit.Point, point), Is.LessThan(.035f),
                context + " probe=" + point.ToString("F5") + " hit=" + hit.Point.ToString("F5") +
                " forward=" + forward.ToString("F5") + " fraction=" + hit.Fraction);
        }

        private sealed class HeadContactSurface
        {
            internal string Name;
            internal Vector3[] Vertices;
            internal int[] Triangles;
        }

        private static List<HeadContactSurface> BakeHeadContactSurfaces(CombatActor target, bool retained)
        {
            var result = new List<HeadContactSurface>();
            var scratch = new Mesh { name = "Test head contact surface" };
            try
            {
                foreach (SkinnedMeshRenderer skin in target.DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!skin.enabled || !skin.gameObject.activeInHierarchy || skin.sharedMesh == null) continue;
                    string name = skin.name;
                    if (retained)
                    {
                        if (!name.StartsWith("Fracture Sector", StringComparison.Ordinal)) continue;
                        int separator = name.IndexOf("__", StringComparison.Ordinal);
                        if (separator < 0) continue;
                        name = name.Substring(separator + 2);
                        if (name != "Interior" && !IsIndependentHeadFlesh(name)) continue;
                    }
                    else if (!IsIndependentHeadFlesh(name)) continue;
                    skin.BakeMesh(scratch, true);
                    Vector3[] points = scratch.vertices;
                    for (int i = 0; i < points.Length; i++) points[i] = skin.transform.TransformPoint(points[i]);
                    result.Add(new HeadContactSurface { Name = skin.name, Vertices = points, Triangles = scratch.triangles });
                }
            }
            finally { UnityEngine.Object.Destroy(scratch); }
            Assert.That(result, Is.Not.Empty, "The oracle needs actual enabled rendered flesh, independently of hurtbox data.");
            return result;
        }

        private static bool IsIndependentHeadFlesh(string name) => name == "GEO_Head" || name == "GEO_FaceSurface" ||
            name.StartsWith("GEO_Ear.", StringComparison.Ordinal) || name.StartsWith("GEO_EarFold.", StringComparison.Ordinal);

        private static Vector3 SelectHeadContactFeature(List<HeadContactSurface> surfaces,
            Vector3 origin, Vector3 right, Vector3 up, Vector3 forward, string feature)
        {
            HeadContactSurface selected = null;
            if (feature == "ear")
            {
                Vector3 edge = Vector3.zero;
                float extreme = float.NegativeInfinity;
                foreach (HeadContactSurface surface in surfaces)
                    if (surface.Name.StartsWith("GEO_Ear.", StringComparison.Ordinal))
                    {
                        Vector3 point = ExtremeHeadContactPoint(surface, right);
                        float side = Vector3.Dot(point - origin, right);
                        if (side > extreme) { extreme = side; edge = point; }
                    }
                Assert.That(float.IsNegativeInfinity(extreme), Is.False, "The rig needs a real outer ear.");
                return edge;
            }
            string required = "GEO_FaceSurface";
            foreach (HeadContactSurface surface in surfaces)
                if (surface.Name == required) { selected = surface; break; }
            Assert.That(selected, Is.Not.Null, "Missing rendered " + required);
            if (feature == "nose") return ExtremeHeadContactPoint(selected, forward);
            float low = float.PositiveInfinity, high = float.NegativeInfinity;
            foreach (Vector3 point in selected.Vertices)
            { float height = Vector3.Dot(point - origin, up); low = Mathf.Min(low, height); high = Mathf.Max(high, height); }
            Vector3 cheek = Vector3.zero;
            float best = float.NegativeInfinity;
            foreach (Vector3 point in selected.Vertices)
            {
                float height = Mathf.InverseLerp(low, high, Vector3.Dot(point - origin, up));
                if (height < .35f || height > .65f) continue;
                float side = Vector3.Dot(point - origin, right);
                if (side > best) { best = side; cheek = point; }
            }
            Assert.That(float.IsNegativeInfinity(best), Is.False, "The face needs a real cheek row.");
            return cheek;
        }

        private static Vector3 ExtremeHeadContactPoint(HeadContactSurface surface, Vector3 axis)
        {
            Vector3 result = Vector3.zero;
            float extreme = float.NegativeInfinity;
            foreach (Vector3 point in surface.Vertices)
            {
                float projection = Vector3.Dot(point, axis);
                if (projection > extreme) { extreme = projection; result = point; }
            }
            return result;
        }

        private static Vector3 EmptyHeadSilhouetteCorner(List<HeadContactSurface> surfaces,
            Vector3 origin, Vector3 right, Vector3 up)
        {
            float rightEdge = float.NegativeInfinity, top = float.NegativeInfinity;
            foreach (HeadContactSurface surface in surfaces)
                foreach (Vector3 point in surface.Vertices)
                {
                    Vector3 offset = point - origin;
                    rightEdge = Mathf.Max(rightEdge, Vector3.Dot(offset, right));
                    top = Mathf.Max(top, Vector3.Dot(offset, up));
                }
            return origin + right * (rightEdge - .002f) + up * (top - .008f);
        }

        // This is a 2D silhouette oracle, not the runtime's 3D swept-triangle
        // solver. A projected gap bounds the distance from the entire bullet ray.
        private static float ProjectedHeadGap(List<HeadContactSurface> surfaces, Vector3 point, Vector3 right, Vector3 up)
        {
            Vector2 probe = Project(point);
            float squared = float.PositiveInfinity;
            foreach (HeadContactSurface surface in surfaces)
                for (int i = 0; i < surface.Triangles.Length; i += 3)
                {
                    Vector2 a = Project(surface.Vertices[surface.Triangles[i]]);
                    Vector2 b = Project(surface.Vertices[surface.Triangles[i + 1]]);
                    Vector2 c = Project(surface.Vertices[surface.Triangles[i + 2]]);
                    float ab = Cross(b - a, probe - a), bc = Cross(c - b, probe - b), ca = Cross(a - c, probe - c);
                    if (Mathf.Abs(Cross(b - a, c - a)) > .000000001f &&
                        (ab >= 0f && bc >= 0f && ca >= 0f || ab <= 0f && bc <= 0f && ca <= 0f)) return 0f;
                    squared = Mathf.Min(squared, SegmentGap(a, b), SegmentGap(b, c), SegmentGap(c, a));
                }
            return Mathf.Sqrt(squared);

            Vector2 Project(Vector3 value) => new Vector2(Vector3.Dot(value, right), Vector3.Dot(value, up));
            float SegmentGap(Vector2 a, Vector2 b)
            {
                Vector2 delta = b - a;
                float fraction = delta.sqrMagnitude > .000000000001f ? Mathf.Clamp01(Vector2.Dot(probe - a, delta) / delta.sqrMagnitude) : 0f;
                return (probe - a - delta * fraction).sqrMagnitude;
            }
            float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        }
    }
}

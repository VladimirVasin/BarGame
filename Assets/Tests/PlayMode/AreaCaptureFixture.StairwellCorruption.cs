using System.Collections;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        private static Shot[] CorruptedStairwellShots(StairwellInteriorRoot stairwell)
        {
            Assert.That(stairwell.Layout.IsCorrupted, Is.True);
            Assert.That(stairwell.Arrival, Is.EqualTo(StairwellArrivalKind.ApartmentDoor));
            Assert.That(stairwell.World.UpperBlocker, Is.Null);
            Assert.That(stairwell.World.Root.Find("Stairwell Dressing/Impassable Upper Debris"), Is.Null);
            Assert.That(stairwell.World.DescentWall, Is.Not.Null);
            Assert.That(stairwell.World.UpperLanding, Is.Not.Null);
            Assert.That(stairwell.World.Root.Find("Corrupted Upper Landing End Wall"), Is.Null);
            Assert.That(stairwell.World.Root.Find("Corrupted Endless Staircase").childCount, Is.EqualTo(12));
            Assert.That(stairwell.Atmosphere.UpperGlow, Is.Not.Null);
            Assert.That(stairwell.Atmosphere.UpperGlowHalo.IsVisible, Is.True);
            Physics.SyncTransforms();
            Vector3 glow = stairwell.Atmosphere.UpperGlow.transform.position;
            foreach (Vector3 eye in new[] { new Vector3(3.75f, 4.9f, -3.72f),
                         new Vector3(-3.70f, 5.45f, -4.18f) })
            {
                Vector3 from = stairwell.World.Root.TransformPoint(eye);
                Assert.That(Physics.Linecast(from, glow, out RaycastHit hit,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.False,
                    "The door view must see the glow through real shell openings; blocked by " +
                    (hit.collider != null ? hit.collider.name : "nothing"));
            }
            Assert.That(GameSessionState.HeroCorruptionLevel, Is.EqualTo(7));
            var hero = (Player3DCharacterPresentation)stairwell.Player.Visual;
            Assert.That(hero.CorruptionAppearance.AppliedLevel, Is.EqualTo(7));
            Transform room = stairwell.World.Root;
            StairwellCameraShot apartmentShot = StairwellFixedCameraController.CreateDefaultShots(stairwell.Layout)[2];
            Assert.That(Physics.Linecast(room.TransformPoint(apartmentShot.Position),
                room.TransformPoint(new Vector3(-1.45f, 23.9f, 48.2f)),
                out RaycastHit farHit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.False,
                "The actual apartment camera must see the distant treads, not an intervening ceiling: " +
                (farHit.collider != null ? farHit.collider.name : "nothing"));
            return new[]
            {
                Shot.At("corrupted-01-apartment-gameplay", room.TransformPoint(apartmentShot.Position),
                    room.TransformPoint(apartmentShot.Position + apartmentShot.Rotation * Vector3.forward * 4f),
                    apartmentShot.FieldOfView, delayFrames: 4),
                Shot.At("corrupted-02-sealed-descent", room.TransformPoint(new Vector3(1.45f, 4.95f, -4.18f)),
                    room.TransformPoint(new Vector3(1.45f, 4.95f, -2.88f)), 68f, delayFrames: 4),
                Shot.At("corrupted-03-open-upper-flight", room.TransformPoint(new Vector3(-1.45f, 5.05f, -4.05f)),
                    room.TransformPoint(new Vector3(-1.45f, 5.7f, 1.3f)), 70f, delayFrames: 4),
                Shot.At("corrupted-04-upper-gameplay", room.TransformPoint(new Vector3(-1.45f, 7.15f, 2.07f)),
                    room.TransformPoint(new Vector3(-1.45f, 7.15f, 2.07f) +
                        Quaternion.Euler(85f, 180f, 0f) * Vector3.forward * 4f), 100f, delayFrames: 4),
                Shot.At("corrupted-05-upper-placeholder", room.TransformPoint(new Vector3(-1.45f, 6.45f, .9f)),
                    glow, 70f, delayFrames: 4),
                Shot.At("corrupted-06-apartment-door-endless-glow", room.TransformPoint(new Vector3(3.75f, 4.9f, -3.72f)),
                    glow, 78f, delayFrames: 4)
            };
        }

        private static IEnumerator VerifyCorruptedStairwellRoute(StairwellInteriorRoot stairwell)
        {
            PlayerMotor motor = stairwell.Player.Motor;
            Transform hero = stairwell.Player.GameObject.transform;
            Transform room = stairwell.World.Root;
            motor.SetInputEnabled(false);
            // A real capsule must stop against the new wall before it can descend.
            yield return WalkStairwellWaypoint(stairwell, new Vector3(1.45f, 3.34f, -3.62f));
            for (int frame = 0; frame < 90 && !motor.InteractionPoseMoveStalled; frame++)
            {
                motor.MoveTowardsApproachWaypoint(room.TransformPoint(new Vector3(1.45f, 3.34f, -1.9f)), .025f, 1f / 60f);
                yield return null;
            }
            Assert.That(room.InverseTransformPoint(hero.position).z, Is.LessThan(-3.20f),
                "The descent is sealed by physical wall collision, not a prompt or invisible restriction.");
            motor.CancelInteractionPoseMove();
            yield return WalkStairwellWaypoint(stairwell, new Vector3(-1.45f, 3.34f, -3.72f));
            yield return WalkStairwellWaypoint(stairwell, new Vector3(-1.45f, 3.34f, -3.2f));
            yield return WalkStairwellWaypoint(stairwell, new Vector3(-1.45f, 4.94f, 1.35f));
            for (int frame = 0; frame < 8; frame++) yield return null;
            Assert.That(room.InverseTransformPoint(hero.position).y,
                Is.EqualTo(stairwell.Layout.UpperFlight.TopElevation + PlayerFactory.GroundedRootOffset).Within(.03f),
                "The ordinary hero capsule stands on the upper landing with clear headroom.");
            Assert.That(stairwell.FixedCamera.ActiveShotKind, Is.EqualTo(StairwellCameraShotKind.UpperLanding));
            yield return WalkStairwellWaypoint(stairwell, new Vector3(-1.45f, 4.94f, 1.78f));
            for (int frame = 0; frame < 4; frame++) yield return null;
            Camera camera = Camera.main;
            foreach (Vector3 point in new[] { hero.position - Vector3.up * PlayerFactory.GroundedRootOffset,
                         hero.position + Vector3.up * 1.65f })
            {
                Vector3 projected = camera.WorldToViewportPoint(point);
                Assert.That(projected.z, Is.GreaterThan(0f));
                Assert.That(projected.y, Is.InRange(.02f, .98f),
                    "The upper camera must retain head and feet at the far end of its supported landing.");
            }
            yield return CaptureHeroAppearanceFrame(camera, "corrupted-stairwell-upper-hero");
            Collider cap = stairwell.World.Root.Find("Corrupted Upper Temporary Grille").GetComponent<Collider>();
            Assert.That(cap.bounds.min.z, Is.EqualTo(2.2f).Within(.005f));
            for (int frame = 0; frame < 60 && !motor.InteractionPoseMoveStalled; frame++)
            {
                motor.MoveTowardsApproachWaypoint(room.TransformPoint(new Vector3(-1.45f, 4.94f, 2.8f)), .025f, 1f / 60f);
                yield return null;
            }
            Assert.That(room.InverseTransformPoint(hero.position).z, Is.LessThan(2f),
                "The placeholder end is solid and cannot lead into unsupported space.");
            motor.CancelInteractionPoseMove();
            // The prototype has a supported, enclosed end, with a continuous return to Home.
            yield return WalkStairwellWaypoint(stairwell, new Vector3(-1.45f, 3.34f, -3.72f));
            yield return WalkStairwellWaypoint(stairwell, stairwell.Layout.ApartmentDoorActionRoot);
            Assert.That(stairwell.ApartmentEntrance.isActiveAndEnabled, Is.True);
            // A street-side debug arrival remains in its lower compartment and can leave safely.
            motor.Teleport(room.TransformPoint(stairwell.Layout.StreetSpawn));
            yield return WalkStairwellWaypoint(stairwell, stairwell.Layout.StreetDoorActionRoot);
            Assert.That(stairwell.StreetExit.isActiveAndEnabled, Is.True);
            motor.CancelInteractionPoseMove();
            motor.SetInputEnabled(true);
            AssertDamageNeutralParity(stairwell);
        }

        private static void AssertDamageNeutralParity(StairwellInteriorRoot root)
        {
            Camera camera = Camera.main;
            Vector3 savedPosition = camera.transform.position;
            Quaternion savedRotation = camera.transform.rotation;
            float savedFov = camera.fieldOfView;
            var shot = StairwellFixedCameraController.CreateDefaultShots(root.Layout)[2];
            var renderers = root.World.Root.GetComponentsInChildren<MeshRenderer>();
            var blocks = new MaterialPropertyBlock[renderers.Length];
            var materials = new Material[renderers.Length];
            var scratch = new MaterialPropertyBlock();
            try
            {
                camera.transform.SetPositionAndRotation(shot.Position, shot.Rotation);
                camera.fieldOfView = shot.FieldOfView;
                for (int i = 0; i < renderers.Length; i++)
                {
                    blocks[i] = new MaterialPropertyBlock();
                    renderers[i].GetPropertyBlock(blocks[i]);
                    materials[i] = renderers[i].sharedMaterial;
                    renderers[i].GetPropertyBlock(scratch);
                    scratch.SetVector("_SurfaceDamageMode", Vector4.zero);
                    renderers[i].SetPropertyBlock(scratch);
                }
                Color32[] neutral = DamageParityPixels(camera);
                for (int i = 0; i < renderers.Length; i++)
                    if (materials[i].shader.name == "Bar Promenade/Surface Damage Lit")
                        renderers[i].sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
                Color32[] original = DamageParityPixels(camera);
                long error = 0;
                int different = 0;
                for (int i = 0; i < neutral.Length; i++)
                {
                    int delta = System.Math.Abs(neutral[i].r - original[i].r) +
                        System.Math.Abs(neutral[i].g - original[i].g) + System.Math.Abs(neutral[i].b - original[i].b);
                    error += delta;
                    if (delta > 30) different++;
                }
                Assert.That(error / (neutral.Length * 3d), Is.LessThan(3d),
                    "With damage disabled, the new full Lit path must preserve the original lighting and textures.");
                Assert.That(different / (double)neutral.Length, Is.LessThan(.04d),
                    "The new receiver must not introduce stripe/grid artifacts into ordinary surface shading.");
            }
            finally
            {
                for (int i = 0; i < renderers.Length; i++)
                    if (blocks[i] != null)
                    { renderers[i].sharedMaterial = materials[i]; renderers[i].SetPropertyBlock(blocks[i]); }
                camera.transform.SetPositionAndRotation(savedPosition, savedRotation);
                camera.fieldOfView = savedFov;
            }
        }

        private static Color32[] DamageParityPixels(Camera camera)
        {
            var target = new RenderTexture(Width, Height, 24);
            var pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            RenderTexture previousTarget = camera.targetTexture, previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
                pixels.Apply();
                return pixels.GetPixels32();
            }
            finally
            {
                camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
                target.Release(); Object.Destroy(target); Object.Destroy(pixels);
            }
        }

        private static IEnumerator WalkStairwellWaypoint(StairwellInteriorRoot stairwell, Vector3 local)
        {
            PlayerMotor motor = stairwell.Player.Motor;
            Vector3 target = stairwell.World.Root.TransformPoint(local);
            bool reached = false;
            for (int frame = 0; frame < 600 && !reached; frame++)
            {
                reached = motor.MoveTowardsApproachWaypoint(target, .025f, 1f / 60f);
                Assert.That(motor.InteractionPoseMoveStalled, Is.False,
                    "The real stairwell capsule must reach " + local + "; stopped at " + motor.transform.position);
                yield return null;
            }
            Assert.That(reached, Is.True, "The stairwell route must reach " + local);
            motor.CancelInteractionPoseMove();
        }
    }
}

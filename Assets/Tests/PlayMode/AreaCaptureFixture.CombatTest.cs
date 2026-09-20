using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using static BarPromenade.Tests.PlayMode.CombatTuning;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class CombatTestAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            Type.GetType("BarPromenade.Editor.ProjectSceneSetup, BarPromenade.Editor", true)
                .GetMethod("ConfigureCombatTestScene", Type.EmptyTypes).Invoke(null, null);
            Type.GetType("BarPromenade.Editor.Player3DV2AssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
            Type.GetType("BarPromenade.Editor.CombatTestAssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("The isolated melee arena and its real rig, weapon and contact poses.")]
        [PrebuildSetup(typeof(CombatTestAssetsSetup))]
        public IEnumerator CombatTest()
        {
            float previousCaptureDelta = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 60f;
            try { yield return CaptureCombatTest(); }
            finally { Time.captureDeltaTime = previousCaptureDelta; }
        }

        [UnityTest]
        [Explicit("The shoulder camera, charged strikes and damage. Run without -batchmode to capture the actual HUD too.")]
        [PrebuildSetup(typeof(CombatTestAssetsSetup))]
        public IEnumerator CombatTestLockedCamera()
        {
            float previousCaptureDelta = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 60f;
            var input = new InputTestFixture();
            Keyboard keyboard = null;
            try
            {
                input.Setup();
                keyboard = InputSystem.AddDevice<Keyboard>();
                yield return CaptureCombatTestLockedCamera(input, keyboard);
            }
            finally
            {
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                input.TearDown();
                Time.captureDeltaTime = previousCaptureDelta;
            }
        }

        private IEnumerator CaptureCombatTestLockedCamera(InputTestFixture input, Keyboard keyboard)
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest);
            var root = Object.FindAnyObjectByType<CombatTestRoot>();
            Assert.That(root, Is.Not.Null);
            root.AutomaticSimulation = false;
            root.SetSparring(false);
            Camera camera = root.CameraFollow.Camera;
            Assert.That(root.CameraFollow.enabled && root.CameraFollow.TargetLockActive, Is.True);
            // Every frame uses the active gameplay controller. No camera transform,
            // FOV or independent staged viewpoint is supplied by this fixture.
            for (int frame = 0; frame < 42; frame++) yield return null;
            CaptureCurrentCamera(camera, SceneIds.CombatTest, "camera-01-entry");

            CombatCapturePair(root);
            for (int frame = 0; frame < 42; frame++) yield return null;
            CaptureCurrentCamera(camera, SceneIds.CombatTest, "camera-02-close");

            input.Press(keyboard.dKey, queueEventOnly: true);
            for (int frame = 0; frame < 18; frame++) yield return null;
            CaptureCurrentCamera(camera, SceneIds.CombatTest, "camera-03-strafe");
            input.Release(keyboard.dKey, queueEventOnly: true);
            for (int frame = 0; frame < 12; frame++) yield return null;

            root.Hero.ResetActor(new Vector3(-2f, PlayerFactory.GroundedRootOffset, 1f),
                new Vector3(3f, 0f, -2f).normalized);
            root.Opponent.ResetActor(new Vector3(1f, PlayerFactory.GroundedRootOffset, -1f),
                new Vector3(-3f, 0f, 2f).normalized);
            Physics.SyncTransforms();
            for (int frame = 0; frame < 42; frame++) yield return null;
            CaptureCurrentCamera(camera, SceneIds.CombatTest, "camera-04-side");

            // The arena's imported central obstacle is immediately behind the
            // hero, so this records the real camera boom's collision response.
            root.Hero.ResetActor(new Vector3(3.85f, PlayerFactory.GroundedRootOffset, 1f), Vector3.right);
            root.Opponent.ResetActor(new Vector3(6f, PlayerFactory.GroundedRootOffset, 1f), Vector3.left);
            Physics.SyncTransforms();
            for (int frame = 0; frame < 42; frame++) yield return null;
            CaptureCurrentCamera(camera, SceneIds.CombatTest, "camera-05-wall");

            CombatCapturePair(root);
            for (int contact = 0; contact < HitsToDefeat; contact++)
            {
                root.Hero.ResetActor(root.Opponent.transform.position - Vector3.forward * 1.1f, Vector3.forward);
                Physics.SyncTransforms();
                CombatStrikeUntilContact(root.Hero, root.Opponent);
                if (!root.Opponent.State.IsDefeated) root.Tick(.8f);
            }
            root.Tick(.18f);
            Assert.That(root.Opponent.IsRagdollActive, Is.True);
            for (int frame = 0; frame < 240; frame++)
            {
                root.Tick(1f / 60f);
                yield return null;
            }
            CaptureCurrentCamera(camera, SceneIds.CombatTest, "camera-06-defeated");
            Assert.That(root.CameraFollow.TargetLockActive, Is.True);
            yield return CaptureCombatCharge(root, camera);
            yield return CaptureCombatDamage(root, camera);
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu);
        }

        private IEnumerator CaptureCombatTest()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest);
            var root = Object.FindAnyObjectByType<CombatTestRoot>();
            Assert.That(root, Is.Not.Null);
            root.AutomaticSimulation = false;
            for (int i = 0; i < 12; i++) yield return null;
            Camera camera = Camera.main;
            root.CameraFollow.enabled = false;
            root.Hero.ResetActor(new Vector3(0, .04f, 0), Vector3.forward);
            root.Opponent.ResetActor(new Vector3(0, .04f, 1.18f), Vector3.back);
            Physics.SyncTransforms();
            camera.transform.SetPositionAndRotation(new Vector3(3.2f, 2.0f, -2.7f),
                Quaternion.LookRotation(new Vector3(0f, 1.0f, .6f) - new Vector3(3.2f, 2.0f, -2.7f)));
            camera.fieldOfView = 52f;
            yield return null;
            CaptureCurrentCamera(camera, SceneIds.CombatTest, "01-ready");
            CaptureCombatGrip(camera, root.Hero, "grip-hero-ready");
            CaptureCombatGrip(camera, root.Opponent, "grip-opponent-ready");
            Assert.That(root.Hero.TryAttack(), Is.True);
            root.Hero.Step(.42f);
            yield return null;
            CaptureCurrentCamera(camera, SceneIds.CombatTest, "02-windup");
            CaptureCombatGrip(camera, root.Hero, "grip-hero-windup");
            root.Hero.Step(.14f);
            yield return null;
            CaptureCurrentCamera(camera, SceneIds.CombatTest, "03-contact");
            CaptureCombatGrip(camera, root.Hero, "grip-hero-contact");
            CaptureCombatGrip(camera, root.Opponent, "grip-opponent-hit");
            root.Hero.Step(1f);
            root.Hero.SetBlock(true); root.Hero.Present();
            yield return null;
            CaptureCurrentCamera(camera, SceneIds.CombatTest, "04-guard");
            CaptureCombatGrip(camera, root.Hero, "grip-hero-guard");
            root.Hero.SetBlock(false);

            CombatCapturePair(root);
            root.Hero.SetBlock(true);
            CombatStrikeUntilContact(root.Opponent, root.Hero);
            root.Hero.Step(.1f);
            Assert.That(((Player3DCharacterPresentation)root.Player.Visual).ActiveClipName,
                Is.EqualTo("CombatGuardImpact"));
            yield return null;
            CaptureCurrentCamera(camera, SceneIds.CombatTest, "05-guard-impact");
            CaptureCombatGrip(camera, root.Opponent, "grip-opponent-contact");
            CaptureCombatGrip(camera, root.Hero, "grip-hero-guard-impact");

            // Keep guard held so the target cannot regenerate between the real blocked contacts that empty its meter.
            for (int contact = 1; contact <= AffordableBlocks; contact++)
            {
                root.Hero.Step(.3f);
                root.Opponent.ResetActor(new Vector3(0f, .04f, 1.1f), Vector3.back);
                Physics.SyncTransforms();
                CombatStrikeUntilContact(root.Opponent, root.Hero);
            }
            Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.GuardBroken));
            root.Hero.Step(.12f);
            Assert.That(((Player3DCharacterPresentation)root.Player.Visual).ActiveClipName,
                Is.EqualTo("CombatGuardBreak"));
            yield return null;
            CaptureCurrentCamera(camera, SceneIds.CombatTest, "06-guard-break");

            CombatCapturePair(root);
            // The imported arena obstacle is visible in the recoil frame and owns its collision.
            root.Hero.ResetActor(new Vector3(2f, .04f, 1f), Vector3.right);
            root.Opponent.ResetActor(new Vector3(4.1f, .04f, 1f), Vector3.left);
            camera.transform.SetPositionAndRotation(new Vector3(-.5f, 2f, -2f),
                Quaternion.LookRotation(new Vector3(2.6f, 1f, 1f) - new Vector3(-.5f, 2f, -2f)));
            Physics.SyncTransforms();
            Assert.That(root.Hero.TryAttack(), Is.True);
            for (int step = 0; step < ContactTicks(120f) + 14 && root.Hero.State.Phase != MeleePhase.Recovery; step++)
                root.Hero.Step(1f / 120f);
            Assert.That(((Player3DCharacterPresentation)root.Player.Visual).ActiveClipName,
                Is.EqualTo("CombatRecoil"));
            Assert.That(root.Opponent.State.Health, Is.EqualTo(S.MaxHealth));
            root.Hero.Step(.12f);
            yield return null;
            CaptureCurrentCamera(camera, SceneIds.CombatTest, "07-wall-recoil");

            camera.transform.SetPositionAndRotation(new Vector3(3.2f, 2.0f, -2.7f),
                Quaternion.LookRotation(new Vector3(0f, .8f, .6f) - new Vector3(3.2f, 2.0f, -2.7f)));
            foreach (bool defeatHero in new[] { false, true })
            {
                CombatCapturePair(root);
                CombatActor target = defeatHero ? root.Hero : root.Opponent;
                CombatActor attacker = defeatHero ? root.Opponent : root.Hero;
                for (int contact = 0; contact < HitsToDefeat; contact++)
                {
                    Vector3 facing = defeatHero ? Vector3.back : Vector3.forward;
                    attacker.ResetActor(target.transform.position - facing * 1.1f, facing);
                    Physics.SyncTransforms();
                    CombatStrikeUntilContact(attacker, target);
                    if (!target.State.IsDefeated) root.Tick(.8f);
                }
                Assert.That(target.State.IsDefeated, Is.True);
                root.Tick(.08f);
                Assert.That(target.ActiveClipName, Is.EqualTo("CombatDefeat"));
                Assert.That(target.IsRagdollActive, Is.False);
                yield return null;
                CaptureCurrentCamera(camera, SceneIds.CombatTest, defeatHero ? "10-hero-defeat" : "08-opponent-defeat");
                root.Tick(.1f);
                Assert.That(target.IsRagdollActive, Is.True);
                Assert.That(target.GetComponentInChildren<NpcHandPose>().RightGripWeight, Is.Zero,
                    "The defeated hand must release the dropped crowbar.");
                for (int frame = 0; frame < 300 && !target.Ragdoll.IsSettled; frame++) yield return null;
                yield return null; // Skinning must see the physical bones before the camera renders.
                CaptureCurrentCamera(camera, SceneIds.CombatTest, defeatHero ? "11-hero-ragdoll" : "09-opponent-ragdoll");
            }
            root.ResetRound();
            yield return null;
            AssertCombatGrip(root.Hero);
            AssertCombatGrip(root.Opponent);
            camera.transform.SetPositionAndRotation(new Vector3(9f, 8f, -10f),
                Quaternion.LookRotation(new Vector3(0f, .5f, 0f) - new Vector3(9f, 8f, -10f)));
            camera.fieldOfView = 60f;
            yield return null;
            CaptureCurrentCamera(camera, SceneIds.CombatTest, "00-arena");
            root.Hero.enabled = false;
            root.Opponent.enabled = false;
            Assert.That(root.Hero.GetComponentInChildren<NpcHandPose>().RightGripWeight, Is.Zero);
            Assert.That(root.Opponent.GetComponentInChildren<NpcHandPose>().RightGripWeight, Is.Zero);
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu);
        }

        private static NpcHandPose AssertCombatGrip(CombatActor actor)
        {
            if (actor.IsHero)
            {
                // Batch coroutines resume before LateUpdate; measure the same
                // final two-hand pose that the gameplay camera will render.
                var visual = actor.GetComponentInChildren<Player3DCharacterPresentation>();
                Assert.That(visual, Is.Not.Null);
                visual.ReapplyLatePresentationPose();
            }
            NpcHandPose pose = actor.GetComponentInChildren<NpcHandPose>();
            Assert.That(pose, Is.Not.Null, actor.name);
            Assert.That(pose.RightGripWeight, Is.EqualTo(1f), actor.name);
            Assert.That(pose.LeftGripWeight, Is.InRange(actor.SupportGripWeight * .70f, actor.SupportGripWeight + .001f),
                "The support fingers loosen for a regrip and release completely at round end.");
            if (actor.SupportGripWeight > .99f)
                Assert.That(Vector3.Distance(pose.CylinderCentre(true), actor.SupportGripWorldPosition),
                    Is.LessThan(.012f), "The support hand must remain on the visible shaft.");
            Vector3 centre = pose.CylinderCentre(false), axis = pose.CylinderAxis(false);
            Assert.That(Vector3.Distance(CombatAssetProvider.FindAnchor(actor.Weapon, "Grip").position, centre),
                Is.LessThan(.001f), "The rubber handle must run through the closed fingers, not the neutral socket.");
            Assert.That(Vector3.Dot(actor.Weapon.transform.up, axis), Is.GreaterThan(.999f));
            var mesh = new Mesh();
            try
            {
                int fingers = 0, thumbs = 0;
                foreach (NpcHandPose.HandBinding hand in pose.Hands)
                {
                    if (hand.IsLeft) continue;
                    foreach (SkinnedMeshRenderer renderer in hand.Renderers)
                    {
                        bool finger = renderer.name.Contains("Finger"), thumb = renderer.name.Contains("Thumb");
                        if (!renderer.enabled || !renderer.gameObject.activeInHierarchy || !(finger || thumb)) continue;
                        renderer.BakeMesh(mesh, true);
                        float minimumRadius = float.PositiveInfinity, farSide = float.NegativeInfinity;
                        foreach (Vector3 vertex in mesh.vertices)
                        {
                            Vector3 offset = renderer.transform.TransformPoint(vertex) - centre;
                            minimumRadius = Mathf.Min(minimumRadius, Vector3.ProjectOnPlane(offset, axis).magnitude);
                            farSide = Mathf.Max(farSide, Vector3.Dot(offset, pose.PalmNormal(false)));
                        }
                        Assert.That(minimumRadius, Is.InRange(.019f, .036f), renderer.name + " must touch the handle surface.");
                        Assert.That(farSide, Is.GreaterThan(.004f), renderer.name + " must curl around the far side.");
                        if (finger) fingers++;
                        else thumbs++;
                    }
                }
                Assert.That(fingers, Is.EqualTo(4), "All four visible fingers must wrap the handle.");
                Assert.That(thumbs, Is.EqualTo(1), "The visible thumb must close on the other side of the handle.");
            }
            finally { Object.DestroyImmediate(mesh); }
            return pose;
        }

        private static void CaptureCombatGrip(Camera camera, CombatActor actor, string name)
        {
            NpcHandPose pose = AssertCombatGrip(actor);
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float fov = camera.fieldOfView, near = camera.nearClipPlane;
            try
            {
                Vector3 axis = pose.CylinderAxis(false), normal = pose.PalmNormal(false);
                Vector3 target = pose.CylinderCentre(false) + axis * .045f;
                Vector3 eye = target + normal * .48f + Vector3.Cross(axis, normal) * .20f + axis * .10f;
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, axis));
                camera.fieldOfView = 42f;
                camera.nearClipPlane = .02f;
                CaptureCurrentCamera(camera, SceneIds.CombatTest, name);
            }
            finally
            {
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fov;
                camera.nearClipPlane = near;
            }
        }

        private static void CombatCapturePair(CombatTestRoot root)
        {
            root.SetSparring(false);
            root.Hero.ResetActor(new Vector3(0f, .04f, 0f), Vector3.forward);
            root.Opponent.ResetActor(new Vector3(0f, .04f, 1.1f), Vector3.back);
            Physics.SyncTransforms();
        }

        private static void CombatStrikeUntilContact(CombatActor attacker, CombatActor target)
        {
            float health = target.State.Health, stamina = target.State.Stamina;
            Assert.That(attacker.TryAttack(), Is.True);
            for (int step = 0; step < ContactTicks(120f) + 14 && target.State.Health == health && target.State.Stamina == stamina; step++)
                attacker.Step(1f / 120f);
            Assert.That(target.State.Health != health || target.State.Stamina != stamina, Is.True,
                "A reaction capture must be caused by contact from the actual authored weapon.");
        }
    }
}

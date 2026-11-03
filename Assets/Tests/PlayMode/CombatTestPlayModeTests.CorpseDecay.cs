using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
        [Category("CurrentCombat")]
        [UnityTest]
        public IEnumerator Warehouse_CorpseDecayPreservesDeathAndPoolLifecycle()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.WarehouseSeedForTests = 0;
            var pacing = new CombatEncounterSettings(3, .05f, .05f, 3f, 3f);
            Assert.That(CombatTestStartService.TryStart(new CombatTestSelection(
                new CombatLoadoutSelection(CombatWeaponId.Pistol), CombatTestMode.Endless, pacing)), Is.True);
            yield return AwaitSelectedCombatRange();
            root.SendMessage("OnApplicationFocus", true);
            GameInput.HandleApplicationFocus(true);
            Assert.That(root.CorpseDecay, Is.Not.Null);

            MethodInfo arrive = typeof(CombatTestRoot).GetMethod("AdvanceEndlessInterval",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo suppress = typeof(CombatTestRoot).GetMethod("SetRoomOpponentSuppression",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(arrive, Is.Not.Null);
            Assert.That(suppress, Is.Not.Null);
            Assert.That(root.LiveOpponentCount, Is.EqualTo(3),
                "The warehouse starts with its prepared population already placed.");
            // Keep ordinary decisions out of this presentation regression; the
            // real death, physics, arrival, input and cleanup owners stay active.
            suppress.Invoke(root, new object[] { true });
            CombatActor first = root.Opponents[0], second = root.Opponents[1];
            Assert.That(first.IsRoom && second.IsDocker, Is.True);
            root.Hero.ResetActor(new Vector3(0f, PlayerFactory.GroundedRootOffset, 1f), Vector3.back);
            first.ResetActor(new Vector3(-1.2f, PlayerFactory.GroundedRootOffset, -2f), Vector3.forward);
            second.ResetActor(new Vector3(1.2f, PlayerFactory.GroundedRootOffset, -2f), Vector3.forward);
            root.Opponents[2].ResetActor(new Vector3(9f, PlayerFactory.GroundedRootOffset, -7f), Vector3.left);
            Physics.SyncTransforms();
            foreach (CombatActor target in new[] { first, second })
            {
                DefeatBodyTargetWithProjectile(target, root.Hero, 510);
                if (!target.State.IsDefeated) DefeatBodyTargetWithProjectile(target, root.Hero, 511);
                Assert.That(target.State.IsDefeated && target.IsRagdollActive, Is.True);
            }
            ApplyKnownBodyContact(first, root.Hero, BodyDamageRegion.LeftForearm, 0, 2f, 512);
            Assert.That(root.BodyEffects.DetachedPartCountFor(first), Is.GreaterThan(0));
            root.Tick(0f);
            Assert.That((bool)arrive.Invoke(root, new object[] { root.NextOpponentSeconds + .01f }), Is.False,
                "An arrival must defer instead of deleting a body while its terminal fall is moving.");
            Assert.That(first.gameObject.activeInHierarchy && first.State.IsDefeated, Is.True);

            Transform snapshot = null;
            float progress = -1f;
            for (int frame = 0; frame < 480; frame++)
            {
                root.Tick(Time.fixedDeltaTime);
                if (root.CorpseDecay.TryGetSnapshot(first, out snapshot, out progress)) break;
                yield return new WaitForFixedUpdate();
            }
            Assert.That(snapshot, Is.Not.Null, "The settled, damaged body must survive pool reuse as independent remains.");
            Assert.That(progress, Is.Zero.Within(.001f), "The complete silhouette retains its quiet hold after handoff.");
            Assert.That(snapshot.IsChildOf(first.transform), Is.False);
            MeshRenderer[] surfaces = snapshot.GetComponentsInChildren<MeshRenderer>();
            Assert.That(surfaces, Is.Not.Empty);
            foreach (MeshRenderer surface in surfaces)
                foreach (Material material in surface.sharedMaterials)
                    Assert.That(material.HasProperty("_CorpseDecay"), Is.True,
                        "Clothes, anatomy and detached surfaces all need the same opaque clipping contract.");
            Bounds heldBounds = CorpseDecayBounds(surfaces);
            var surfaceDescription = new System.Text.StringBuilder();
            foreach (MeshRenderer surface in surfaces)
                if (surface.bounds.size.magnitude > 8f)
                    surfaceDescription.AppendLine(surface.name + ": world=" + surface.bounds + " scale=" + surface.transform.lossyScale +
                        " mesh=" + surface.GetComponent<MeshFilter>().sharedMesh.bounds);
            Assert.That(heldBounds.size.magnitude, Is.InRange(.5f, 8f),
                "Handoff must preserve actual imported scale and the damaged body's footprint. " + surfaceDescription);
            CaptureCorpseDecay(heldBounds, "01-held");

            for (int retry = 0; retry < 3 && first.State.IsDefeated; retry++)
                arrive.Invoke(root, new object[] { root.NextOpponentSeconds + .01f });
            Assert.That(first.State.IsDefeated, Is.False, "The old actor slot can be reused while its remains stay visible.");
            Assert.That(root.CorpseDecay.TryGetSnapshot(first, out Transform retained, out progress), Is.True);
            Assert.That(retained, Is.SameAs(snapshot));
            Assert.That(Vector3.Distance(CorpseDecayBounds(surfaces).center, heldBounds.center), Is.LessThan(.001f),
                "Resetting the source rig cannot move its frozen remains or share actor-owned baked meshes.");

            AdvanceDuel(WarehouseCorpseDecay.HoldSeconds + WarehouseCorpseDecay.DissolveSeconds * .5f, .1f);
            Assert.That(root.CorpseDecay.TryGetSnapshot(first, out retained, out progress), Is.True);
            Assert.That(progress, Is.InRange(.45f, .55f));
            CaptureCorpseDecay(heldBounds, "02-breaking");
            float heldProgress = progress;
            Assert.That(root.PauseMenu.Open(), Is.True);
            root.Tick(4f);
            yield return null;
            Assert.That(root.CorpseDecay.TryGetSnapshot(first, out retained, out progress), Is.True);
            Assert.That(progress, Is.EqualTo(heldProgress).Within(.0001f), "Pause holds the one material/particle clock.");
            Assert.That(root.PauseMenu.Cancel(), Is.True);
            yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Corpse decay pause did not release input.");
            root.SendMessage("OnApplicationFocus", true);
            GameInput.HandleApplicationFocus(true);

            AdvanceDuel(WarehouseCorpseDecay.DissolveSeconds * .5f + .05f, .1f);
            Assert.That(root.CorpseDecay.TryGetSnapshot(first, out retained, out progress), Is.True);
            Assert.That(progress, Is.EqualTo(1f).Within(.001f));
            CaptureCorpseDecay(heldBounds, "03-tail");
            AdvanceDuel(WarehouseCorpseDecay.TailSeconds + .1f, .1f);
            Assert.That(root.CorpseDecay.TryGetSnapshot(first, out _, out _), Is.False);
            Assert.That(root.CorpseDecay.ActiveCount, Is.LessThanOrEqualTo(WarehouseCorpseDecay.Capacity));
            CaptureCorpseDecay(heldBounds, "04-cleared");
            root.ResetRound();
            Assert.That(root.CorpseDecay.ActiveCount, Is.Zero, "Reset clears all retained surfaces, particles and audio.");
            Assert.That(root.BodyEffects.ActiveFragmentCount, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }

        private static Bounds CorpseDecayBounds(MeshRenderer[] surfaces)
        {
            var bounds = surfaces[0].bounds;
            for (int i = 1; i < surfaces.Length; i++) bounds.Encapsulate(surfaces[i].bounds);
            return bounds;
        }

        private void CaptureCorpseDecay(Bounds body, string shot)
        {
            Camera camera = root.CameraFollow.Camera;
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float fov = camera.fieldOfView;
            try
            {
                Vector3 focus = body.center;
                Vector3 eye = focus + new Vector3(-2.5f, 1.6f, 2.5f);
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
                camera.fieldOfView = 44f;
                const string folder = "CombatTest/corpse-decay";
                string path = Path.Combine(Directory.GetCurrentDirectory(), "Captures", folder, shot + ".png");
                LogAssert.Expect(LogType.Log, "Area capture wrote " + path);
                AreaCaptureFixture.CaptureCurrentCamera(camera, folder, shot);
            }
            finally { camera.transform.SetPositionAndRotation(position, rotation); camera.fieldOfView = fov; }
        }
    }
}

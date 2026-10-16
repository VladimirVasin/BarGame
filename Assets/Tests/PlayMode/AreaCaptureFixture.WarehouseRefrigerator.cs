using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Focused guided refrigerator entry and the real chest beam, with open-cavity captures.")]
        public IEnumerator CombatWarehouseRefrigeratorEntryLightsCavity()
        {
            CombatTestRoot root = null;
            WarehouseRefrigeratorPresentationProbe probe = null;
            float previousDelta = Time.captureDeltaTime;
            bool previousBackground = Application.runInBackground;
            var input = new InputTestFixture();
            input.Setup();
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            Time.captureDeltaTime = 1f / 60f;
            Application.runInBackground = true;
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.WarehouseSeedForTests = 0;
            GameSessionState.BeginNewGame();
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest, LoadSceneMode.Single);
                root = Object.FindAnyObjectByType<CombatTestRoot>();
                Assert.That(root != null && root.IsInitialized, Is.True);
                probe = root.gameObject.AddComponent<WarehouseRefrigeratorPresentationProbe>();
                root.AutomaticSimulation = false;
                root.SetSparring(false);
                root.SetDuelLogging(false);
                root.SendMessage("OnApplicationFocus", true);
                GameInput.HandleApplicationFocus(true);
                for (int frame = 0; frame < SettleFrames; frame++) yield return null;
                WarehouseRefrigerator[] fridges = root.GetComponentsInChildren<WarehouseRefrigerator>();
                Array.Sort(fridges, (a, b) => a.Floor.CompareTo(b.Floor));
                Assert.That(fridges.Length, Is.EqualTo(3));
                Light source = root.Flashlight.SpotLight;
                foreach (WarehouseRefrigerator fridge in fridges)
                {
                    Light[] existingLights = root.GetComponentsInChildren<Light>(true);
                    Vector3 dock = fridge.transform.TransformPoint(fridge.Plan.ApproachPosition);
                    Vector3 localOffset = new Vector3(fridge.Floor == 1 ? -.32f : .32f, 0f, -.20f);
                    Vector3 start = dock + fridge.transform.TransformVector(localOffset);
                    float turn = fridge.Floor == 0 ? 180f : fridge.Floor == 1 ? 90f : -90f;
                    Vector3 facing = Quaternion.AngleAxis(turn, fridge.transform.up) * fridge.transform.forward;
                    root.Hero.ResetActor(start, facing);
                    Physics.SyncTransforms();
                    root.CameraFollow.ClearFixedPose();
                    root.CameraFollow.Snap();
                    input.Set(mouse.position, root.CameraFollow.Camera.pixelRect.center);
                    yield return null;
                    Assert.That(fridge.BeginInteraction(), Is.True);
                    Assert.That(fridge.Interaction.OwnsInteraction && fridge.Interaction.IsPositioning, Is.True);
                    if (fridge.Floor == 1)
                    {
                        fridge.gameObject.SetActive(false);
                        yield return null;
                        AssertWarehouseFridgeReleased(root);
                        Assert.That(fridge.Interaction.IsPositioning, Is.False,
                            "Disabling during guided entry must release the unfinished dock owner.");
                        fridge.gameObject.SetActive(true);
                        yield return null;
                        Assert.That(fridge.BeginInteraction(), Is.True);
                    }
                    Assert.That(Vector3.Distance(root.Hero.transform.position, start), Is.LessThan(.005f),
                        "Opening must guide the visible hero rather than immediately snapping to the cabinet.");
                    input.Press(keyboard.dKey, queueEventOnly: true);
                    Vector3 previousPosition = root.Hero.transform.position;
                    Quaternion previousRotation = root.Hero.transform.rotation;
                    bool aimedBeforeBrowsing = false, checkedOpeningBeam = false;
                    for (int frame = 0; frame < 600 && fridge.Interaction.Phase != HomeRefrigeratorInteractionPhase.Inspecting; frame++)
                    {
                        Assert.That(fridge.Interaction.OwnsInteraction, Is.True, "The reachable approach must retain its modal owner.");
                        Assert.That(root.Player.Motor.InputEnabled || root.Player.Interactor.InputEnabled, Is.False);
                        yield return null;
                        Assert.That(Vector3.Distance(previousPosition, root.Hero.transform.position), Is.LessThan(.15f),
                            "The shared motor must approach continuously despite held manual movement.");
                        Assert.That(Quaternion.Angle(previousRotation, root.Hero.transform.rotation), Is.LessThan(30f),
                            "The entry turn must remain visible rather than snapping from the initial facing.");
                        previousPosition = root.Hero.transform.position;
                        previousRotation = root.Hero.transform.rotation;
                        if (root.Flashlight.HasInspectionAim &&
                            fridge.Interaction.Phase != HomeRefrigeratorInteractionPhase.Inspecting)
                            aimedBeforeBrowsing = true;
                        if (!checkedOpeningBeam && fridge.Interaction.Phase == HomeRefrigeratorInteractionPhase.Opening &&
                            fridge.View.DoorOpenAmount >= .98f)
                        {
                            yield return AtWarehouseFridgePresentation(probe, () =>
                            {
                                CaptureCurrentCamera(root.CameraFollow.Camera, SceneIds.CombatTest,
                                    "Test warehouse - Refrigerator beam during opening on floor " + (fridge.Floor + 1));
                                AssertWarehouseFridgeBeam(root, fridge, source);
                            });
                            checkedOpeningBeam = true;
                        }
                    }
                    input.Release(keyboard.dKey, queueEventOnly: true);
                    yield return null;
                    Assert.That(fridge.Interaction.Phase, Is.EqualTo(HomeRefrigeratorInteractionPhase.Inspecting));
                    Assert.That(fridge.Interaction.IsPositioning, Is.False);
                    Assert.That(aimedBeforeBrowsing && checkedOpeningBeam, Is.True,
                        "The real beam must already light the cavity while its door finishes opening.");
                    Assert.That(Vector3.Distance(root.Hero.transform.position, dock), Is.LessThan(.015f));
                    Assert.That(Vector3.Angle(root.Hero.transform.forward, fridge.transform.forward), Is.LessThan(.5f),
                        "Away-facing and sideways entries must settle at the same authored cabinet facing.");
                    CollectionAssert.AreEquivalent(existingLights, root.GetComponentsInChildren<Light>(true),
                        "Opening a refrigerator must retain the existing physical lights.");
                    yield return AtWarehouseFridgePresentation(probe, () =>
                    {
                        AssertWarehouseFridgeBeam(root, fridge, source);
                        AssertWarehouseFridgeIlluminated(root.CameraFollow.Camera, fridge, source);
                        CaptureCurrentCamera(root.CameraFollow.Camera, SceneIds.CombatTest,
                            "Test warehouse - Guided refrigerator light on floor " + (fridge.Floor + 1));
                    });
                    if (fridge.Floor == 0)
                    {
                        yield return VerifyWarehouseFridgeCursorLag(root, fridge, source, input, mouse, probe);
                        HomeRefrigeratorItemInspectionController nested = fridge.Interaction.ItemInspection;
                        Assert.That(nested.TryBeginInspection(fridge.View.Items[0]), Is.True);
                        yield return AwaitWarehouseFridgeCondition(() => nested.IsInspecting,
                            "The nested can inspection did not settle.");
                        Vector3 heldTarget = default;
                        Quaternion heldBeam = default;
                        yield return AtWarehouseFridgePresentation(probe, () =>
                        {
                            heldTarget = root.Flashlight.InspectionAimTarget;
                            heldBeam = source.transform.rotation;
                        });
                        input.Set(mouse.position, root.CameraFollow.Camera.pixelRect.center + Vector2.left * 140f);
                        InputSystem.Update();
                        yield return AtWarehouseFridgePresentation(probe, () =>
                        {
                            Assert.That(root.Flashlight.InspectionAimTarget, Is.EqualTo(heldTarget));
                            Assert.That(Quaternion.Angle(heldBeam, source.transform.rotation), Is.LessThan(.01f),
                                "Nested item inspection keeps the accepted chest beam instead of following its cursor.");
                        });
                        Assert.That(nested.RequestReturn(), Is.True);
                        yield return AwaitWarehouseFridgeCondition(() => !nested.IsActive,
                            "The nested can did not return to its refrigerator shelf.");
                    }
                    if (fridge.Floor == 2)
                    {
                        fridge.gameObject.SetActive(false);
                        yield return null;
                        AssertWarehouseFridgeReleased(root);
                        fridge.gameObject.SetActive(true);
                    }
                    else
                    {
                        Assert.That(fridge.Interaction.RequestClose(), Is.True);
                        yield return AwaitWarehouseFridgeCondition(() => !fridge.Interaction.OwnsInteraction,
                            "The refrigerator close did not release its owner.");
                        yield return null;
                        AssertWarehouseFridgeReleased(root);
                        if (fridge.Floor == 0)
                        {
                            // With no approach distance, the neutral handoff must
                            // also clear a freshly restored combat/recovery pose.
                            root.Hero.SetCombatFocused(true);
                            root.Hero.Present();
                            input.Set(mouse.position, root.CameraFollow.Camera.pixelRect.center);
                            Assert.That(fridge.BeginInteraction(), Is.True);
                            yield return AwaitWarehouseFridgeCondition(
                                () => fridge.Interaction.Phase == HomeRefrigeratorInteractionPhase.Inspecting,
                                "The exact-dock reopen did not finish its neutral handoff.");
                            yield return null;
                            Assert.That(Vector3.Distance(root.Hero.transform.position, dock), Is.LessThan(.015f));
                            yield return AtWarehouseFridgePresentation(probe, () =>
                            {
                                AssertWarehouseFridgeBeam(root, fridge, source);
                                AssertWarehouseFridgeIlluminated(root.CameraFollow.Camera, fridge, source);
                                CaptureCurrentCamera(root.CameraFollow.Camera, SceneIds.CombatTest,
                                    "Test warehouse - Refrigerator light after immediate reopen");
                            });
                            Assert.That(fridge.Interaction.RequestClose(), Is.True);
                            yield return AwaitWarehouseFridgeCondition(() => !fridge.Interaction.OwnsInteraction,
                                "The immediate reopen did not release its owner.");
                            yield return null;
                            AssertWarehouseFridgeReleased(root);
                        }
                    }
                }
            }
            finally
            {
                if (probe != null)
                {
                    probe.CancelPending();
                    Object.Destroy(probe);
                }
                if (root != null)
                {
                    foreach (WarehouseRefrigerator fridge in root.GetComponentsInChildren<WarehouseRefrigerator>(true))
                        fridge.Interaction?.CancelInteraction();
                    root.AutomaticSimulation = false;
                }
                input.TearDown();
                Time.captureDeltaTime = previousDelta;
                Application.runInBackground = previousBackground;
                CombatTestRoot.UseLegacyArenaForTests = false;
                CombatTestRoot.WarehouseSeedForTests = null;
            }
        }

        private static IEnumerator AtWarehouseFridgePresentation(WarehouseRefrigeratorPresentationProbe probe, Action sample)
        {
            probe.Schedule(sample);
            for (int frame = 0; frame < 60 && !probe.Completed; frame++) yield return null;
            Assert.That(probe.Completed, Is.True, "The refrigerator's final presentation probe did not run.");
            if (probe.Failure != null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(probe.Failure).Throw();
        }

        private static IEnumerator VerifyWarehouseFridgeCursorLag(CombatTestRoot root, WarehouseRefrigerator fridge,
            Light source, InputTestFixture input, Mouse mouse, WarehouseRefrigeratorPresentationProbe probe)
        {
            Quaternion before = default;
            Vector3 target = fridge.View.InteriorRoot.TransformPoint(new Vector3(.30f, .10f, 0f));
            Vector2 pointer = default;
            yield return AtWarehouseFridgePresentation(probe, () =>
            {
                before = source.transform.rotation;
                Vector3 screen = root.CameraFollow.Camera.WorldToScreenPoint(target);
                pointer = new Vector2(screen.x, screen.y);
            });
            input.Set(mouse.position, pointer);
            InputSystem.Update();
            float firstStep = 0f;
            yield return AtWarehouseFridgePresentation(probe, () =>
            {
                Assert.That(Vector3.Distance(root.Flashlight.InspectionAimTarget, target), Is.LessThan(.025f));
                firstStep = Quaternion.Angle(before, source.transform.rotation);
                Assert.That(firstStep, Is.GreaterThan(.001f), "The next real presentation frame begins following the cursor.");
            });
            for (int frame = 0; frame < 40; frame++) yield return null;
            Vector3 settledTarget = default;
            Quaternion settledBeam = default;
            yield return AtWarehouseFridgePresentation(probe, () =>
            {
                Assert.That(Quaternion.Angle(before, source.transform.rotation), Is.GreaterThan(firstStep + .05f),
                    "The physical beam approaches the pointer over later frames rather than snapping on its first frame.");
                settledTarget = root.Flashlight.InspectionAimTarget;
                settledBeam = source.transform.rotation;
            });
            using (GameTimeScaleRuntime.AcquirePause())
            {
                input.Set(mouse.position, pointer + Vector2.left * 140f);
                InputSystem.Update();
                yield return AtWarehouseFridgePresentation(probe, () =>
                {
                    Assert.That(root.Flashlight.InspectionAimTarget, Is.EqualTo(settledTarget));
                    Assert.That(Quaternion.Angle(settledBeam, source.transform.rotation), Is.LessThan(.01f),
                        "Pause retains the final presented beam while the pointer moves.");
                });
            }
        }

        private static void AssertWarehouseFridgeBeam(CombatTestRoot root, WarehouseRefrigerator fridge, Light source)
        {
            Assert.That(root.Flashlight.HasInspectionAim, Is.True);
            Assert.That(root.Flashlight.SpotLight, Is.SameAs(source));
            Assert.That(source.transform, Is.SameAs(root.Flashlight.Lens));
            Assert.That(source.transform.IsChildOf(root.Hero.transform), Is.True);
            Assert.That(source.isActiveAndEnabled && source.intensity > 0f, Is.True);
            Vector3 target = fridge.View.InteriorRoot.position;
            Vector3 ray = target - source.transform.position;
            Assert.That(Vector3.Angle(source.transform.forward, ray), Is.LessThan(source.innerSpotAngle * .5f),
                "The actual cavity centre must be inside the bright part of the physical chest beam. " +
                WarehouseFridgeBeamDiagnostic(root, fridge, source, target));
            Vector3 localSource = fridge.transform.InverseTransformPoint(source.transform.position);
            Vector3 localTarget = fridge.transform.InverseTransformPoint(target);
            float front = fridge.Plan.CavityCenterLocal.z - fridge.Plan.CavitySize.z * .5f;
            float fraction = (front - localSource.z) / (localTarget.z - localSource.z);
            Assert.That(fraction, Is.InRange(0f, 1f), "The chest light must approach through the cabinet's open front.");
            Vector3 entrance = Vector3.Lerp(localSource, localTarget, fraction) - fridge.Plan.CavityCenterLocal;
            Assert.That(Mathf.Abs(entrance.x), Is.LessThan(fridge.Plan.CavitySize.x * .5f - .02f));
            Assert.That(Mathf.Abs(entrance.y), Is.LessThan(fridge.Plan.CavitySize.y * .5f - .02f));
            Physics.SyncTransforms();
            foreach (RaycastHit hit in Physics.RaycastAll(source.transform.position, ray.normalized, ray.magnitude,
                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                // Home retains one logical whole-cabinet collision envelope;
                // its hollow rendered cavity, checked above, owns light occlusion.
                if (hit.collider == fridge.View.BodyCollider || hit.transform.IsChildOf(root.Hero.transform)) continue;
                Assert.Fail("The open cavity beam is blocked by " + hit.transform.name + ".");
            }
        }

        private static string WarehouseFridgeBeamDiagnostic(CombatTestRoot root, WarehouseRefrigerator fridge,
            Light source, Vector3 target)
        {
            Transform actor = root.Hero.transform;
            Vector3 actual = actor.InverseTransformDirection(source.transform.forward);
            Vector3 wanted = actor.InverseTransformDirection((target - source.transform.position).normalized);
            Vector3 torso = root.Player.Visual is Player3DCharacterPresentation hero
                ? hero.TorsoLookAnglesDegrees : Vector3.zero;
            float actualYaw = Mathf.Atan2(actual.x, actual.z) * Mathf.Rad2Deg;
            float actualPitch = Mathf.Atan2(-actual.y, new Vector2(actual.x, actual.z).magnitude) * Mathf.Rad2Deg;
            float wantedYaw = Mathf.Atan2(wanted.x, wanted.z) * Mathf.Rad2Deg;
            float wantedPitch = Mathf.Atan2(-wanted.y, new Vector2(wanted.x, wanted.z).magnitude) * Mathf.Rad2Deg;
            return $"floor={fridge.Floor}; phase={fridge.Interaction.Phase}; door={fridge.View.DoorOpenAmount:F4}; " +
                $"root={actor.position:F4}; rootForward={actor.forward:F4}; source={source.transform.position:F4}; " +
                $"sourceLocal={actor.InverseTransformPoint(source.transform.position):F4}; target={target:F4}; " +
                $"aimTarget={root.Flashlight.InspectionAimTarget:F4}; sourcePitchYaw={actualPitch:F3}/{actualYaw:F3}; " +
                $"wantedPitchYaw={wantedPitch:F3}/{wantedYaw:F3}; torsoPitchYawRoll={torso:F3}; " +
                $"coneInnerOuter={source.innerSpotAngle:F3}/{source.spotAngle:F3}; intensity={source.intensity:F4}.";
        }

        private static void AssertWarehouseFridgeReleased(CombatTestRoot root)
        {
            Assert.That(root.Flashlight.HasInspectionAim || BarMinigameModalLock.IsAnyLocked, Is.False);
            Assert.That(root.Player.Motor.InputEnabled && root.Player.Interactor.InputEnabled, Is.True);
            Assert.That(root.CameraFollow.FixedPoseActive, Is.False);
        }

        private static void AssertWarehouseFridgeIlluminated(Camera camera, WarehouseRefrigerator fridge, Light source)
        {
            // Compare the same real cavity and camera with only its existing
            // chest source disabled; a target property alone cannot prove light.
            Color32[] lit = ReadIndicatorFrame(camera), dark;
            try
            {
                source.enabled = false;
                dark = ReadIndicatorFrame(camera);
            }
            finally { source.enabled = true; }
            Vector2 low = Vector2.one, high = Vector2.zero;
            for (int corner = 0; corner < 4; corner++)
            {
                Vector3 local = new Vector3((corner & 1) == 0 ? -.30f : .30f,
                    (corner & 2) == 0 ? -.60f : .60f, .10f);
                Vector3 point = camera.WorldToViewportPoint(fridge.View.InteriorRoot.TransformPoint(local));
                Assert.That(point.z, Is.GreaterThan(0f));
                low = Vector2.Min(low, new Vector2(point.x, point.y));
                high = Vector2.Max(high, new Vector2(point.x, point.y));
            }
            int minX = Mathf.Clamp(Mathf.FloorToInt(low.x * 160), 0, 159);
            int maxX = Mathf.Clamp(Mathf.CeilToInt(high.x * 160), 0, 159);
            int minY = Mathf.Clamp(Mathf.FloorToInt(low.y * 90), 0, 89);
            int maxY = Mathf.Clamp(Mathf.CeilToInt(high.y * 90), 0, 89);
            int brightened = 0;
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    int index = y * 160 + x;
                    int difference = lit[index].r + lit[index].g + lit[index].b -
                        dark[index].r - dark[index].g - dark[index].b;
                    if (difference >= 8) brightened++;
                }
            Assert.That(brightened, Is.GreaterThan(20),
                "The physical chest lamp must visibly illuminate the open cavity; floor=" + fridge.Floor + ".");
        }

        private static IEnumerator AwaitWarehouseFridgeCondition(Func<bool> ready, string message)
        {
            for (int frame = 0; frame < 360 && !ready(); frame++) yield return null;
            Assert.That(ready(), Is.True, message);
        }
    }

    // Test coroutines resume before LateUpdate, and WaitForEndOfFrame does
    // not dispatch in batch runs. Sample after every owner of the real rig,
    // camera, chest light and refrigerator visibility has finished its frame.
    [DefaultExecutionOrder(2000)]
    public sealed class WarehouseRefrigeratorPresentationProbe : MonoBehaviour
    {
        private Action pending;
        public bool Completed { get; private set; }
        public Exception Failure { get; private set; }

        public void Schedule(Action sample)
        {
            if (pending != null) throw new InvalidOperationException("A refrigerator frame sample is already pending.");
            pending = sample ?? throw new ArgumentNullException(nameof(sample));
            Completed = false;
            Failure = null;
        }

        public void CancelPending() => pending = null;

        private void LateUpdate()
        {
            Action sample = pending;
            pending = null;
            if (sample == null) return;
            try { sample(); }
            catch (Exception exception) { Failure = exception; }
            finally { Completed = true; }
        }
    }
}

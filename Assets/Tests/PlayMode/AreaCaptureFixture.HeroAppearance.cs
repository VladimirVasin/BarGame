using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class HeroAppearanceAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            Type.GetType("BarPromenade.Editor.Player3DV2AssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
            Type.GetType("BarPromenade.Editor.BarBartenderV2AssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        [UnityTest, Timeout(120000)]
        [Explicit("Focused hero model, wardrobe, jacket motion and physical-hair acceptance in the production Home scene.")]
        [PrebuildSetup(typeof(HeroAppearanceAssetsSetup))]
        public IEnumerator HeroAppearance()
        {
            HomeInteriorRoot home = null;
            Player3DCharacterPresentation presentation = null;
            PlayerAttentionController attention = null;
            bool attentionWasEnabled = false;
            GameObject probeRoot = null;
            GameObject studioLights = null;
            float previousDelta = Time.captureDeltaTime;
            PlayerWardrobe.OutfitSnapshot outfit = null;
            PlayerWardrobe wardrobe = null;
            try
            {
                GameSessionState.BeginNewGame();
                GameSessionState.TryStartGameTimeFromWake();
                Time.captureDeltaTime = 1f / 60f;
                yield return LoadScarfScene<HomeInteriorRoot>(SceneIds.HomeInterior,
                    value => value.IsInitialized, value => home = value);
                home.CameraFollow.enabled = false;
                home.FixedCamera.enabled = false;
                home.Player.Motor.enabled = false;
                home.Player.Motor.Teleport(Vector3.up * 50f);
                studioLights = new GameObject("Temporary hero acceptance lighting");
                foreach (var lamp in new[] { (new Vector3(28f, -35f, 0f), 1.6f), (new Vector3(12f, 145f, 0f), .8f) })
                {
                    var lampObject = new GameObject("Hero studio light");
                    lampObject.transform.SetParent(studioLights.transform, false);
                    lampObject.transform.rotation = Quaternion.Euler(lamp.Item1);
                    Light light = lampObject.AddComponent<Light>();
                    light.type = LightType.Directional;
                    light.intensity = lamp.Item2;
                    light.color = new Color(1f, .93f, .86f);
                }
                Transform hero = home.Player.GameObject.transform;
                hero.rotation = Quaternion.identity;
                presentation = (Player3DCharacterPresentation)home.Player.Visual;
                attention = home.Player.GameObject.GetComponent<PlayerAttentionController>();
                attentionWasEnabled = attention != null && attention.enabled;
                if (attention != null) attention.enabled = false;
                presentation.SetAttentionFocus(null);
                Player3DAssetRegistry registry = presentation.Registry;
                wardrobe = registry.GetComponent<PlayerWardrobe>();
                PlayerHair hair = registry.GetComponent<PlayerHair>();
                PlayerJacketCloth jacketCloth = registry.GetComponent<PlayerJacketCloth>();
                Assert.That(wardrobe, Is.Not.Null);
                wardrobe.ValidateBindings();
                Assert.That(hair != null && hair.HasAuthoredBindings && hair.IsRuntimeDriven, Is.True);
                Assert.That(hair.JointCount, Is.EqualTo(12));
                Assert.That(jacketCloth != null && jacketCloth.HasAuthoredBindings && jacketCloth.IsRuntimeDriven, Is.True);
                Assert.That(jacketCloth.SurfaceCount, Is.EqualTo(11));
                Assert.That(registry.Animations.Count, Is.GreaterThanOrEqualTo(48));
                outfit = wardrobe.CaptureOutfit();
                var contactShells = new PlayerScarfBodyContacts(registry);
                Assert.That(contactShells.Count, Is.LessThanOrEqualTo(17));
                Assert.That(contactShells.EnvelopeRenderer(Player3DAnatomicalPart.Torso).name, Is.EqualTo("CLO_JacketBody"));
                var insideJacket = new PlayerScarfBodyContacts(registry, "jacket");
                Assert.That(insideJacket.EnvelopeRenderer(Player3DAnatomicalPart.Torso).name, Is.EqualTo("CLO_ShirtBody"),
                    "The jacket contacts the underlying shirt, not its own outer envelope.");
                foreach (PlayerWardrobe.GarmentBinding garment in wardrobe.Garments)
                {
                    Assert.That(wardrobe.IsEquipped(garment.Id), Is.True);
                    Assert.That(garment.Renderers.All(renderer => renderer.enabled), Is.True, garment.Id);
                    Assert.That(garment.CoveredBodyRenderers.All(renderer => !renderer.enabled), Is.True,
                        "The main presentation must not reveal the hidden base body.");
                }
                Camera camera = Camera.main;
                Assert.That(camera, Is.Not.Null);
                yield return ScarfFrames(30);
                foreach (var shot in new[] {
                    ("01-front", new Vector3(0f, 1.0f, 3.0f), .9f),
                    ("02-three-quarter", new Vector3(2.2f, 1.05f, 2.5f), .9f),
                    ("03-back", new Vector3(-.8f, 1.05f, -2.8f), .95f),
                    ("04-head", new Vector3(.62f, 1.58f, 1.1f), 1.53f) })
                {
                    AimHeroAppearance(camera, hero, shot.Item2, shot.Item3);
                    yield return ScarfFrames(2);
                    yield return CaptureHeroAppearanceFrame(camera, shot.Item1, () => AssertHairRootFrame(hair));
                }
                Vector3 handTarget = registry.Anchors.RightGrip.position;
                Vector3 handCamera = handTarget + hero.TransformVector(new Vector3(-.42f, .08f, .5f));
                camera.transform.SetPositionAndRotation(handCamera, Quaternion.LookRotation(handTarget - handCamera));
                camera.fieldOfView = 34f;
                yield return ScarfFrames(2);
                yield return CaptureHeroAppearanceFrame(camera, "04b-hand-and-cuff", () => AssertHairRootFrame(hair));

                string jacket = wardrobe.GetEquippedItem("jacket");
                wardrobe.SetSlot("jacket", null);
                contactShells.UpdatePose();
                Assert.That(contactShells.EnvelopeRenderer(Player3DAnatomicalPart.Torso).name, Is.EqualTo("CLO_ShirtBody"));
                string shirt = wardrobe.GetEquippedItem("shirt");
                wardrobe.SetSlot("shirt", null);
                contactShells.UpdatePose();
                Assert.That(contactShells.EnvelopeRenderer(Player3DAnatomicalPart.Torso).name, Is.EqualTo("GEO_Torso"));
                wardrobe.SetSlot("shirt", shirt);
                Assert.That(wardrobe.Garments.Where(item => item.Slot == "jacket")
                    .SelectMany(item => item.Renderers).All(renderer => !renderer.enabled), Is.True);
                Assert.Throws<ArgumentException>(() => wardrobe.SetSlot("jacket", wardrobe.GetEquippedItem("boots")));
                Assert.That(wardrobe.GetEquippedItem("jacket"), Is.Null, "Invalid replacement must not mutate the outfit.");
                AimHeroAppearance(camera, hero, new Vector3(1.3f, 1.05f, 2.8f), .9f);
                yield return ScarfFrames(2);
                yield return CaptureHeroAppearanceFrame(camera, "05-shirt-without-jacket", () => AssertHairRootFrame(hair));
                using (var armOwner = new HeroArmOwner(registry))
                    Assert.That(armOwner.Subset.VisibleRenderers.All(renderer => !renderer.name.StartsWith("CLO_Jacket", StringComparison.Ordinal)),
                        Is.True, "First-person hands must use the current outfit.");
                Player3DBathingAppearance bath = Player3DBathingAppearance.Apply(registry);
                try
                {
                    Assert.That(wardrobe.Garments.SelectMany(item => item.Renderers).All(renderer => !renderer.enabled), Is.True);
                    Assert.That(wardrobe.BodyRenderers.All(renderer => renderer.enabled), Is.True);
                    contactShells.UpdatePose();
                    Assert.That(contactShells.EnvelopeRenderer(Player3DAnatomicalPart.Torso).name, Is.EqualTo("GEO_Torso"));
                    Assert.Throws<InvalidOperationException>(() => wardrobe.SetSlot("jacket", jacket));
                }
                finally { bath.Restore(); }
                Assert.That(wardrobe.GetEquippedItem("jacket"), Is.Null, "Bathing restores the selected outfit, including removed items.");
                wardrobe.RestoreOutfit(outfit);
                using (home.Player.PresentationVisibility.AcquireHidden(home))
                {
                    contactShells.UpdatePose();
                    Assert.That(contactShells.EnvelopeRenderer(Player3DAnatomicalPart.Torso).name, Is.EqualTo("CLO_JacketBody"),
                        "Camera visibility cannot remove the worn coat from physical contacts.");
                }
                var seatedObject = new GameObject("Hero appearance seated clothing probe");
                HomeToiletSeatedAppearance seated = seatedObject.AddComponent<HomeToiletSeatedAppearance>();
                try
                {
                    seated.Initialize(home);
                    Assert.That(seated.Prepare(), Is.True, "The seated module must bind to the upgraded body and trousers.");
                    Assert.That(seated.Begin(), Is.True);
                    seated.Present(.5f);
                    Assert.That(seated.Renderers.Count, Is.EqualTo(10));
                    seated.End();
                    foreach (PlayerWardrobe.GarmentBinding garment in wardrobe.Garments)
                        Assert.That(garment.Renderers.All(renderer => renderer.enabled), Is.True);
                }
                finally { seated.End(); Object.Destroy(seatedObject); }

                foreach (string clip in new[] { "Walk", "Run", "ColdHold", "SmokeLoop" })
                {
                    Assert.That(presentation.TryBeginClip(clip), Is.True);
                    presentation.SampleActiveClip(.4f);
                    AimHeroAppearance(camera, hero, new Vector3(1.8f, 1.0f, 2.8f), .95f);
                    yield return ScarfFrames(2);
                    yield return CaptureHeroAppearanceFrame(camera, "06-pose-" + clip, () => AssertHairRootFrame(hair));
                    presentation.EndClip();
                }

                probeRoot = new GameObject("Hero appearance passive secondary-motion probe");
                probeRoot.transform.position = Vector3.up * 80f;
                Player3DAssetRegistry probe = Player3DResources.Instantiate(probeRoot.transform);
                PlayerJacketCloth probeCloth = probe.GetComponent<PlayerJacketCloth>();
                Assert.That(probeCloth.HasAuthoredBindings && !probeCloth.IsRuntimeDriven, Is.True);
                Assert.That(PlayerJacketCloth.NodeCount, Is.EqualTo(16));
                Assert.That(probeCloth.SurfaceCount, Is.EqualTo(11));
                Mesh[] importedCloth = Enumerable.Range(0, probeCloth.SurfaceCount).Select(probeCloth.SourceMesh).ToArray();
                Assert.That(importedCloth.All(mesh => mesh.isReadable), Is.True,
                    "Jacket source vertices must remain readable in a packaged player, not only the Editor.");
                Vector3[][] importedVertices = importedCloth.Select(mesh => mesh.vertices).ToArray();
                probeCloth.ApplyAt(0d, false, false, new WindSample(0f, 0f));
                for (int i = 1; i <= 60; i++) probeCloth.ApplyAt(i / 60d, false, false, new WindSample(0f, 0f));
                Vector3[] calmOffsets = JacketOffsets(probeCloth);
                Vector3[][] calmMeshes = Enumerable.Range(0, probeCloth.SurfaceCount)
                    .Select(index => probeCloth.DeformedMesh(index).vertices).ToArray();
                int torsoSurface = Enumerable.Range(0, probeCloth.SurfaceCount).Single(index =>
                    probe.MeshBindings.Any(binding => binding.MeshName == "CLO_JacketBody" &&
                        binding.Renderer is SkinnedMeshRenderer skin && skin.sharedMesh == probeCloth.DeformedMesh(index)));
                SkinnedMeshRenderer probeTorso = (SkinnedMeshRenderer)probe.MeshBindings.Single(
                    binding => binding.MeshName == "CLO_JacketBody").Renderer;
                int[] pinnedTorsoVertices = JacketPinnedTorsoVertices(probe, probeTorso, importedCloth[torsoSurface]);
                Assert.That(pinnedTorsoVertices.Length, Is.GreaterThan(0), "The authored jacket must keep chest vertices pinned.");
                probeCloth.RequestReset();
                probeCloth.ApplyAt(0d, false, true, new WindSample(4f, 1f));
                for (int i = 1; i <= 60; i++)
                {
                    probeCloth.ApplyAt(i / 60d, false, true, new WindSample(4f, 1f));
                    AssertJacketNodeBounds(probeCloth);
                }
                float windResponse = JacketOffsets(probeCloth).Zip(calmOffsets, Vector3.Distance).Max();
                Assert.That(windResponse, Is.GreaterThan(.002f), "Stationary wind must change the cloth beyond its calm gravity sag.");
                for (int surface = 0; surface < importedCloth.Length; surface++)
                {
                    Assert.That(probeCloth.DeformedMesh(surface), Is.Not.SameAs(importedCloth[surface]));
                    Assert.That(importedCloth[surface].vertices, Is.EqualTo(importedVertices[surface]), "Imported jacket mesh " + surface);
                }
                Vector3[] windyTorso = probeCloth.DeformedMesh(torsoSurface).vertices;
                float torsoWindMetres = JacketWorldDelta(probe, probeCloth, torsoSurface, calmMeshes[torsoSurface]);
                Assert.That(torsoWindMetres, Is.GreaterThan(.002f),
                    "The visible jacket surface must receive the solved wind deformation.");
                int rightCuffSurface = Enumerable.Range(0, probeCloth.SurfaceCount).Single(index =>
                    probe.MeshBindings.Any(binding => binding.MeshName == "CLO_JacketCuff.R" &&
                        binding.Renderer is SkinnedMeshRenderer skin && skin.sharedMesh == probeCloth.DeformedMesh(index)));
                float cuffWindMetres = JacketWorldDelta(probe, probeCloth, rightCuffSurface, calmMeshes[rightCuffSurface]);
                float cuffNodeResponse = Enumerable.Range(12, 4).Max(index => Vector3.Distance(
                    probeCloth.WorldPoint(index) - probeCloth.RestWorldPoint(index), calmOffsets[index]));
                Debug.Log($"HERO_CLOTH_WORLD_MM: torso={torsoWindMetres * 1000f:F3}, cuff={cuffWindMetres * 1000f:F3}, cuffNodes={cuffNodeResponse * 1000f:F3}");
                Assert.That(cuffWindMetres, Is.GreaterThan(.0005f),
                    "The visible cuff must move at least half a millimetre under full-strength wind (measured through its bone skin, in world metres).");
                AssertJacketCuffContacts(probe);
                foreach (int vertex in pinnedTorsoVertices)
                    Assert.That(windyTorso[vertex], Is.EqualTo(importedVertices[torsoSurface][vertex]), "Pinned jacket chest " + vertex);
                AimHeroAppearance(camera, probeRoot.transform, new Vector3(1.4f, 1.02f, 2.8f), .9f);
                yield return ScarfFrames(2);
                yield return CaptureHeroAppearanceFrame(camera, "06a-jacket-stationary-wind");
                using (var armOwner = new HeroArmOwner(probe))
                {
                    PlayerJacketCloth firstPersonCloth = armOwner.Subset.Registry.GetComponent<PlayerJacketCloth>();
                    Assert.That(firstPersonCloth.IsRuntimeDriven, Is.False);
                    AssertJacketMeshCopy(probeCloth, firstPersonCloth, "First-person jacket");
                    Assert.That(armOwner.Subset.VisibleRenderers.Any(renderer => renderer.name == "CLO_JacketCuff.R"), Is.True);
                }
                Vector3[][] pausedCloth = Enumerable.Range(0, probeCloth.SurfaceCount).Select(index => probeCloth.DeformedMesh(index).vertices).ToArray();
                Vector3[] pausedNodes = Enumerable.Range(0, PlayerJacketCloth.NodeCount).Select(probeCloth.WorldPoint).ToArray();
                probeCloth.ApplyAt(1.1d, true, true, new WindSample(9f, 2f));
                Assert.That(probeCloth.LastStepSeconds, Is.Zero);
                Assert.That(Enumerable.Range(0, PlayerJacketCloth.NodeCount).Select(probeCloth.WorldPoint).ToArray(), Is.EqualTo(pausedNodes));
                for (int surface = 0; surface < pausedCloth.Length; surface++)
                    Assert.That(probeCloth.DeformedMesh(surface).vertices, Is.EqualTo(pausedCloth[surface]), "Paused jacket mesh " + surface);

                probeCloth.RequestReset();
                probeCloth.ApplyAt(0d, false, false, new WindSample(0f, 0f));
                for (int i = 1; i <= 60; i++) probeCloth.ApplyAt(i / 60d, false, false, new WindSample(0f, 0f));
                for (int i = 1; i <= 30; i++)
                {
                    float phase = i / 30f;
                    probeRoot.transform.position = Vector3.up * 80f + Vector3.right * (.35f * phase * phase);
                    probeCloth.ApplyAt(1d + i / 60d, false, false, new WindSample(0f, 0f));
                    AssertJacketNodeBounds(probeCloth);
                }
                float inertiaResponse = JacketOffsets(probeCloth).Zip(calmOffsets, Vector3.Distance).Max();
                Assert.That(inertiaResponse, Is.GreaterThan(.002f), "Acceleration without wind must produce cloth inertia.");
                AimHeroAppearance(camera, probeRoot.transform, new Vector3(-1.4f, 1.02f, 2.8f), .9f);
                yield return ScarfFrames(2);
                yield return CaptureHeroAppearanceFrame(camera, "06a-jacket-calm-acceleration");
                int clothResets = probeCloth.ResetCount;
                probeRoot.transform.position += Vector3.right * 2f;
                probeCloth.ApplyAt(1.52d, false, false, new WindSample(0f, 0f));
                Assert.That(probeCloth.ResetCount, Is.EqualTo(clothResets + 1));
                Assert.That(probeCloth.MaximumSpeed, Is.Zero);
                PlayerWardrobe probeWardrobe = probe.GetComponent<PlayerWardrobe>();
                string probeJacket = probeWardrobe.GetEquippedItem("jacket");
                probeWardrobe.SetSlot("jacket", null);
                probeCloth.ApplyAt(1.54d, false, true, new WindSample(9f, 2f));
                Assert.That(probeCloth.IsActive, Is.False);
                for (int surface = 0; surface < importedCloth.Length; surface++)
                    Assert.That(probeCloth.DeformedMesh(surface).vertices, Is.EqualTo(importedVertices[surface]), "Removed jacket restores mesh " + surface);
                clothResets = probeCloth.ResetCount;
                probeWardrobe.SetSlot("jacket", probeJacket);
                probeCloth.ApplyAt(1.56d, false, false, new WindSample(0f, 0f));
                Assert.That(probeCloth.IsActive, Is.True);
                Assert.That(probeCloth.ResetCount, Is.EqualTo(clothResets + 1));
                Assert.That(probeCloth.MaximumSpeed, Is.Zero);

                PlayerHair probeHair = probe.GetComponent<PlayerHair>();
                Assert.That(probeHair.IsRuntimeDriven, Is.False);
                yield return CaptureHeroHairRest(camera, probeRoot.transform, probe);
                probeHair.ApplyAt(0d, false, true, new WindSample(4f, 1f));
                float movement = 0f;
                for (int i = 1; i <= 30; i++)
                {
                    probeRoot.transform.localRotation = Quaternion.Euler(0f, i * .8f, 0f);
                    probeHair.ApplyAt(i / 60d, false, true, new WindSample(4f, 1f));
                    movement = Mathf.Max(movement, probeHair.MaximumDisplacement);
                    Assert.That(probeHair.MaximumLengthError, Is.LessThan(.0001f));
                    for (int chain = 0; chain < 3; chain++)
                    for (int segment = 0; segment < 3; segment++)
                        Assert.That(probeHair.BendDegrees(chain, segment), Is.LessThanOrEqualTo(
                            (segment == 0 ? PlayerHair.RootBendLimitDegrees : segment == 1 ? PlayerHair.MiddleBendLimitDegrees : PlayerHair.TipBendLimitDegrees) + .1f));
                }
                Assert.That(movement, Is.InRange(.001f, .30f), "Hair must move while retaining its bounded shape.");
                Vector3[] paused = Enumerable.Range(0, 12).Select(probeHair.WorldPoint).ToArray();
                Transform[] hairJoints = new[] { "HairBack", "HairLeft", "HairRight" }.SelectMany(chain =>
                    new[] { ".00", ".01", ".02", ".Tip" }.Select(suffix => probe.GetComponentsInChildren<Transform>(true)
                        .Single(joint => joint.name == chain + suffix))).ToArray();
                Vector3[] solvedHairPositions = hairJoints.Select(joint => joint.localPosition).ToArray();
                Quaternion[] solvedHairRotations = hairJoints.Select(joint => joint.localRotation).ToArray();
                foreach (Transform joint in hairJoints)
                { joint.localPosition += Vector3.one * .01f; joint.localRotation = Quaternion.Euler(40f, 20f, 30f); }
                probeHair.ApplyAt(.6d, true, true, new WindSample(9f, 2f));
                Assert.That(Enumerable.Range(0, 12).Select(probeHair.WorldPoint).ToArray(), Is.EqualTo(paused));
                Assert.That(hairJoints.Select(joint => joint.localPosition).ToArray(), Is.EqualTo(solvedHairPositions));
                for (int joint = 0; joint < hairJoints.Length; joint++)
                    Assert.That(Quaternion.Angle(hairJoints[joint].localRotation, solvedHairRotations[joint]), Is.LessThan(.001f),
                        "Paused hair must restore solved locals if Animator wrote the authored pose again: " + joint);
                int resets = probeHair.ResetCount;
                probeRoot.transform.position += Vector3.right * 2f;
                probeHair.ApplyAt(.62d, false, false, new WindSample(0f, 0f));
                Assert.That(probeHair.ResetCount, Is.EqualTo(resets + 1));
                foreach (Player3DMeshBinding binding in registry.MeshBindings.Where(value => value.Role == "hair"))
                    Assert.That(Player3DHeadVisibility.IsHeadGeometry(binding.BoneName), Is.True);

                Assert.That(GameSessionState.TryAddInventoryItem(InventoryItemId.Scarf), Is.True);
                Assert.That(GameSessionState.TrySetInventoryItemEquipped(InventoryItemId.Scarf, true), Is.True);
                hero.rotation = Quaternion.Euler(0f, 35f, 0f);
                AimHeroAppearance(camera, hero, new Vector3(-.65f, 1.55f, -1.3f), 1.47f);
                yield return ScarfFrames(16);
                yield return CaptureHeroAppearanceFrame(camera, "06b-hair-with-scarf", () => AssertHairRootFrame(hair));

                studioLights.SetActive(false);
                if (attention != null) attention.enabled = attentionWasEnabled;
                home.Player.Motor.Teleport(new Vector3(2.075f, .12f, 2.78f));
                home.FixedCamera.enabled = true;
                yield return ScarfFrames(12);
                Assert.That(home.BathroomMirror.IsActive, Is.True);
                Assert.That(home.BathroomMirror.TwinUnpairedBoneCount, Is.Zero);
                Assert.That(home.BathroomMirror.TwinUnpairedRendererCount, Is.Zero);
                PlayerHair twin = home.BathroomMirror.Twin.GetComponent<PlayerHair>();
                PlayerJacketCloth twinCloth = home.BathroomMirror.Twin.GetComponent<PlayerJacketCloth>();
                Assert.That(twin != null && !twin.IsRuntimeDriven, Is.True);
                Assert.That(twinCloth != null && !twinCloth.IsRuntimeDriven, Is.True);
                // Observe source/mirror after both LateUpdate owners have completed.
                var mirrorTarget = new RenderTexture(640, 360, 24);
                RenderTexture originalTarget = camera.targetTexture;
                mirrorTarget.Create(); camera.targetTexture = mirrorTarget;
                try
                {
                    yield return ScarfRenderedFrame(camera, () =>
                    {
                        for (int i = 0; i < 12; i++)
                            Assert.That(Vector3.Distance(registry.transform.InverseTransformPoint(hair.WorldPoint(i)),
                                twin.transform.InverseTransformPoint(twin.WorldPoint(i))), Is.LessThan(.0001f), "Mirror hair point " + i);
                        AssertJacketMeshCopy(jacketCloth, twinCloth, "Mirror jacket");
                    });
                }
                finally { camera.targetTexture = originalTarget; mirrorTarget.Release(); Object.Destroy(mirrorTarget); }
                yield return CaptureHeroAppearanceFrame(camera, "07-mirror", () => AssertHairRootFrame(hair));
                Debug.Log("HERO_APPEARANCE_ACCEPTED: independent outfit/skin, jacket wind/inertia with pinned chest and owned meshes, current first-person sleeves, bathing restore, secondary-motion pause/reset and shared mirror gameplay frames.");
            }
            finally
            {
                presentation?.EndClip();
                if (attention != null) attention.enabled = attentionWasEnabled;
                if (wardrobe != null && outfit != null && !wardrobe.HasAppearanceLease && !wardrobe.IsVisibilityLocked) wardrobe.RestoreOutfit(outfit);
                if (probeRoot != null) Object.Destroy(probeRoot);
                if (studioLights != null) Object.Destroy(studioLights);
                Time.captureDeltaTime = previousDelta;
            }
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
        }

        private static void AimHeroAppearance(Camera camera, Transform hero, Vector3 offset, float targetHeight)
        {
            Vector3 target = hero.position + Vector3.up * targetHeight;
            Vector3 position = hero.TransformPoint(offset);
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
            camera.fieldOfView = 42f;
        }

        private static Vector3[] JacketOffsets(PlayerJacketCloth cloth) => Enumerable.Range(0, PlayerJacketCloth.NodeCount)
            .Select(index => cloth.WorldPoint(index) - cloth.RestWorldPoint(index)).ToArray();

        private static IEnumerator CaptureHeroAppearanceFrame(Camera camera, string shot, Action sample = null)
        {
            var target = new RenderTexture(Width, Height, 24);
            RenderTexture originalTarget = camera.targetTexture;
            target.Create();
            camera.targetTexture = target;
            try
            {
                // Read the actual pipeline output after animation, secondary
                // motion and mirror LateUpdate; never render a partial pose.
                yield return ScarfRenderedFrame(camera, () =>
                {
                    sample?.Invoke();
                    var pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                    RenderTexture previous = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = target;
                        pixels.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
                        pixels.Apply();
                        Assert.That(IsBlank(pixels), Is.False, "Hero capture must show the live scene.");
                        string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", "HeroAppearance");
                        Directory.CreateDirectory(folder);
                        File.WriteAllBytes(Path.Combine(folder, shot + ".png"), pixels.EncodeToPNG());
                    }
                    finally { RenderTexture.active = previous; Object.Destroy(pixels); }
                });
            }
            finally { camera.targetTexture = originalTarget; target.Release(); Object.Destroy(target); }
        }

        private static IEnumerator CaptureHeroHairRest(Camera camera, Transform actor, Player3DAssetRegistry registry)
        {
            PlayerHair hair = registry.GetComponent<PlayerHair>();
            hair.RequestReset();
            hair.ApplyAt(0d, false, false, new WindSample(0f, 0f));
            for (int frame = 1; frame <= 120; frame++)
                hair.ApplyAt(frame / 60d, false, false, new WindSample(0f, 0f));
            Assert.That(hair.MaximumDisplacement, Is.LessThan(.025f),
                "Calm settled hair must retain the authored silhouette with its body contacts active.");
            AimHeroAppearance(camera, actor, new Vector3(.62f, 1.58f, 1.1f), 1.53f);
            yield return CaptureHeroAppearanceFrame(camera, "06c-hair-calm-rest", () => AssertHairRootFrame(hair));
            hair.RequestReset();
        }

        private static void AssertHairRootFrame(PlayerHair hair)
        {
            Transform[] hierarchy = hair.GetComponentsInChildren<Transform>(true);
            string[] names = { "HairBack.00", "HairLeft.00", "HairRight.00" };
            for (int chain = 0; chain < names.Length; chain++)
            {
                int index = chain * 4;
                Transform root = hierarchy.Single(joint => joint.name == names[chain]);
                Vector3 attachment = hair.AuthoredWorldPoint(index);
                Assert.That(Vector3.Distance(hair.RestWorldPoint(index), attachment), Is.LessThan(.0001f),
                    names[chain] + " target must use the final rendered head frame.");
                Assert.That(Vector3.Distance(hair.WorldPoint(index), attachment), Is.LessThan(.0001f),
                    names[chain] + " simulated root must remain kinematic.");
                Assert.That(Vector3.Distance(root.position, attachment), Is.LessThan(.0001f),
                    names[chain] + " visible root must match the same head attachment.");
            }
        }

        private static void AssertJacketNodeBounds(PlayerJacketCloth cloth)
        {
            for (int node = 0; node < PlayerJacketCloth.NodeCount; node++)
                Assert.That(Vector3.Distance(cloth.WorldPoint(node), cloth.RestWorldPoint(node)),
                    Is.LessThanOrEqualTo(node < 8 ? .0751f : .0251f), "Jacket motion limit " + node);
        }

        private static void AssertJacketMeshCopy(PlayerJacketCloth source, PlayerJacketCloth target, string label)
        {
            Assert.That(target.SurfaceCount, Is.EqualTo(source.SurfaceCount));
            for (int surface = 0; surface < source.SurfaceCount; surface++)
            {
                int match = Enumerable.Range(0, target.SurfaceCount).Single(index => target.SourceMesh(index) == source.SourceMesh(surface));
                Assert.That(target.DeformedMesh(match), Is.Not.Null, label + " owns its copy");
                Assert.That(target.DeformedMesh(match), Is.Not.SameAs(source.DeformedMesh(surface)), label + " cannot share mutable buffers");
                Assert.That(target.DeformedMesh(match).vertices, Is.EqualTo(source.DeformedMesh(surface).vertices), label + " surface " + surface);
            }
        }

        private static int[] JacketPinnedTorsoVertices(Player3DAssetRegistry registry, SkinnedMeshRenderer renderer, Mesh source)
        {
            Vector3[] vertices = source.vertices;
            BoneWeight[] weights = source.boneWeights;
            Matrix4x4[] bindposes = source.bindposes;
            Transform[] bones = renderer.bones;
            return Enumerable.Range(0, vertices.Length).Where(index =>
            {
                BoneWeight weight = weights[index];
                Vector3 world = Vector3.zero;
                void Add(int bone, float amount)
                { if (amount > 0f) world += (bones[bone].localToWorldMatrix * bindposes[bone]).MultiplyPoint3x4(vertices[index]) * amount; }
                Add(weight.boneIndex0, weight.weight0); Add(weight.boneIndex1, weight.weight1);
                Add(weight.boneIndex2, weight.weight2); Add(weight.boneIndex3, weight.weight3);
                return registry.transform.InverseTransformPoint(world).y >= 1.12f;
            }).ToArray();
        }

        private static float JacketWorldDelta(Player3DAssetRegistry registry, PlayerJacketCloth cloth, int surface, Vector3[] previous)
        {
            Mesh mesh = cloth.DeformedMesh(surface);
            SkinnedMeshRenderer renderer = registry.MeshBindings.Select(binding => binding.Renderer).OfType<SkinnedMeshRenderer>()
                .Single(value => value.sharedMesh == mesh);
            Vector3[] vertices = mesh.vertices;
            BoneWeight[] weights = mesh.boneWeights;
            Matrix4x4[] bindposes = mesh.bindposes;
            Transform[] bones = renderer.bones;
            float maximum = 0f;
            for (int index = 0; index < vertices.Length; index++)
            {
                Vector3 delta = vertices[index] - previous[index], world = Vector3.zero;
                BoneWeight weight = weights[index];
                void Add(int bone, float amount)
                { if (amount > 0f) world += (bones[bone].localToWorldMatrix * bindposes[bone]).MultiplyVector(delta) * amount; }
                Add(weight.boneIndex0, weight.weight0); Add(weight.boneIndex1, weight.weight1);
                Add(weight.boneIndex2, weight.weight2); Add(weight.boneIndex3, weight.weight3);
                maximum = Mathf.Max(maximum, world.magnitude);
            }
            return maximum;
        }

        private static void AssertJacketCuffContacts(Player3DAssetRegistry registry)
        {
            var contacts = new PlayerScarfBodyContacts(registry, "jacket", useMeshSupportPlanes: true);
            contacts.UpdatePose();
            var scratch = new Mesh { name = "Hero jacket cuff contact proof" };
            try
            {
                foreach (Player3DMeshBinding binding in registry.MeshBindings.Where(binding =>
                    binding.MeshName == "CLO_JacketCuff.L" || binding.MeshName == "CLO_JacketCuff.R"))
                {
                    var renderer = (SkinnedMeshRenderer)binding.Renderer;
                    renderer.BakeMesh(scratch, true);
                    Vector3[] vertices = scratch.vertices;
                    for (int i = 0; i < vertices.Length; i++)
                        Assert.That(contacts.Contains(renderer.transform.TransformPoint(vertices[i]), .001f), Is.False,
                            binding.MeshName + " visible vertex remains inside its measured body/hand shell: " + i);
                }
            }
            finally { Object.Destroy(scratch); }
        }

        private sealed class HeroArmOwner : IDisposable
        {
            private readonly GameObject owner = new GameObject("Hero appearance first-person probe");
            public readonly Player3DFirstPersonSubset Subset;
            public HeroArmOwner(Player3DAssetRegistry source)
            {
                owner.transform.position = Vector3.up * 50f;
                Subset = Player3DFirstPersonSubset.Create(owner.transform, Player3DFirstPersonSide.Right, 0, "Current outfit arm", source);
            }
            public void Dispose() { Subset.Dispose(); Object.Destroy(owner); }
        }
    }
}

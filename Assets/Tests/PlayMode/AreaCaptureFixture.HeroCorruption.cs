using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest, Timeout(120000)]
        [Explicit("Focused seven-stage hero decay, shared mirror/first-person appearance and visual acceptance. Run alone.")]
        [PrebuildSetup(typeof(HeroAppearanceAssetsSetup))]
        public IEnumerator HeroCorruptionAppearance()
        {
            HomeInteriorRoot home = null;
            Player3DCharacterPresentation presentation = null;
            PlayerAttentionController attention = null;
            bool attentionWasEnabled = false;
            GameObject lighting = null;
            GameObject mirrorOwner = null;
            HomeMirrorHeroTwin mirror = null;
            float previousDelta = Time.captureDeltaTime;
            try
            {
                GameSessionState.BeginNewGame();
                AssertHeroCorruptionSessionTransaction();
                Assert.That(GameSessionState.TryStartGameTimeFromWake(), Is.True);
                Time.captureDeltaTime = 1f / 60f;
                yield return LoadScarfScene<HomeInteriorRoot>(SceneIds.HomeInterior,
                    value => value.IsInitialized, value => home = value);
                home.CameraFollow.enabled = false;
                home.FixedCamera.enabled = false;
                home.Player.Motor.enabled = false;
                home.Player.Motor.Teleport(Vector3.up * 50f);
                Transform hero = home.Player.GameObject.transform;
                hero.rotation = Quaternion.identity;
                presentation = (Player3DCharacterPresentation)home.Player.Visual;
                attention = home.Player.GameObject.GetComponent<PlayerAttentionController>();
                attentionWasEnabled = attention != null && attention.enabled;
                if (attention != null) attention.enabled = false;
                presentation.SetAttentionFocus(null);
                Player3DAssetRegistry registry = presentation.Registry;
                PlayerCorruptionAppearance appearance = registry.GetComponent<PlayerCorruptionAppearance>();
                Assert.That(appearance, Is.Not.Null, "The ordinary production hero owns the decay appearance.");
                Assert.That(appearance.AppliedLevel, Is.EqualTo(1));
                PlayerHair physicalHair = registry.GetComponent<PlayerHair>();
                Assert.That(physicalHair, Is.Not.Null);
                float shortHairLength = 0f;

                lighting = new GameObject("Temporary hero decay inspection lights");
                foreach (var lamp in new[] {
                    (new Vector3(28f, -35f, 0f), 1.6f),
                    (new Vector3(12f, 145f, 0f), .8f) })
                {
                    var source = new GameObject("Hero decay inspection light");
                    source.transform.SetParent(lighting.transform, false);
                    source.transform.rotation = Quaternion.Euler(lamp.Item1);
                    Light light = source.AddComponent<Light>();
                    light.type = LightType.Directional;
                    light.intensity = lamp.Item2;
                    light.color = new Color(1f, .93f, .86f);
                }

                // A passive twin exercises the same path as the bathroom without
                // introducing another scene, camera, animation or simulation.
                mirrorOwner = new GameObject("Temporary hero decay mirror proof");
                mirror = HomeMirrorHeroTwin.TryCreate(registry, home.transform,
                    mirrorOwner.transform, "Hero decay passive twin", 0);
                Assert.That(mirror, Is.Not.Null);
                Camera camera = Camera.main;
                Assert.That(camera, Is.Not.Null);
                foreach (int level in new[] { 1, 4, 7 })
                {
                    Assert.That(GameSessionState.TrySetDebugHeroCorruptionLevel(level), Is.True);
                    yield return ScarfFrames(12);
                    Assert.That(appearance.AppliedLevel, Is.EqualTo(level));
                    float hairLength = 0f;
                    for (int point = 1; point < PlayerHair.PointsPerChain; point++)
                        hairLength += Vector3.Distance(physicalHair.RestWorldPoint(point - 1), physicalHair.RestWorldPoint(point));
                    if (level == 1)
                    {
                        shortHairLength = hairLength;
                        Assert.That(shortHairLength, Is.GreaterThan(.001f));
                    }
                    if (level == 7)
                        Assert.That(hairLength, Is.GreaterThan(shortHairLength * 2f),
                            "The maximum must grow visibly longer physical hair, beyond the initial cropped profile.");
                    Renderer coat = registry.MeshBindings.Single(value => value.MeshName == "CLO_JacketBody").Renderer;
                    var coatProperties = new MaterialPropertyBlock();
                    coat.GetPropertyBlock(coatProperties);
                    if (level > 1)
                    {
                        Texture2D wear = Resources.Load<Texture2D>("Player/Corruption/Clothing" + level);
                        Assert.That(wear, Is.Not.Null);
                        Assert.That(coatProperties.GetTexture("_BaseMap"),
                            Is.SameAs(wear),
                            "The coat must display its authored stains/abrasion atlas instead of a flat tint.");
                    }
                    mirror.Registry.gameObject.SetActive(true);
                    mirror.Sync();
                    AssertHeroCorruptionCopy(registry, mirror.Registry, "Mirror", level);
                    mirror.Registry.gameObject.SetActive(false);
                    using (var arm = new HeroArmOwner(registry))
                    {
                        arm.Subset.RefreshAppearance();
                        AssertHeroCorruptionCopy(registry, arm.Subset.Registry, "First person", level, armsOnly: true);
                    }
                    foreach (var shot in new[] {
                        ("full", new Vector3(1.8f, 1.05f, 2.8f), .9f),
                        ("profile", new Vector3(2.6f, 1.05f, .15f), .9f),
                        ("jacket", new Vector3(.50f, 1.22f, 1.12f), 1.16f),
                        ("head", new Vector3(.42f, 1.57f, 1.1f), 1.49f) })
                    {
                        AimHeroAppearance(camera, hero, shot.Item2, shot.Item3);
                        yield return ScarfFrames(2);
                        yield return CaptureHeroAppearanceFrame(camera,
                            "corruption-" + level + "-" + shot.Item1);
                    }
                    if (level == 7)
                        yield return CaptureHeroCorruptionHairShadowPair(camera, registry);
                }

                var head = (SkinnedMeshRenderer)registry.MeshBindings.Single(value => value.MeshName == "GEO_Head").Renderer;
                int shape = CharacterJointDeformation.FindShape(head.sharedMesh, "HeroCorruption");
                Assert.That(shape, Is.GreaterThanOrEqualTo(0), "The maximum must change authored geometry.");
                var delta = new Vector3[head.sharedMesh.vertexCount];
                head.sharedMesh.GetBlendShapeFrameVertices(shape,
                    head.sharedMesh.GetBlendShapeFrameCount(shape) - 1, delta, null, null);
                float maximumWorldDelta = delta.Max(value => head.transform.TransformVector(value).magnitude);
                Assert.That(maximumWorldDelta, Is.GreaterThan(.015f),
                    "A face colour change alone cannot meet the maximum corruption contract.");

                int calendar = GameSessionState.GameDayNumber;
                GameSessionState.UpdateDrinkingProgress(0, GameSessionState.LastAlcoholicDrink, GameSessionState.DrinksConsumed);
                GameSessionState.SetHeroMouthSoiled(true, "decay_acceptance");
                GameSessionState.SetHeroMouthSoiled(false, "decay_acceptance_wash");
                yield return ScarfFrames(2);
                Assert.That(GameSessionState.HeroCorruptionLevel, Is.EqualTo(7),
                    "Sobering and washing remove temporary state, never accumulated decay.");
                Assert.That(GameSessionState.GameDayNumber, Is.EqualTo(calendar));

                Player3DBathingAppearance bath = Player3DBathingAppearance.Apply(registry);
                try
                {
                    yield return ScarfFrames(2);
                    Assert.That(appearance.AppliedLevel, Is.EqualTo(7));
                    Assert.That(head.GetBlendShapeWeight(shape), Is.EqualTo(100f).Within(.01f));
                }
                finally { bath.Restore(); }
                yield return ScarfFrames(2);
                Assert.That(appearance.AppliedLevel, Is.EqualTo(7));

                lighting.SetActive(false);
                home.Player.Motor.Teleport(home.BedInteractionPlan.ExitRootPosition);
                home.FixedCamera.enabled = true;
                yield return ScarfFrames(12);
                yield return CaptureHeroAppearanceFrame(camera, "corruption-7-home");
                home.Player.Motor.Teleport(new Vector3(2.075f, .12f, 2.78f));
                yield return ScarfFrames(12);
                Assert.That(home.BathroomMirror.IsActive, Is.True);
                yield return CaptureHeroAppearanceFrame(camera, "corruption-7-bathroom", () =>
                    AssertHeroCorruptionCopy(registry, home.BathroomMirror.Twin, "Live bathroom", 7));

                // The developer may inspect any exact stage in either direction;
                // gameplay progression remains monotonic.
                Assert.That(GameSessionState.TrySetDebugHeroCorruptionLevel(1), Is.True);
                yield return ScarfFrames(2);
                Assert.That(appearance.AppliedLevel, Is.EqualTo(1));
                Assert.That(head.GetBlendShapeWeight(shape), Is.Zero.Within(.01f));

                Debug.Log("HERO_CORRUPTION_ACCEPTED: authored human geometry, seven stage selection, passive mirror/first-person parity, bathing restore and sober persistence.");
            }
            finally
            {
                presentation?.EndClip();
                if (attention != null) attention.enabled = attentionWasEnabled;
                mirror?.Destroy();
                if (mirrorOwner != null) Object.Destroy(mirrorOwner);
                if (lighting != null) Object.Destroy(lighting);
                Time.captureDeltaTime = previousDelta;
            }
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            GameSessionState.BeginNewGame();
            Assert.That(GameSessionState.HeroCorruptionLevel, Is.EqualTo(1));
        }

        private static IEnumerator CaptureHeroCorruptionHairShadowPair(Camera camera, Player3DAssetRegistry registry)
        {
            Renderer[] hair = registry.MeshBindings.Where(value =>
                    value.Role == "hair" || value.PaletteMaterialName != null &&
                    value.PaletteMaterialName.StartsWith("MAT_Hair", StringComparison.Ordinal))
                .Select(value => value.Renderer).Where(value => value != null).Distinct().ToArray();
            Assert.That(hair.Length, Is.GreaterThan(0));
            ShadowCastingMode[] originalShadows = hair.Select(value => value.shadowCastingMode).ToArray();
            bool[] originalVisibility = hair.Select(value => value.enabled).ToArray();
            using (GameTimeScaleRuntime.AcquirePause())
            {
                // Freeze the completed ordinary pose and the solved hair together;
                // only hair shadow casting differs between these two exact frames.
                yield return ScarfFrames(2);
                Assert.That(registry.TryGetPart(Player3DAnatomicalPart.Neck, out Player3DAnatomicalPartBinding neck), Is.True);
                Transform[] bones = registry.Animator.GetComponentsInChildren<Transform>(true).Where(value =>
                    value == registry.Anchors.Head || value == neck.Bone ||
                    value.name.StartsWith("Hair", StringComparison.Ordinal)).ToArray();
                Vector3[] positions = bones.Select(value => value.localPosition).ToArray();
                Quaternion[] rotations = bones.Select(value => value.localRotation).ToArray();
                void AssertFrozen()
                {
                    for (int index = 0; index < bones.Length; index++)
                    {
                        Assert.That(Vector3.Distance(bones[index].localPosition, positions[index]), Is.LessThan(.001f),
                            "Diagnostic position freeze: " + bones[index].name);
                        Assert.That(Quaternion.Angle(bones[index].localRotation, rotations[index]), Is.LessThan(.1f),
                            "Diagnostic rotation freeze: " + bones[index].name);
                    }
                }
                try
                {
                    yield return CaptureHeroAppearanceFrame(camera, "corruption-7-head-shadow-normal", AssertFrozen);
                    foreach (Renderer renderer in hair) renderer.shadowCastingMode = ShadowCastingMode.Off;
                    yield return CaptureHeroAppearanceFrame(camera, "corruption-7-head-hair-shadow-off", AssertFrozen);
                    foreach (Renderer renderer in hair) renderer.enabled = false;
                    yield return CaptureHeroAppearanceFrame(camera, "corruption-7-head-hair-hidden", AssertFrozen);
                    AssertHeroCorruptionRenderedFace(camera, registry);
                }
                finally
                {
                    for (int index = 0; index < hair.Length; index++)
                        if (hair[index] != null)
                        {
                            hair[index].shadowCastingMode = originalShadows[index];
                            hair[index].enabled = originalVisibility[index];
                        }
                }
            }
        }

        private static void AssertHeroCorruptionSessionTransaction()
        {
            Assert.That(GameSessionState.TryOrderDrink(DrinkId.Vodka, out DrinkOrderToken paid).Succeeded, Is.True);
            Assert.That(paid, Is.Not.Null);
            Assert.That(GameSessionState.HeroCorruptionDose, Is.Zero,
                "A paid glass adds no dose before the physical sip commits.");
            GameSessionState.UpdateDrinkingProgress(100, DrinkId.Vodka, 0);
            Assert.That(GameSessionState.TryConsumeOrderedDrink(paid), Is.True);
            Assert.That(GameSessionState.IntoxicationLevel, Is.EqualTo(100));
            Assert.That(GameSessionState.HeroCorruptionDose, Is.EqualTo(18),
                "A completed serving adds its full dose beyond the current intoxication cap.");
            Assert.That(GameSessionState.TryConsumeOrderedDrink(paid), Is.False);
            Assert.That(GameSessionState.HeroCorruptionDose, Is.EqualTo(18));
            Assert.That(GameSessionState.TryPurchaseDrink(DrinkId.Water).Succeeded, Is.True);
            Assert.That(GameSessionState.HeroCorruptionDose, Is.EqualTo(18),
                "Water cannot reuse the preserved alcoholic-drink context as a dose.");

            GameSessionState.CommitDrinkingProgress(100, DrinkId.Vodka, 31, 0);
            Assert.That(GameSessionState.HeroCorruptionDose, Is.EqualTo(540));
            Assert.That(GameSessionState.HeroCorruptionLevel, Is.EqualTo(4));
            GameSessionState.CommitDrinkingProgress(100, DrinkId.Vodka, 31, 0);
            Assert.That(GameSessionState.HeroCorruptionDose, Is.EqualTo(540), "Duplicate progress commits are inert.");
            GameSessionState.RelieveIntoxication(20, "corruption_acceptance");
            GameSessionState.AdvanceIntoxicationRecovery(100000f);
            GameSessionState.ResetFatigueAfterSleep();
            Assert.That(GameSessionState.IntoxicationLevel, Is.Zero);
            Assert.That(GameSessionState.HeroCorruptionDose, Is.EqualTo(540));

            int[] thresholds = { 0, 120, 300, 540, 840, 1200, 1620 };
            for (int stage = 0; stage < thresholds.Length; stage++)
            {
                Assert.That(HeroCorruptionRules.ResolveLevel(thresholds[stage]), Is.EqualTo(stage + 1));
                if (stage > 0)
                    Assert.That(HeroCorruptionRules.ResolveLevel(thresholds[stage] - 1), Is.EqualTo(stage));
            }
            Assert.That(HeroCorruptionRules.AddDose(int.MaxValue - 2, 3), Is.EqualTo(int.MaxValue));
            GameSessionState.BeginNewGame();
            Assert.That(GameSessionState.HeroCorruptionDose, Is.Zero);
            Assert.That(GameSessionState.HeroCorruptionLevel, Is.EqualTo(1));
        }

        private static void AssertHeroCorruptionCopy(Player3DAssetRegistry source,
            Player3DAssetRegistry copy, string label, int level, bool armsOnly = false)
        {
            int matched = 0;
            var sourceProperties = new MaterialPropertyBlock();
            var targetProperties = new MaterialPropertyBlock();
            foreach (Player3DMeshBinding binding in source.MeshBindings)
            {
                if (armsOnly && binding.MeshName != "GEO_Hand.R" && binding.MeshName != "CLO_JacketSleeve.R") continue;
                if (!(binding.Renderer is SkinnedMeshRenderer original)) continue;
                int originalShape = CharacterJointDeformation.FindShape(original.sharedMesh, "HeroCorruption");
                int shortHairShape = CharacterJointDeformation.FindShape(original.sharedMesh, "HeroHairShort");
                int longHairShape = CharacterJointDeformation.FindShape(original.sharedMesh, "HeroHairLong");
                if (originalShape < 0 && shortHairShape < 0 && longHairShape < 0) continue;
                Player3DMeshBinding targetBinding = copy.MeshBindings.Single(value => value.MeshName == binding.MeshName);
                var target = (SkinnedMeshRenderer)targetBinding.Renderer;
                foreach (string name in new[] { "HeroCorruption", "HeroHairShort", "HeroHairLong" })
                {
                    int fromShape = CharacterJointDeformation.FindShape(original.sharedMesh, name);
                    if (fromShape < 0) continue;
                    int toShape = CharacterJointDeformation.FindShape(target.sharedMesh, name);
                    Assert.That(toShape, Is.GreaterThanOrEqualTo(0), label + " " + binding.MeshName + " " + name);
                    Assert.That(target.GetBlendShapeWeight(toShape),
                        Is.EqualTo(original.GetBlendShapeWeight(fromShape)).Within(.01f), label + " " + binding.MeshName + " " + name);
                }
                sourceProperties.Clear(); targetProperties.Clear();
                original.GetPropertyBlock(sourceProperties);
                target.GetPropertyBlock(targetProperties);
                Assert.That(targetProperties.GetTexture("_BaseMap"), Is.SameAs(sourceProperties.GetTexture("_BaseMap")),
                    label + " " + binding.MeshName + " keeps the same expression/skin atlas.");
                Assert.That(targetProperties.GetColor("_BaseColor"), Is.EqualTo(sourceProperties.GetColor("_BaseColor")),
                    label + " " + binding.MeshName + " keeps the same illness tint.");
                if (originalShape >= 0)
                {
                    if (level == 1) Assert.That(original.GetBlendShapeWeight(originalShape), Is.Zero);
                    else Assert.That(original.GetBlendShapeWeight(originalShape), Is.GreaterThan(0f));
                }
                matched++;
            }
            Assert.That(matched, Is.GreaterThan(0), label + " must copy actual authored corruption geometry.");
        }
    }
}

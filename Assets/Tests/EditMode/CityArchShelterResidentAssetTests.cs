using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade.Tests.EditMode
{
    [Category("CityArchShelter")]
    public sealed class CityArchShelterResidentAssetTests
    {
        private const string AnimationManifestPath =
            "Assets/Pedestrians/Animations/NightlifeShelterResidents.json";

        [Test]
        public void Build_InvalidProvidersLeaveNoPartialShelterRoot()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            CityArchShelterPlan plan = CityArchShelterPlanner.Create(layout);
            Assert.That(plan.IsEnabled, Is.True);
            var parent = new GameObject("Shelter Failure Test Parent");
            try
            {
                Assert.Throws<InvalidOperationException>(() =>
                    CityArchShelterWorldBuilder.Build(
                        parent.transform,
                        layout,
                        plan,
                        null,
                        null));
                Assert.That(
                    parent.transform.Find(
                        CityArchShelterWorldBuilder.RootName),
                    Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void Provider_BindsThreeDetailedPassiveHeroCompatibleLoops()
        {
            CityArchShelterResidentProvider provider =
                CityArchShelterResidentProvider.Load();
            Assert.That(provider, Is.Not.Null);
            Assert.DoesNotThrow(provider.ValidateOrThrow);

            CityArchShelterResidentRole[] roles =
                (CityArchShelterResidentRole[])Enum.GetValues(
                    typeof(CityArchShelterResidentRole));
            Avatar sharedAvatar = null;
            foreach (CityArchShelterResidentRole role in roles)
            {
                GameObject prefab = provider.GetPrefab(role);
                Assert.That(prefab, Is.Not.Null, role.ToString());
                Assert.That(
                    AssetDatabase.GetAssetPath(prefab),
                    Does.StartWith("Assets/Pedestrians/Staged/Prefabs/"));

                GameObject instance = UnityEngine.Object.Instantiate(prefab);
                try
                {
                    CityArchShelterResidentAssetRegistry registry = instance
                        .GetComponentInChildren<
                            CityArchShelterResidentAssetRegistry>(true);
                    Assert.That(registry, Is.Not.Null);
                    Assert.That(registry.Role, Is.EqualTo(role));
                    Assert.That(registry.Animator.enabled, Is.True);
                    Assert.That(registry.Animator.avatar.isValid, Is.True);
                    Assert.That(
                        registry.Animator.runtimeAnimatorController,
                        Is.Null);
                    Assert.That(registry.Animator.applyRootMotion, Is.False);
                    if (sharedAvatar == null)
                    {
                        sharedAvatar = registry.Animator.avatar;
                    }
                    else
                    {
                        Assert.That(
                            registry.Animator.avatar,
                            Is.SameAs(sharedAvatar));
                    }

                    Assert.That(
                        registry.IdleClip.name,
                        Is.EqualTo(ExpectedClipName(role)));
                    Assert.That(
                        registry.IdleClip.length,
                        Is.EqualTo(ExpectedClipLength(role)).Within(0.002f));
                    Assert.That(registry.IdleClip.isLooping, Is.True);
                    Assert.That(
                        AssetDatabase.GetAssetPath(registry.IdleClip),
                        Is.EqualTo(
                            "Assets/Pedestrians/Animations/" +
                            "NightlifeShelterResidents.fbx"));

                    Assert.That(registry.DetailAtlas.width, Is.EqualTo(256));
                    Assert.That(registry.DetailAtlas.height, Is.EqualTo(256));
                    Assert.That(
                        registry.DetailAtlas.filterMode,
                        Is.EqualTo(FilterMode.Point));
                    Assert.That(
                        registry.DetailAtlas.wrapMode,
                        Is.EqualTo(TextureWrapMode.Clamp));
                    Assert.That(registry.DetailAtlas.mipmapCount, Is.EqualTo(1));
                    Assert.That(registry.TriangleCount, Is.InRange(1500, 2300));
                    Assert.That(
                        registry.RendererBindings.Count,
                        Is.EqualTo(
                            instance.GetComponentsInChildren<Renderer>(true)
                                .Length));
                    Assert.That(registry.LocalBounds.size.sqrMagnitude,
                        Is.GreaterThan(0f));

                    registry.ApplyAppearance();
                    var properties = new MaterialPropertyBlock();
                    int texturedRendererCount = 0;
                    foreach (CityArchShelterResidentRendererBinding binding in
                             registry.RendererBindings)
                    {
                        Assert.That(binding, Is.Not.Null);
                        Assert.That(binding.Renderer, Is.Not.Null);
                        Assert.That(
                            binding.Renderer.shadowCastingMode,
                            Is.EqualTo(ShadowCastingMode.On));
                        Assert.That(binding.Renderer.receiveShadows, Is.True);
                        if (!binding.UsesDetailAtlas)
                        {
                            continue;
                        }

                        binding.Renderer.GetPropertyBlock(properties);
                        Assert.That(
                            properties.GetTexture("_BaseMap"),
                            Is.SameAs(registry.DetailAtlas));
                        properties.Clear();
                        texturedRendererCount++;
                    }

                    Assert.That(texturedRendererCount,
                        Is.GreaterThanOrEqualTo(8));
                    Assert.That(
                        instance.GetComponentsInChildren<Collider>(true),
                        Is.Empty);
                    Assert.That(
                        instance.GetComponentsInChildren<Collider2D>(true),
                        Is.Empty);
                    Assert.That(
                        instance.GetComponentsInChildren<Rigidbody>(true),
                        Is.Empty);
                    Assert.That(
                        instance.GetComponentsInChildren<Rigidbody2D>(true),
                        Is.Empty);
                    Assert.That(
                        instance.GetComponentsInChildren<AudioSource>(true),
                        Is.Empty);
                    Assert.That(
                        instance.GetComponentsInChildren<Light>(true),
                        Is.Empty);
                    Assert.That(
                        instance.GetComponentsInChildren<
                            PlayerAttentionMagnet>(true),
                        Is.Empty);

                    var presentation = instance.AddComponent<
                        CityArchShelterResidentPresentation>();
                    presentation.Initialize(registry, 9001 + (int)role);
                    Assert.That(presentation.IsInitialized, Is.True);
                    Assert.That(presentation.Role, Is.EqualTo(role));
                    Assert.That(
                        presentation.ActiveClip,
                        Is.SameAs(registry.IdleClip));
                    Assert.That(
                        presentation.PlaybackSpeed,
                        Is.InRange(0.96f, 1.04f));
                    Assert.That(
                        presentation.NormalizedTime,
                        Is.InRange(0f, 1f));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void AnimationManifest_EnvelopesFitTheAuthoredMattress()
        {
            TextAsset source = AssetDatabase.LoadAssetAtPath<TextAsset>(
                AnimationManifestPath);
            Assert.That(source, Is.Not.Null);
            AnimationManifest manifest = JsonUtility.FromJson<
                AnimationManifest>(source.text);
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.clips, Has.Length.EqualTo(3));

            foreach (AnimationClipManifest clip in manifest.clips)
            {
                AssertVector2(clip.animated_local_xz_min_m);
                AssertVector2(clip.animated_local_xz_max_m);
                AssertVector2(clip.animated_local_xz_size_m);
                for (int axis = 0; axis < 2; axis++)
                {
                    float measured = clip.animated_local_xz_max_m[axis] -
                                     clip.animated_local_xz_min_m[axis];
                    Assert.That(
                        clip.animated_local_xz_size_m[axis],
                        Is.EqualTo(measured).Within(0.00001f));
                    Assert.That(measured, Is.GreaterThan(0f));
                }
            }

            AnimationClipManifest sleeper = manifest.clips.Single(clip =>
                clip.name == "ShelterSleeperBreath");
            AnimationClipManifest seated = manifest.clips.Single(clip =>
                clip.name == "ShelterSeatedWarm");
            Assert.That(seated.seated, Is.True);
            Assert.That(seated.floor_seated, Is.True);
            Assert.That(
                seated.floor_seated_hip_contact_min_m,
                Is.InRange(0f, 0.025f));
            Assert.That(
                seated.floor_seated_hip_contact_max_m,
                Is.InRange(
                    seated.floor_seated_hip_contact_min_m,
                    0.03f));
            Assert.That(
                seated.floor_seated_boot_contact_min_m,
                Is.InRange(0f, 0.015f));
            Assert.That(
                seated.floor_seated_boot_contact_max_m,
                Is.InRange(
                    seated.floor_seated_boot_contact_min_m,
                    0.02f));
            Assert.That(
                seated.floor_seated_min_boot_separation_m,
                Is.GreaterThanOrEqualTo(0.18f));
            AssertVector2(sleeper.mattress_footprint_m);
            AssertVector2(sleeper.mattress_used_half_extents_m);
            AssertVector2(sleeper.mattress_clearance_m);
            AssertVector2(sleeper.animated_mattress_xz_min_m);
            AssertVector2(sleeper.animated_mattress_xz_max_m);
            Assert.That(
                sleeper.mattress_footprint_m,
                Is.EqualTo(new[]
                {
                    CityArchShelterPlanner.BeddingMattressLength,
                    CityArchShelterPlanner.BeddingMattressWidth
                }).Within(0.00001f));
            Assert.That(
                sleeper.mattress_yaw_degrees,
                Is.EqualTo(0f).Within(0.00001f));
            for (int axis = 0; axis < 2; axis++)
            {
                float measuredHalfExtent = Mathf.Max(
                    Mathf.Abs(sleeper.animated_mattress_xz_min_m[axis]),
                    Mathf.Abs(sleeper.animated_mattress_xz_max_m[axis]));
                Assert.That(
                    sleeper.mattress_used_half_extents_m[axis],
                    Is.EqualTo(measuredHalfExtent)
                        .Within(0.00001f));
                Assert.That(
                    sleeper.mattress_clearance_m[axis],
                    Is.GreaterThanOrEqualTo(0f));
                Assert.That(
                    sleeper.mattress_used_half_extents_m[axis] +
                    sleeper.mattress_clearance_m[axis],
                    Is.EqualTo(sleeper.mattress_footprint_m[axis] * 0.5f)
                        .Within(0.00001f));
            }

            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            CityArchShelterPlan plan = CityArchShelterPlanner.Create(layout);
            CityArchShelterPropDescriptor bedding = plan.Props.Single(prop =>
                prop.Kind == CityArchShelterPropKind.Bedding);
            Assert.That(
                bedding.Bounds.size.x,
                Is.GreaterThan(sleeper.mattress_footprint_m[0]));
            Assert.That(
                bedding.Bounds.size.z,
                Is.GreaterThan(sleeper.mattress_footprint_m[1]));
            CityArchShelterNpcAnchorDescriptor sleeperAnchor = plan
                .NpcAnchors.Single(anchor =>
                    anchor.Stage == CityArchShelterNpcStageKind.Sleeper);
            Assert.That(
                sleeperAnchor.Position,
                Is.EqualTo(new Vector3(
                    bedding.Position.x,
                    bedding.Position.y +
                    CityArchShelterPlanner.BeddingMattressTop,
                    bedding.Position.z)));
            Assert.That(
                Mathf.DeltaAngle(
                    bedding.Rotation.eulerAngles.y,
                    Quaternion.LookRotation(
                        sleeperAnchor.Facing,
                        Vector3.up).eulerAngles.y),
                Is.EqualTo(0f).Within(0.00001f));
        }

        [Test]
        public void AnimationManifest_DeclaresDistinctActiveMotionBeats()
        {
            TextAsset source = AssetDatabase.LoadAssetAtPath<TextAsset>(
                AnimationManifestPath);
            Assert.That(source, Is.Not.Null);
            AnimationManifest manifest = JsonUtility.FromJson<
                AnimationManifest>(source.text);
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.clips, Has.Length.EqualTo(3));

            AssertMotionBeats(
                manifest.clips.Single(clip =>
                    clip.name == "ShelterStandingWarm"),
                new[]
                {
                    "hands_at_heat",
                    "rub_palms_left",
                    "rub_palms_right",
                    "rub_palms_left",
                    "rub_palms_right",
                    "hands_at_heat"
                },
                new[] { 0.125f, 0.375f, 0.4375f, 0.5f, 0.5625f, 0.875f });
            AssertMotionBeats(
                manifest.clips.Single(clip =>
                    clip.name == "ShelterSeatedWarm"),
                new[]
                {
                    "cold_shiver",
                    "both_hands_to_heat",
                    "arms_fold_for_warmth",
                    "both_hands_to_heat"
                },
                new[] { 0.125f, 0.5f, 0.75f, 0.875f });
            AssertMotionBeats(
                manifest.clips.Single(clip =>
                    clip.name == "ShelterSleeperBreath"),
                new[]
                {
                    "deep_inhale",
                    "deep_exhale",
                    "curl_tighter",
                    "shoulder_resettle",
                    "deep_inhale"
                },
                new[] { 0.125f, 0.25f, 0.5f, 0.75f, 0.875f });
        }

        private static void AssertMotionBeats(
            AnimationClipManifest clip,
            string[] expectedNames,
            float[] expectedTimes)
        {
            Assert.That(clip, Is.Not.Null);
            Assert.That(expectedTimes, Has.Length.EqualTo(expectedNames.Length));
            Assert.That(
                clip.motion_beats,
                Has.Length.EqualTo(expectedNames.Length),
                clip.name);

            float previousTime = -1f;
            for (int index = 0; index < expectedNames.Length; index++)
            {
                MotionBeatManifest beat = clip.motion_beats[index];
                Assert.That(beat, Is.Not.Null, $"{clip.name} beat {index}");
                Assert.That(
                    beat.name,
                    Is.EqualTo(expectedNames[index]),
                    $"{clip.name} beat {index}");
                Assert.That(
                    beat.normalized_time,
                    Is.EqualTo(expectedTimes[index]).Within(0.000001f),
                    $"{clip.name} beat {beat.name}");
                Assert.That(
                    beat.normalized_time,
                    Is.InRange(0f, 1f),
                    $"{clip.name} beat {beat.name}");
                Assert.That(
                    beat.normalized_time,
                    Is.GreaterThan(previousTime),
                    $"{clip.name} beat order");
                previousTime = beat.normalized_time;
            }
        }

        private static void AssertVector2(float[] values)
        {
            Assert.That(values, Has.Length.EqualTo(2));
            Assert.That(
                values.All(value =>
                    !float.IsNaN(value) && !float.IsInfinity(value)),
                Is.True);
        }

        private static string ExpectedClipName(
            CityArchShelterResidentRole role)
        {
            switch (role)
            {
                case CityArchShelterResidentRole.StandingWarmer:
                    return "ShelterStandingWarm";
                case CityArchShelterResidentRole.SeatedWarmer:
                    return "ShelterSeatedWarm";
                case CityArchShelterResidentRole.Sleeper:
                    return "ShelterSleeperBreath";
                default:
                    throw new ArgumentOutOfRangeException(nameof(role));
            }
        }

        private static float ExpectedClipLength(
            CityArchShelterResidentRole role)
        {
            switch (role)
            {
                case CityArchShelterResidentRole.StandingWarmer:
                    return 8f;
                case CityArchShelterResidentRole.SeatedWarmer:
                    return 9f;
                case CityArchShelterResidentRole.Sleeper:
                    return 10f;
                default:
                    throw new ArgumentOutOfRangeException(nameof(role));
            }
        }

        [Serializable]
        private sealed class AnimationManifest
        {
            public AnimationClipManifest[] clips;
        }

        [Serializable]
        private sealed class AnimationClipManifest
        {
            public string name;
            public MotionBeatManifest[] motion_beats;
            public float[] animated_local_xz_min_m;
            public float[] animated_local_xz_max_m;
            public float[] animated_local_xz_size_m;
            public float[] mattress_footprint_m;
            public float mattress_yaw_degrees;
            public float[] animated_mattress_xz_min_m;
            public float[] animated_mattress_xz_max_m;
            public float[] mattress_used_half_extents_m;
            public float[] mattress_clearance_m;
            public bool seated;
            public bool floor_seated;
            public float floor_seated_hip_contact_min_m;
            public float floor_seated_hip_contact_max_m;
            public float floor_seated_boot_contact_min_m;
            public float floor_seated_boot_contact_max_m;
            public float floor_seated_min_boot_separation_m;
        }

        [Serializable]
        private sealed class MotionBeatManifest
        {
            public string name;
            public float normalized_time;
        }
    }
}

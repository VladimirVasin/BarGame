using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityBalconySmokerTests
    {
        private const int Seed = GameSessionState.DefaultCitySeed;
        [Test]
        public void Plan_IsDeterministicSparseAndUsesAuthoredBalconyDocks()
        {
            CityLayout layout = CreateLayout();
            CityBuildingAssetRegistry registry =
                GetResidentialRegistry();

            CityBalconySmokerPlan first =
                CityBalconySmokerPlan.Create(layout, registry);
            CityBalconySmokerPlan second =
                CityBalconySmokerPlan.Create(layout, registry);

            int eligible = layout.BuildingLots.Count(item =>
                item.IsOrdinaryBuilding &&
                item.District == CityDistrictKind.Residential);
            Assert.That(first.Space,
                Is.EqualTo(CityBalconySmokerSpace.CityWorld));
            Assert.That(first.Count,
                Is.LessThanOrEqualTo(
                    CityBalconySmokerPlan.MaximumSmokerCount));
            Assert.That(first.Count,
                Is.EqualTo(second.Count));
            if (eligible > 0 && registry.BalconySlots.Count > 0)
            {
                Assert.That(first.Count, Is.GreaterThanOrEqualTo(1));
            }

            CollectionAssert.AreEqual(
                first.Smokers.Select(item => item.StableId),
                second.Smokers.Select(item => item.StableId));
            var occupiedLots = new HashSet<Vector2Int>();
            for (int index = 0; index < first.Smokers.Count; index++)
            {
                CityBalconySmokerDescriptor descriptor =
                    first.Smokers[index];
                CityBalconySmokerDescriptor repeated =
                    second.Smokers[index];
                Assert.That(occupiedLots.Add(descriptor.LotCell), Is.True);
                Assert.That(descriptor.Position,
                    Is.EqualTo(repeated.Position));
                Assert.That(descriptor.Facing,
                    Is.EqualTo(repeated.Facing));
                Assert.That(descriptor.PaletteVariant,
                    Is.EqualTo(repeated.PaletteVariant));
                Assert.That(descriptor.AnimationPhase01,
                    Is.EqualTo(repeated.AnimationPhase01));
                Assert.That(descriptor.ArchetypeDesignId,
                    Is.EqualTo(repeated.ArchetypeDesignId));
                Assert.That(
                    CityBalconySmokerArchetypeCatalog.IsEligible(
                        descriptor.ArchetypeDesignId),
                    Is.True);

                BuildingLot lot = layout.BuildingLots.Single(item =>
                    item.Cell == descriptor.LotCell);
                Assert.That(lot.IsOrdinaryBuilding, Is.True);
                Assert.That(lot.IsPlayerHome, Is.False);
                Assert.That(lot.District,
                    Is.EqualTo(CityDistrictKind.Residential));
                CityBuildingBalconySlot slot =
                    registry.BalconySlots.Single(item =>
                        item.StableId ==
                        descriptor.BalconySlotStableId);
                float lowestDock = registry.BalconySlots.Min(item =>
                    item.LocalNpcDock.y);
                Assert.That(slot.LocalNpcDock.y,
                    Is.EqualTo(lowestDock).Within(
                        CityBalconySmokerPlan
                            .ReadableBalconyRowTolerance),
                    "City fog and the real player camera make upper-storey " +
                    "smokers unreadable from the street.");
                CityBuildingPrototypePose pose =
                    CityBuildingPrototypePlacement.ResolveCityPose(
                        lot,
                        registry);
                AssertVector(
                    descriptor.Position,
                    pose.TransformPoint(slot.LocalNpcDock));
                AssertVector(
                    descriptor.Facing,
                    pose.Rotation * slot.LocalOutward);
                Assert.That(descriptor.PaletteVariant, Is.InRange(0, 3));
                Assert.That(descriptor.AnimationPhase01,
                    Is.InRange(0f, 1f));
            }
        }

        [Test]
        public void CandidateCatalogue_CoversEveryEligibleBuilding()
        {
            CityLayout layout = CreateLayout();
            CityBuildingAssetRegistry registry =
                GetResidentialRegistry();

            IReadOnlyList<CityBalconySmokerDescriptor> first =
                CityBalconySmokerPlan.CreateCandidates(layout, registry);
            IReadOnlyList<CityBalconySmokerDescriptor> second =
                CityBalconySmokerPlan.CreateCandidates(layout, registry);
            int eligible = layout.BuildingLots.Count(item =>
                item.IsOrdinaryBuilding &&
                item.District == CityDistrictKind.Residential);

            Assert.That(first.Count, Is.EqualTo(eligible));
            Assert.That(first.Count,
                Is.GreaterThan(CityBalconySmokerPlan.MaximumSmokerCount),
                "The runtime must choose from the current player area, not " +
                "from one or two buildings fixed at city load.");
            CollectionAssert.AreEqual(
                first.Select(item => item.StableId),
                second.Select(item => item.StableId));
            Assert.That(
                first.Select(item => item.LotCell).Distinct().Count(),
                Is.EqualTo(first.Count));
            Assert.That(
                first.All(item =>
                    CityBalconySmokerArchetypeCatalog.IsEligible(
                        item.ArchetypeDesignId)),
                Is.True);
            float lowestDock = registry.BalconySlots.Min(item =>
                item.LocalNpcDock.y);
            Assert.That(
                first.All(item =>
                    Mathf.Abs(
                        registry.BalconySlots.Single(slot =>
                            slot.StableId == item.BalconySlotStableId)
                            .LocalNpcDock.y - lowestDock) <=
                    CityBalconySmokerPlan.ReadableBalconyRowTolerance),
                Is.True);
        }

        [Test]
        public void Director_SpawnsLocallyThenReleasesByPlayerDistance()
        {
            CityLayout layout = CreateLayout();
            CityBalconySmokerDescriptor candidate =
                CityBalconySmokerPlan
                    .CreateCandidates(layout, GetResidentialRegistry())[0];
            var player = new GameObject("Balcony Director Player");
            var root = new GameObject("Balcony Director Test");
            CityBalconySmokerDirector director = null;
            try
            {
                player.transform.position = candidate.Position +
                    (candidate.Facing * -
                     CityBalconySmokerDirector
                         .PreferredMinimumSpawnDistance) +
                    (Vector3.down * 12f);
                director = root.AddComponent<
                    CityBalconySmokerDirector>();
                director.Initialize(
                    layout.Seed,
                    new[] { candidate },
                    player.transform,
                    forcedRandomState: 1u);

                director.Advance(
                    CityBalconySmokerDirector
                        .MaximumInitialSpawnDelay + 0.01f);
                director.Advance(
                    CityBalconySmokerDirector
                        .MaximumSpawnRetryDelay + 0.01f);
                Assert.That(director.ActiveCount, Is.Zero,
                    "A balcony on the far side of its source building must " +
                    "not consume the local active cap.");

                player.transform.position = candidate.Position +
                    (candidate.Facing *
                     CityBalconySmokerDirector
                         .PreferredMinimumSpawnDistance) +
                    (Vector3.down * 12f);
                director.Advance(
                    CityBalconySmokerDirector
                        .MaximumSpawnRetryDelay + 0.01f);
                if (director.ActiveCount == 0)
                {
                    director.Advance(
                        CityBalconySmokerDirector
                            .MaximumSpawnRetryDelay + 0.01f);
                }

                Assert.That(director.ActiveCount, Is.EqualTo(1),
                    "An eligible residential area may miss one chance, but " +
                    "must not remain empty through a second opportunity.");
                Assert.That(director.IsActive(candidate.StableId), Is.True);
                CityBalconySmokerPresentation presentation =
                    director.GetActivePresentation(candidate.StableId);
                Assert.That(presentation, Is.Not.Null);
                Assert.That(presentation.Descriptor.StableId,
                    Is.EqualTo(candidate.StableId));
                Assert.That(presentation.transform.position,
                    Is.EqualTo(candidate.Position));
                Assert.That(
                    Vector3.Distance(
                        player.transform.position,
                        presentation.transform.position),
                    Is.LessThanOrEqualTo(
                        CityBalconySmokerDirector
                            .MaximumVisibleSpawnDistance));
                Vector3 candidateToPlayer =
                    player.transform.position - candidate.Position;
                candidateToPlayer.y = 0f;
                Assert.That(
                    Vector3.Dot(
                        candidate.Facing,
                        candidateToPlayer.normalized),
                    Is.GreaterThanOrEqualTo(
                        CityBalconySmokerDirector
                            .MinimumFrontFacingDot));

                player.transform.position = candidate.Position +
                    (candidate.Facing *
                     (CityBalconySmokerDirector.DespawnDistance + 0.1f));
                director.Advance(0f);

                Assert.That(director.ActiveCount, Is.Zero);
                Assert.That(director.IsActive(candidate.StableId), Is.False);
            }
            finally
            {
                director?.Shutdown();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void Director_PrefersAnEligibleBalconyAheadOfTravel()
        {
            string archetype = CityBalconySmokerArchetypeCatalog
                .EligibleDesignIds[0];
            var ahead = new CityBalconySmokerDescriptor(
                "ahead-balcony-smoker",
                new Vector2Int(1, 1),
                "ahead-lower-balcony",
                archetype,
                new Vector3(0f, 7f, 15f),
                Vector3.back,
                new Vector3(0f, 7f, 15f),
                Vector3.back,
                0,
                0f);
            var behind = new CityBalconySmokerDescriptor(
                "behind-balcony-smoker",
                new Vector2Int(2, 2),
                "behind-lower-balcony",
                archetype,
                new Vector3(0f, 7f, -15f),
                Vector3.forward,
                new Vector3(0f, 7f, -15f),
                Vector3.forward,
                1,
                0.5f);
            var player = new GameObject("Balcony Heading Player");
            var root = new GameObject("Balcony Heading Director Test");
            CityBalconySmokerDirector director = null;
            try
            {
                director = root.AddComponent<
                    CityBalconySmokerDirector>();
                director.Initialize(
                    Seed,
                    new[] { behind, ahead },
                    player.transform,
                    forcedRandomState: 1u);
                player.transform.position = Vector3.forward *
                    (CityBalconySmokerDirector
                         .HeadingRefreshMovement + 0.01f);

                director.Advance(
                    CityBalconySmokerDirector
                        .MaximumInitialSpawnDelay + 0.01f);
                if (director.ActiveCount == 0)
                {
                    director.Advance(
                        CityBalconySmokerDirector
                            .MaximumSpawnRetryDelay + 0.01f);
                }

                Assert.That(director.IsActive(ahead.StableId), Is.True);
                Assert.That(director.IsActive(behind.StableId), Is.False);
            }
            finally
            {
                director?.Shutdown();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void TransformForHome_KeepsOnlySameFullNearbyPrototypes()
        {
            CityLayout layout = CreateLayout();
            HomeExteriorContextPlan context =
                HomeExteriorContextPlanner.Generate(layout);
            CityBalconySmokerPlan city =
                CityBalconySmokerPlan.Create(
                    layout,
                    GetResidentialRegistry());

            CityBalconySmokerPlan home = city.TransformForHome(context);

            CityBalconySmokerDescriptor[] expected = city.Smokers
                .Where(item => IsFullyImported(context, item.LotCell))
                .OrderBy(item => item.StableId)
                .ToArray();
            Assert.That(home.Space,
                Is.EqualTo(CityBalconySmokerSpace.HomeLocal));
            CollectionAssert.AreEqual(
                expected.Select(item => item.StableId),
                home.Smokers.Select(item => item.StableId));
            for (int index = 0; index < expected.Length; index++)
            {
                CityBalconySmokerDescriptor source = expected[index];
                CityBalconySmokerDescriptor transformed =
                    home.Smokers[index];
                AssertVector(
                    transformed.Position,
                    PlayerHomeBalconyGeometry.ToHomeLocal(
                        context.PlayerHome,
                        source.CityWorldPosition));
                AssertVector(
                    transformed.Facing,
                    PlayerHomeBalconyGeometry.ToHomeLocalDirection(
                        context.PlayerHome,
                        source.CityWorldFacing));
                AssertVector(
                    transformed.CityWorldPosition,
                    source.CityWorldPosition);
                AssertVector(
                    transformed.CityWorldFacing,
                    source.CityWorldFacing);
                Assert.That(transformed.PaletteVariant,
                    Is.EqualTo(source.PaletteVariant));
                Assert.That(transformed.AnimationPhase01,
                    Is.EqualTo(source.AnimationPhase01));
                Assert.That(transformed.ArchetypeDesignId,
                    Is.EqualTo(source.ArchetypeDesignId));
            }
        }

        [Test]
        public void ArchetypeChoice_IsDeterministicAndVariesAcrossSeeds()
        {
            IReadOnlyList<string> eligible =
                CityBalconySmokerArchetypeCatalog.EligibleDesignIds;
            Assert.That(eligible.Count, Is.GreaterThanOrEqualTo(2));
            CollectionAssert.AreEqual(
                CityPedestrianResources.Archetypes.Select(
                    item => item.DesignId),
                eligible,
                "Every current roaming pedestrian has the shared Hero V2 " +
                "Avatar and canonical smoking sockets; none needs an " +
                "arbitrary silhouette exclusion.");
            var observed = new HashSet<string>();
            var cell = new Vector2Int(7, 11);
            for (int seed = 0; seed < 128; seed++)
            {
                string first = CityBalconySmokerPlan
                    .ResolveArchetypeDesignId(seed, cell, eligible);
                string second = CityBalconySmokerPlan
                    .ResolveArchetypeDesignId(seed, cell, eligible);
                Assert.That(second, Is.EqualTo(first));
                Assert.That(eligible, Does.Contain(first));
                observed.Add(first);
            }

            Assert.That(observed.Count, Is.GreaterThan(1));
        }

        [Test]
        public void AnimationDefinition_MirrorsHeroSmokingHolds()
        {
            PlayerAnimatedInteractionDefinition definition =
                CityBalconySmokerPresentation
                    .CreateAnimationDefinition();

            Assert.That(definition.LoopClipName,
                Is.EqualTo("SmokeLoop"));
            Assert.That(definition.LoopFrameCount,
                Is.EqualTo(HomeBalconySmokingPlan.LoopFrameCount));
            Assert.That(definition.LoopFramesPerSecond,
                Is.EqualTo(HomeBalconySmokingPlan.LoopFramesPerSecond));
            Assert.That(
                definition.GetLoopFrameExtraHoldSeconds(
                    HomeBalconySmokingPlan.RestHoldLoopFrame),
                Is.EqualTo(HomeBalconySmokingPlan.RestHoldSeconds));
            Assert.That(
                definition.GetLoopFrameExtraHoldSeconds(
                    HomeBalconySmokingPlan.InhaleHoldLoopFrame),
                Is.EqualTo(HomeBalconySmokingPlan.InhaleHoldSeconds));
            Assert.That(
                definition.GetLoopFrameExtraHoldSeconds(
                    HomeBalconySmokingPlan.BreathHoldLoopFrame),
                Is.EqualTo(HomeBalconySmokingPlan.BreathHoldSeconds));
            Assert.That(
                definition.GetLoopFrameExtraHoldSeconds(
                    HomeBalconySmokingPlan.ExhaleHoldLoopFrame),
                Is.EqualTo(HomeBalconySmokingPlan.ExhaleHoldSeconds));
        }

        /// <summary>
        /// The cigarette is no longer borrowed from the babushka's skin:
        /// any eligible design takes the cigarette hand prop on its own
        /// `SOCKET_Cigarette.R`, and the prop is plain mesh geometry that
        /// rides the socket. A design without that socket is ineligible
        /// upstream, so here the attach must simply land.
        /// </summary>
        [Test]
        public void AttachedCigarette_IsTheHandPropUnderTheCigaretteSocket()
        {
            string designId = CityBalconySmokerArchetypeCatalog
                .EligibleDesignIds
                .First(item => item !=
                    CityPedestrianResources.BabushkaDesignId);
            Assert.That(CityPedestrianResources.TryGetArchetype(
                designId,
                out CityPedestrianArchetype archetype), Is.True);
            Assert.That(
                CityPedestrianHandProps.IsAvailable(
                    CityPedestrianHandPropId.Cigarette),
                Is.True,
                "The cigarette hand prop prefab gates every balcony smoker.");
            var parent = new GameObject("Attached Cigarette Test");
            try
            {
                Assert.That(CityPedestrianResources.TryInstantiate(
                    CityPedestrianResources.LoadPrefab(archetype),
                    parent.transform,
                    out CityPedestrianAssetRegistry target), Is.True);

                CityPedestrianHandPropRegistry cigarette =
                    CityPedestrianHandProps.Attach(
                        target,
                        CityPedestrianHandPropId.Cigarette,
                        2);
                Assert.That(cigarette, Is.Not.Null);
                Assert.That(
                    cigarette.Id,
                    Is.EqualTo(CityPedestrianHandPropId.Cigarette));
                Assert.That(cigarette.PaletteVariant, Is.EqualTo(2));
                Transform socket = CityPedestrianHandProps.FindSocket(
                    target.ModelRoot,
                    CityBalconySmokerPresentation.CigaretteSocketName);
                Assert.That(socket, Is.Not.Null, designId);
                Assert.That(cigarette.transform.parent, Is.SameAs(socket));
                Assert.That(
                    Vector3.Distance(
                        cigarette.transform.position,
                        socket.position),
                    Is.LessThan(0.02f),
                    "The prop root must sit on the socket.");
                CollectionAssert.AreEquivalent(
                    new[] { "ACC_Cigarette", "ACC_CigaretteEmber" },
                    cigarette.Renderers.Select(item => item.name));
                foreach (Renderer renderer in cigarette.Renderers)
                {
                    Assert.That(
                        renderer,
                        Is.InstanceOf<MeshRenderer>(),
                        renderer.name);
                    Assert.That(
                        renderer.transform.IsChildOf(socket),
                        Is.True,
                        renderer.name);
                    Assert.That(
                        renderer.sharedMaterial,
                        Is.SameAs(target.Renderers[0].sharedMaterial),
                        "The prop renders in the body's shared material.");
                }
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void Factory_UsesLiteralHeroClipAndAuthoredCigaretteProps()
        {
            CityLayout layout = CreateLayout();
            CityBalconySmokerPlan plan =
                CityBalconySmokerPlan.Create(
                    layout,
                    GetResidentialRegistry());
            var parent = new GameObject("Balcony Smoker Factory Test");
            CityBalconySmokerRuntime runtime = null;
            try
            {
                runtime = CityBalconySmokerFactory.Create(
                    parent.transform,
                    plan);
                Player3DAssetRegistry hero = Player3DResources
                    .LoadPrefab()
                    .GetComponent<Player3DAssetRegistry>();
                Assert.That(hero.TryGetAnimation(
                    "SmokeLoop",
                    out Player3DAnimationBinding smokeBinding), Is.True);

                Assert.That(runtime.RootGameObject, Is.Not.Null);
                Assert.That(runtime.RootGameObject.name,
                    Is.EqualTo(CityBalconySmokerFactory.RuntimeRootName));
                Assert.That(runtime.Count, Is.EqualTo(plan.Count));
                foreach (CityBalconySmokerPresentation presentation in
                         runtime.Presentations)
                {
                    Assert.That(presentation.IsInitialized, Is.True);
                    Assert.That(presentation.Registry.DesignId,
                        Is.EqualTo(
                            presentation.Descriptor.ArchetypeDesignId));
                    Assert.That(presentation.Registry.Animator.avatar,
                        Is.SameAs(hero.Animator.avatar));
                    Assert.That(presentation.ActiveClip,
                        Is.SameAs(smokeBinding.Clip));
                    Assert.That(presentation.ExhaleEffect.IsManualBurstMode,
                        Is.True);
                    Assert.That(
                        presentation.ExhaleEffect.Particles.emission
                            .burstCount,
                        Is.Zero);
                    AssertAttachedCigarette(presentation);
                    AssertBodyCarriesNoProp(presentation.Registry);
                }

                AssertPassive(runtime.RootGameObject);
                runtime.SetVisible(false);
                Assert.That(runtime.IsVisible, Is.False);
                runtime.SetVisible(true);
                Assert.That(runtime.IsVisible, Is.True);
                if (runtime.Count > 0)
                {
                    HomeBalconySmokingExhaleEffect effect =
                        runtime.Presentations[0].ExhaleEffect;
                    Assert.That(effect.EmitManualBurst(), Is.True);
                    Assert.That(effect.ManualBurstCount, Is.EqualTo(1));
                }

                runtime.Shutdown();
                Assert.That(runtime.RootGameObject, Is.Null);
                Assert.That(runtime.Count, Is.Zero);
            }
            finally
            {
                runtime?.Shutdown();
                Object.DestroyImmediate(parent);
            }
        }

        private static CityLayout CreateLayout()
        {
            return CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                Seed);
        }

        private static CityBuildingAssetRegistry GetResidentialRegistry()
        {
            GameObject prefab = CityBuildingAssetProvider
                .LoadOrThrow()
                .GetPrefabOrThrow(CityDistrictKind.Residential);
            CityBuildingAssetRegistry registry =
                prefab.GetComponent<CityBuildingAssetRegistry>();
            Assert.That(registry, Is.Not.Null);
            registry.ValidateOrThrow();
            return registry;
        }

        private static bool IsFullyImported(
            HomeExteriorContextPlan context,
            Vector2Int lotCell)
        {
            BuildingLot lot = context.NearbyLots.FirstOrDefault(item =>
                item.Cell == lotCell && item.IsOrdinaryBuilding);
            return lot != null &&
                   CityBuildingPrototypeWorldBuilder.ClassifyHomeExterior(
                       context,
                       lot) == CityBuildingExteriorFit.Full;
        }

        private static void AssertAttachedCigarette(
            CityBalconySmokerPresentation presentation)
        {
            CityPedestrianHandPropRegistry held = presentation.HeldCigarette;
            Assert.That(held, Is.Not.Null, "No cigarette hand prop attached.");
            Assert.That(
                held.Id,
                Is.EqualTo(CityPedestrianHandPropId.Cigarette));
            Transform socket = held.transform.parent;
            Assert.That(socket, Is.Not.Null);
            Assert.That(
                socket.name,
                Is.EqualTo(CityBalconySmokerPresentation.CigaretteSocketName));
            Assert.That(
                socket.IsChildOf(presentation.Registry.ModelRoot),
                Is.True,
                "The cigarette socket must belong to this smoker's rig.");
            CollectionAssert.AreEquivalent(
                new[] { "ACC_Cigarette", "ACC_CigaretteEmber" },
                presentation.CigaretteRenderers.Select(item => item.name));
            foreach (Renderer renderer in presentation.CigaretteRenderers)
            {
                Assert.That(renderer.enabled, Is.True, renderer.name);
                Assert.That(
                    renderer,
                    Is.InstanceOf<MeshRenderer>(),
                    renderer.name);
                Assert.That(
                    renderer.transform.IsChildOf(socket),
                    Is.True,
                    renderer.name);
            }
        }

        /// <summary>
        /// The body ships nothing in its hands, so there is nothing to
        /// hide: no renderer on a smoker's body may carry a hand-prop
        /// part name. Exact names, never prefixes — `ACC_PipeManifold`
        /// on the pipeback roller is his own.
        /// </summary>
        private static void AssertBodyCarriesNoProp(
            CityPedestrianAssetRegistry registry)
        {
            HashSet<string> propParts =
                CityPedestrianHandPropTests.CollectPropPartNames();
            foreach (Renderer renderer in registry.Renderers)
            {
                Assert.That(
                    propParts.Contains(renderer.name),
                    Is.False,
                    $"{registry.DesignId} still carries the hand-prop part " +
                    $"'{renderer.name}' on its body.");
            }
        }

        private static void AssertPassive(GameObject root)
        {
            Assert.That(root.GetComponentsInChildren<Collider>(true),
                Is.Empty);
            Assert.That(root.GetComponentsInChildren<Collider2D>(true),
                Is.Empty);
            Assert.That(root.GetComponentsInChildren<Rigidbody>(true),
                Is.Empty);
            Assert.That(root.GetComponentsInChildren<Rigidbody2D>(true),
                Is.Empty);
            Assert.That(root.GetComponentsInChildren<AudioSource>(true),
                Is.Empty);
            Assert.That(root.GetComponentsInChildren<Light>(true),
                Is.Empty);
            Assert.That(root.GetComponentsInChildren<Camera>(true),
                Is.Empty);
            Assert.That(
                root.GetComponentsInChildren<MonoBehaviour>(true)
                    .Any(item => item is IInteractable),
                Is.False);
        }

        private static void AssertVector(Vector3 actual, Vector3 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.001f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.001f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.001f));
        }
    }
}

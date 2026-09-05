using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class DryingYardBabushkaTests
    {
        private const int Seed = GameSessionState.DefaultCitySeed;

        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");

        [Test]
        public void Plan_PlacesThreeBabushkasInsideTheDryingYard()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                Seed);
            DryingYardBabushkaPlan plan =
                DryingYardBabushkaPlan.Create(layout);

            Assert.That(plan.IsPresent, Is.True);
            Assert.That(plan.Stances, Has.Count.EqualTo(3));

            CityDistrictPointOfInterestDescriptor descriptor =
                FindDryingYard(layout);
            Rect bounds = descriptor.PublicBounds;
            int beaterCount = 0;
            int smokerCount = 0;
            var palettes = new HashSet<int>();
            foreach (DryingYardBabushkaStance stance in plan.Stances)
            {
                Assert.That(
                    bounds.Contains(new Vector2(
                        stance.Position.x,
                        stance.Position.z)),
                    Is.True,
                    $"{stance.Role} stands outside the drying yard.");
                Assert.That(
                    stance.Facing.magnitude,
                    Is.EqualTo(1f).Within(0.001f));
                Assert.That(
                    Mathf.Abs(stance.Facing.y),
                    Is.LessThan(0.001f),
                    "Stance facings stay horizontal.");
                Assert.That(palettes.Add(stance.PaletteVariant), Is.True,
                    "Every babushka wears her own palette variant.");
                if (stance.Role == DryingYardBabushkaRole.CarpetBeater)
                {
                    beaterCount++;
                }
                else
                {
                    smokerCount++;
                }
            }

            Assert.That(beaterCount, Is.EqualTo(2));
            Assert.That(smokerCount, Is.EqualTo(1));

            // The two beaters work opposite sides of the rack, each
            // wired to her own carpet, desynchronized so their strikes
            // never fall into lockstep.
            DryingYardBabushkaStance beaterA = plan.Stances[0];
            DryingYardBabushkaStance beaterB = plan.Stances[1];
            Assert.That(
                Vector3.Dot(beaterA.Facing, beaterB.Facing),
                Is.LessThan(-0.9f),
                "The beaters face each other across the rack.");
            Assert.That(beaterA.Strolls, Is.False);
            Assert.That(beaterB.Strolls, Is.False);
            Assert.That(
                beaterA.CarpetId,
                Is.EqualTo(CityDryingYardCarpetRegistry.SouthCarpetId));
            Assert.That(
                beaterB.CarpetId,
                Is.EqualTo(CityDryingYardCarpetRegistry.NorthCarpetId));
            Assert.That(
                beaterA.PlaybackSpeed,
                Is.Not.EqualTo(beaterB.PlaybackSpeed).Within(0.001f));
            Assert.That(
                beaterA.PhaseOffsetSeconds,
                Is.Not.EqualTo(beaterB.PhaseOffsetSeconds)
                    .Within(0.001f));

            // The smoker strolls her corridor back and forth past the
            // beaters, close enough to read as company.
            DryingYardBabushkaStance smoker = plan.Stances[2];
            Assert.That(smoker.Strolls, Is.True);
            Assert.That(smoker.CarpetId, Is.Null);
            Assert.That(
                bounds.Contains(new Vector2(
                    smoker.PathEnd.x,
                    smoker.PathEnd.z)),
                Is.True,
                "The stroll corridor stays inside the drying yard.");
            float pathLength = Vector3.Distance(
                smoker.Position,
                smoker.PathEnd);
            Assert.That(pathLength, Is.InRange(3f, 6f));
            Assert.That(
                Vector3.Dot(
                    smoker.Facing,
                    (smoker.PathEnd - smoker.Position).normalized),
                Is.GreaterThan(0.99f),
                "The smoker starts facing along her corridor.");
            foreach (DryingYardBabushkaStance beater in
                     new[] { beaterA, beaterB })
            {
                float closest = DistancePointToSegment(
                    beater.Position,
                    smoker.Position,
                    smoker.PathEnd);
                Assert.That(
                    closest,
                    Is.InRange(0.6f, 3.6f),
                    "The stroll passes by each beater without " +
                    "walking through her.");
            }
        }

        private static float DistancePointToSegment(
            Vector3 point,
            Vector3 start,
            Vector3 end)
        {
            Vector3 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared < 0.0001f)
            {
                return Vector3.Distance(point, start);
            }

            float amount = Mathf.Clamp01(
                Vector3.Dot(point - start, segment) / lengthSquared);
            return Vector3.Distance(point, start + segment * amount);
        }

        /// <summary>
        /// The carpets are Unity's - simulated cloth on the shared Home
        /// rug albedo, each with its static fold cap over the bar - so
        /// they are still asked for by name.
        ///
        /// The rack they hang on is not. It used to be three runtime
        /// boxes this test named; it is authored geometry now, baked
        /// into the yard's single painted-metal batch, and asking only
        /// that the batch arrived would prove nothing - it carries the
        /// drying frames and the floodlight too, and a sibling test
        /// already pins its presence.
        ///
        /// What the rack still owns on Unity's side is a pair of
        /// obstacle colliders at its posts, and that is the half worth
        /// guarding: a collider with nothing drawn inside it is an
        /// invisible wall. So the posts are checked as geometry and
        /// the crossbar is knowingly left unguarded - nothing collides
        /// with it, and a floating carpet is cheaper than a wall.
        /// </summary>
        [Test]
        public void Build_DryingYardCarriesTheCarpetRack()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                Seed);
            var parent = new GameObject("Carpet Rack Test");
            try
            {
                GameObject root =
                    CityDistrictPointOfInterestWorldBuilder.Build(
                        parent.transform,
                        layout);

                string[] carpetParts =
                {
                    "Beaten Carpet South",
                    "Beaten Carpet North",
                    "Beaten Carpet South Fold",
                    "Beaten Carpet North Fold"
                };
                var found = new HashSet<string>();
                Texture rugTexture =
                    HomeSurfaceAppearance.GetTexture(
                        HomeSurfaceKind.Rug);
                foreach (Renderer renderer in
                         root.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (string part in carpetParts)
                    {
                        if (renderer.name != part)
                        {
                            continue;
                        }

                        found.Add(part);
                        var properties = new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(properties);
                        Assert.That(
                            properties.GetTexture(BaseMapId),
                            Is.SameAs(rugTexture),
                            $"{part} must carry the rug albedo.");
                    }
                }

                Assert.That(found, Is.EquivalentTo(carpetParts));
                AssertRackPostsAreDrawn(root);

                // In the city each carpet is real simulated cloth,
                // registered for the strike driver and deliberately
                // outside the laundry's weather-wind registry.
                Cloth south = CityDryingYardCarpetRegistry.Find(
                    CityDryingYardCarpetRegistry.SouthCarpetId);
                Cloth north = CityDryingYardCarpetRegistry.Find(
                    CityDryingYardCarpetRegistry.NorthCarpetId);
                Assert.That(south, Is.Not.Null);
                Assert.That(north, Is.Not.Null);
                Assert.That(south, Is.Not.SameAs(north));
                Assert.That(
                    south.name,
                    Is.EqualTo("Beaten Carpet South"));
                Assert.That(
                    south.transform.IsChildOf(root.transform),
                    Is.True);
                Assert.That(
                    south.damping,
                    Is.GreaterThan(0.5f),
                    "A heavy pile carpet hangs damped.");

                AssertYardCollidersClearApproaches(root, layout);
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void Build_BabushkaStancesClearTheYardObstacles()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                Seed);
            DryingYardBabushkaPlan plan =
                DryingYardBabushkaPlan.Create(layout);
            Assert.That(plan.IsPresent, Is.True);
            var parent = new GameObject("Babushka Stance Test");
            try
            {
                GameObject root =
                    CityDistrictPointOfInterestWorldBuilder.Build(
                        parent.transform,
                        layout);
                Physics.SyncTransforms();
                foreach (DryingYardBabushkaStance stance in plan.Stances)
                {
                    // A stroller is checked along her whole corridor.
                    var samples = new List<Vector3> { stance.Position };
                    if (stance.Strolls)
                    {
                        for (int step = 1; step <= 4; step++)
                        {
                            samples.Add(Vector3.Lerp(
                                stance.Position,
                                stance.PathEnd,
                                step / 4f));
                        }
                    }

                    foreach (Collider collider in
                             root.GetComponentsInChildren<Collider>(
                                 true))
                    {
                        // The paving is the ground she stands on, not
                        // an obstacle.
                        if (collider.name ==
                            CityDistrictPointOfInterestWorldBuilder
                                .PublicGroundName)
                        {
                            continue;
                        }

                        Bounds bounds = collider.bounds;
                        var footprint = Rect.MinMaxRect(
                            bounds.min.x,
                            bounds.min.z,
                            bounds.max.x,
                            bounds.max.z);
                        // A 0.30 m body radius around each stance and
                        // stroll sample must stay clear of every yard
                        // obstacle.
                        foreach (Vector3 sample in samples)
                        {
                            var body = Rect.MinMaxRect(
                                sample.x - 0.30f,
                                sample.z - 0.30f,
                                sample.x + 0.30f,
                                sample.z + 0.30f);
                            Assert.That(
                                footprint.Overlaps(body),
                                Is.False,
                                $"'{collider.name}' overlaps the " +
                                $"{stance.Role} stance or corridor.");
                        }
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void Provider_BindsTheStagedPrefabWithoutPublishingIt()
        {
            DryingYardBabushkaProvider provider =
                DryingYardBabushkaProvider.Load();
            Assert.That(
                provider,
                Is.Not.Null,
                "Resources/City/DryingYardBabushkaProvider.asset is " +
                "missing.");
            GameObject prefab = provider.StagedPrefab;
            Assert.That(
                prefab,
                Is.Not.Null,
                "The provider must reference the staged babushka " +
                "prefab.");

            var registry =
                prefab.GetComponent<CityPedestrianAssetRegistry>();
            Assert.That(registry, Is.Not.Null);
            Assert.That(
                registry.DesignId,
                Is.EqualTo(DryingYardBabushkaProvider.DesignId));
            Assert.That(registry.IdleClip, Is.Not.Null);
            Assert.That(
                registry.IdleClip.name,
                Is.EqualTo("BabushkaSmoke"));
            Assert.That(registry.WalkClip, Is.Not.Null);
            Assert.That(
                registry.WalkClip.name,
                Is.EqualTo("BabushkaBeat"));
            Assert.That(registry.SitClip, Is.Null);
            Assert.That(registry.IdleClip.isLooping, Is.True);
            Assert.That(registry.WalkClip.isLooping, Is.True);

            // Since 2026-09-05 the body ships empty-handed: the beater
            // and the cigarette are hand-prop prefabs the yard attaches
            // per role, so a roaming copy of her carries nothing. A
            // renderer with a prop's name on the body would be the old
            // skinned prop creeping back into the FBX.
            var rendererNames = new HashSet<string>();
            foreach (Renderer renderer in
                     prefab.GetComponentsInChildren<Renderer>(true))
            {
                rendererNames.Add(renderer.name);
            }

            foreach (string propPart in new[]
                     {
                         "ACC_BeaterHandle",
                         "ACC_BeaterNeck",
                         "ACC_BeaterPaddleRise",
                         "ACC_BeaterPaddleTip",
                         "ACC_Cigarette",
                         "ACC_CigaretteEmber"
                     })
            {
                Assert.That(
                    rendererNames.Contains(propPart),
                    Is.False,
                    $"The babushka body still carries '{propPart}'; hand " +
                    "props are separate prefabs now.");
            }

            Assert.That(
                CityPedestrianHandProps.FindSocket(
                    prefab.transform,
                    CityPedestrianHandPropId.CarpetBeater),
                Is.Not.Null,
                "The beater rides SOCKET_Grip.R.");
            Assert.That(
                CityPedestrianHandProps.FindSocket(
                    prefab.transform,
                    CityPedestrianHandPropId.Cigarette),
                Is.Not.Null,
                "The cigarette rides SOCKET_Cigarette.R.");

            Assert.That(
                prefab.GetComponentsInChildren<Collider>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<Light>(true),
                Is.Empty);
            // She used to be required to stay OUT of Resources, because
            // staged and roaming were opposites. Since 2026-09-02 she is
            // both: she still beats her carpet here, and she also walks the
            // pavement, so the pool has to be able to `Resources.Load` her.
            // What the yard actually depends on is that the provider and the
            // pool agree on ONE prefab - two copies would drift, and the
            // drift would be silent.
            GameObject published =
                Resources.Load<GameObject>("Pedestrians/YardBabushka3D");
            Assert.That(
                published,
                Is.Not.Null,
                "The babushka roams as well as standing here, so her prefab " +
                "must be loadable from Resources.");
            Assert.That(
                published,
                Is.SameAs(prefab),
                "The yard and the street must share one prefab asset.");
            Assert.That(
                CityPedestrianResources.Roams(
                    DryingYardBabushkaProvider.DesignId),
                Is.True);
        }

        /// <summary>
        /// The role decides the prop: a beater holds the carpet beater in
        /// her right grip, the smoker holds the cigarette on the
        /// cigarette socket, and neither holds the other's. Judged in
        /// world space through the presentation's own accessor, because
        /// the old name-table visibility could hide the wrong one
        /// silently and this must not.
        /// </summary>
        [Test]
        public void Presentation_AttachesTheBeaterOrTheCigarettePerRole()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                Seed);
            DryingYardBabushkaPlan plan =
                DryingYardBabushkaPlan.Create(layout);
            Assert.That(plan.IsPresent, Is.True);
            DryingYardBabushkaProvider provider =
                DryingYardBabushkaProvider.Load();
            Assert.That(provider, Is.Not.Null);
            Assert.That(provider.StagedPrefab, Is.Not.Null);

            var parent = new GameObject("Babushka Hand Prop Test");
            try
            {
                bool sawBeater = false;
                bool sawSmoker = false;
                foreach (DryingYardBabushkaStance stance in plan.Stances)
                {
                    GameObject instance = Object.Instantiate(
                        provider.StagedPrefab,
                        parent.transform);
                    var registry =
                        instance.GetComponent<CityPedestrianAssetRegistry>();
                    Assert.That(registry, Is.Not.Null);
                    var presentation = instance
                        .AddComponent<DryingYardBabushkaPresentation>();
                    presentation.Initialize(registry, stance);

                    CityPedestrianHandPropId expectedId =
                        stance.Role == DryingYardBabushkaRole.CarpetBeater
                            ? CityPedestrianHandPropId.CarpetBeater
                            : CityPedestrianHandPropId.Cigarette;
                    CityPedestrianHandPropRegistry held =
                        presentation.HeldProp;
                    Assert.That(held, Is.Not.Null, stance.Role.ToString());
                    Assert.That(
                        held.Id,
                        Is.EqualTo(expectedId),
                        stance.Role.ToString());
                    Transform socket = CityPedestrianHandProps.FindSocket(
                        registry.ModelRoot,
                        expectedId);
                    Assert.That(socket, Is.Not.Null, stance.Role.ToString());
                    Assert.That(
                        held.transform.parent,
                        Is.SameAs(socket),
                        $"The {stance.Role} prop must hang off " +
                        $"'{CityPedestrianHandProps.GetSocketName(expectedId)}'.");
                    Assert.That(
                        Vector3.Distance(
                            held.transform.position,
                            socket.position),
                        Is.LessThan(0.02f),
                        $"The {stance.Role} prop root drifted off its socket.");
                    Assert.That(
                        held.PaletteVariant,
                        Is.EqualTo(stance.PaletteVariant % 4),
                        "The prop wears the stance's palette.");
                    Assert.That(
                        CityPedestrianHandProps.FindAttached(
                            CityPedestrianHandProps.FindSocket(
                                registry.ModelRoot,
                                CityPedestrianHandPropId.CarpetBeater),
                            CityPedestrianHandPropId.CarpetBeater) != null,
                        Is.EqualTo(
                            stance.Role == DryingYardBabushkaRole.CarpetBeater),
                        "Only a beater holds the beater.");
                    Assert.That(
                        CityPedestrianHandProps.FindAttached(
                            CityPedestrianHandProps.FindSocket(
                                registry.ModelRoot,
                                CityPedestrianHandPropId.Cigarette),
                            CityPedestrianHandPropId.Cigarette) != null,
                        Is.EqualTo(
                            stance.Role != DryingYardBabushkaRole.CarpetBeater),
                        "Only the smoker holds the cigarette.");
                    sawBeater |=
                        stance.Role == DryingYardBabushkaRole.CarpetBeater;
                    sawSmoker |=
                        stance.Role != DryingYardBabushkaRole.CarpetBeater;
                }

                Assert.That(sawBeater && sawSmoker, Is.True,
                    "The plan must exercise both roles.");
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        private static CityDistrictPointOfInterestDescriptor
            FindDryingYard(CityLayout layout)
        {
            for (int index = 0;
                 index < layout.DistrictPointsOfInterest.Count;
                 index++)
            {
                if (layout.DistrictPointsOfInterest[index].Kind ==
                    CityDistrictPointOfInterestKind
                        .ResidentialDryingYard)
                {
                    return layout.DistrictPointsOfInterest[index];
                }
            }

            Assert.Fail("The default city builds a drying yard.");
            return default;
        }

        private static void AssertYardCollidersClearApproaches(
            GameObject root,
            CityLayout layout)
        {
            CityDistrictPointOfInterestDescriptor descriptor =
                FindDryingYard(layout);
            Physics.SyncTransforms();
            foreach (Collider collider in
                     root.GetComponentsInChildren<Collider>(true))
            {
                if (collider.name ==
                    CityDistrictPointOfInterestWorldBuilder
                        .PublicGroundName)
                {
                    continue;
                }

                Bounds bounds = collider.bounds;
                var footprint = Rect.MinMaxRect(
                    bounds.min.x,
                    bounds.min.z,
                    bounds.max.x,
                    bounds.max.z);
                for (int accessIndex = 0;
                     accessIndex < descriptor.Accesses.Count;
                     accessIndex++)
                {
                    Assert.That(
                        footprint.Overlaps(
                            descriptor.Accesses[accessIndex]
                                .ApproachBounds),
                        Is.False,
                        $"'{collider.name}' blocks public access " +
                        $"'{descriptor.Accesses[accessIndex].Id}'.");
                }
            }
        }

        /// <summary>
        /// The rack the carpets hang on is authored geometry now - two
        /// posts and a crossbar baked into the yard's one painted-metal
        /// batch - so it has no part names left to ask for. What is
        /// still Unity's is the pair of obstacle colliders standing at
        /// the posts, and those are the half that hurts: a collider
        /// with nothing drawn inside it is an invisible wall.
        ///
        /// So each post collider has to enclose some of the shell's
        /// triangles. Deliberately narrow: it does not pin the
        /// crossbar, which nothing collides with, nor the posts' size -
        /// only that solid metal is drawn where the player is stopped.
        /// </summary>
        private static void AssertRackPostsAreDrawn(GameObject root)
        {
            Transform shell = FindNode(
                root,
                "Imported Residential Drying Yard " +
                "Residential_PaintedMetal");
            Mesh mesh = shell.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh, Is.Not.Null);

            string[] posts =
            {
                "Carpet Rack Post South Collider",
                "Carpet Rack Post North Collider"
            };
            foreach (string post in posts)
            {
                Collider collider =
                    FindNode(root, post).GetComponent<Collider>();
                Assert.That(collider, Is.Not.Null);
                Assert.That(
                    DrawsInside(mesh, shell, collider.bounds),
                    Is.True,
                    $"'{post}' stops the player where the shell " +
                    "draws nothing.");
            }
        }

        /// <summary>
        /// Whether a batched mesh puts any triangle inside a collider.
        /// The shell is one mesh for the whole yard, so its bounds
        /// cannot answer this. The mesh sits at the recipe's own
        /// origin and the recipe carries the site's yaw, so the
        /// collider is folded into the shell's frame corner by corner.
        /// </summary>
        private static bool DrawsInside(
            Mesh mesh,
            Transform frame,
            Bounds worldBox)
        {
            var box = new Bounds();
            for (int corner = 0; corner < 8; corner++)
            {
                var point = new Vector3(
                    (corner & 1) == 0 ? worldBox.min.x : worldBox.max.x,
                    (corner & 2) == 0 ? worldBox.min.y : worldBox.max.y,
                    (corner & 4) == 0 ? worldBox.min.z : worldBox.max.z);
                Vector3 local = frame.InverseTransformPoint(point);
                if (corner == 0)
                {
                    box = new Bounds(local, Vector3.zero);
                }
                else
                {
                    box.Encapsulate(local);
                }
            }

            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            for (int index = 0; index < triangles.Length; index += 3)
            {
                Vector3 first = vertices[triangles[index]];
                Vector3 second = vertices[triangles[index + 1]];
                Vector3 third = vertices[triangles[index + 2]];
                Vector3 low = Vector3.Min(
                    first,
                    Vector3.Min(second, third));
                Vector3 high = Vector3.Max(
                    first,
                    Vector3.Max(second, third));
                if (low.x <= box.max.x && high.x >= box.min.x &&
                    low.y <= box.max.y && high.y >= box.min.y &&
                    low.z <= box.max.z && high.z >= box.min.z)
                {
                    return true;
                }
            }

            return false;
        }

        private static Transform FindNode(GameObject root, string name)
        {
            foreach (Transform node in
                     root.GetComponentsInChildren<Transform>(true))
            {
                if (node.name == name)
                {
                    return node;
                }
            }

            Assert.Fail($"The drying yard builds no '{name}'.");
            return null;
        }
    }
}

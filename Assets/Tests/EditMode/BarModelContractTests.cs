using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// Holds the joint between the Blender bar model and
    /// `BarInteriorLayoutPlan`.
    ///
    /// Everything else about the room can be judged by eye. This cannot:
    /// the model and the plan describe the SAME room from two files, and
    /// if they ever disagree the failure is silent - the bartender pours
    /// through a wall, or a booth stands where the plan says there is
    /// floor. So the size of the room and the position of every anchor
    /// are asserted against the planner, not merely against themselves.
    /// </summary>
    public sealed class BarModelContractTests
    {
        private const string ManifestPath = "Assets/Bar/Models/Bar3D.json";
        private const string ModelPath =
            "Assets/Bar/Models/BarInterior3D.fbx";
        private const string ExteriorManifestPath =
            "Assets/Bar/Models/BarFacade3D.json";
        private const string ExteriorModelPath =
            "Assets/Bar/Models/BarFacade3D.fbx";
        private const float Tolerance = 0.01f;

        [Test]
        public void BarModel_ImportsAsAPassiveResourcePrefab()
        {
            BarManifest manifest = LoadManifest();

            Assert.That(manifest.design_id, Is.EqualTo("bar_interior_v2"));
            Assert.That(manifest.colliders, Is.False);
            Assert.That(manifest.lights, Is.False);
            Assert.That(manifest.cameras, Is.False);
            Assert.That(manifest.animation_count, Is.Zero);

            ModelImporter importer =
                AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            Assert.That(importer, Is.Not.Null, "the model did not import");
            Assert.That(
                importer.animationType,
                Is.EqualTo(ModelImporterAnimationType.None));
            Assert.That(importer.preserveHierarchy, Is.True);
            Assert.That(importer.optimizeGameObjects, Is.False);
            Assert.That(importer.importCameras, Is.False);
            Assert.That(importer.importLights, Is.False);
            Assert.That(
                importer.addCollider,
                Is.False,
                "collision is authored, not taken from the meshes");
            Assert.That(
                importer.materialImportMode,
                Is.EqualTo(ModelImporterMaterialImportMode.None),
                "materials from the FBX would break district tinting");
        }

        [Test]
        public void BarExterior_IsAFixedMetrePassiveAuthoredBuilding()
        {
            BarManifest manifest = LoadManifest(ExteriorManifestPath);

            Assert.That(manifest.design_id, Is.EqualTo("bar_exterior_v2"));
            Assert.That(manifest.dimensions_m.width,
                Is.EqualTo(12.2645f).Within(0.0001f));
            Assert.That(manifest.dimensions_m.depth,
                Is.EqualTo(13.5237f).Within(0.0001f));
            Assert.That(manifest.dimensions_m.height,
                Is.EqualTo(9.3435f).Within(0.0001f));
            Assert.That(manifest.colliders, Is.False);
            Assert.That(manifest.lights, Is.False);
            Assert.That(manifest.cameras, Is.False);
            Assert.That(manifest.animation_count, Is.Zero);

            BarManifestAnchor door = manifest.anchors.Single(
                anchor => anchor.role == "exterior_door");
            Assert.That(door.name, Is.EqualTo("Door"));
            Assert.That(door.local_position, Has.Length.EqualTo(3));
            Assert.That(door.local_position,
                Is.EqualTo(new[] { 0f, 0f, 0f }));
            Assert.That(
                manifest.anchors.Count(anchor =>
                    anchor.role == "sign_pivot"),
                Is.EqualTo(1));
            BarManifestAnchor sign = manifest.anchors.Single(anchor =>
                anchor.role == "sign_pivot");
            Assert.That(sign.local_position, Has.Length.EqualTo(3));
            Assert.That(
                sign.local_position[1],
                Is.InRange(0.90f, 2.80f),
                "The blade sign must attach to the solid pier, not the " +
                "centre upper window axis.");

            string[] requiredParts =
            {
                "Pub Brick Shell",
                "Pub Rendered Upper Storey",
                "Pub Slate Roof",
                "Pub Brick Chimneys",
                "Pub Chimney Pots",
                "Pub Ground Floor Glass",
                "Pub Upper Sash Frames",
                "Pub Upper Windows Warm",
                "Pub Upper Window Dark",
                "Pub Side Service Door",
                "Bar Entrance Flanking Panels",
                "Bar Outer Bay Flanking Panels",
                "Bar Entrance Reveal Panels",
                "Bar Door",
                "Bar Door Frame Left",
                "Bar Door Frame Right",
                "Bar Entrance Canopy"
            };
            for (int index = 0; index < requiredParts.Length; index++)
            {
                Assert.That(
                    manifest.parts.Any(part =>
                        part.name == requiredParts[index]),
                    Is.True,
                    $"the exterior has no '{requiredParts[index]}' part");
            }

            BarManifestPart brick = manifest.parts.Single(part =>
                part.name == "Pub Brick Shell");
            BarManifestPart plaster = manifest.parts.Single(part =>
                part.name == "Pub Rendered Upper Storey");
            Assert.That(brick.sheet, Is.EqualTo("ExteriorBrick"));
            Assert.That(plaster.sheet, Is.EqualTo("ExteriorPlaster"));
            Assert.That(
                BarExteriorSurfaceAppearance.TryResolveSheet(
                    brick.sheet,
                    out BarExteriorSurfaceKind brickSurface),
                Is.True);
            Assert.That(brickSurface,
                Is.EqualTo(BarExteriorSurfaceKind.Brick));
            Assert.That(
                BarExteriorSurfaceAppearance.GetRecipe(brickSurface)
                    .ResourcePath,
                Is.EqualTo(
                    BarExteriorSurfaceAppearance.BrickTextureResourcePath));
            Assert.That(
                BarExteriorSurfaceAppearance.TryResolveSheet(
                    plaster.sheet,
                    out BarExteriorSurfaceKind plasterSurface),
                Is.True);
            Assert.That(plasterSurface,
                Is.EqualTo(BarExteriorSurfaceKind.Plaster));
            Assert.That(
                BarExteriorSurfaceAppearance.GetRecipe(plasterSurface)
                    .ResourcePath,
                Is.EqualTo(
                    BarExteriorSurfaceAppearance
                        .PlasterTextureResourcePath));

            ModelImporter importer =
                AssetImporter.GetAtPath(ExteriorModelPath) as ModelImporter;
            Assert.That(importer, Is.Not.Null, "the exterior did not import");
            Assert.That(importer.animationType,
                Is.EqualTo(ModelImporterAnimationType.None));
            Assert.That(importer.importCameras, Is.False);
            Assert.That(importer.importLights, Is.False);
            Assert.That(importer.addCollider, Is.False);

            GameObject prefab = BarModelResources.LoadFacadePrefab();
            Assert.That(prefab, Is.Not.Null);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                BarAssetRegistry registry =
                    instance.GetComponent<BarAssetRegistry>();
                Assert.That(registry, Is.Not.Null);
                Assert.That(registry.DesignId,
                    Is.EqualTo("bar_exterior_v2"));
                Assert.That(instance.transform.localScale,
                    Is.EqualTo(Vector3.one));
                Assert.That(registry.Dimensions.Width,
                    Is.EqualTo(12.2645f).Within(0.0001f));
                Assert.That(registry.Dimensions.Depth,
                    Is.EqualTo(13.5237f).Within(0.0001f));
                Assert.That(registry.Dimensions.Height,
                    Is.EqualTo(9.3435f).Within(0.0001f));
                AssertAnchor(
                    registry,
                    instance.transform,
                    "exterior_door",
                    Vector3.zero);
                Assert.That(
                    registry.TryGetAnchor(
                        "sign_pivot",
                        out Transform signPivot),
                    Is.True);
                Assert.That(signPivot, Is.Not.Null);
                Vector3 signPosition = instance.transform
                    .InverseTransformPoint(signPivot.position);
                Assert.That(
                    signPosition.z,
                    Is.InRange(0.90f, 2.80f),
                    "The imported blade-sign anchor must remain on the " +
                    "solid pier beside the upper windows.");
                Assert.That(
                    instance.GetComponentsInChildren<Collider>(true),
                    Is.Empty);
                Assert.That(
                    instance.GetComponentsInChildren<Light>(true),
                    Is.Empty);
                Assert.That(
                    instance.GetComponentsInChildren<Camera>(true),
                    Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void BarModel_MatchesTheRoomTheLayoutPlannerPublishes()
        {
            BarManifest manifest = LoadManifest();
            BarInteriorLayoutPlan plan = SamplePlan();

            Assert.That(
                manifest.dimensions_m.width,
                Is.EqualTo(plan.RoomSize.x).Within(0.001f),
                "the model and the planner disagree on room width");
            Assert.That(
                manifest.dimensions_m.depth,
                Is.EqualTo(plan.RoomSize.y).Within(0.001f),
                "the model and the planner disagree on room depth");
            Assert.That(
                manifest.dimensions_m.height,
                Is.EqualTo(plan.RoomHeight).Within(0.001f));
            Assert.That(
                manifest.wall_thickness_m,
                Is.EqualTo(plan.WallThickness).Within(0.001f));
            Assert.That(manifest.door_opening_m.width, Is.EqualTo(3.2f));
        }

        [Test]
        public void BarModel_AnchorsLandOnThePlansOwnStations()
        {
            BarInteriorLayoutPlan plan = SamplePlan();
            GameObject prefab = BarModelResources.LoadInteriorPrefab();
            Assert.That(
                prefab,
                Is.Not.Null,
                "the bar prefab has not been built; run " +
                "Bar Promenade/Bar/Build Runtime Prefabs");

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                BarAssetRegistry registry =
                    instance.GetComponent<BarAssetRegistry>();
                Assert.That(registry, Is.Not.Null);

                AssertAnchor(
                    registry, instance.transform, "counter_station",
                    plan.CounterStationPosition);
                AssertAnchor(
                    registry, instance.transform, "activity_station",
                    plan.ActivityStationPosition);
                AssertAnchor(
                    registry, instance.transform, "entrance",
                    new Vector3(0f, 0f, -plan.RoomSize.y * 0.5f));
                AssertAnchor(
                    registry, instance.transform, "room_centre",
                    Vector3.zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void BarModel_CarriesGeometryAndNothingElse()
        {
            GameObject prefab = BarModelResources.LoadInteriorPrefab();
            Assert.That(prefab, Is.Not.Null);

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                Assert.That(
                    instance.GetComponentsInChildren<Collider>(true),
                    Is.Empty,
                    "collision is added by the placer, from the manifest");
                Assert.That(
                    instance.GetComponentsInChildren<Light>(true),
                    Is.Empty,
                    "light comes from the layout plan's light anchors");
                Assert.That(
                    instance.GetComponentsInChildren<Camera>(true),
                    Is.Empty);
                Assert.That(
                    instance.GetComponentsInChildren<Animator>(true),
                    Is.Empty);
                Assert.That(
                    instance.GetComponentsInChildren<Rigidbody>(true),
                    Is.Empty);

                BarAssetRegistry registry =
                    instance.GetComponent<BarAssetRegistry>();
                Assert.That(registry, Is.Not.Null);
                Assert.That(registry.Parts, Is.Not.Empty);
                Assert.That(
                    registry.Parts.All(
                        binding =>
                            binding != null &&
                            binding.Renderer != null &&
                            !string.IsNullOrWhiteSpace(binding.Role) &&
                            binding.Renderer.sharedMaterials.Length == 1 &&
                            binding.Renderer.sharedMaterial != null),
                    Is.True,
                    "every part needs exactly one bound material");

                //  Exactly two shared materials across 150-odd parts: lit
                //  and emissive. That is what makes a district tint a
                //  property block rather than an asset per district per
                //  part.
                Assert.That(
                    registry.Parts
                        .Select(binding => binding.Renderer.sharedMaterial)
                        .Distinct()
                        .Count(),
                    Is.EqualTo(2));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void BarModel_CarriesEveryActivitySetAndDistrictDressing()
        {
            BarManifest manifest = LoadManifest();

            //  Four activity sets and four district dressings, because
            //  the room varies in exactly those two ways and no other.
            //  If an enum ever gains a member with no authored set, the
            //  bar would silently lose its dressing rather than fail.
            foreach (BarActivityKind activity in
                     Enum.GetValues(typeof(BarActivityKind))
                         .Cast<BarActivityKind>())
            {
                if (activity == BarActivityKind.None)
                {
                    continue;
                }

                string group = $"activity:{activity}";
                Assert.That(
                    manifest.parts.Any(part => part.group == group),
                    Is.True,
                    $"the model has no authored set for {activity}");
            }

            foreach (BarDistrictMood mood in
                     Enum.GetValues(typeof(BarDistrictMood))
                         .Cast<BarDistrictMood>())
            {
                string group = $"district:{mood}";
                Assert.That(
                    manifest.parts.Any(part => part.group == group),
                    Is.True,
                    $"the model has no authored dressing for {mood}");
            }
        }

        [Test]
        public void BarModel_DeclaresOnlyTintsTheIdentityCanAnswer()
        {
            BarManifest manifest = LoadManifest();
            BarInteriorLayoutPlan plan = SamplePlan();

            //  A tint naming a field that does not exist throws at
            //  resolve time - deep inside a room build, on one district
            //  only. Asking every one of them here turns that into a
            //  named failure.
            foreach (BarManifestPart part in manifest.parts)
            {
                Assert.That(part.tint, Is.Not.Null, part.name);
                var spec = new BarTintSpec(
                    part.tint.field,
                    Color.white,
                    part.tint.scale,
                    part.tint.lerp_field,
                    Color.white,
                    part.tint.lerp_t);
                Assert.DoesNotThrow(
                    () => spec.Resolve(plan.DistrictIdentity),
                    $"part '{part.name}' asks for an unknown tint");
            }
        }

        [Test]
        public void PlacedRoom_PutsItsLandmarksWhereTheyWereBuiltBefore()
        {
            //  The model's anchors are asserted above; this asserts that
            //  the PLACER reads them correctly. It did not: it took
            //  `localPosition` from a node whose parent carries the FBX
            //  unit factor of 100, so the jukebox stood in the middle of
            //  the floor at a sixteenth of a metre from the room centre.
            //  Every numeric check passed. Only a rendered frame showed
            //  it, and this is that frame turned into an assertion.
            var host = new GameObject("Bar Placement Test");
            try
            {
                BarInteriorLayoutPlan plan = SamplePlan();
                Transform room = BarInteriorWorldBuilder.Build(
                    host.transform,
                    plan);

                AssertPlaced(room, "Bar Jukebox",
                    new Vector3(6.4f, 0f, -6.78f));
                AssertPlaced(room, "Slow Ceiling Fan",
                    new Vector3(0f, 4.35f, 0.75f));

                //  One pendant per light anchor, hung where the plan says
                //  and at the size it was authored - a lamp cloned from a
                //  template is the easiest thing in this room to place
                //  correctly and lose the scale of.
                for (int index = 0;
                     index < plan.LightAnchors.Count;
                     index++)
                {
                    BarInteriorLightAnchor light = plan.LightAnchors[index];
                    Transform shade =
                        room.Find($"Practical Shade {index + 1}");
                    Assert.That(
                        shade,
                        Is.Not.Null,
                        $"no shade for light anchor {index + 1}");
                    Assert.That(
                        Vector3.Distance(
                            shade.localPosition,
                            light.Position),
                        Is.LessThan(0.02f),
                        $"shade {index + 1} hangs away from its anchor");

                    //  Across in BOTH ground axes and shallow in the
                    //  vertical one. Measuring only `size.x` passed a
                    //  shade lying on its side, which is how every
                    //  pendant in the room came to hang horizontally:
                    //  the clone dropped the template's rotation, and a
                    //  disc 0.58 m across is 0.58 m across whichever way
                    //  up it is.
                    Renderer shadeRenderer = shade.GetComponent<Renderer>();
                    Assert.That(shadeRenderer, Is.Not.Null);
                    Bounds shadeBounds = shadeRenderer.bounds;
                    Assert.That(
                        shadeBounds.size.x,
                        Is.EqualTo(0.58f).Within(0.05f),
                        $"shade {index + 1} is {shadeBounds.size} across, " +
                        "not 0.58 m");
                    Assert.That(
                        shadeBounds.size.z,
                        Is.EqualTo(0.58f).Within(0.05f),
                        $"shade {index + 1} is {shadeBounds.size} across, " +
                        "not 0.58 m");
                    Assert.That(
                        shadeBounds.size.y,
                        Is.EqualTo(0.28f).Within(0.05f),
                        $"shade {index + 1} is {shadeBounds.size} - it is " +
                        "lying on its side instead of hanging");

                    //  And the flex reaches the ceiling from the anchor.
                    //  This is the assertion that catches a lamp cloned
                    //  at the wrong orientation outright: stretched along
                    //  the wrong axis the cable grew thicker, not longer,
                    //  and never left the anchor.
                    Transform flex =
                        room.Find($"Practical Cable {index + 1}");
                    Assert.That(
                        flex,
                        Is.Not.Null,
                        $"no cable for light anchor {index + 1}");
                    Bounds flexBounds =
                        flex.GetComponent<Renderer>().bounds;
                    Assert.That(
                        flexBounds.max.y,
                        Is.EqualTo(plan.RoomHeight).Within(0.05f),
                        $"cable {index + 1} tops out at {flexBounds.max.y}, " +
                        $"not at the ceiling ({plan.RoomHeight} m)");
                    Assert.That(
                        flexBounds.size.y,
                        Is.EqualTo(
                                plan.RoomHeight - light.Position.y)
                            .Within(0.05f),
                        $"cable {index + 1} hangs {flexBounds.size} - it " +
                        "was stretched along the wrong axis");
                }

                //  And the room is the size it was authored at, not a
                //  hundredth of it.
                Transform floor = room.Find("Floor");
                Assert.That(floor, Is.Not.Null);
                Renderer floorRenderer = floor.GetComponent<Renderer>();
                Assert.That(floorRenderer, Is.Not.Null);
                Assert.That(
                    floorRenderer.bounds.size.x,
                    Is.EqualTo(plan.RoomSize.x).Within(0.05f),
                    "the placed floor is not the size the plan publishes");
                Assert.That(
                    floorRenderer.bounds.size.z,
                    Is.EqualTo(plan.RoomSize.y).Within(0.05f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void PlacedRoom_CollidesWhereItsGeometryIs()
        {
            //  Collision is authored in ROOM-space metres, so it has to
            //  hang off the room. Hung off the model part it describes,
            //  it is read in a local space that carries the FBX unit
            //  factor of a hundred AND the Blender-to-Unity axis
            //  conversion of ninety degrees about X: the floor came out
            //  a 2200x1600x24 m slab tipped on its side and sunk twelve
            //  metres. The room had no ground. The hero fell through it
            //  forever and the chase camera, whose probe started inside
            //  the slab, collapsed onto his head - and every number the
            //  tests above measure was still correct, because none of
            //  them is a collider.
            var host = new GameObject("Bar Collision Test");
            try
            {
                BarInteriorLayoutPlan plan = SamplePlan();
                Transform room = BarInteriorWorldBuilder.Build(
                    host.transform,
                    plan);

                var envelope = new Bounds(
                    new Vector3(0f, plan.RoomHeight * 0.5f, 0f),
                    new Vector3(
                        plan.RoomSize.x + 2f,
                        plan.RoomHeight + 6f,
                        plan.RoomSize.y + 2f));

                Collider[] colliders =
                    room.GetComponentsInChildren<Collider>(true);
                Assert.That(
                    colliders,
                    Is.Not.Empty,
                    "the placed room carries no collision at all");

                foreach (Collider collider in colliders)
                {
                    Bounds bounds = collider.bounds;
                    Assert.That(
                        envelope.Contains(bounds.min) &&
                        envelope.Contains(bounds.max),
                        Is.True,
                        $"'{collider.transform.name}' collides at " +
                        $"{bounds.center} across {bounds.size}, which is " +
                        "not inside the room it belongs to");
                }

                //  The one that matters most: something to stand on
                //  where the plan puts the hero down.
                Assert.That(
                    HasGroundUnder(colliders, plan.PlayerSpawn),
                    Is.True,
                    "there is nothing to stand on under the player's " +
                    $"spawn point {plan.PlayerSpawn}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static bool HasGroundUnder(
            Collider[] colliders,
            Vector3 spawn)
        {
            foreach (Collider collider in colliders)
            {
                if (collider.isTrigger)
                {
                    continue;
                }

                Bounds bounds = collider.bounds;
                if (bounds.min.x <= spawn.x &&
                    bounds.max.x >= spawn.x &&
                    bounds.min.z <= spawn.z &&
                    bounds.max.z >= spawn.z &&
                    bounds.max.y > -0.5f &&
                    bounds.max.y <= spawn.y + 0.2f)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AssertPlaced(
            Transform room,
            string name,
            Vector3 expected)
        {
            Transform placed = room.Find(name);
            Assert.That(placed, Is.Not.Null, $"the room has no '{name}'");
            Assert.That(
                Vector3.Distance(placed.localPosition, expected),
                Is.LessThan(0.02f),
                $"'{name}' stands at {placed.localPosition}, not {expected}");
        }

        [Test]
        public void BarModel_KeepsTheRoomTheSameShapeInEveryDistrict()
        {
            //  The migration is one-to-one, so the geometry may not vary
            //  with the district - only the tint may. If a district ever
            //  changed the room, one authored model would be the wrong
            //  tool and this test is where that surfaces.
            Vector2 first = default;
            bool started = false;
            foreach (CityDistrictKind district in
                     Enum.GetValues(typeof(CityDistrictKind))
                         .Cast<CityDistrictKind>())
            {
                BarInteriorLayoutPlan plan = BarInteriorLayoutPlanner.Generate(
                    20260826,
                    "bar-contract",
                    BarActivityKind.BeerPong,
                    district);
                if (!started)
                {
                    first = plan.RoomSize;
                    started = true;
                    continue;
                }

                Assert.That(
                    plan.RoomSize,
                    Is.EqualTo(first),
                    $"district {district} changes the room's size");
            }

            Assert.That(started, Is.True);
        }

        private static void AssertAnchor(
            BarAssetRegistry registry,
            Transform root,
            string role,
            Vector3 expected)
        {
            Assert.That(
                registry.TryGetAnchor(role, out Transform anchor),
                Is.True,
                $"the model has no '{role}' anchor");

            Vector3 actual = root.InverseTransformPoint(anchor.position);
            Assert.That(
                actual.x, Is.EqualTo(expected.x).Within(Tolerance),
                $"'{role}' x drifted from the plan");
            Assert.That(
                actual.y, Is.EqualTo(expected.y).Within(Tolerance),
                $"'{role}' y drifted from the plan");
            Assert.That(
                actual.z, Is.EqualTo(expected.z).Within(Tolerance),
                $"'{role}' z drifted from the plan");
        }

        private static BarInteriorLayoutPlan SamplePlan()
        {
            return BarInteriorLayoutPlanner.Generate(
                20260826,
                "bar-contract",
                BarActivityKind.BeerPong,
                CityDistrictKind.Nightlife);
        }

        private static BarManifest LoadManifest()
        {
            return LoadManifest(ManifestPath);
        }

        private static BarManifest LoadManifest(string path)
        {
            TextAsset source =
                AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            Assert.That(
                source,
                Is.Not.Null,
                $"'{path}' is missing; run the Blender generator");
            BarManifest manifest =
                JsonUtility.FromJson<BarManifest>(source.text);
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.parts, Is.Not.Null.And.Not.Empty);
            return manifest;
        }

        [Serializable]
        private sealed class BarManifest
        {
            public string design_id;
            public BarDimensionsManifest dimensions_m;
            public float wall_thickness_m;
            public BarOpeningManifest door_opening_m;
            public bool colliders;
            public bool lights;
            public bool cameras;
            public int animation_count;
            public int triangle_count;
            public BarManifestAnchor[] anchors;
            public BarManifestPart[] parts;
        }

        [Serializable]
        private sealed class BarManifestAnchor
        {
            public string name;
            public string role;
            public float[] local_position;
        }

        [Serializable]
        private sealed class BarManifestPart
        {
            public string name;
            public string role;
            public string group;
            public string sheet;
            public bool emissive;
            public BarManifestTint tint;
        }

        [Serializable]
        private sealed class BarManifestTint
        {
            public string field;
            public float scale;
            public string lerp_field;
            public float lerp_t;
        }

        [Serializable]
        private sealed class BarDimensionsManifest
        {
            public float width;
            public float depth;
            public float height;
        }

        [Serializable]
        private sealed class BarOpeningManifest
        {
            public float width;
            public float height;
        }
    }
}

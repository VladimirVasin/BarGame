using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Rescue paving/snow regression and the village lighting/forest review.")]
        [PrebuildSetup(typeof(VillageArtAssetsSetup))]
        public IEnumerator AlpineVillagePolish()
        {
            GameSessionState.TryStartGameTimeFromWake();
            Assert.That(GameSessionState.TrySetDebugGameDay(2), Is.True);
            GameSessionState.AdvanceGameTime((float)((12d * 60d - GameSessionState.GameTimeOfDayMinutes) /
                GameTimeState.GameMinutesPerRealSecond));
            AlpineVillageRoot root = null;
            try
            {
                yield return Capture(SceneIds.AlpineVillage, () =>
                {
                    root = Object.FindAnyObjectByType<AlpineVillageRoot>();
                    return root != null && root.World != null && root.Player.GameObject != null ? root : null;
                }, () =>
                {
                    AlpineVillageAbandonedPlot plot = root.Plan.Expansion.Abandonment.RescueForecourtPlot;
                    AssertRescuePavingDimensions(root, plot);
                    AlpineVillageSnowTreading snow = root.World.SnowTreading;
                    snow.enabled = false;
                    Mesh mesh = snow.GetComponent<MeshFilter>().sharedMesh;
                    Vector3[] original = mesh.vertices;
                    int[] apronVertices = RescueSnowVertices(root, plot, mesh);
                    AssertRescueSnowAbovePaving(plot, mesh.vertices, apronVertices);
                    var shots = new List<Shot>();
                    Vector3 front = plot.World(new Vector2(0f, 15.5f));
                    Vector3 target = plot.World(new Vector2(0f, 7f)) + Vector3.up * .9f;
                    shots.Add(RescuePolishShot(root, "polish-00-rescue-forecourt", front, target));
                    shots.Add(RescuePolishShot(root, "polish-01-rescue-pressed",
                        plot.World(new Vector2(-5.7f, 13.5f)), target, () =>
                        {
                            foreach (int index in apronVertices) snow.Press(original[index]);
                            snow.Advance(.11f);
                            Vector3[] pressed = mesh.vertices;
                            AssertRescueSnowAbovePaving(plot, pressed, apronVertices);
                            bool lowered = false;
                            foreach (int index in apronVertices)
                                lowered |= pressed[index].y < original[index].y - .01f;
                            Assert.That(lowered, Is.True, "The regression never pressed actual apron snow.");
                        }));
                    shots.Add(RescuePolishShot(root, "polish-02-rescue-refilled", front, target, () =>
                    {
                        snow.Advance(60f);
                        Vector3[] restored = mesh.vertices;
                        AssertRescueSnowAbovePaving(plot, restored, apronVertices);
                        foreach (int index in apronVertices)
                            Assert.That(restored[index].y, Is.EqualTo(original[index].y).Within(.001f),
                                "Refilling changed the paved snow support.");
                    }));
                    AppendVillagePolishEnvironmentShots(root, shots);
                    return shots.ToArray();
                });
            }
            finally
            {
                if (root != null && root.World != null && root.World.SnowTreading != null)
                    root.World.SnowTreading.enabled = true;
            }
        }

        private static void AssertRescuePavingDimensions(AlpineVillageRoot root, AlpineVillageAbandonedPlot plot)
        {
            Transform yard = root.World.Root.transform.Find("Village Expansion/" +
                AlpineVillageAbandonmentPlan.RootName + "/" + plot.Id + "/Former Household Yard");
            Assert.That(yard, Is.Not.Null);
            float baseTop = float.NegativeInfinity, pavingTop = float.NegativeInfinity;
            Vector2 apronMin = Vector2.one * float.PositiveInfinity;
            Vector2 apronMax = Vector2.one * float.NegativeInfinity;
            foreach (MeshFilter filter in yard.GetComponentsInChildren<MeshFilter>())
            {
                bool apron = filter.name.Contains("OldForecourt");
                bool edges = filter.name.Contains("ExposedForecourtEdges");
                if (!apron && !edges) continue;
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                {
                    Vector3 local = Quaternion.Inverse(plot.Rotation) *
                        (filter.transform.TransformPoint(vertex) - plot.GroundCenter);
                    if (apron)
                    {
                        baseTop = Mathf.Max(baseTop, local.y);
                        apronMin = Vector2.Min(apronMin, new Vector2(local.x, local.z));
                        apronMax = Vector2.Max(apronMax, new Vector2(local.x, local.z));
                    }
                    // Exclude the two taller curb runs; they frame the paving.
                    if (edges && Mathf.Abs(local.x) < 6f) pavingTop = Mathf.Max(pavingTop, local.y);
                }
            }
            Assert.That(baseTop, Is.EqualTo(.14f).Within(.005f), "Actual imported forecourt height.");
            Rect bounds = AlpineVillageAbandonmentPlan.RescueForecourtBounds;
            Assert.That(Vector2.Distance(apronMin, bounds.min), Is.LessThan(.01f), "Imported apron minimum.");
            Assert.That(Vector2.Distance(apronMax, bounds.max), Is.LessThan(.01f), "Imported apron maximum.");
            Assert.That(pavingTop, Is.EqualTo(AlpineVillageAbandonmentPlan.RescueForecourtPavingTop)
                .Within(.005f), "The snow support no longer covers the imported flagstones.");
        }

        private static int[] RescueSnowVertices(AlpineVillageRoot root, AlpineVillageAbandonedPlot plot, Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            var local = new Vector2[vertices.Length];
            for (int index = 0; index < vertices.Length; index++)
                local[index] = plot.ToPlot(root.Plan.Expansion.ToLocal(vertices[index]));
            int[] triangles = mesh.triangles;
            var selected = new HashSet<int>();
            for (int index = 0; index < triangles.Length; index += 3)
            {
                Vector2 a = local[triangles[index]], b = local[triangles[index + 1]], c = local[triangles[index + 2]];
                Rect extent = Rect.MinMaxRect(Mathf.Min(a.x, b.x, c.x), Mathf.Min(a.y, b.y, c.y),
                    Mathf.Max(a.x, b.x, c.x), Mathf.Max(a.y, b.y, c.y));
                if (!extent.Overlaps(AlpineVillageAbandonmentPlan.RescueForecourtBounds)) continue;
                selected.Add(triangles[index]); selected.Add(triangles[index + 1]); selected.Add(triangles[index + 2]);
            }
            Assert.That(selected, Is.Not.Empty, "No actual snow triangle covers the rescue apron.");
            var result = new int[selected.Count];
            selected.CopyTo(result);
            return result;
        }

        private static void AssertRescueSnowAbovePaving(AlpineVillageAbandonedPlot plot,
            Vector3[] vertices, int[] apronVertices)
        {
            float support = plot.GroundCenter.y + AlpineVillageAbandonmentPlan.RescueForecourtPavingTop +
                AlpineVillageAbandonmentPlan.RescueForecourtSnowClearance;
            foreach (int index in apronVertices)
                Assert.That(vertices[index].y, Is.GreaterThanOrEqualTo(support - .001f),
                    "A rendered snow triangle cuts the paved apron: " + vertices[index]);
        }

        private static Shot RescuePolishShot(AlpineVillageRoot root, string name,
            Vector3 foot, Vector3 target, Action prepare = null)
        {
            int frames = 0;
            return Shot.At(name, foot + Vector3.up * EyeHeight, target, 74f, 0, () =>
            {
                if (frames++ == 0)
                {
                    root.Player.Motor.Teleport(foot + Vector3.up * PlayerFactory.GroundedRootOffset);
                    prepare?.Invoke();
                }
                AlpineColdExposureDriver frost = Object.FindAnyObjectByType<AlpineColdExposureDriver>();
                if (frost != null) frost.Model.Reset();
                if (frames < 12) return false;
                foreach (Renderer renderer in root.Player.GameObject.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled = false;
                return true;
            });
        }

        [UnityTest]
        [Explicit("Abandoned settlement: actual placement, neighbours and eye-height art review.")]
        [PrebuildSetup(typeof(VillageArtAssetsSetup))]
        public IEnumerator AlpineVillageAbandonment()
        {
            GameSessionState.TryStartGameTimeFromWake();
            Assert.That(GameSessionState.TrySetDebugGameDay(2), Is.True);
            GameSessionState.AdvanceGameTime((float)(100f / GameTimeState.GameMinutesPerRealSecond));
            var failures = new List<string>();
            var report = new AbandonmentReport();
            AlpineVillageRoot root = null;
            yield return Capture(SceneIds.AlpineVillage, () =>
            {
                root = Object.FindAnyObjectByType<AlpineVillageRoot>();
                return root != null && root.World != null && root.Player.GameObject != null ? root : null;
            }, () =>
            {
                // Reuse the established expansion contract, not its old photo set.
                AbandonmentCheck(failures, "existing expansion", () =>
                    AppendVillageExpansionShots(root, new List<Shot>()));
                var shots = new List<Shot>();
                Transform settlement = root.World.Root.transform.Find(
                    "Village Expansion/" + AlpineVillageAbandonmentPlan.RootName);
                Assert.That(settlement, Is.Not.Null, "The abandoned settlement did not build.");
                Physics.SyncTransforms();
                report.coreTerrainBounds = root.Plan.CoreTerrainBounds;
                AbandonmentCheck(failures, "passive settlement", () =>
                {
                    Assert.That(settlement.GetComponentsInChildren<Light>(true), Is.Empty);
                    Assert.That(settlement.GetComponentsInChildren<AudioSource>(true), Is.Empty);
                    Assert.That(settlement.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty,
                        "New scenery must not silently acquire NPCs, interactions or light drivers.");
                    foreach (Renderer renderer in settlement.GetComponentsInChildren<Renderer>(true))
                    {
                        var block = new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(block);
                        Assert.That(block.GetColor("_EmissionColor").maxColorComponent, Is.LessThan(.001f), renderer.name);
                        foreach (Material material in renderer.sharedMaterials)
                            if (material.HasProperty("_EmissionColor") && material.IsKeywordEnabled("_EMISSION"))
                                Assert.That(material.GetColor("_EmissionColor").maxColorComponent,
                                    Is.LessThan(.001f), renderer.name + " emissive material");
                    }
                });

                foreach (AlpineVillageAbandonedPlot plot in root.Plan.Expansion.Abandonment.Plots)
                    AbandonmentCheck(failures, plot.Id + " placement", () =>
                        VerifyAbandonedPlot(root, plot, settlement.Find(plot.Id)));
                AbandonmentCheck(failures, "old paths", () => VerifyAbandonedPathClearance(root, settlement));

                var buildings = ResolveAbandonmentBuildings(root);
                foreach (AbandonmentBuilding building in buildings)
                    AbandonmentCheck(failures, building.Site.Id + " neighbours", () =>
                        AuditBuildingNeighbours(root, building, buildings, report, failures));

                AlpineVillageLaneSample axis = root.Plan.Lane.Sample(2f);
                Vector3 axisFoot = axis.Position - axis.Forward * PlatformApronSetback;
                shots.Add(AbandonmentShot(root, "abandonment-00-preserved-arrival-axis", axisFoot,
                    root.Plan.MothersHouse.GroundCenter + Vector3.up * LandmarkAimHeight, 50f, true));
                foreach (AlpineVillageAbandonedPlot plot in root.Plan.Expansion.Abandonment.Plots)
                {
                    if (!TryAbandonmentFoot(root, plot.GroundCenter, plot.Rotation, plot.Size, 0,
                            out Vector3 foot, true))
                    {
                        failures.Add(plot.Id + ": no free front composition point");
                        continue;
                    }
                    shots.Add(AbandonmentShot(root, "abandonment-10-" + plot.Id, foot,
                        plot.GroundCenter + Vector3.up * Mathf.Min(2.8f, plot.Height * .45f), 74f));
                    if (plot.Yard == null || plot.Yard.StartsWith("Household", StringComparison.Ordinal)) continue;
                    if (TryAbandonmentFoot(root, plot.GroundCenter, plot.Rotation, plot.Size, 1,
                            out Vector3 sideFoot, true))
                        shots.Add(AbandonmentShot(root, "abandonment-20-" + plot.Id + "-yard", sideFoot,
                            plot.World(new Vector2(0f, plot.YardBounds.yMax * .55f)) + Vector3.up, 78f));
                    else
                    {
                        failures.Add(plot.Id + ": no free side view of civic yard");
                        shots.Add(AbandonmentShot(root, "abandonment-20-" + plot.Id + "-yard-blocked-side", foot,
                            plot.World(new Vector2(0f, plot.YardBounds.yMax * .55f)) + Vector3.up, 78f));
                    }
                    if (TryAbandonmentFoot(root, plot.GroundCenter, plot.Rotation, plot.Size, 2,
                            out Vector3 rearFoot, true))
                        shots.Add(AbandonmentShot(root, "abandonment-21-" + plot.Id + "-rear", rearFoot,
                            plot.World(new Vector2(0f, plot.YardBounds.yMin * .75f)) + Vector3.up, 85f));
                }

                // Include the longest observed building link, where fog is most demanding.
                AbandonmentVisibility longest = null;
                foreach (AbandonmentVisibility view in report.views)
                    if (view.visible && (longest == null || view.distance > longest.distance)) longest = view;
                if (longest != null)
                {
                    shots.Add(AbandonmentShot(root, "abandonment-30-longest-neighbour-day",
                        longest.eye - Vector3.up * EyeHeight, longest.targetPoint, 60f, true));
                    shots.Add(AbandonmentShot(root, "abandonment-31-longest-neighbour-gust",
                        longest.eye - Vector3.up * EyeHeight, longest.targetPoint, 60f, false, true));
                    shots.Add(AbandonmentShot(root, "abandonment-40-longest-neighbour-night",
                        longest.eye - Vector3.up * EyeHeight, longest.targetPoint, 60f, true, false, true));
                    shots.Add(AbandonmentShot(root, "abandonment-41-longest-neighbour-night-gust",
                        longest.eye - Vector3.up * EyeHeight, longest.targetPoint, 60f, false, true));
                }
                shots.Add(AbandonmentShot(root, "abandonment-42-arrival-axis-night", axisFoot,
                    root.Plan.MothersHouse.GroundCenter + Vector3.up * LandmarkAimHeight, 50f, true, false, true));
                report.failures = failures.ToArray();
                string folder = Path.Combine(Directory.GetCurrentDirectory(), "Captures", SceneIds.AlpineVillage);
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "abandonment-visibility.json"), JsonUtility.ToJson(report, true));
                return shots.ToArray();
            });
            Assert.That(failures, Is.Empty, "Abandoned-settlement contracts; frames/JSON retained:\n" +
                string.Join("\n", failures));
        }

        private static void AbandonmentCheck(List<string> failures, string label, Action check)
        {
            try { check(); }
            catch (Exception exception) { failures.Add(label + ": " + exception.Message); }
        }

        private static void VerifyAbandonedPlot(AlpineVillageRoot root, AlpineVillageAbandonedPlot plot, Transform body)
        {
            Assert.That(body, Is.Not.Null);
            // Direct model children only: a broad yard must not hide a 1/100-scale house.
            Bounds meshBounds = default;
            bool hasGeometry = false;
            foreach (Transform part in body)
            {
                MeshFilter filter = part.GetComponent<MeshFilter>();
                if (filter == null) continue;
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                {
                    Vector3 point = body.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                    if (!hasGeometry) { meshBounds = new Bounds(point, Vector3.zero); hasGeometry = true; }
                    else meshBounds.Encapsulate(point);
                }
            }
            Assert.That(hasGeometry, Is.True);
            Assert.That(meshBounds.size.x, Is.InRange(plot.Size.x * .7f, plot.Size.x * 1.75f), "Metre-scale width.");
            Assert.That(meshBounds.size.z, Is.InRange(plot.Size.y * .7f, plot.Size.y * 1.75f), "Metre-scale depth.");
            if (plot.IsBuilding)
                Assert.That(meshBounds.size.y, Is.InRange(plot.Height * .5f, plot.Height * 1.7f), "Metre-scale height.");
            foreach (float x in new[] { -.5f, 0f, .5f })
            foreach (float z in new[] { -.5f, 0f, .5f })
            {
                Vector3 corner = plot.World(Vector2.Scale(plot.Size, new Vector2(x, z)));
                var xz = new Vector2(corner.x, corner.z);
                bool onGround = root.Plan.CoreTerrainBounds.Contains(xz) || root.Plan.Expansion.ContainsGround(xz, .2f);
                Assert.That(onGround, Is.True, "Building footprint leaves available ground at " + corner);
                Assert.That(AlpineVillageTerrainSampler.SampleHeight(root.Plan, xz),
                    Is.EqualTo(plot.GroundCenter.y).Within(.3f), "Foundation and terrain shelf disagree.");
            }
            if (plot.Closed)
            {
                Assert.That(root.World.WalkableArea.Contains(plot.GroundCenter, .25f), Is.False,
                    "Closed building has no movement footprint.");
                for (int side = 0; side < 4; side++)
                {
                    Vector2 direction = AbandonmentSide(side);
                    Vector3 outside = plot.World(Vector2.Scale(direction, plot.Size * .5f + Vector2.one * .5f));
                    Vector3 target = plot.GroundCenter + Vector3.up * 1.4f;
                    Vector3 eye = outside + Vector3.up * 1.4f;
                    Assert.That(Physics.Linecast(eye, target, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore),
                        Is.True, "Closed side " + side + " has no physical wall.");
                    Assert.That(hit.transform.IsChildOf(body), Is.True, "Another object masks a missing shell.");
                }
            }
            else
            {
                // Open ruins must retain at least one physically free interior
                // point accepted by the walking mask, not one full blocking box.
                bool openPoint = false;
                for (float x = -.3f; x <= .31f; x += .15f)
                for (float z = -.3f; z <= .31f; z += .15f)
                {
                    Vector3 point = plot.World(Vector2.Scale(plot.Size, new Vector2(x, z)));
                    if (root.World.WalkableArea.Contains(point, .25f) && AbandonmentCapsuleFree(point)) openPoint = true;
                }
                Assert.That(openPoint, Is.True, "A full movement mask or invisible solid fills the open ruin.");
            }
        }

        private static void VerifyAbandonedPathClearance(AlpineVillageRoot root, Transform settlement)
        {
            var conflicts = new List<string>();
            foreach (AlpineVillagePathDescriptor path in AlpineVillagePathPlanner.Create(root.Plan))
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt(path.LengthXZ));
                for (int i = 0; i <= steps; i++)
                {
                    Vector3 point = Vector3.Lerp(path.Start, path.End, i / (float)steps);
                    point.y = AlpineVillageTerrainSampler.SampleHeight(root.Plan, new Vector2(point.x, point.z));
                    foreach (Collider collider in Physics.OverlapCapsule(point + Vector3.up * .55f,
                        point + Vector3.up * 1.5f, .28f, ~0, QueryTriggerInteraction.Ignore))
                        if (collider.transform.IsChildOf(settlement))
                        {
                            conflicts.Add(path.StableId + " at " + point + " blocked by " + collider.name);
                            break;
                        }
                }
            }
            Assert.That(conflicts, Is.Empty, string.Join("\n", conflicts));
        }

        private sealed class AbandonmentBuilding
        {
            public AlpineVillageBuildingSite Site;
            public Transform Transform;
            public Vector3 Center;
            public Quaternion Rotation;
        }

        private static List<AbandonmentBuilding> ResolveAbandonmentBuildings(AlpineVillageRoot root)
        {
            var result = new List<AbandonmentBuilding>();
            foreach (AlpineVillageBuildingSite site in root.Plan.Expansion.Abandonment.Buildings)
            {
                Transform target;
                Quaternion rotation = Quaternion.LookRotation(root.Plan.Uphill, Vector3.up);
                Vector3 center = root.Plan.Expansion.ToWorld(site.Center);
                if (site.Abandoned != null)
                {
                    target = root.World.Root.transform.Find("Village Expansion/" +
                        AlpineVillageAbandonmentPlan.RootName + "/" + site.Id);
                    rotation = site.Abandoned.Rotation;
                    center = site.Abandoned.GroundCenter;
                }
                else if (root.World.SemanticObjects.TryGetValue(site.Id, out target))
                {
                    foreach (AlpineVillagePlotDescriptor plot in root.Plan.Plots)
                        if (plot.StableId == site.Id)
                        { rotation = Quaternion.LookRotation(plot.Facing, Vector3.up); center = plot.GroundCenter; break; }
                }
                else
                {
                    string name = site.Id == "ski-lodge" ? "Ski Lodge" :
                        site.Id == "service-shed" ? "Service Shed" : "Former Trade Warehouse";
                    target = root.World.Root.transform.Find("Village Expansion/" + name);
                }
                Assert.That(target, Is.Not.Null, "No actual building for " + site.Id);
                result.Add(new AbandonmentBuilding { Site = site, Transform = target, Center = center, Rotation = rotation });
            }
            return result;
        }

        private static void AuditBuildingNeighbours(AlpineVillageRoot root, AbandonmentBuilding source,
            List<AbandonmentBuilding> buildings, AbandonmentReport report, List<string> failures)
        {
            for (int side = 0; side < 4; side++)
            {
                var entry = new AbandonmentVisibility { source = source.Site.Id,
                    side = new[] { "front", "right", "back", "left" }[side] };
                report.views.Add(entry);
                if (!TryAbandonmentFoot(root, source.Center, source.Rotation, source.Site.Size, side, out Vector3 foot))
                {
                    entry.blockedBy = "no free eye-height approach/side point";
                    failures.Add(entry.source + "/" + entry.side + ": " + entry.blockedBy);
                    continue;
                }
                entry.eye = foot + Vector3.up * EyeHeight;
                var candidates = new List<AbandonmentBuilding>(buildings);
                candidates.Remove(source);
                candidates.Sort((a, b) => (a.Center - foot).sqrMagnitude.CompareTo((b.Center - foot).sqrMagnitude));
                foreach (AbandonmentBuilding target in candidates)
                {
                    float distance = Vector3.Distance(entry.eye, target.Center + Vector3.up * 2f);
                    if (distance > 36f) break;
                    int rays = 0, columns = 0;
                    Vector3 toward = target.Center - foot;
                    Vector3 across = Vector3.Cross(Vector3.up, toward).normalized;
                    string blocker = string.Empty;
                    float span = Mathf.Min(target.Site.Size.x, target.Site.Size.y) * .32f;
                    for (int column = -1; column <= 1; column++)
                    {
                        bool columnVisible = false;
                        foreach (float fraction in new[] { .3f, .5f, .7f })
                        {
                            Vector3 point = target.Center + across * (column * span) +
                                Vector3.up * Mathf.Clamp(target.Site.Height * fraction, 1.05f, 4.2f);
                            // Continue through the footprint: a ruined room can
                            // have empty air at its centre and a visible rear wall.
                            Vector3 end = point + (point - entry.eye).normalized *
                                Mathf.Max(target.Site.Size.x, target.Site.Size.y);
                            if (!AbandonmentVisible(root.Plan, entry.eye, end, target.Transform, out string obstruction))
                            { blocker = obstruction; continue; }
                            rays++; columnVisible = true;
                        }
                        if (columnVisible) columns++;
                    }
                    bool visible = rays >= 3 && columns >= 2;
                    Vector3 targetPoint = target.Center + Vector3.up * Mathf.Min(2.5f, target.Site.Height * .5f);
                    if (!visible && target.Site.Abandoned != null && !target.Site.Abandoned.Closed)
                        visible = VisibleAbandonedRuinFaces(root.Plan, entry.eye, target, span,
                            out rays, out targetPoint, out blocker);
                    if (visible)
                    {
                        entry.visible = true; entry.target = target.Site.Id; entry.distance = distance;
                        entry.clearRays = rays; entry.targetPoint = targetPoint;
                        // Diagnostic only: the rendered crest/night frames judge actual contrast.
                        entry.crestFogTransmission = Mathf.Exp(-Mathf.Pow(.045f * distance, 2f));
                        break;
                    }
                    entry.blockedBy += (string.IsNullOrEmpty(entry.blockedBy) ? "" : "; ") +
                        target.Site.Id + ": " + blocker;
                }
                if (!entry.visible)
                    failures.Add(entry.source + "/" + entry.side + ": no visible building within 36 m; " + entry.blockedBy);
            }
        }

        private static bool VisibleAbandonedRuinFaces(AlpineVillagePlan plan, Vector3 eye,
            AbandonmentBuilding target, float minimumSpan, out int rays, out Vector3 targetPoint,
            out string blocker)
        {
            // A ruined house has real holes: the footprint-centred grid can aim
            // entirely through air below an intact roof quarter. Target actual
            // substantial faces instead, retaining the grid's minimum visible
            // width and independent rays; loose small debris cannot qualify.
            Vector3 across = Vector3.Cross(Vector3.up, target.Center - eye).normalized;
            var visible = new List<Vector3>();
            float left = float.PositiveInfinity, right = float.NegativeInfinity;
            rays = 0;
            targetPoint = target.Center;
            blocker = "no substantial remaining wall or roof is visible";
            foreach (Transform part in target.Transform)
            {
                // Household yards are nested beneath the building and are not
                // evidence that the building itself can be seen.
                MeshFilter filter = part.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null || part.GetComponent<MeshCollider>() == null) continue;
                Mesh mesh = filter.sharedMesh;
                Vector3[] vertices = mesh.vertices;
                int[] indices = mesh.triangles;
                for (int i = 0; i < vertices.Length; i++) vertices[i] = part.TransformPoint(vertices[i]);
                for (int i = 0; i < indices.Length; i += 3)
                {
                    Vector3 a = vertices[indices[i]], b = vertices[indices[i + 1]], c = vertices[indices[i + 2]];
                    Vector3 point = (a + b + c) / 3f;
                    Vector3 areaNormal = Vector3.Cross(b - a, c - a) * .5f;
                    if (point.y < target.Center.y + .9f || areaNormal.magnitude < .18f ||
                        Mathf.Abs(Vector3.Dot(areaNormal, (eye - point).normalized)) < .04f) continue;
                    bool distinct = true;
                    foreach (Vector3 existing in visible)
                        if (Vector3.Distance(existing, point) < .45f) { distinct = false; break; }
                    if (!distinct) continue;
                    Vector3 end = point + (point - eye).normalized * .06f;
                    if (!AbandonmentVisible(plan, eye, end, target.Transform, out string obstruction,
                        out Vector3 contact))
                    { blocker = obstruction; continue; }
                    // Hitting an unrelated fragment in front of the requested
                    // face does not turn a narrow sliver into a broad facade.
                    if (Vector3.Distance(contact, point) > .2f) continue;
                    foreach (Vector3 existing in visible)
                        if (Vector3.Distance(existing, contact) < .45f) { distinct = false; break; }
                    if (!distinct) continue;
                    visible.Add(contact);
                    float horizontal = Vector3.Dot(contact - target.Center, across);
                    left = Mathf.Min(left, horizontal);
                    right = Mathf.Max(right, horizontal);
                    rays = visible.Count;
                    if (rays < 3 || right - left < minimumSpan) continue;
                    targetPoint = Vector3.zero;
                    foreach (Vector3 hit in visible) targetPoint += hit;
                    targetPoint /= visible.Count;
                    return true;
                }
            }
            if (rays > 0)
                blocker += $"; substantial face hits {rays}, visible width {right - left:F2}/{minimumSpan:F2} m";
            return false;
        }

        private static bool AbandonmentVisible(AlpineVillagePlan plan, Vector3 eye, Vector3 target,
            Transform targetRoot, out string blocker)
            => AbandonmentVisible(plan, eye, target, targetRoot, out blocker, out _);

        private static bool AbandonmentVisible(AlpineVillagePlan plan, Vector3 eye, Vector3 target,
            Transform targetRoot, out string blocker, out Vector3 contact)
        {
            blocker = string.Empty;
            contact = default;
            if (!Physics.Linecast(eye, target, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
            { blocker = "ray missed actual target geometry"; return false; }
            if (!hit.transform.IsChildOf(targetRoot))
            { blocker = hit.collider.name; return false; }
            Vector3 endpoint = hit.point;
            contact = endpoint;
            Vector2 a = new Vector2(eye.x, eye.z), b = new Vector2(endpoint.x, endpoint.z);
            Vector2 delta = b - a;
            foreach (MountainRoadForestDescriptor tree in plan.Trees.CrownedTrees)
            {
                Vector2 center = new Vector2(tree.Position.x, tree.Position.z);
                float t = Mathf.Clamp01(Vector2.Dot(center - a, delta) / Mathf.Max(.001f, delta.sqrMagnitude));
                if (t <= .001f || t >= .999f) continue;
                float height = Mathf.Lerp(eye.y, endpoint.y, t) - tree.Position.y;
                if (height < 0f || height > tree.Height) continue;
                float radius = tree.TrunkRadius;
                // Conifers have layered irregular crowns without physics colliders.
                // Their outer cone is deliberately conservative for a clear-view claim.
                if (height >= tree.Height * .15f)
                    radius = Mathf.Max(radius, tree.CrownRadius * Mathf.Clamp01((1f - height / tree.Height) / .65f) + .15f);
                if (Vector2.Distance(a + delta * t, center) <= radius)
                { blocker = tree.StableId; return false; }
            }
            return true;
        }

        private static Vector2 AbandonmentSide(int side) => side == 0 ? Vector2.up :
            side == 1 ? Vector2.right : side == 2 ? Vector2.down : Vector2.left;

        private static bool TryAbandonmentFoot(AlpineVillageRoot root, Vector3 center, Quaternion rotation,
            Vector2 size, int side, out Vector3 point, bool composition = false)
        {
            Vector2 direction = AbandonmentSide(side);
            Vector2 tangent = new Vector2(direction.y, -direction.x);
            float width = side % 2 == 0 ? size.x : size.y;
            float[] setbacks = composition ? new[] { 9f, 7f, 5f, 3f } : new[] { 3f, 1.6f, 4.5f, 6f };
            foreach (float setback in setbacks)
            foreach (float offset in new[] { 0f, -.3f, .3f })
            {
                Vector2 local = Vector2.Scale(direction, size * .5f + Vector2.one * setback) + tangent * (offset * width);
                point = center + rotation * new Vector3(local.x, 0f, local.y);
                point.y = AlpineVillageTerrainSampler.SampleHeight(root.Plan, new Vector2(point.x, point.z));
                if (root.World.WalkableArea.Contains(point, .3f) && AbandonmentCapsuleFree(point)) return true;
            }
            point = center;
            return false;
        }

        private static bool AbandonmentCapsuleFree(Vector3 point) => !Physics.CheckCapsule(
            point + Vector3.up * .55f, point + Vector3.up * 1.5f, .28f, ~0, QueryTriggerInteraction.Ignore);

        private static Shot AbandonmentShot(AlpineVillageRoot root, string name, Vector3 foot,
            Vector3 target, float fov, bool trough = false, bool crest = false, bool night = false)
        {
            bool moved = false;
            int frames = 0;
            return Shot.At(name, foot + Vector3.up * EyeHeight, target, fov, 0, () =>
            {
                if (!moved)
                {
                    if (night && GameSessionState.GameTimeOfDayMinutes < 21d * 60d)
                        GameSessionState.AdvanceGameTime((float)((21d * 60d - GameSessionState.GameTimeOfDayMinutes) /
                            GameTimeState.GameMinutesPerRealSecond));
                    root.SetWarmthGrade(0f);
                    root.Player.Motor.Teleport(foot + Vector3.up * PlayerFactory.GroundedRootOffset);
                    moved = true;
                }
                bool ready = ++frames > 12 && (!trough || root.StormWave <= GustTroughWave) &&
                    (!crest || root.StormWave >= GustCrestWave);
                if (ready)
                    foreach (Renderer renderer in root.Player.GameObject.GetComponentsInChildren<Renderer>(true))
                        renderer.enabled = false;
                return ready;
            });
        }

        [Serializable]
        private sealed class AbandonmentVisibility
        {
            public string source, side, target, blockedBy;
            public Vector3 eye, targetPoint;
            public bool visible;
            public int clearRays;
            public float distance, crestFogTransmission;
        }

        [Serializable]
        private sealed class AbandonmentReport
        {
            public Rect coreTerrainBounds;
            public List<AbandonmentVisibility> views = new List<AbandonmentVisibility>();
            public string[] failures;
        }
    }
}

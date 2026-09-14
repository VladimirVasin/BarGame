using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        private static void VerifyEastOpenForefield(CityGameRoot city, CityEastExitPlan exit)
        {
            foreach (bool addAfterExclusion in new[] { false, true })
            {
                var mask = new RoadWalkableArea();
                if (!addAfterExclusion) mask.Add(new Rect(0f, 0f, 10f, 10f));
                mask.Exclude(new Rect(4f, 4f, 2f, 2f));
                if (addAfterExclusion) mask.Add(new Rect(0f, 0f, 10f, 10f));
                Assert.That(mask.Contains(new Vector3(5f, 0f, 5f), .35f), Is.False,
                    "Preserving radius traversal around a cut must never reopen the excluded solid.");
                foreach (float z in new[] { 2f, 8f })
                for (float x = 3.5f; x < 6.5f; x += .025f)
                {
                    Vector3 from = new Vector3(x, 0f, z), next = from + Vector3.right * .025f;
                    Assert.That(mask.Constrain(from, next, .35f).x, Is.EqualTo(next.x).Within(.001f),
                        "A rectangular solid must not extend an invisible radius wall through its empty corner regions.");
                }
            }
            CharacterController body = city.Player.Motor.GetComponent<CharacterController>();
            Assert.That(body, Is.Not.Null);
            float radius = body.radius;
            CityEastExitDressingPlan dressing = CityEastExitDressingPlan.Create(exit);
            Assert.That(city.Night, Is.Not.Null);
            Bounds[] lampPoles = city.Night.Plan.StreetLamps.Select(lamp =>
                CityStaticCollisionBuilder.CreateLowerPoleBounds(lamp.Position)).ToArray();
            // Existing furniture and the authored street-lamp pole footprints
            // require walking around a visible solid. No collider hierarchy or
            // name can exempt unrelated/invisible obstructions from this sweep.
            bool Occupied(Vector2 point)
            {
                Rect booth = exit.BoothBounds;
                foreach (Bounds pole in lampPoles)
                {
                    Vector3 center = new Vector3(point.x, pole.center.y, point.y);
                    if ((pole.ClosestPoint(center) - center).sqrMagnitude <= radius * radius) return true;
                }
                return point.x >= booth.xMin - radius && point.x <= booth.xMax + radius &&
                    point.y >= booth.yMin - radius && point.y <= booth.yMax + radius ||
                    dressing.BlocksStandingAt(point, radius);
            }
            float Ground(Vector2 point)
            {
                if (exit.RoadBounds.Contains(point)) return exit.SampleRoadTop(point.x, point.y);
                if (CityTerrainSurfacePlan.TrySampleGroundTop(city.Layout, point, out float top, out _)) return top;
                Assert.That(city.Layout.ElevationPlan.TrySampleSurface(point, CitySurfaceRole.RoadTop, out top, out _),
                    Is.True, "The foreground sweep must stay on real terrain or the adjoining street: " + point);
                return top;
            }
            void Lane(Vector2 from, Vector2 to, string id)
            {
                int count = Mathf.CeilToInt(Vector2.Distance(from, to) / .25f);
                for (int step = 0; step < count; step++)
                {
                    Vector2 a = Vector2.Lerp(from, to, step / (float)count);
                    Vector2 b = Vector2.Lerp(from, to, (step + 1f) / count);
                    if (Occupied(a) || Occupied(b)) continue;
                    float ay = Ground(a), by = Ground(b);
                    Vector3 first = new Vector3(a.x, ay, a.y), next = new Vector3(b.x, by, b.y);
                    Vector3 constrained = city.World.WalkableArea.Constrain(first, next, radius);
                    Assert.That(new Vector2(constrained.x - next.x, constrained.z - next.z).sqrMagnitude,
                        Is.LessThan(.000001f), "A hidden walk-mask boundary cuts " + id + " at " + first);
                    Vector3 travel = next - first;
                    // Match the controller's step clearance: short curbs and
                    // embedded footing bases are ordinary stepping surfaces.
                    // Full-height invisible walls still meet this actual body.
                    Vector3 lower = first + body.center - Vector3.up * (body.height * .5f - radius) +
                        Vector3.up * (body.stepOffset + .015f);
                    Vector3 upper = first + body.center + Vector3.up * (body.height * .5f - radius);
                    foreach (RaycastHit hit in Physics.CapsuleCastAll(lower, upper, radius, travel.normalized,
                        travel.magnitude, ~0, QueryTriggerInteraction.Ignore))
                    {
                        if (hit.collider.transform.IsChildOf(city.Player.GameObject.transform) ||
                            hit.collider.transform.IsChildOf(city.EastGuards.transform) || hit.normal.y > .7f) continue;
                        Assert.Fail("The hero's body hits an unplanned wall in " + id + " at " + first +
                            ": " + hit.collider.name + " at " + hit.point);
                    }
                }
            }
            float fenceSideX = exit.CheckpointPosition.x - .21f - radius - .08f;
            Lane(new Vector2(fenceSideX, exit.YardBounds.yMin + 1f),
                new Vector2(fenceSideX, exit.NorthYardBounds.yMax - 1f), "full west checkpoint frontage");
            foreach (float laneX in new[] { exit.YardBounds.xMin + 2.4f, exit.YardBounds.xMin + 3.75f,
                exit.YardBounds.xMin + 5.1f })
                Lane(new Vector2(laneX, exit.YardBounds.yMin + 1f),
                    new Vector2(laneX, exit.NorthYardBounds.yMax - 1f), "continuous pedestrian strip at X=" + laneX);
            Lane(new Vector2(exit.CheckpointPosition.x - 5.2f, exit.YardBounds.yMin - .7f),
                new Vector2(exit.YardBounds.xMax - 1f, exit.YardBounds.yMin - .7f), "church side of the south return fence");
            foreach (float offset in new[] { -5f, 6.6f, 10f })
                Lane(new Vector2(exit.ApproachStart.x - city.Layout.RoadWidth * .5f, exit.ApproachStart.z + offset),
                    new Vector2(fenceSideX, exit.ApproachStart.z + offset), "street to off-asphalt fence " + offset);
            // Cover every terrain-cell join along the outer frontage as well
            // as the road mouth: the old rectangular mask could trap a body
            // where two visually continuous ground cells met.
            for (float z = exit.YardBounds.yMin + 2f; z < exit.NorthYardBounds.yMax - 2f; z += city.Layout.NodeSpacing.y)
                Lane(new Vector2(exit.ApproachStart.x - .8f, z), new Vector2(fenceSideX, z),
                    "main-road edge to west fence at Z=" + z);
            for (float joint = exit.YardBounds.yMin + city.Layout.NodeSpacing.y;
                joint < exit.NorthYardBounds.yMax - 1f; joint += city.Layout.NodeSpacing.y)
            foreach (float offset in new[] { -.02f, 0f, .02f })
            {
                float z = joint + offset;
                Lane(new Vector2(exit.ApproachStart.x - .8f, z), new Vector2(fenceSideX, z),
                    "street/terrain-cell corner at Z=" + joint + "; offset=" + offset);
            }
            Debug.Log("EAST OPEN FOREGROUND: actual hero capsule and small-step walk constraint reach the accessible fence frontage and church return.");
        }

        private static float EastSwaleBroadSection(CityEastSwalePlan swale, float amount)
        {
            float first = swale.StartZ + 38f, last = swale.EndZ - 10f;
            float z = Mathf.Lerp(first, last, amount);
            foreach (float crossing in swale.CrossingZ)
                if (Mathf.Abs(z - crossing) < 8f)
                    z = crossing + 8f <= last ? crossing + 8f : crossing - 8f;
            return z;
        }

        private static void VerifyEastSwale(CityGameRoot city, CityEastExitPlan exit,
            CityMapCityTeleportGround landing)
        {
            CityEastSwalePlan swale = exit.Swale;
            Transform litterRoot = GameObject.Find(CityEastLitterWorldBuilder.RootName).transform;
            var litterSolids = CityEastLitterPlan.Create(exit).Parts.Where(part => part.Item.Solid)
                .ToDictionary(part => part.Id, StringComparer.Ordinal);
            Assert.That(swale, Is.Not.Null);
            Assert.That(swale.CrossingZ.Count, Is.GreaterThanOrEqualTo(2),
                "A long lowered verge needs several broad pedestrian crossings.");
            foreach (CityFringeYardDescriptor yard in city.World.FringeYardPlan.Yards)
            foreach (CityFringeYardPartDescriptor part in yard.Parts)
                if (yard.Kind == CityFringeYardKind.EastUtilityEdge &&
                    part.Style == CityFringeYardStyle.ServiceGround &&
                    !part.Footprint.Overlaps(exit.RoadBounds))
                    Assert.That(part.Footprint.xMin, Is.GreaterThan(exit.CheckpointPosition.x),
                        "An old service apron must not cross the public swale and continue through the closed fence.");
            MeshFilter ground = GameObject.Find(CityFringeYardGroundWorldBuilder.GenericGroundObjectName)
                .GetComponent<MeshFilter>();
            MeshCollider collider = ground.GetComponent<MeshCollider>();
            Assert.That(collider, Is.Not.Null);
            Vector3[] vertices = ground.sharedMesh.vertices.Select(ground.transform.TransformPoint).ToArray();
            int[] sourceTriangles = ground.sharedMesh.triangles;
            var triangles = new System.Collections.Generic.List<int>();
            Rect sampleBand = swale.Bounds;
            sampleBand.xMin -= 1f; sampleBand.xMax += 1f;
            for (int i = 0; i < sourceTriangles.Length; i += 3)
            {
                Vector3 a = vertices[sourceTriangles[i]], b = vertices[sourceTriangles[i + 1]],
                    c = vertices[sourceTriangles[i + 2]];
                if (Mathf.Max(a.x, b.x, c.x) < sampleBand.xMin ||
                    Mathf.Min(a.x, b.x, c.x) > sampleBand.xMax ||
                    Mathf.Max(a.z, b.z, c.z) < sampleBand.yMin ||
                    Mathf.Min(a.z, b.z, c.z) > sampleBand.yMax) continue;
                triangles.Add(sourceTriangles[i]); triangles.Add(sourceTriangles[i + 1]); triangles.Add(sourceTriangles[i + 2]);
            }

            foreach (float amount in new[] { .15f, .50f, .85f })
            {
                float z = EastSwaleBroadSection(swale, amount);
                float center = swale.CenterX(z), half = swale.HalfWidth(z);
                Assert.That(half * 2f, Is.InRange(3f, 5.01f), "Broad swale width at Z=" + z);
                float west = Ground(new Vector2(center - half - .05f, z));
                float east = Ground(new Vector2(center + half + .05f, z));
                float bed = Ground(new Vector2(center, z));
                Assert.That((west + east) * .5f - bed, Is.InRange(.30f, .52f),
                    "The visible ground must actually descend between its shoulders, not carry a raised drain sheet: Z=" + z);
                float previous = west;
                for (int station = 0; station <= 8; station++)
                {
                    float x = Mathf.Lerp(center - half, center + half, station / 8f);
                    Vector2 point = new Vector2(x, z);
                    float top = Ground(point);
                    Assert.That(top, Is.EqualTo(exit.SampleGroundTop(point)).Within(.03f),
                        "The physical swale and shared ground sampler disagree at " + point);
                    if (station > 0)
                        Assert.That(Mathf.Abs(top - previous) / (half / 4f), Is.LessThan(.50f),
                            "A dry roadside hollow must have walkable banks at " + point);
                    previous = top;
                }
            }

            foreach (float z in swale.CrossingZ)
            {
                float center = swale.CenterX(z), half = swale.HalfWidth(z);
                foreach (float offset in new[] { -1.5f, 0f, 1.5f })
                    Assert.That(-swale.SampleOffset(new Vector2(swale.CenterX(z + offset), z + offset)),
                        Is.InRange(0f, .065f), "The whole broad crossing core stays shallow, not just its centre.");
                float previous = Ground(new Vector2(center - half - .5f, z));
                for (float x = center - half - .25f; x <= center + half + .5f; x += .25f)
                {
                    Vector2 point = new Vector2(x, z);
                    float top = Ground(point);
                    Assert.That(Mathf.Abs(top - previous) / .25f, Is.LessThan(.25f),
                        "Crossing the swale must not introduce a kerb or steep transverse ramp at " + point);
                    previous = top;
                    Assert.That(landing.TryResolveStandingPosition(point, out Vector3 feet), Is.True);
                    Assert.That(Vector2.Distance(point, new Vector2(feet.x, feet.z)), Is.LessThan(.02f),
                        "The crossing must be accessible at its actual position.");
                }
                previous = Ground(new Vector2(swale.CenterX(z - 5f), z - 5f));
                for (float sampleZ = z - 4.75f; sampleZ <= z + 5f; sampleZ += .25f)
                {
                    float top = Ground(new Vector2(swale.CenterX(sampleZ), sampleZ));
                    Assert.That(Mathf.Abs(top - previous) / .25f, Is.LessThan(.38f),
                        "A crossing must return gently to the lower bed, without a vertical dam at Z=" + sampleZ);
                    previous = top;
                }
            }
            for (float z = exit.YardBounds.yMin; z <= exit.NorthYardBounds.yMax; z += 4f)
            foreach (float x in new[] { exit.YardBounds.xMin, exit.YardBounds.xMin + 5.5f,
                exit.CheckpointPosition.x - .4f, exit.CheckpointPosition.x })
                Assert.That(swale.SampleOffset(new Vector2(x, z)), Is.EqualTo(0f).Within(.001f),
                    "The existing street-side path and fence foundations stay at their original height.");
            foreach (float x in new[] { exit.YardBounds.xMin + 5.5f, exit.YardBounds.xMin + 8f,
                exit.CheckpointPosition.x })
                Assert.That(swale.SampleOffset(new Vector2(x, exit.YardBounds.yMin)), Is.EqualTo(0f),
                    "The church's independently graded garden seam remains intact.");
            Debug.Log("EAST SWALE: measured render/standing-ground depression, broad gentle crossings and unchanged street/fence/garden edges.");

            float Ground(Vector2 point)
            {
                float top = float.NegativeInfinity;
                for (int i = 0; i < triangles.Count; i += 3)
                {
                    Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                    Vector2 ab = new Vector2(b.x - a.x, b.z - a.z), ac = new Vector2(c.x - a.x, c.z - a.z);
                    Vector2 ap = point - new Vector2(a.x, a.z);
                    float area = ab.x * ac.y - ab.y * ac.x;
                    if (Mathf.Abs(area) < .000001f) continue;
                    float u = (ap.x * ac.y - ap.y * ac.x) / area;
                    float v = (ab.x * ap.y - ab.y * ap.x) / area;
                    if (u < -.00001f || v < -.00001f || u + v > 1.00001f) continue;
                    top = Mathf.Max(top, a.y + u * (b.y - a.y) + v * (c.y - a.y));
                }
                Assert.That(float.IsNegativeInfinity(top), Is.False, "The real yard skin must cover " + point);
                var ray = new Ray(new Vector3(point.x, top + 2f, point.y), Vector3.down);
                Assert.That(collider.Raycast(ray, out RaycastHit hit, 4f), Is.True,
                    "The lowered visible ground needs its own standing collider at " + point);
                Assert.That(hit.point.y, Is.EqualTo(top).Within(.03f),
                    "Visual and collision swale heights differ at " + point);
                foreach (RaycastHit other in Physics.RaycastAll(ray, 4f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (other.collider.transform.IsChildOf(city.Player.GameObject.transform) ||
                        other.collider.transform.IsChildOf(city.EastGuards.transform) || other.normal.y < .7f) continue;
                    // VerifyEastLitter has measured these finite bodies and
                    // their map exclusions. They are ordinary objects resting
                    // in the swale, not a second standing-ground skin.
                    if (other.collider.transform.parent == litterRoot &&
                        litterSolids.TryGetValue(other.collider.name, out CityEastLitterPart litter) &&
                        litter.Footprint.Contains(point)) continue;
                    Assert.That(other.point.y, Is.LessThanOrEqualTo(top + .035f),
                        "A competing raised surface must not leave the hero above the hollow: " + other.collider.name + " at " + point);
                }
                return top;
            }
        }

        private static void RecordEastMotorTrace(PlayerMotor motor, int frame,
            System.Collections.Generic.Queue<string> trace)
        {
            CharacterController body = motor.GetComponent<CharacterController>();
            PlayerMotorContactSample contact = motor.LastContact;
            trace.Enqueue("frame=" + frame + "; pos=" + motor.transform.position.ToString("F5") +
                "; input=" + GameInput.ReadMovement() + "; planar=" + motor.PlanarVelocity.ToString("F5") +
                "; controllerVelocity=" + body.velocity.ToString("F5") + "; grounded=" + motor.IsGrounded +
                "; flags=" + contact.Flags + "/" + body.collisionFlags + "; side=" + contact.HasSideCollision +
                "; area=" + contact.HasAreaRefusal + "; push=" + contact.ConstraintPush.ToString("F5") +
                "; normal=" + contact.Normal.ToString("F5") + "; hit=" + contact.Point.ToString("F5"));
            while (trace.Count > 8) trace.Dequeue();
        }

        private static void DiagnoseEastWalkingStall(CityGameRoot city, Camera camera, string lane,
            System.Collections.Generic.Queue<string> trace)
        {
            PlayerMotor motor = city.Player.Motor;
            CharacterController body = motor.GetComponent<CharacterController>();
            Debug.Log("EAST WALK STALL " + lane + "\n" + string.Join("\n", trace));
            Vector3 center = body.transform.TransformPoint(body.center);
            Vector3 lower = center - Vector3.up * (body.height * .5f - body.radius);
            Vector3 upper = center + Vector3.up * (body.height * .5f - body.radius);
            Vector3 desired = motor.transform.position + motor.transform.forward * .025f;
            Debug.Log("EAST WALK STALL body: radius=" + body.radius + "; step=" + body.stepOffset +
                "; skin=" + body.skinWidth + "; lower=" + lower.ToString("F5") + "; upper=" + upper.ToString("F5") +
                "; desired=" + desired.ToString("F5") + "; constrained=" +
                city.World.WalkableArea.Constrain(motor.transform.position, desired, body.radius).ToString("F5"));
            foreach (RaycastHit hit in Physics.CapsuleCastAll(lower, upper, body.radius, motor.transform.forward,
                .6f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(motor.transform)) continue;
                Debug.Log("EAST WALK STALL raw capsule: " + Describe(hit.collider) + "; distance=" + hit.distance +
                    "; point=" + hit.point.ToString("F5") + "; normal=" + hit.normal.ToString("F5"));
            }
            foreach (Collider collider in Physics.OverlapCapsule(lower, upper, body.radius, ~0, QueryTriggerInteraction.Ignore))
                if (!collider.transform.IsChildOf(motor.transform))
                    Debug.Log("EAST WALK STALL raw overlap: " + Describe(collider));
            CaptureCurrentCamera(camera, SceneIds.City, "east-exit-stalled-" + lane);

            string Describe(Collider collider)
            {
                Renderer renderer = collider.GetComponent<Renderer>();
                return collider.name + " (" + collider.GetType().Name + "); parent=" + collider.transform.parent?.name +
                    "; bounds=" + collider.bounds + "; localRenderer=" + (renderer == null ? "none" : renderer.enabled.ToString());
            }
        }

        private static void VerifyEastGroundTransition(CityGameRoot city, CityEastExitPlan exit,
            CityMapCityTeleportGround landing)
        {
            CityEastGroundTransitionPlan transition = CityEastGroundTransitionPlan.Create(city.Layout);
            Assert.That(transition.IsEnabled, Is.True);
            MeshFilter[] surfaces =
            {
                GameObject.Find(CityChurchGroundWorldBuilder.ObjectName).GetComponent<MeshFilter>(),
                GameObject.Find(CityFringeYardGroundWorldBuilder.GenericGroundObjectName).GetComponent<MeshFilter>()
            };
            Texture texture = null;
            foreach (MeshFilter filter in surfaces)
            {
                int expectedSlots = filter.name == CityFringeYardGroundWorldBuilder.GenericGroundObjectName ? 3 : 2;
                Assert.That(filter.sharedMesh.subMeshCount, Is.EqualTo(expectedSlots),
                    "The blended ground replaces faces in the existing skin: " + filter.name);
                Assert.That(filter.sharedMesh.GetTriangles(1), Is.Not.Empty);
                Assert.That(filter.GetComponent<MeshCollider>(), Is.Not.Null,
                    "The original standing surface remains with its rendered ground.");
                Material[] materials = filter.GetComponent<Renderer>().sharedMaterials;
                Assert.That(materials.Length, Is.EqualTo(expectedSlots));
                var properties = new MaterialPropertyBlock();
                filter.GetComponent<Renderer>().GetPropertyBlock(properties, 1);
                Texture assigned = properties.GetTexture("_BaseMap");
                Assert.That(assigned, Is.Not.Null, "Transition material needs its baked colour surface: " + filter.name +
                    "; tint=" + properties.GetColor("_BaseColor") + "; uv=" + properties.GetVector("_BaseMap_ST"));
                Assert.That(properties.GetColor("_BaseColor"), Is.EqualTo(Color.white));
                if (texture == null) texture = assigned;
                else Assert.That(assigned, Is.SameAs(texture), "Both grounds must share one transition texture.");
            }

            // Sample the real skin on both sides of the visible former seam.
            // This verifies its original collider too, so a visually smooth
            // replacement cannot create a walking gap or a floating surface.
            for (int sample = 0; sample < 4; sample++)
            {
                float x = exit.YardBounds.xMin + 2f + sample * 2f;
                float[] heights = new float[2];
                Vector2[] uvs = new Vector2[2];
                for (int side = 0; side < 2; side++)
                {
                    Vector2 point = new Vector2(x, transition.SeamZ + (side == 0 ? -.015f : .015f));
                    Assert.That(TryReadEastSurface(surfaces[side], point, out heights[side], out uvs[side]), Is.True,
                        "Both visible ground skins must reach the shared seam at " + point);
                    var ray = new Ray(new Vector3(x, heights[side] + 2f, point.y), Vector3.down);
                    Assert.That(surfaces[side].GetComponent<MeshCollider>().Raycast(ray, out RaycastHit hit, 4f), Is.True);
                    Assert.That(heights[side], Is.EqualTo(hit.point.y).Within(.015f),
                        "Transition render and collision differ at " + point);
                    Assert.That(landing.TryResolveStandingPosition(point, out _), Is.True,
                        "The church/post seam remains walkable at " + point);
                }
                Assert.That(heights[0], Is.EqualTo(heights[1]).Within(.05f), "The former seam must not become a step.");
                Assert.That(uvs[0].x, Is.EqualTo(uvs[1].x).Within(.0001f), "The shared texture must stay in phase.");
                Assert.That(Mathf.Abs(uvs[0].y - uvs[1].y), Is.LessThan(.006f), "Texture V must be continuous across the seam.");
            }
            foreach (float offset in new[] { -2f, 2f })
            {
                Vector2 point = new Vector2(exit.YardBounds.xMin + 2f, transition.SeamZ + offset);
                Assert.That(landing.TryResolveStandingPosition(point, out Vector3 feet), Is.True);
                Assert.That(FootstepGroundOverlay.TryFind(feet, out FootstepGroundKind kind), Is.True);
                Assert.That(kind, Is.EqualTo(transition.SoilWeight(point) >= .5f ? FootstepGroundKind.Soil : FootstepGroundKind.Grass),
                    "Footsteps must follow the visible material outline at " + point);
            }

            // PS1 screen snapping exposes a T-junction even when height/UV
            // probes agree. The close, unpaved seam needs the same vertices
            // on both terrain owners, including the yard's extra detail cuts.
            var edges = new System.Collections.Generic.SortedDictionary<float, Vector3>[2];
            for (int side = 0; side < 2; side++)
            {
                edges[side] = new System.Collections.Generic.SortedDictionary<float, Vector3>();
                Mesh mesh = surfaces[side].sharedMesh;
                Vector3[] vertices = mesh.vertices;
                foreach (int index in mesh.GetTriangles(1))
                {
                    Vector3 vertex = vertices[index];
                    if (Mathf.Abs(vertex.z - transition.SeamZ) > .00001f ||
                        vertex.x < exit.CheckpointPosition.x - 7f || vertex.x > exit.CheckpointPosition.x + 8f) continue;
                    edges[side][Mathf.Round(vertex.x * 10000f) / 10000f] = vertex;
                }
            }
            Assert.That(edges[0].Count, Is.GreaterThan(4));
            Assert.That(edges[0].Keys, Is.EquivalentTo(edges[1].Keys),
                "Different seam edge subdivisions open black cracks under PS1 vertex snapping.");
            foreach (var edge in edges[0])
                Assert.That(Vector3.Distance(edge.Value, edges[1][edge.Key]), Is.LessThan(.0001f),
                    "Both ground skins must snap the exact same shared edge position.");
        }

        private static bool TryReadEastSurface(MeshFilter filter, Vector2 point, out float height, out Vector2 uv)
        {
            Mesh mesh = filter.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Vector2[] coordinates = mesh.uv;
            int[] triangles = mesh.GetTriangles(1);
            height = 0f; uv = default;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = filter.transform.TransformPoint(vertices[triangles[i]]);
                Vector3 b = filter.transform.TransformPoint(vertices[triangles[i + 1]]);
                Vector3 c = filter.transform.TransformPoint(vertices[triangles[i + 2]]);
                Vector2 ab = new Vector2(b.x - a.x, b.z - a.z), ac = new Vector2(c.x - a.x, c.z - a.z);
                Vector2 ap = point - new Vector2(a.x, a.z);
                float area = ab.x * ac.y - ab.y * ac.x;
                if (Mathf.Abs(area) < .000001f) continue;
                float u = (ap.x * ac.y - ap.y * ac.x) / area;
                float v = (ab.x * ap.y - ab.y * ap.x) / area;
                if (u < -.00001f || v < -.00001f || u + v > 1.00001f) continue;
                height = a.y + u * (b.y - a.y) + v * (c.y - a.y);
                uv = coordinates[triangles[i]] * (1f - u - v) + coordinates[triangles[i + 1]] * u + coordinates[triangles[i + 2]] * v;
                return true;
            }
            return false;
        }

        private static void VerifyEastLocalLights(CityGameRoot city, bool day)
        {
            Light[] lights = city.World.EastExit.Root.GetComponentsInChildren<Light>(true);
            Assert.That(lights.Length, Is.EqualTo(2), "Only the two accepted local site lights are added to the post.");
            CityNightAtmosphere atmosphere = UnityEngine.Object.FindAnyObjectByType<CityNightAtmosphere>();
            Assert.That(atmosphere, Is.Not.Null);
            Assert.That(atmosphere.RealtimeLightCount, Is.EqualTo(CityNightAtmosphere.MaximumRealtimeLights),
                "Local fixtures must not consume or expand the existing street/bar pool.");
            foreach (Light light in lights)
            {
                bool canopy = light.name == CityEastExitWorldBuilder.CanopyLightName;
                Assert.That(canopy || light.name == CityEastExitWorldBuilder.ServiceLightName, Is.True);
                float intensity = canopy ? CityEastExitWorldBuilder.CanopyNightIntensity : CityEastExitWorldBuilder.ServiceNightIntensity;
                Assert.That(light.enabled && light.gameObject.activeInHierarchy, Is.True);
                Assert.That(light.intensity, Is.EqualTo(intensity * GameTimeDayNightRules.FixtureFactor(
                    CityNightSiteLightRegistry.NightFactor)).Within(.001f));
                Assert.That(light.intensity, Is.GreaterThanOrEqualTo(intensity * GameTimeDayNightRules.DayFixtureFloor - .001f));
                Assert.That(light.type, Is.EqualTo(LightType.Spot));
                Assert.That(light.shadows, Is.EqualTo(LightShadows.Hard));
                Assert.That(light.range, Is.InRange(5f, 6.5f));
                Assert.That(Vector3.Dot(light.transform.forward, Vector3.down), Is.GreaterThan(.8f));
                CityLightHalo halo = light.GetComponentInChildren<CityLightHalo>(true);
                Assert.That(halo != null && halo.IsVisible, Is.True, "Every local fixture keeps its small daytime fog halo.");
                Transform fixture = light.transform.parent;
                Transform anchor = fixture.GetComponentsInChildren<Transform>(true).Single(part =>
                    part.name == (canopy ? "CanopyLampLightAnchor" : "ServiceWallLampLightAnchor"));
                Assert.That(Vector3.Distance(anchor.position, light.transform.position), Is.LessThan(.001f),
                    "The Light must originate at the imported lens in world metres.");
                Assert.That((light.transform.lossyScale - Vector3.one).sqrMagnitude, Is.LessThan(.000001f),
                    "Emitter and halo must stay outside the imported unit-scaled hierarchy.");
                Vector3 expectedAnchor = fixture.TransformPoint(canopy ? new Vector3(0f, -.065f, 0f) : new Vector3(0f, -.105f, -.195f));
                Assert.That(Vector3.Distance(anchor.position, expectedAnchor), Is.LessThan(.003f),
                    "An imported unit/axis error must not detach the emitter from its fixture.");
                Bounds bounds = default;
                bool found = false;
                foreach (MeshRenderer renderer in fixture.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!found) { bounds = renderer.bounds; found = true; }
                    else bounds.Encapsulate(renderer.bounds);
                }
                Assert.That(found, Is.True);
                Assert.That(bounds.size.magnitude, Is.InRange(.25f, .80f), "Local lamp must retain its actual modest metre scale.");
            }
            Debug.Log("EAST LOCAL LIGHTS: fixed canopy/shed fixtures and unchanged street pool at " + (day ? "day" : "night") + ".");
        }

        private static Vector3 ResolveEastLightGround(Light light)
        {
            Physics.SyncTransforms();
            Assert.That(Physics.Raycast(light.transform.position, light.transform.forward,
                out RaycastHit hit, light.range, ~0, QueryTriggerInteraction.Ignore), Is.True,
                light.name + " must aim at an actual supporting surface within its range.");
            Assert.That(hit.normal.y, Is.GreaterThan(.7f), light.name + " must reach its ground patch.");
            Debug.Log("EAST SERVICE LIGHT ground: emitter=" + light.transform.position + ", forward=" +
                light.transform.forward + ", surface=" + hit.collider.name + " at " + hit.point);
            return hit.point;
        }

        private static Vector3 ResolveEastServiceView(CityEastExitPlan exit, CityMapCityTeleportGround landing,
            Vector3 target)
        {
            // The old shot aimed at a separately estimated shed from the road
            // junction. Follow the actual lamp's illuminated ground from the
            // accessible side of the fence, aligned with this particular shed.
            foreach (float offset in new[] { 0f, -.7f, .7f, -1.4f, 1.4f, -2.1f, 2.1f })
            {
                Vector2 point = new Vector2(exit.CheckpointPosition.x - 1.3f, target.z + offset);
                if (!landing.TryResolveStandingPosition(point, out Vector3 feet)) continue;
                if ((new Vector2(feet.x, feet.z) - point).sqrMagnitude > .01f) continue;
                Vector3 eye = feet + Vector3.up * EyeHeight;
                // The boundary's conservative collision is deliberately solid;
                // the physical iron mesh has gaps. Check terrain occlusion
                // independently so an invisible wall proxy does not disqualify
                // every honest view through the closed fence.
                bool groundBlocked = false;
                Vector3 sight = target + Vector3.up * .45f - eye;
                foreach (RaycastHit hit in Physics.RaycastAll(eye, sight.normalized,
                    sight.magnitude - .2f, ~0, QueryTriggerInteraction.Ignore))
                    if (hit.collider.name == CityFringeYardGroundWorldBuilder.GenericGroundObjectName ||
                        hit.collider.name == CityChurchGroundWorldBuilder.ObjectName) groundBlocked = true;
                if (groundBlocked) continue;
                Debug.Log("EAST SERVICE VIEW: eye=" + eye + ", target=" + target + ", distance=" + sight.magnitude);
                return feet;
            }
            Assert.Fail("The first shed's real light patch needs a clear, accessible view beside the closed fence: " + target);
            return default;
        }

        private static EastSurfaceLightSample MeasureEastSurfaceLight(Camera camera, Light light, string phase)
        {
            bool enabled = light.enabled;
            try
            {
                Color32[] lit = ReadCanopyLightPixels(camera);
                light.enabled = false;
                Color32[] dark = ReadCanopyLightPixels(camera);
                int changed = 0;
                long delta = 0;
                for (int i = 0; i < lit.Length; i++)
                {
                    int difference = lit[i].r + lit[i].g + lit[i].b - dark[i].r - dark[i].g - dark[i].b;
                    if (difference <= 6) continue;
                    changed++; delta += difference;
                }
                // The authored emissive glass and halo are identical in these
                // consecutive renders: only illumination of real ground varies.
                Debug.Log("EAST SURFACE LIGHT " + light.name + " " + phase + ": pixels=" + changed + ", RGB delta=" + delta);
                return new EastSurfaceLightSample(light.name, phase, changed, delta);
            }
            finally { light.enabled = enabled; }
        }

        private readonly struct EastSurfaceLightSample
        {
            internal EastSurfaceLightSample(string name, string phase, int changed, long delta)
            { this.name = name; this.phase = phase; this.changed = changed; this.delta = delta; }
            private readonly string name, phase;
            private readonly int changed;
            private readonly long delta;
            internal void Verify()
            {
                Assert.That(changed, Is.GreaterThan(80), name + " must illuminate surfaces at " + phase + ".");
                Assert.That(delta, Is.GreaterThan(1600), name + " needs a readable pool at " + phase + ".");
            }
        }
    }
}

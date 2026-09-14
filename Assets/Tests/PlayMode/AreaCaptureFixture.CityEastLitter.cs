using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        private static void VerifyEastLitter(CityGameRoot city, CityEastExitPlan exit,
            CityMapCityTeleportGround landing)
        {
            CityLitterCatalog catalog = CityLitterCatalog.Load();
            CityEastLitterPlan plan = CityEastLitterPlan.Create(exit);
            CityEastLitterPlan rebuilt = CityEastLitterPlan.Create(CityEastExitPlanner.Create(city.Layout), catalog);
            GameObject root = GameObject.Find(CityEastLitterWorldBuilder.RootName);
            Assert.That(root, Is.Not.Null);
            Assert.That(catalog.Items.Count, Is.InRange(30, 40), "The pack must contain distinct authored variants.");
            Assert.That(plan.Parts.Count, Is.InRange(250, 330), "The reduced litter density must still cover the whole strip.");
            Assert.That(root.transform.childCount, Is.EqualTo(plan.Parts.Count));
            Assert.That(rebuilt.Parts.Count, Is.EqualTo(plan.Parts.Count));
            Assert.That(plan.Parts.Select(part => part.Item.Name).Distinct(),
                Is.EquivalentTo(catalog.Items.Select(item => item.Name)), "Declared variants must appear in the actual strip.");
            Assert.That(root.GetComponentsInChildren<Light>(true), Is.Empty);
            Assert.That(root.GetComponentsInChildren<AudioSource>(true), Is.Empty);
            Assert.That(root.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            Assert.That(root.GetComponentsInChildren<Animator>(true), Is.Empty);

            var placed = root.transform.Cast<Transform>().ToDictionary(item => item.name, StringComparer.Ordinal);
            MeshCollider[] support = city.World.Root.GetComponentsInChildren<MeshCollider>(true)
                .Where(collider => collider.name == CityFringeYardGroundWorldBuilder.GenericGroundObjectName ||
                    collider.name == CityChurchGroundWorldBuilder.ObjectName).ToArray();
            Assert.That(support, Is.Not.Empty, "The litter must rest on the scene's actual terrain collision.");
            var verticesByMesh = new Dictionary<Mesh, Vector3[]>();
            var firstMeshes = new Dictionary<string, Mesh[]>(StringComparer.Ordinal);
            var firstMaterials = new Dictionary<string, Material[]>(StringComparer.Ordinal);
            float minimumClearance = float.PositiveInfinity, maximumContactGap = 0f;
            for (int index = 0; index < plan.Parts.Count; index++)
            {
                CityEastLitterPart part = plan.Parts[index], again = rebuilt.Parts[index];
                Assert.That(again.Id, Is.EqualTo(part.Id));
                Assert.That(again.Item.Name, Is.EqualTo(part.Item.Name));
                Assert.That(again.Position, Is.EqualTo(part.Position), "Reconstruction must retain the litter's own deterministic layout.");
                Assert.That(again.Rotation, Is.EqualTo(part.Rotation));
                Assert.That(again.Scale, Is.EqualTo(part.Scale));
                Assert.That(plan.IsPlacementAllowed(part.Footprint, part.Item.Solid), Is.True,
                    "The entire item must clear asphalt, the fence, crossings and existing furniture: " + part.Id);
                Assert.That(part.Footprint.Overlaps(exit.ClearanceBounds), Is.False, part.Id);
                Assert.That(placed.TryGetValue(part.Id, out Transform instance), Is.True, part.Id);
                Assert.That(instance.position.x, Is.EqualTo(part.Position.x).Within(.001f), part.Id);
                Assert.That(instance.position.z, Is.EqualTo(part.Position.z).Within(.001f), part.Id);
                Assert.That(Quaternion.Angle(instance.rotation, part.Rotation), Is.LessThan(.02f),
                    "Actual rigid poses must retain the authored slope and yaw: " + part.Id);
                MeshFilter[] filters = instance.GetComponentsInChildren<MeshFilter>(true);
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                Assert.That(filters, Is.Not.Empty, "A placement declaration cannot substitute for visible geometry: " + part.Id);
                Assert.That(renderers, Is.Not.Empty, part.Id);
                Assert.That(renderers.All(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy), Is.True, part.Id);
                Mesh[] meshes = filters.Select(filter => filter.sharedMesh).ToArray();
                Material[] materials = renderers.SelectMany(renderer => renderer.sharedMaterials).ToArray();
                Assert.That(meshes.All(mesh => mesh != null), Is.True, part.Id);
                Assert.That(materials.All(material => material != null), Is.True, part.Id);
                if (firstMeshes.TryGetValue(part.Item.Name, out Mesh[] first))
                {
                    Assert.That(meshes, Is.EqualTo(first), "Rigid litter instances must reuse imported meshes: " + part.Id);
                    Assert.That(materials, Is.EqualTo(firstMaterials[part.Item.Name]),
                        "Litter must reuse materials rather than allocate per placement: " + part.Id);
                }
                else
                {
                    firstMeshes.Add(part.Item.Name, meshes);
                    firstMaterials.Add(part.Item.Name, materials);
                }

                bool firstVertex = true;
                Bounds measured = default;
                float contactGap = float.PositiveInfinity;
                Quaternion inverse = Quaternion.Inverse(instance.rotation);
                foreach (MeshFilter filter in filters)
                {
                    if (!verticesByMesh.TryGetValue(filter.sharedMesh, out Vector3[] vertices))
                    {
                        vertices = filter.sharedMesh.vertices;
                        verticesByMesh.Add(filter.sharedMesh, vertices);
                    }
                    foreach (Vector3 vertex in vertices)
                    {
                        Vector3 world = filter.transform.TransformPoint(vertex);
                        Vector3 metres = inverse * (world - instance.position);
                        if (firstVertex) { measured = new Bounds(metres, Vector3.zero); firstVertex = false; }
                        else measured.Encapsulate(metres);
                        float ground = float.NegativeInfinity;
                        var ray = new Ray(world + Vector3.up * 2f, Vector3.down);
                        foreach (MeshCollider collider in support)
                            if (collider.Raycast(ray, out RaycastHit hit, 4f)) ground = Mathf.Max(ground, hit.point.y);
                        Assert.That(float.IsNegativeInfinity(ground), Is.False,
                            "Every rendered vertex needs actual ground below it: " + part.Id + " at " + world);
                        float clearance = world.y - ground;
                        contactGap = Mathf.Min(contactGap, clearance);
                        minimumClearance = Mathf.Min(minimumClearance, clearance);
                    }
                }
                Assert.That(firstVertex, Is.False, part.Id);
                for (int axis = 0; axis < 3; axis++)
                    Assert.That(measured.size[axis], Is.EqualTo(part.Item.Bounds.size[axis] * part.Scale).Within(.012f),
                        "The placed geometry lost its authored metre scale: " + part.Id + "; axis=" + axis);
                Assert.That(contactGap, Is.InRange(-.015f, .04f),
                    "A rigid bottle, can or bicycle must touch the real terrain without sinking or hovering: " + part.Id);
                maximumContactGap = Mathf.Max(maximumContactGap, contactGap);
                Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
                if (part.Item.Solid)
                {
                    Assert.That(part.Footprint.Overlaps(plan.PedestrianCorridor), Is.False,
                        "Large discarded objects cannot obstruct the continuous walking corridor: " + part.Id);
                    Assert.That(colliders.Length, Is.EqualTo(1), "Visible large objects need one finite collision body: " + part.Id);
                    Assert.That(colliders[0], Is.TypeOf<BoxCollider>(), part.Id);
                    var box = (BoxCollider)colliders[0];
                    Assert.That(box.transform, Is.SameAs(instance), part.Id);
                    Assert.That(box.enabled && !box.isTrigger, Is.True, part.Id);
                    Assert.That(box.center, Is.EqualTo(part.Item.Bounds.center), part.Id);
                    Assert.That(box.size, Is.EqualTo(part.Item.Bounds.size),
                        "The solid must be bounded by the measured authored object, not an oversized invisible wall: " + part.Id);
                    Assert.That(landing.TryResolveStandingPosition(new Vector2(part.Position.x, part.Position.z), out _),
                        Is.False, "Map arrivals must exclude the same large objects: " + part.Id);
                }
                else Assert.That(colliders, Is.Empty, "Small litter must never catch an ordinary footstep: " + part.Id);
            }
            VerifyEastLitterDistribution(plan);
            Debug.Log("EAST LITTER: all authored variants visibly instantiated at metre scale, deterministic full-width distribution, " +
                "shared meshes/materials, passive small objects and clear solid routes verified; terrain minimum=" +
                minimumClearance + " m; largest contact gap=" + maximumContactGap + " m.");
        }

        private static void VerifyEastLitterDistribution(CityEastLitterPlan plan)
        {
            const int Bands = 5;
            var counts = new int[Bands];
            var area = new float[Bands];
            float width = plan.Bounds.width / Bands;
            foreach (CityEastLitterPart part in plan.Parts)
            {
                int band = Mathf.Clamp(Mathf.FloorToInt((part.Position.x - plan.Bounds.xMin) / width), 0, Bands - 1);
                Assert.That(part.Band, Is.EqualTo(band), "Band metadata must describe the actual position: " + part.Id);
                if (!part.Item.Solid) counts[band]++;
            }
            // Compare density on available land, not raw counts: crossings,
            // the checkpoint and the existing vegetation remove unequal areas.
            const float Step = .45f, ProbeSize = .24f;
            for (int band = 0; band < Bands; band++)
            for (float x = plan.Bounds.xMin + band * width + Step * .5f;
                 x < plan.Bounds.xMin + (band + 1) * width; x += Step)
            for (float z = plan.Bounds.yMin + Step * .5f; z < plan.Bounds.yMax; z += Step)
                if (plan.IsPlacementAllowed(new Rect(x - ProbeSize * .5f, z - ProbeSize * .5f, ProbeSize, ProbeSize), false))
                    area[band] += Step * Step;
            var density = new float[Bands];
            for (int band = 0; band < Bands; band++)
            {
                Assert.That(area[band], Is.GreaterThan(0f), "Each width band must retain usable litter ground.");
                Assert.That(counts[band], Is.GreaterThan(0), "The street-side and middle bands need visible small litter too.");
                density[band] = counts[band] / area[band];
            }
            Assert.That(density.Max() / density.Min(), Is.LessThanOrEqualTo(2f),
                "Small objects must have comparable available-area density across the entire strip, not gather by the fence.");
            float minimumGap = float.PositiveInfinity;
            string nearestPair = null;
            for (int first = 0; first < plan.Parts.Count; first++)
            for (int second = first + 1; second < plan.Parts.Count; second++)
            {
                Rect a = plan.Parts[first].Footprint, b = plan.Parts[second].Footprint;
                float dx = Mathf.Max(0f, a.xMin - b.xMax, b.xMin - a.xMax);
                float dz = Mathf.Max(0f, a.yMin - b.yMax, b.yMin - a.yMax);
                float gap = Mathf.Sqrt(dx * dx + dz * dz);
                if (gap >= minimumGap) continue;
                minimumGap = gap;
                nearestPair = plan.Parts[first].Id + " / " + plan.Parts[second].Id;
            }
            Assert.That(minimumGap, Is.GreaterThanOrEqualTo(CityEastLitterPlan.MinimumItemClearance - .001f),
                "Full object footprints need visible separation rather than tight little piles: " + nearestPair);
            for (int section = 0; section < 3; section++)
            {
                float start = Mathf.Lerp(plan.Bounds.yMin, plan.Bounds.yMax, section / 3f);
                float end = Mathf.Lerp(plan.Bounds.yMin, plan.Bounds.yMax, (section + 1) / 3f);
                Assert.That(plan.Parts.Any(part => part.Position.z >= start && part.Position.z < end), Is.True,
                    "Near, middle and far thirds of the strip must all contain litter.");
            }
            Debug.Log("EAST LITTER WIDTH: small-object counts=" + string.Join(",", counts) +
                "; available-area density=" + string.Join(",", density.Select(value => value.ToString("F3"))) +
                " /m2; minimum footprint gap=" + minimumGap + " m.");
        }

        private static IEnumerator CaptureEastLitterDetails(CityGameRoot city, CityEastExitPlan exit)
        {
            CityEastLitterPlan plan = CityEastLitterPlan.Create(exit);
            Transform root = GameObject.Find(CityEastLitterWorldBuilder.RootName).transform;
            var placed = root.Cast<Transform>().ToDictionary(item => item.name, StringComparer.Ordinal);
            Camera camera = Camera.main;
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            bool oldFollow = follow != null && follow.enabled, oldInput = city.Player.Motor.InputEnabled;
            Vector3 oldPosition = camera.transform.position;
            Quaternion oldRotation = camera.transform.rotation;
            float oldFov = camera.fieldOfView;
            try
            {
                if (follow != null) follow.enabled = false;
                city.Player.Motor.SetInputEnabled(false);
                Vector3 mouthView = new Vector3(.35f, .5f, .85f);
                yield return Detail("bottle", "BottleGreen", mouthView);
                yield return Detail("can", "CanOpen", new Vector3(.35f, .9f, .85f));
                yield return Detail("bicycle-no-wheels", "BicycleNoWheels", new Vector3(-.65f, .8f, -.9f));
                float z = Mathf.Lerp(exit.CheckpointPosition.z + 20f, plan.Bounds.yMax - 15f, .5f);
                Vector3 target = new Vector3(plan.Bounds.center.x, 0f, z);
                target.y = exit.SampleGroundTop(new Vector2(target.x, target.z));
                Vector3 eye = target + new Vector3(0f, 8f, -3f);
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                camera.fieldOfView = 60f;
                for (int frame = 0; frame < 4; frame++) yield return null;
                CaptureCurrentCamera(camera, SceneIds.City, "east-litter-full-width-oblique-day");
            }
            finally
            {
                if (follow != null) follow.enabled = oldFollow;
                camera.transform.SetPositionAndRotation(oldPosition, oldRotation);
                camera.fieldOfView = oldFov;
                city.Player.Motor.SetInputEnabled(oldInput);
            }

            IEnumerator Detail(string name, string variant, Vector3 localView)
            {
                bool can = variant == "CanOpen";
                foreach (CityEastLitterPart part in plan.Parts.Where(candidate => candidate.Item.Name == variant)
                    .OrderBy(candidate => Mathf.Abs(candidate.Position.z - exit.CheckpointPosition.z - 35f)))
                {
                    // A low close-up selected only by Z can land behind a
                    // foreground leaf. The open can uses the already verified
                    // foliage-free corridor and an unobstructed raised eye.
                    if (can && (part.Position.x < plan.PedestrianCorridor.xMin + .65f ||
                        part.Position.x > plan.PedestrianCorridor.xMax - .65f)) continue;
                    Renderer[] renderers = placed[part.Id].GetComponentsInChildren<Renderer>();
                    Bounds bounds = renderers[0].bounds;
                    foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
                    float extent = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                    float distance = Mathf.Max(can ? .85f : .65f, extent * 1.9f);
                    Vector3 eye = bounds.center + part.Rotation * localView.normalized * distance;
                    if (can)
                    {
                        eye.y = Mathf.Max(eye.y, bounds.max.y + .55f);
                        if (Physics.Linecast(eye, bounds.center, ~0, QueryTriggerInteraction.Ignore)) continue;
                    }
                    camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(bounds.center - eye));
                    camera.fieldOfView = can ? 32f : 44f;
                    for (int frame = 0; frame < 4; frame++) yield return null;
                    Debug.Log("EAST LITTER DETAIL " + name + ": item=" + part.Id + "; eye=" + eye + "; target=" + bounds.center);
                    CaptureCurrentCamera(camera, SceneIds.City, "east-litter-" + name + "-day");
                    yield break;
                }
                Assert.Fail("No unobstructed real placement was available for the litter detail: " + variant);
            }
        }
    }
}

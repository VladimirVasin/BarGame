using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    internal static class CityBalconyWashWorld
    {
        internal const string Resource = "City/Laundry/BalconyWash";
        private static readonly Dictionary<string, MeshFilter> Parts = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, Mesh> Garments = new(StringComparer.Ordinal);
        private static readonly Color RopeTint = new(.40f, .36f, .29f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Parts.Clear();
            foreach (var mesh in Garments.Values) if (mesh) UnityEngine.Object.Destroy(mesh);
            Garments.Clear();
        }

        internal static void Build(Transform parent, IReadOnlyList<CityBalconyWashLine> lines, BuildingLot home = null, Predicate<Vector3> include = null)
        {
            foreach (var line in lines)
            {
                if (include != null && !include(line.Start)) continue;
                if (home != null && (!Visible(line.Start) || !Visible(line.End))) continue;
                var root = new GameObject(line.Id).transform;
                root.SetParent(parent, false);
                root.localPosition = home == null ? line.Start : PlayerHomeBalconyGeometry.ToHomeLocal(home, line.Start);
                if (home != null) root.localRotation = Quaternion.LookRotation(
                    PlayerHomeBalconyGeometry.ToHomeLocalDirection(home, Vector3.forward));
                Rope(root, line);
                Fitting(root, "RailLoop", Vector3.zero, Quaternion.FromToRotation(Vector3.up, line.StartAxis), 1f, null);
                Vector3 receiver = line.End - line.Start;
                if (line.ToPole)
                {
                    float scale = line.EndNormal.magnitude / .146f;
                    Fitting(root, "PoleLoop", receiver - line.EndNormal, Quaternion.identity, scale, null);
                }
                else if (line.Mount == WashMount.Wall)
                    Fitting(root, "WallEye", receiver, Quaternion.LookRotation(line.EndNormal), 1f, "SteelDark");
                else Fitting(root, "RailLoop", receiver, Quaternion.FromToRotation(Vector3.up, line.EndNormal), 1f, null);

                for (int i = 0; i < line.Count; i++)
                {
                    line.Pegs(i, out var a, out var b);
                    Vector3 right = (b - a).normalized;
                    Vector3 forward = Vector3.Cross(right, Vector3.up).normalized;
                    Quaternion rotation = Quaternion.LookRotation(forward, Vector3.Cross(forward, right));
                    Fitting(root, "Peg", a - line.Start, rotation, 1f, "Timber");
                    Fitting(root, "Peg", b - line.Start, rotation, 1f, "Timber");
                    Hang(root, line.Garment(i), line.Cloth(i), rotation, line);
                }
            }
            bool Visible(Vector3 point) => Vector2.Distance(new Vector2(point.x, point.z),
                new Vector2(home.DoorPosition.x, home.DoorPosition.z)) < HomeExteriorContextPlanner.ViewRadius - 1f;
        }

        private static MeshFilter Part(string name)
        {
            if (Parts.Count == 0)
            {
                var source = Resources.Load<GameObject>(Resource);
                if (source == null) throw new InvalidOperationException("Missing Blender balcony wash kit.");
                foreach (var part in source.GetComponentsInChildren<MeshFilter>(true))
                    Parts.Add(part.name, part);
            }
            return Parts[name];
        }

        // Read the imported geometry in authored metres, including the FBX root's basis and scale.
        private static Mesh MetricMesh(string name)
        {
            var source = Part(name);
            var mesh = new Mesh { name = "Balcony wash " + name };
            mesh.CombineMeshes(new[] { new CombineInstance { mesh=source.sharedMesh,
                transform=source.transform.localToWorldMatrix } });
            return mesh;
        }

        private static void Fitting(Transform parent, string name, Vector3 position, Quaternion rotation, float scale, string material)
        {
            var source = Part(name);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation * source.transform.rotation;
            go.transform.localScale = source.transform.lossyScale * scale;
            go.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
            var renderer = go.AddComponent<MeshRenderer>();
            if (material != null) renderer.sharedMaterial = CityPortAssetProvider.GetSurfaceMaterial(material);
            else
            {
                renderer.sharedMaterial = ClothPanelFactory.TwoSidedMaterial;
                CityPointOfInterestSurfaceAppearance.ApplyClothPanel(renderer, RopeTint, .16f, .028f);
            }
        }

        private static void Rope(Transform parent, CityBalconyWashLine line)
        {
            Mesh mesh = MetricMesh("Rope");
            var positions = mesh.vertices;
            var across = Vector3.Cross(line.End - line.Start, Vector3.up).normalized;
            for (int i = 0; i < positions.Length; i++)
            {
                var p = positions[i];
                positions[i] = line.Point(p.x) - line.Start + Vector3.up * p.y + across * p.z;
            }
            mesh.vertices = positions;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject("Clothesline");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = ClothPanelFactory.TwoSidedMaterial;
            CityPointOfInterestSurfaceAppearance.ApplyClothPanel(renderer, RopeTint, line.Length, .028f);
            go.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);
        }

        private static void Hang(Transform root, string name, CityWindDressingClothDescriptor clothPlan,
            Quaternion rotation, CityBalconyWashLine line)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = clothPlan.Position - line.Start;
            go.transform.localRotation = rotation;
            if (!Garments.TryGetValue(name, out Mesh mesh))
            {
                mesh = MetricMesh(name);
                var weights = new BoneWeight[mesh.vertexCount];
                for (int i = 0; i < weights.Length; i++) weights[i].weight0 = 1f;
                mesh.boneWeights = weights;
                mesh.bindposes = new[] { Matrix4x4.identity };
                Garments.Add(name, mesh);
            }
            var renderer = go.AddComponent<SkinnedMeshRenderer>();
            renderer.bones = new[] { go.transform };
            renderer.rootBone = go.transform;
            renderer.sharedMesh = mesh;
            renderer.sharedMaterial = ClothPanelFactory.TwoSidedMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
            var bounds = mesh.bounds;
            bounds.Expand(2f * (line.ClothTravel + .05f));
            renderer.localBounds = bounds;
            CityPointOfInterestSurfaceAppearance.ApplyClothPanel(renderer, clothPlan.Color, clothPlan.Width, clothPlan.Height);

            var cloth = go.AddComponent<Cloth>();
            var particles = cloth.vertices;
            var limits = new ClothSkinningCoefficient[particles.Length];
            bool towel = name == "Towel";
            float travel = towel ? line.ClothTravel * .575f : line.ClothTravel;
            int pinCount = 0;
            for (int i = 0; i < limits.Length; i++)
            {
                // The only two particles at y=0 are the sewn corners or the shirt shoulders.
                bool pinned = particles[i].y > -.003f;
                if (pinned) pinCount++;
                limits[i].maxDistance = pinned ? 0f : travel;
                limits[i].collisionSphereDistance = float.MaxValue;
            }
            if (pinCount != 2) throw new InvalidOperationException($"{name}: expected two cloth pins, found {pinCount}.");
            cloth.coefficients = limits;
            cloth.useGravity = true;
            cloth.bendingStiffness = towel ? .54f : .18f;
            cloth.stretchingStiffness = .93f;
            cloth.damping = towel ? .52f : .32f;
            cloth.worldVelocityScale = 0f;
            cloth.worldAccelerationScale = 0f;
            CityClothWindRegistry.Register(cloth);
        }
    }
}

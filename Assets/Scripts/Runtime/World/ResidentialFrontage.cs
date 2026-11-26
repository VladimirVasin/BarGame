using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    // A city lot is also the address in the clipped view from Home. Never seed
    // household furnishings from a scene, Unity instance ID or game clock.
    internal readonly struct ResidentialAddress
    {
        public readonly uint Key;
        public readonly int Family;

        public ResidentialAddress(BuildingLot lot, int seed)
        {
            Key = CityExteriorAppearance.StableHash(seed, lot.Cell.x, lot.Cell.y,
                Mathf.RoundToInt(lot.Center.x * 100f), Mathf.RoundToInt(lot.Center.z * 100f), lot.BuildingVariant);
            // Different neighbouring street numbers get different construction
            // families. A periodic extra step breaks up the diagonal stripes.
            int x = lot.Cell.x, z = lot.Cell.y;
            int street = x + Mathf.FloorToInt((x + (seed & 7)) / 4f);
            int block = z + Mathf.FloorToInt((z + ((seed >> 3) & 7)) / 5f);
            Family = ((street + block * 2 + (seed & 15)) % 3 + 3) % 3;
        }

        public static bool Applies(BuildingLot lot) => lot.IsOrdinaryBuilding && lot.District == CityDistrictKind.Residential;

        public uint Household(CityBuildingWindowSlot slot)
        {
            int apartment = slot.Side == "Front" && slot.Floor > 0 ? slot.Bay / 2 : slot.Bay;
            int side = slot.Side == "Front" ? 0 : slot.Side == "Rear" ? 1 : slot.Side == "Left" ? 2 : 3;
            return Household(side, slot.Floor, apartment);
        }

        public uint Household(int side, int floor, int apartment) =>
            CityExteriorAppearance.Mix(Key, (uint)(side * 4096 + floor * 128 + apartment));

        public static uint VillageKey(string id)
        {
            uint value = 2166136261u;
            foreach (char c in id) value = unchecked((value ^ c) * 16777619u);
            return CityExteriorAppearance.Mix(value, 37);
        }

        public Color Tint(Color facade)
        {
            Color finish = Family == 0 ? new Color(1.12f, 1.04f, .91f) :
                Family == 1 ? new Color(.94f, 1.07f, 1.08f) : new Color(1.12f, .98f, .94f);
            return facade * finish;
        }
    }

    // Authored in metres around a sill, deck or shutter hinge. Bake only once
    // per address and once again if the terrain survey closes an opening.
    internal sealed class ResidentialFrontage
    {
        private static Dictionary<string, MeshFilter> sources;
        private static Material finish, glazing;
        private readonly List<CombineInstance> opaque = new List<CombineInstance>();
        private readonly List<CombineInstance> glass = new List<CombineInstance>();
        private readonly Transform parent;
        private readonly bool clip;
        private readonly Matrix4x4 toHome;
        internal const string RootName = "Address Frontage";

        internal ResidentialFrontage(Transform parent, bool clip = false)
        {
            this.parent = parent;
            this.clip = clip;
            toHome = Matrix4x4.TRS(parent.localPosition, parent.localRotation, parent.localScale);
        }

        internal void Add(string module, Vector3 point, Vector3 outward, Vector3 scale)
        {
            if (sources == null)
            {
                var model = Resources.Load<GameObject>("Housing/FrontageModules");
                var catalog = new Dictionary<string, MeshFilter>(StringComparer.Ordinal);
                foreach (var part in model.GetComponentsInChildren<MeshFilter>(true))
                    catalog.Add(part.name, part);
                finish = Resources.Load<Material>("Housing/Frontage");
                glazing = Resources.Load<Material>("Housing/FrontageGlass");
                if (!finish || !glazing) throw new InvalidOperationException("Missing housing finishes.");
                sources = catalog;
            }
            var source = sources[module];
            var pose = Matrix4x4.TRS(point, Quaternion.LookRotation(outward), scale) * source.transform.localToWorldMatrix;
            if (clip)
            {
                Matrix4x4 local = toHome * pose;
                Bounds b = source.sharedMesh.bounds;
                float extent = Mathf.Abs(local.m00) * b.extents.x + Mathf.Abs(local.m01) * b.extents.y +
                    Mathf.Abs(local.m02) * b.extents.z;
                if (local.MultiplyPoint3x4(b.center).x - extent < HomeExteriorViewBuilder.ContextMinimumX) return;
            }
            (module == "BalconyGlass" || module == "WindowGlass" ? glass : opaque).Add(new CombineInstance { mesh = source.sharedMesh, transform = pose });
        }

        internal void Finish()
        {
            if (opaque.Count == 0 && glass.Count == 0) return;
            var root = new GameObject(RootName).transform;
            root.SetParent(parent, false);
            Bake(root, opaque, finish, "Joinery and belongings");
            Bake(root, glass, glazing, "Frontage glazing");
        }

        private static void Bake(Transform root, List<CombineInstance> parts, Material material, string name)
        {
            if (parts.Count == 0) return;
            var child = new GameObject(name);
            child.transform.SetParent(root, false);
            var mesh = new Mesh { name = name };
            int vertices = 0;
            foreach (var part in parts) vertices += part.mesh.vertexCount;
            if (vertices > ushort.MaxValue) mesh.indexFormat = IndexFormat.UInt32;
            mesh.CombineMeshes(parts.ToArray(), true, true, false);
            mesh.UploadMeshData(true);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            child.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);
            var renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            if (material == glazing) renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
    }
}

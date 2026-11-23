using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Authored, UV-mapped assets placed on the coastal plan's original anchors.</summary>
    internal sealed class CityBoatRentalArt
    {
        private readonly Dictionary<string, MeshFilter> meshes = new(StringComparer.Ordinal);
        private readonly Material finish;
        private readonly Quaternion inverseBasis;
        private readonly float unit;
        private readonly List<RuntimeMeshPlacement> pier = new();
        private readonly List<RuntimeMeshPlacement> slip = new();
        private readonly Transform parent;

        internal CityBoatRentalArt(Transform parent)
        {
            this.parent = parent;
            var kit = Resources.Load<GameObject>("City/BoatRental");
            finish = Resources.Load<Material>("City/Surfaces/BoatRental");
            if (!kit || !finish) throw new InvalidOperationException("Missing boat rental station assets.");
            foreach (MeshFilter part in kit.GetComponentsInChildren<MeshFilter>(true))
                meshes.Add(part.name, part);
            Transform owner = meshes["Hut"].transform.parent;
            Vector3 forward = owner.Find("AxisForward").position - owner.position;
            Vector3 up = owner.Find("AxisUp").position - owner.position;
            inverseBasis = Quaternion.Inverse(Quaternion.LookRotation(forward, up));
            unit = up.magnitude;
        }

        private RuntimeMeshPlacement Place(string name, Vector3 at, Quaternion rotation, Vector3 size)
        {
            MeshFilter part = meshes[name];
            Quaternion axes = inverseBasis * part.transform.rotation;
            Vector3 local = Quaternion.Inverse(axes) * size;
            Vector3 scale = Vector3.Scale(part.transform.lossyScale / unit,
                new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z)));
            return new RuntimeMeshPlacement(part.sharedMesh, at, rotation * axes, scale);
        }

        private void Draw(string name, List<RuntimeMeshPlacement> placements, CityWetSurfaceKind response)
        {
            if (placements.Count == 0) return;
            GameObject host = RuntimePrimitiveFactory.CreateCombinedMeshes("Boat Rental " + name, parent, placements, Color.white);
            Renderer surface = host.GetComponent<Renderer>();
            surface.sharedMaterial = finish;
            CityWetSurfaceRegistry.Register(surface, response, Color.white);
        }

        // Whole imported objects can share their original GPU mesh, including repeated hull variants.
        private GameObject Single(string name, RuntimeMeshPlacement part, CityWetSurfaceKind response)
        {
            Renderer surface = Instance("Boat Rental " + name, parent, part, finish);
            CityWetSurfaceRegistry.Register(surface, response, Color.white);
            return surface.gameObject;
        }

        private static Renderer Instance(string name, Transform parent, RuntimeMeshPlacement part, Material material)
        {
            var host = new GameObject(name);
            host.transform.SetParent(parent, false);
            host.transform.SetLocalPositionAndRotation(part.Center, part.Rotation);
            host.transform.localScale = part.Scale;
            host.AddComponent<MeshFilter>().sharedMesh = part.Mesh;
            var surface = host.AddComponent<MeshRenderer>();
            surface.sharedMaterial = material;
            return surface;
        }

        internal void Hut(Vector3 ground) => Single("Hut", Place("Hut", ground, Quaternion.identity, Vector3.one), CityWetSurfaceKind.WeatheredTimber);

        internal void Hull(int ordinal, CitySeacoastBoatVariant variant, Vector3 ground, Quaternion facing)
        {
            var hull = Place("Hull" + (int)variant, ground, facing, Vector3.one);
            var host = Single("Hull " + ordinal, hull, CityWetSurfaceKind.WeatheredTimber);
            host.AddComponent<MeshCollider>().sharedMesh = hull.Mesh;
            FootstepGround.Stamp(host.transform, FootstepGroundKind.Wood);
        }

        internal void Oar(Vector3 ground, Quaternion facing) =>
            Single("Oar", Place("Oar", ground, facing, Vector3.one), CityWetSurfaceKind.WeatheredTimber);

        internal Renderer Bulb(Transform assembly, Vector3 at)
        {
            var bulb = Place("Bulb", at, Quaternion.identity, Vector3.one);
            return Instance("Hut Lamp Bulb", assembly, bulb, CityNightResources.EmissiveMaterial);
        }

        internal bool Append(CitySeacoastPartDescriptor part, float sea)
        {
            Vector3 at = part.Center;
            Vector3 size = part.Size;
            switch (part.Kind)
            {
                case CitySeacoastPartKind.HutSign:
                    Single("Printed Sign", Place("Sign", at, part.Rotation, size), CityWetSurfaceKind.PaintedSteel);
                    return true;
                case CitySeacoastPartKind.PierDeck:
                    pier.Add(Place("Deck", at, part.Rotation, new Vector3(size.x/2.2f, size.y/.12f, size.z)));
                    return true;
                case CitySeacoastPartKind.PierPile:
                    float bottom = at.y - size.y*.5f, top = at.y + size.y*.5f;
                    float tide = Mathf.Clamp(sea + .10f, bottom, top);
                    Pile("WetPile", bottom, tide);
                    Pile("Pile", tide, top);
                    return true;
                case CitySeacoastPartKind.PierRail when part.StableId == "seacoast-pier-rail":
                    int bays = Mathf.Max(1, Mathf.FloorToInt((size.z + .7f)/2.4f));
                    float pitch = size.z / bays;
                    for (int bay = 0; bay < bays; bay++)
                    {
                        Vector3 midpoint = at + part.Rotation * Vector3.forward * ((bay+.5f)*pitch-size.z*.5f);
                        pier.Add(Place("Rail", midpoint, part.Rotation, new Vector3(size.x/.08f, size.y/.10f, pitch-.006f)));
                    }
                    return true;
                case CitySeacoastPartKind.Slipway:
                    slip.Add(Place("Slipway", at, part.Rotation, new Vector3(size.x/4f, size.y/.18f, size.z/1.70f)));
                    return true;
                default:
                    return false;
            }

            void Pile(string name, float low, float high)
            {
                if (high <= low) return;
                var centre = new Vector3(at.x,(low+high)*.5f,at.z);
                pier.Add(Place(name, centre, part.Rotation, new Vector3(size.x/.26f, high-low, size.z/.26f)));
            }
        }

        internal void FinishPier()
        {
            Draw("Pier", pier, CityWetSurfaceKind.WeatheredTimber);
            Draw("Slipway", slip, CityWetSurfaceKind.Sidewalk);
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    internal sealed class CitySouthTunnelArt
    {
        private readonly CityImportedMeshKit kit = new("City/SouthTunnel", "Portal");
        private readonly Material finish = Resources.Load<Material>("City/Surfaces/SouthTunnel");

        internal void BuildPortal(Transform parent, CityMountainTunnelDescriptor tunnel)
        {
            Quaternion facing = Quaternion.LookRotation(tunnel.Axis, Vector3.up);
            var mouth = Instance("Mountain Tunnel Portal", "Portal", tunnel.PortalGroundCenter,
                new Vector3(tunnel.OpeningWidth / 8f, tunnel.OpeningHeight / 5.5f, 1f), true);
            CityWetSurfaceRegistry.Register(mouth, CityWetSurfaceKind.DressedGranite, Color.white);

            const float springBack = .64f;
            float end = Mathf.Min(tunnel.VisualDepth, CityMountainBoundaryDefinition.TunnelStraightDepth);
            int count = Mathf.CeilToInt((end - springBack) / 3f);
            float pitch = (end - springBack) / count;
            for (int i = 0; i < count; i++)
            {
                Vector3 at = tunnel.PortalGroundCenter + tunnel.Axis * (springBack + (i + .5f) * pitch);
                var scale = new Vector3(tunnel.OpeningWidth / 8f, tunnel.OpeningHeight / 5.5f, pitch / 3f);
                Instance("Tunnel Vault Course", "Barrel", at, scale, false);
                Instance("Tunnel Vault Bed", "Bedding", at, scale, true);
            }

            for (float distance = CityMountainBoundaryDefinition.TunnelSegmentLength; distance <= end; distance += CityMountainBoundaryDefinition.TunnelSegmentLength)
                Instance("Tunnel Rib", "Rib", tunnel.SamplePath(distance).Position,
                    new Vector3(tunnel.OpeningWidth / 8f, tunnel.OpeningHeight / 5.5f, 1f), true);

            Renderer Instance(string name, string mesh, Vector3 origin, Vector3 scale, bool physical)
            {
                var part = kit.Place(mesh, origin, facing, scale);
                var host = new GameObject(name);
                host.transform.SetParent(parent, false);
                host.transform.SetLocalPositionAndRotation(part.Center, part.Rotation);
                host.transform.localScale = part.Scale;
                host.AddComponent<MeshFilter>().sharedMesh = part.Mesh;
                var surface = host.AddComponent<MeshRenderer>();
                surface.sharedMaterial = finish;
                if (physical) host.AddComponent<MeshCollider>().sharedMesh = part.Mesh;
                return surface;
            }
        }

        // Rut pigment occupies the right half of the last atlas row (1.2% gutter).
        // The outside fifth meets the uncut verge, including the irregular shoulder.
        private static float WheelDepth(Vector2 uv)
        {
            float lateral = Mathf.Abs((uv.x - .75f) / .244f);
            return .024f * (1f - Mathf.SmoothStep(0f, 1f, lateral / .80f));
        }

        internal void BuildForecourt(Transform parent, CityFringeYardDescriptor yard, ISet<string> replaced)
        {
            var concrete = new List<RuntimeMeshPlacement>();
            var iron = new List<RuntimeMeshPlacement>();
            var grates = new List<RuntimeMeshPlacement>();
            var tracks = new List<RuntimeMeshPlacement>();
            Vector3? head = null, lamp = null, braceFoot = null;
            foreach (var part in yard.Parts)
            {
                Vector3 size = part.Size;
                switch (part.Kind)
                {
                    case CityFringeYardPartKind.TunnelCheek:
                        concrete.Add(kit.Place("Cheek", part.Center, part.Rotation, size));
                        break;
                    case CityFringeYardPartKind.RepairFrame when part.Style == CityFringeYardStyle.Iron:
                        bool post = part.StableId.Contains("landmark-return-service-post");
                        iron.Add(kit.Place(post ? "Post" : "Beam", part.Center, part.Rotation, size));
                        if (post)
                        {
                            Vector3 top = part.Center + Vector3.up * (size.y * .5f);
                            if (part.StableId.EndsWith("-01", StringComparison.Ordinal)) head = top;
                            else braceFoot = top;
                        }
                        break;
                    case CityFringeYardPartKind.PracticalHousing:
                        iron.Add(kit.Place("Housing", part.Center, part.Rotation, size));
                        lamp = part.Center + part.Rotation * Vector3.back * (size.z * .46f);
                        break;
                    case CityFringeYardPartKind.DrainCover:
                        grates.Add(kit.Place("Grate", part.Center, part.Rotation, Vector3.one));
                        break;
                    case CityFringeYardPartKind.WheelRut:
                        tracks.Add(kit.Place("Rut", part.Center + part.Rotation * Vector3.up * (size.y * .5f),
                            part.Rotation, new Vector3(size.x, 1f, size.z)));
                        break;
                    default:
                        continue;
                }
                replaced.Add(part.StableId);
            }

            if (head.HasValue && lamp.HasValue)
            {
                Beam(head.Value, lamp.Value, .13f);
                if (braceFoot.HasValue) Beam(braceFoot.Value, Vector3.Lerp(head.Value, lamp.Value, .55f), .09f);
            }
            Draw("Tunnel Formwork Wings", concrete, CityWetSurfaceKind.Sidewalk);
            Draw("Tunnel Service Iron", iron, CityWetSurfaceKind.PaintedSteel);
            Draw("Tunnel Cross Drain", grates, CityWetSurfaceKind.PaintedSteel);
            if (tracks.Count > 0)
            {
                var host = Draw("Tunnel Wheel Impressions", tracks, CityWetSurfaceKind.Ground, true);
                host.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                FootstepGround.Stamp(host, FootstepGroundKind.Soil);
                CityGroundSurfaceSystem.RegisterPaintRelief(host.GetComponent<Renderer>(), 41, WheelDepth);
            }

            void Beam(Vector3 start, Vector3 end, float gauge)
            {
                Vector3 span = end - start;
                iron.Add(kit.Place("Beam", (start + end) * .5f, Quaternion.LookRotation(span, Vector3.up),
                    new Vector3(gauge, gauge, span.magnitude)));
            }

            GameObject Draw(string name, List<RuntimeMeshPlacement> parts, CityWetSurfaceKind response, bool readable = false)
            {
                if (parts.Count == 0) return null;
                var host = RuntimePrimitiveFactory.CreateCombinedMeshes(name, parent, parts, Color.white, keepReadable: readable);
                Renderer surface = host.GetComponent<Renderer>();
                surface.sharedMaterial = finish;
                CityWetSurfaceRegistry.Register(surface, response, Color.white);
                return host;
            }
        }
    }
}

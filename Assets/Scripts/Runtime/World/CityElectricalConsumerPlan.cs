using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    // Position is the centre of the insulator on the imported FacadeInlet.
    // Its backing plate is 12 cm deep and centred 28 cm below this point.
    public readonly struct CityElectricalAttachment
    {
        public readonly Vector3 Position;
        public readonly Vector3 Outward;

        internal CityElectricalAttachment(Vector3 position, Vector3 outward)
        {
            Position = position;
            Outward = outward;
        }
    }

    public sealed class CityElectricalConsumer
    {
        public string StableId { get; }
        public IReadOnlyList<Vector2> Footprint { get; }
        public Rect Bounds { get; }
        public float BaseY { get; }
        public float Height { get; }
        public IReadOnlyList<CityElectricalAttachment> Attachments { get; }
        public BuildingLot Lot { get; }
        // A building may already be wired from a neighbouring building. Its existing
        // cable is drawn by its site owner and still counts towards source connectivity.
        public string SuppliedById { get; }
        public Vector3 ExistingSource { get; }
        public Vector3 ExistingEnd { get; }

        internal CityElectricalConsumer(string id, IReadOnlyList<Vector2> footprint, float baseY, float height,
            CityElectricalAttachment[] attachments, BuildingLot lot = null, string suppliedById = null,
            Vector3 existingSource = default, Vector3 existingEnd = default)
        {
            StableId = id;
            Footprint = footprint;
            Bounds = CityRoadPolygon.Bounds(footprint);
            BaseY = baseY;
            Height = height;
            Attachments = Array.AsReadOnly(attachments);
            Lot = lot;
            SuppliedById = suppliedById;
            ExistingSource = existingSource;
            ExistingEnd = existingEnd;
        }
    }

    /// <summary>Facade contacts on the placed upper-city buildings, in the same metres as City and Home.</summary>
    public sealed class CityElectricalConsumerPlan
    {
        private static readonly ConditionalWeakTable<CityLayout, CityElectricalConsumerPlan> Cache = new();
        public IReadOnlyList<CityElectricalConsumer> Consumers { get; }

        public static CityElectricalConsumerPlan ForLayout(CityLayout layout) =>
            Cache.GetValue(layout, value => new CityElectricalConsumerPlan(value));

        public static string HouseId(CityHillsideBuilding house)
        {
            string bank = house.Bank == CityTerraceTransportBank.West ? "west" : "east";
            return $"ridge-{bank}-house:{house.Lot.Cell.x}:{house.Lot.Cell.y}";
        }

        private CityElectricalConsumerPlan(CityLayout layout)
        {
            var housing = CityHillsideNeighborhoodPlan.ForLayout(layout).AllBuildings;
            var result = new List<CityElectricalConsumer>(housing.Count + 6);
            Consumers = result.AsReadOnly();
            if (housing.Count == 0) return;
            foreach (var house in housing) result.Add(Residence(house));

            // Use the upper kiosk's upstream survey. The aggregate CityKioskSites already
            // reserves the lower network and must never be used while planning that network.
            if (RidgeKioskSitePlan.TryCreate(layout, out var kiosk))
                result.Add(Pavilion(kiosk.StableId, kiosk.Origin, kiosk.Rotation, kiosk.Footprint, 2.74f,
                    CityKioskSitePlan.WallHalfWidth, CityKioskSitePlan.WallHalfDepth, 2.30f, -.85f));

            if (CityPawnshopPlan.TryCreate(layout, out var pawn))
                result.Add(Pavilion("pawnshop", pawn.Origin, pawn.Rotation, pawn.Footprint, CityPawnshopPlan.Height,
                    CityPawnshopPlan.Width * .5f, CityPawnshopPlan.Depth * .5f, 3.42f, 1.5f));

            var glass = CityGlassReturnSitePlan.ForLayout(layout);
            if (glass.IsEnabled)
                result.Add(Pavilion(CityGlassReturnSitePlan.StableId, glass.Origin, glass.Rotation,
                    glass.Footprint, 3.01f, 2.24f, 1.58f, 2.60f, -.95f));

            var loft = CityPigeonLoftPlan.ForLayout(layout);
            if (loft.IsEnabled)
            {
                // The loft's surveyed bounds include open stairs and aviary. Terminate
                // on the timber cabin or an outside upright of the covered aviary.
                var frame = new FacadeFrame(loft.Origin, loft.Rotation);
                result.Add(new CityElectricalConsumer("ridge-pigeon-loft",
                    frame.Rectangle(-1.5f, -1.20f, 2f, 1.20f), loft.Origin.y, 4.50f, new[] {
                        frame.Pin(-.92f, 3.05f, 1.20f, Vector3.forward),
                        frame.Pin(-.92f, 3.05f, -1.20f, Vector3.back),
                        frame.Pin(-1.50f, 3.10f, .73f, Vector3.left),
                        frame.Pin(-.92f, 4.06f, 1.20f, Vector3.forward), frame.Pin(.92f, 4.06f, 1.20f, Vector3.forward),
                        frame.Pin(-.92f, 4.06f, -1.20f, Vector3.back), frame.Pin(.92f, 4.06f, -1.20f, Vector3.back),
                        frame.Pin(-1.50f, 3.94f, .73f, Vector3.left) }));
            }

            var cobbler = CityCobblerSitePlan.ForLayout(layout);
            if (cobbler.IsEnabled)
                result.Add(new CityElectricalConsumer(CityCobblerSitePlan.StableId, cobbler.Footprint,
                    cobbler.Origin.y, 3.10f, Array.Empty<CityElectricalAttachment>(),
                    suppliedById: CityCobblerWorkerPlan.ResidenceId,
                    existingSource: cobbler.Yard.CableStart, existingEnd: cobbler.Yard.CableEnd));

            foreach (var site in CityHillsideSiteDetails.ForLayout(layout))
            {
                if (site.Part != CityHillsideSiteDetails.Kind.YardShelter) continue;
                var frame = new FacadeFrame(site.Origin, site.Rotation);
                var size = CityResidentialCourtyardAssetProvider.GetSize(CityResidentialCourtyardPropKind.UtilityShelter);
                // Only the uprights and top rails are solid at this height.
                result.Add(new CityElectricalConsumer("ridge-court-yard-shelter", frame.Rectangle(
                    -size.x * .5f, -size.z * .5f, size.x * .5f, size.z * .5f), site.Origin.y, size.y,
                    new[] { frame.Pin(-1.1525f, 1.75f, -.49f, Vector3.left),
                            frame.Pin(1.1525f, 1.75f, -.49f, Vector3.right),
                            frame.Pin(-1.12f, 1.75f, -.5225f, Vector3.back),
                            frame.Pin(1.12f, 1.75f, .5225f, Vector3.forward) }));
            }
        }

        private static CityElectricalConsumer Residence(CityHillsideBuilding house)
        {
            var lot = house.Lot;
            float y = Mathf.Min(lot.Height - 1f, 8.6f);
            if (lot.IsPlayerHome)
            {
                var home = new FacadeFrame(lot.DoorPosition - lot.FacadeForward * 6f, lot.FacadeRotation);
                // Side gables rise to 8.54 m at their centre. The front and rear walls end
                // at the 6.82 m eaves; the balcony occupies only the middle of the frontage.
                return new CityElectricalConsumer(HouseId(house), home.Rectangle(-6.5f, -6f, 6.5f, 8.30f),
                    lot.DoorPosition.y, lot.Height, new[] {
                        home.Pin(-6.39f, y, -.75f, Vector3.left), home.Pin(6.39f, y, -.75f, Vector3.right),
                        home.Pin(5.6f, 6.55f, 6f, Vector3.forward), home.Pin(-5.6f, 6.55f, -5.86f, Vector3.back) }, lot);
            }

            var pose = CityBuildingPrototypePlacement.ResolveExpectedCityPose(lot);
            var frame = new FacadeFrame(pose.Position, pose.Rotation);
            var size = CityBuildingAssetProvider.GetExpectedEnvelope(lot.District, lot.BuildingVariant);
            float x = size.x * .5f, back = -size.z * .5f, front = size.z * .5f - 1.2f;
            // The ordinary frontage is recessed 1.2 m behind the nominal envelope.
            // Variant 2 has a five-metre rear wing on its left; its other rear corner is air.
            bool hasWing = lot.BuildingVariant == 2;
            var footprint = hasWing
                ? frame.Polygon(new(-x, back), new(-x + 5f, back), new(-x + 5f, 0f),
                    new(x, 0f), new(x, front), new(-x, front))
                : frame.Rectangle(-x, back, x, front);
            const float corner = .53f;
            return new CityElectricalConsumer(HouseId(house), footprint, pose.Position.y, lot.Height, new[] {
                frame.Pin(-x + corner, y, front, Vector3.forward), frame.Pin(x - corner, y, front, Vector3.forward),
                frame.Pin(x, y, front - corner, Vector3.right), frame.Pin(x, y, (hasWing ? 0f : back) + corner, Vector3.right),
                frame.Pin(-x + corner, y, back, Vector3.back), frame.Pin((hasWing ? -x + 5f : x) - corner, y, back, Vector3.back),
                frame.Pin(-x, y, back + corner, Vector3.left), frame.Pin(-x, y, front - corner, Vector3.left) }, lot);
        }

        // Front and back are kept free of trading windows, doors, signs and awnings.
        // The closed side walls of all three pavilions provide two service approaches each.
        private static CityElectricalConsumer Pavilion(string id, Vector3 origin, Quaternion rotation,
            Vector2[] footprint, float height, float halfWidth, float halfDepth, float y, float rearSide)
        {
            var frame = new FacadeFrame(origin, rotation);
            float back = Mathf.Sign(rearSide);
            return new CityElectricalConsumer(id, footprint, origin.y, height, new[] {
                frame.Pin(-halfWidth, y, rearSide, Vector3.left), frame.Pin(halfWidth, y, rearSide, Vector3.right),
                frame.Pin(-halfWidth, y, -rearSide, Vector3.left), frame.Pin(halfWidth, y, -rearSide, Vector3.right),
                frame.Pin(halfWidth * .76f, y, back * halfDepth, Vector3.forward * back),
                frame.Pin(-halfWidth * .76f, y, back * halfDepth, Vector3.forward * back) });
        }

        private readonly struct FacadeFrame
        {
            private readonly Vector3 origin;
            private readonly Quaternion rotation;
            internal FacadeFrame(Vector3 origin, Quaternion rotation) { this.origin = origin; this.rotation = rotation; }

            internal CityElectricalAttachment Pin(float x, float y, float z, Vector3 normal)
            {
                Vector3 outward = rotation * normal;
                return new CityElectricalAttachment(origin + rotation * new Vector3(x, y, z) + outward * .055f, outward);
            }

            internal Vector2[] Rectangle(float left, float back, float right, float front) =>
                Polygon(new(left, back), new(right, back), new(right, front), new(left, front));

            internal Vector2[] Polygon(params Vector2[] local)
            {
                for (int i = 0; i < local.Length; i++)
                {
                    Vector3 world = origin + rotation * new Vector3(local[i].x, 0f, local[i].y);
                    local[i] = new Vector2(world.x, world.z);
                }
                return CityRoadPolygon.CounterClockwise(local);
            }
        }
    }
}

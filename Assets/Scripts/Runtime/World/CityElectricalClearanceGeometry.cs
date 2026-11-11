using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    // Build once in City space. The imported meshes stay GPU-only; authored solid boxes,
    // balcony decks and renderer-local bounds retain their own frame, scale and height.
    internal sealed class CityElectricalClearanceGeometry
    {
        private const float BucketWidth = 20f;
        private const float CableRadius = .025f;
        private const int Segments = 16; // seventeen rings in the imported CableSpan

        internal readonly struct Part
        {
            internal readonly string Name;
            internal readonly Bounds WorldBounds;
            private readonly Bounds local;
            private readonly Matrix4x4 inverse;
            private readonly Vector3 padding;
            private readonly Vector3 horizontal, vertical;

            internal Part(string name, Bounds bounds, Matrix4x4 frame)
            {
                Name = name;
                local = bounds;
                inverse = frame.inverse;
                var x = frame.MultiplyVector(Vector3.right * bounds.extents.x);
                var y = frame.MultiplyVector(Vector3.up * bounds.extents.y);
                var z = frame.MultiplyVector(Vector3.forward * bounds.extents.z);
                WorldBounds = new Bounds(frame.MultiplyPoint3x4(bounds.center), 2f * (Abs(x) + Abs(y) + Abs(z)));
                // Norms of the inverse rows convert world-space radii into each local axis.
                x = inverse.MultiplyVector(Vector3.right); y = inverse.MultiplyVector(Vector3.up);
                z = inverse.MultiplyVector(Vector3.forward);
                horizontal = new Vector3(Length(x.x,z.x), Length(x.y,z.y), Length(x.z,z.z));
                vertical = Abs(y);
                padding = CableRadius * new Vector3(Length(horizontal.x,y.x), Length(horizontal.y,y.y), Length(horizontal.z,y.z));
            }

            internal bool Cable(CityCoastalCableSpan span, float swing)
            {
                Vector3 a = inverse.MultiplyPoint3x4(span.Start);
                Vector3 delta = inverse.MultiplyVector(span.End - span.Start);
                Vector3 sag = inverse.MultiplyVector(Vector3.up * (4f * span.Sag));
                Vector3 lateral = swing > 0f ? Abs(inverse.MultiplyVector(span.Lateral * swing)) : Vector3.zero;
                Vector3 from = a, low = local.min - padding, high = local.max + padding;
                for (int i = 1; i <= Segments; i++)
                {
                    float t = i / (float)Segments;
                    Vector3 to = a + delta * t - sag * (t * (1f - t));
                    Vector3 sweep = swing > 0f ? lateral * CityCoastalCableSpan.WindEnvelope(
                        Mathf.Clamp(.5f, (i - 1) / (float)Segments, t)) : Vector3.zero;
                    if (Intersects(from, to, low - sweep, high + sweep)) return true;
                    from = to;
                }
                return false;
            }

            internal bool Pole(Vector3 ground, float clearance)
            {
                Vector3 a = inverse.MultiplyPoint3x4(ground);
                Vector3 up = inverse.MultiplyVector(Vector3.up);
                Vector3 pad = horizontal * Mathf.Max(.23f,clearance);
                if (Intersects(a, a + up * 8.3f, local.min - pad, local.max + pad)) return true;
                // The arms can rotate when a branch gains an extra neighbour.
                // Their largest horizontal reach is .95 m in any orientation.
                pad = horizontal * Mathf.Max(.95f,clearance) + vertical * .20f;
                Vector3 arm = a + up * 7.85f;
                if (Contains(arm, local.min - pad, local.max + pad)) return true;
                // The outer ceramic heads sit 65 cm off the shaft and rise above the arm.
                pad = horizontal * .75f + vertical * .11f;
                return Contains(a + up * 8.03f, local.min - pad, local.max + pad);
            }

            private static bool Contains(Vector3 p, Vector3 lo, Vector3 hi) =>
                p.x >= lo.x && p.x <= hi.x && p.y >= lo.y && p.y <= hi.y && p.z >= lo.z && p.z <= hi.z;

            private static bool Intersects(Vector3 a, Vector3 b, Vector3 lo, Vector3 hi)
            {
                float enter = 0f, exit = 1f;
                return Slab(a.x, b.x - a.x, lo.x, hi.x, ref enter, ref exit)
                    && Slab(a.y, b.y - a.y, lo.y, hi.y, ref enter, ref exit)
                    && Slab(a.z, b.z - a.z, lo.z, hi.z, ref enter, ref exit);
            }

            private static bool Slab(float origin, float direction, float low, float high, ref float enter, ref float exit)
            {
                if (Mathf.Abs(direction) < 1e-8f) return origin >= low && origin <= high;
                float first = (low - origin) / direction, last = (high - origin) / direction;
                enter = Mathf.Max(enter, Mathf.Min(first, last));
                exit = Mathf.Min(exit, Mathf.Max(first, last));
                return enter <= exit;
            }

            private static float Length(float x, float y) => Mathf.Sqrt(x*x + y*y);
            private static Vector3 Abs(Vector3 v) => new(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        }

        private readonly List<Part> parts = new();
        private readonly Dictionary<Vector2Int, List<int>> buckets = new();
        private readonly Dictionary<string, MeshRenderer[]> templates = new();
        private readonly Dictionary<(CityDistrictKind,int), CityBuildingAssetRegistry> catalog = new();
        private readonly CityBuildingAssetProvider provider;
        private readonly int[] seen;
        private int stamp;
        internal IReadOnlyList<Part> Parts => parts;

        internal CityElectricalClearanceGeometry(CityLayout layout)
        {
            provider = CityBuildingAssetProvider.LoadOrThrow();
            foreach (var lot in layout.BuildingMasses)
            {
                if (!lot.HasBuilding) continue;
                if (lot.IsOrdinaryBuilding) Ordinary(lot, null);
                else if (lot.IsPlayerHome) Home(lot);
                else SpecialLot(lot);
            }
            var church = CityChurchPlanner.Create(layout);
            if (church != null)
                Add("church", new Bounds(Vector3.up * CityChurchPlanner.ModelHeight * .5f, CityChurchPlanner.ModelLocalSize),
                    Frame(church.ModelRootPosition, church.ModelRotation));
            foreach (var house in CityHillsideNeighborhoodPlan.ForLayout(layout).AllBuildings)
            {
                if (house.Lot.IsPlayerHome) Home(house.Lot);
                else Ordinary(house.Lot, house);
            }
            Pavilions(layout);
            seen = new int[parts.Count];
        }

        internal bool Clear(CityCoastalCableSpan span, float swing = 0f) => !TryFindBlockingPart(span, out _, swing);

        internal bool TryFindBlockingPart(CityCoastalCableSpan span, out string name, float swing = 0f)
        {
            Vector3 axis = swing > 0f ? span.Lateral * swing : Vector3.zero;
            Vector3 padding = Vector3.one * CableRadius + new Vector3(Mathf.Abs(axis.x), 0f, Mathf.Abs(axis.z));
            Vector3 lo = Vector3.Min(span.Start, span.End) - Vector3.up * span.Sag - padding;
            Vector3 hi = Vector3.Max(span.Start, span.End) + padding;
            return Find(new Bounds((lo + hi) * .5f, hi - lo), span, default, false, swing, out name);
        }

        internal bool PoleClear(Vector3 ground, float clearance = .23f)
        {
            float top = 8.3f;
            var bounds = new Bounds(ground + Vector3.up * (top * .5f), new Vector3(Mathf.Max(1.90f,clearance*2f), top, Mathf.Max(1.90f,clearance*2f)));
            return !Find(bounds, default, ground, true, clearance, out _);
        }

        private bool Find(Bounds query, CityCoastalCableSpan span, Vector3 pole, bool isPole, float clearance, out string name)
        {
            // Construction queries execute synchronously on the layout's planning thread.
            if (stamp == int.MaxValue) { Array.Clear(seen, 0, seen.Length); stamp = 0; }
            ++stamp;
            Vector2Int lo = Bucket(query.min), hi = Bucket(query.max);
            for (int x = lo.x; x <= hi.x; x++)
            for (int z = lo.y; z <= hi.y; z++)
            {
                if (!buckets.TryGetValue(new Vector2Int(x,z), out var entries)) continue;
                foreach (int index in entries)
                {
                    if (seen[index] == stamp) continue;
                    seen[index] = stamp;
                    Part part = parts[index];
                    if (!query.Intersects(part.WorldBounds)) continue;
                    if (!(isPole ? part.Pole(pole,clearance) : part.Cable(span,clearance))) continue;
                    name = part.Name;
                    return true;
                }
            }
            name = null;
            return false;
        }

        private void Add(string name, Bounds bounds, Matrix4x4 frame)
        {
            var part = new Part(name, bounds, frame);
            int index = parts.Count;
            parts.Add(part);
            Vector2Int lo = Bucket(part.WorldBounds.min), hi = Bucket(part.WorldBounds.max);
            for (int x = lo.x; x <= hi.x; x++)
            for (int z = lo.y; z <= hi.y; z++)
            {
                var key = new Vector2Int(x,z);
                if (!buckets.TryGetValue(key, out var entries)) buckets.Add(key, entries = new List<int>());
                entries.Add(index);
            }
        }

        private static Vector2Int Bucket(Vector3 point) =>
            new(Mathf.FloorToInt(point.x / BucketWidth), Mathf.FloorToInt(point.z / BucketWidth));

        private static Matrix4x4 Frame(Vector3 origin, Quaternion rotation) => Matrix4x4.TRS(origin, rotation, Vector3.one);

        private void Ordinary(BuildingLot lot, CityHillsideBuilding house)
        {
            var key = (lot.District, lot.BuildingVariant);
            if (!catalog.TryGetValue(key, out var registry))
            {
                registry = provider.GetPrefabOrThrow(key.District, key.BuildingVariant).GetComponent<CityBuildingAssetRegistry>();
                catalog.Add(key, registry);
            }
            var pose = CityBuildingPrototypePlacement.ResolveCityPose(lot, registry);
            Matrix4x4 frame = Frame(pose.Position, pose.Rotation);
            string id = house == null ? $"city:{lot.Cell.x}:{lot.Cell.y}" : CityElectricalConsumerPlan.HouseId(house);
            foreach (var solid in registry.ColliderBounds) Add(id, solid, frame);
            foreach (var slot in registry.BalconySlots)
            {
                var deck = slot.LocalDeckBounds;
                deck.Encapsulate(deck.max + Vector3.up * 1.25f);
                Add(id + " balcony", deck, frame);
            }
            if (registry.TryGetRenderer(CityBuildingMeshRole.Roof, out var roof))
                Add(id + " roof", roof.localBounds, frame * registry.transform.worldToLocalMatrix * roof.localToWorldMatrix);
            if (house == null) return;
            int variant = lot.BuildingVariant;
            if (house.Bank == CityTerraceTransportBank.East)
            {
                float depth = CityBuildingAssetProvider.GetExpectedEnvelope(lot.District, variant).z;
                Model("BalconyGallery" + variant, frame * Matrix4x4.Translate(new Vector3(0f,0f,depth * .5f - 1.2f)), id);
            }
            else
            {
                float height = CityBuildingAssetProvider.GetExpectedBodyTop(lot.District, variant) + .18f;
                Model("SteppedClerestory", frame * Matrix4x4.Translate(new Vector3(.85f,height,.65f)), id);
            }
            var entry = Frame(house.ThresholdWorld, lot.FacadeRotation);
            if (house.HasGlazedBay) Model("GlazedStairBay", entry, id);
            else if (variant == 3 || lot.Cell.y % 3 == 0)
                Add(id + " entrance canopy", CityResidentialCourtyardAssetProvider.GetLocalBounds(
                    CityResidentialCourtyardPropKind.EntranceCanopy), entry);
            if (house.HasAirer) Model("BalconyAirer", Frame(house.AirerOrigin, lot.FacadeRotation), id);
        }

        private void Model(string name, Matrix4x4 frame, string id)
        {
            if (!templates.TryGetValue(name, out var renderers))
            {
                var model = Resources.Load<GameObject>("City/Hillside/" + name);
                if (!model) throw new InvalidOperationException("Missing hillside model " + name);
                templates.Add(name, renderers = model.GetComponentsInChildren<MeshRenderer>(true));
            }
            // This is a bare FBX instantiated under a placement frame. Its root's
            // import scale (100) remains part of the original renderer matrix.
            foreach (var renderer in renderers)
                Add(id + " " + renderer.name, renderer.localBounds, frame * renderer.localToWorldMatrix);
        }

        private void SpecialLot(BuildingLot lot)
        {
            string id = $"city:{lot.Cell.x}:{lot.Cell.y}";
            if (lot.IsBar || lot.IsSupermarket)
            {
                var prefab = lot.IsBar ? BarModelResources.LoadFacadePrefab() : SupermarketExteriorModelResources.LoadPrefab();
                Quaternion rotation = lot.IsBar ? Quaternion.LookRotation(Vector3.Cross(lot.FacadeForward,Vector3.up)) : lot.FacadeRotation;
                Transform anchor;
                bool found = lot.IsBar ? prefab.GetComponent<BarAssetRegistry>().TryGetAnchor("exterior_door", out anchor)
                    : prefab.GetComponent<SupermarketExteriorAssetRegistry>().TryGetAnchor("exterior_door", out anchor);
                if (!found) throw new InvalidOperationException("Missing exterior door for " + id);
                Matrix4x4 frame = Frame(lot.DoorPosition - rotation * prefab.transform.InverseTransformPoint(anchor.position), rotation)
                    * prefab.transform.worldToLocalMatrix;
                foreach (var r in prefab.GetComponentsInChildren<MeshRenderer>(true))
                    if (r.enabled) Add(id + " " + r.name, r.localBounds, frame * r.localToWorldMatrix);
                return;
            }
            foreach (var rect in lot.CreateCollisionFootprints())
                Add(id, new Bounds(new Vector3(rect.center.x, lot.Center.y + lot.Height*.5f,rect.center.y),
                    new Vector3(rect.width, lot.Height, rect.height)), Matrix4x4.identity);
        }

        private void Home(BuildingLot lot)
        {
            var frame = Frame(lot.DoorPosition - lot.FacadeForward * 6f, lot.FacadeRotation);
            Box("home walls", frame, new(-6.39f,0f,-5.86f), new(6.39f,6.82f,6f));
            // The gable ends support a real inlet at 7.8 m. Keep the gables within
            // their masonry planes and the pitched roof above them, including the porch.
            for (int side = -1; side <= 1; side += 2)
                Box("home gable", frame, new(side*6.25f-.14f,6.82f,-5.72f), new(side*6.25f+.14f,8.54f,5.86f));
            Box("home balcony", frame, new(-3.4f,4.52f,6f), new(.5f,5.95f,8.30f));
            HomeRoof(-6.05f,7f,0f,8.8f); HomeRoof(0f,8.8f,8.30f,7f);
            void HomeRoof(float z0,float y0,float z1,float y1)
            {
                Vector3 along = new(0f,y1-y0,z1-z0);
                var bed = frame * Frame(new Vector3(0f,(y0+y1)*.5f-.10f,(z0+z1)*.5f), Quaternion.LookRotation(along));
                Add("home roof", new Bounds(Vector3.zero,new Vector3(13f,.23f,along.magnitude)), bed);
            }
        }

        private void Pavilions(CityLayout layout)
        {
            if (CityHillsideStreetPlan.ForLayout(layout).Routes.Count == 0) return;
            if (RidgeKioskSitePlan.TryCreate(layout, out var kiosk))
            {
                var f = Frame(kiosk.Origin,kiosk.Rotation);
                Box(kiosk.StableId, f, new(-1.5f,0f,-1.25f), new(1.5f,2.51f,1.25f));
                Box("kiosk roof", f, new(-1.57f,2.51f,-1.32f), new(1.57f,2.61f,1.3f));
                Box("kiosk canopy", f, new(-1.55f,2.18f,1.25f), new(1.55f,2.73f,2.17f));
            }
            if (CityPawnshopPlan.TryCreate(layout, out var pawn))
            {
                var f = Frame(pawn.Origin,pawn.Rotation);
                Box("pawnshop", f, new(-4f,0f,-4.5f), new(4f,3.65f,4.5f));
                Box("pawnshop parapet", f, new(-4.10f,3.64f,-4.60f), new(4.10f,4.05f,4.60f));
            }
            var glass = CityGlassReturnSitePlan.ForLayout(layout);
            if (glass.IsEnabled)
            {
                var f = Frame(glass.Origin,glass.Rotation);
                Box("glass return walls", f, new(-2.24f,0f,-1.58f), new(2.24f,2.70f,1.58f));
                Box("glass return roof", f, new(-2.3f,2.70f,-1.6f), new(2.3f,2.81f,1.6f));
                Box("glass return sign and canopy", f, new(-1.79f,2.18f,1.58f), new(1.79f,3.01f,2.93f));
            }
            var loft = CityPigeonLoftPlan.ForLayout(layout);
            if (loft.IsEnabled)
            {
                var f = Frame(loft.Origin,loft.Rotation);
                Box("loft cabin", f, new(-1.50f,2f,-1.20f), new(1.5f,4.43f,1.20f));
                Box("loft aviary", f, new(1.50f,2.22f,-1.13f), new(1.94f,3.83f,1.13f));
                Box("loft aviary canopy", f, new(1.44f,3.82f,-1.17f), new(2f,3.89f,1.17f));
                // The two pitched sheets leave clear air above the gable inlets.
                // One enclosing roof AABB would incorrectly fill that air.
                for (int side = -1; side <= 1; side += 2)
                {
                    var slope = new Vector3(side * 1.68f, -.437f, 0f);
                    var centre = new Vector3(side * .84f, 4.216f, 0f);
                    Add("loft roof", new Bounds(Vector3.zero, new Vector3(2.87f,.075f,slope.magnitude)),
                        f * Frame(centre, Quaternion.LookRotation(slope)));
                }
            }
            var cobbler = CityCobblerSitePlan.ForLayout(layout);
            if (cobbler.IsEnabled)
            {
                var f = Frame(cobbler.Origin,cobbler.Rotation);
                foreach (var box in CityCobblerAssetProvider.Geometry.boxes)
                    Add("cobbler " + box.name, box.MetricBounds, f);
            }
            foreach (var s in CityHillsideSiteDetails.ForLayout(layout))
            {
                if (s.Part != CityHillsideSiteDetails.Kind.YardShelter) continue;
                var f = Frame(s.Origin,s.Rotation);
                Box("yard shelter roof", f, new(-1.25f,2.04f,-.625f), new(1.25f,2.15f,.625f));
                Box("yard shelter body", f, new(-1.1525f,0f,-.5225f), new(1.1525f,2.04f,.5225f));
            }
        }

        private void Box(string id, Matrix4x4 frame, Vector3 low, Vector3 high) =>
            Add(id, new Bounds((low+high)*.5f,high-low),frame);
    }
}

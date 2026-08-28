using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Pure placement contract for the church precinct. The imported model
    /// supplies presentation only; ground, foundation, collision, approach,
    /// door action and City return all come from this immutable plan.
    /// </summary>
    public sealed class CityChurchPlan
    {
        internal CityChurchPlan(
            string areaId,
            IList<Vector2Int> cells,
            Rect grounds,
            float groundTopY,
            CityOpenAreaAccessDescriptor access,
            Vector3 modelRootPosition,
            Quaternion modelRotation,
            Rect modelFootprint,
            Bounds foundationBounds,
            Bounds buildingColliderBounds,
            Rect approachBounds,
            Vector3 entranceOutwardDirection,
            Vector3 altarDirection,
            Vector3 doorGroundPosition,
            Vector3 interactionPosition,
            Vector3 doorDockPosition,
            Vector3 returnPosition,
            float cemeteryClearance)
        {
            AreaId = areaId ?? string.Empty;
            Cells = new ReadOnlyCollection<Vector2Int>(
                new List<Vector2Int>(
                    cells ?? throw new ArgumentNullException(nameof(cells))));
            Grounds = grounds;
            GroundTopY = groundTopY;
            Access = access;
            ModelRootPosition = modelRootPosition;
            ModelRotation = modelRotation;
            ModelFootprint = modelFootprint;
            FoundationBounds = foundationBounds;
            BuildingColliderBounds = buildingColliderBounds;
            ApproachBounds = approachBounds;
            EntranceOutwardDirection = entranceOutwardDirection;
            AltarDirection = altarDirection;
            DoorGroundPosition = doorGroundPosition;
            InteractionPosition = interactionPosition;
            DoorDockPosition = doorDockPosition;
            ReturnPosition = returnPosition;
            CemeteryClearance = cemeteryClearance;
            DoorAction = PlayerDoorActionPlan.CreateStationary(
                interactionPosition,
                doorDockPosition,
                altarDirection);
        }

        public string AreaId { get; }
        public IReadOnlyList<Vector2Int> Cells { get; }
        public Rect Grounds { get; }
        public float GroundTopY { get; }
        public CityOpenAreaAccessDescriptor Access { get; }

        /// <summary>
        /// The prefab uses local +Z as the entrance-outward axis. At the
        /// canonical west facade this therefore points world-west, while
        /// the altar points world-east.
        /// </summary>
        public Vector3 ModelRootPosition { get; }
        public Quaternion ModelRotation { get; }
        public Rect ModelFootprint { get; }
        public Bounds FoundationBounds { get; }
        public Bounds BuildingColliderBounds { get; }
        public Rect ApproachBounds { get; }
        public Vector3 EntranceOutwardDirection { get; }
        public Vector3 AltarDirection { get; }
        public Vector3 DoorGroundPosition { get; }
        public Vector3 InteractionPosition { get; }
        public Vector3 DoorDockPosition { get; }
        public Vector3 ReturnPosition { get; }
        public float CemeteryClearance { get; }
        public PlayerDoorActionPlan DoorAction { get; }
    }

    public static class CityChurchPlanner
    {
        public const string DefaultAreaId = "church";

        /// <summary>
        /// The authored Blender dimensions of the exterior model. One
        /// source asset serves both this landmark and the ChurchInterior
        /// scene, so the City shrinks it at the placer instead of
        /// re-authoring the model and its interior layout contract.
        /// </summary>
        public const float SourceModelWidth = 23f;
        public const float SourceModelHeight = 32f;
        public const float SourceModelLength = 44f;

        /// <summary>
        /// A 44 x 23 x 32 m basilica eight metres off its own frontage
        /// was not a building the player could look at: from the
        /// pavement it filled the whole frame edge to edge, and the town
        /// around it is 18 m blocks. Placed at this fraction it reads as
        /// the provincial parish church it is meant to be and still
        /// towers over the cemetery beside it. The interior is a
        /// separate area and keeps its authored size.
        /// </summary>
        public const float ExteriorModelScale = 0.55f;

        public const float StreetSetback = 10f;
        public const float MinimumCemeteryClearance = 5f;
        public const float FoundationHeight = 0.32f;
        public const float FoundationTopAboveGround = 0.08f;
        public const float ApproachWidth = 3.2f;

        /// <summary>
        /// The forecourt paving is presentation and carries no collider,
        /// so it must never sit between the hero and the ground he is
        /// really standing on. It is laid barely proud of the church
        /// ground - less than the controller's own skin width - and the
        /// slab runs down into the ground rather than floating on it.
        /// </summary>
        public const float ApproachSurfaceTopAboveGround = 0.012f;
        public const float ApproachSurfaceHeight = 0.24f;

        // The ordinary City doors - bar, supermarket, home - all dock
        // and read their trigger at one point 0.8 m out from the leaf,
        // so the hero stands exactly where the prompt is measured.
        public const float DoorDockOutwardDistance = 0.82f;
        public const float InteractionOutwardDistance = 0.82f;
        public const float InteractionHeight = 0.82f;

        /// <summary>
        /// The frontage access point sits on the street's outer edge,
        /// where the pavement is still a couple of decimetres above the
        /// church ground. Leaving the church puts the hero this far in
        /// from it, standing on the forecourt he walked in over rather
        /// than straddling the kerb.
        /// </summary>
        public const float CityReturnInsetFromFrontage = 1.6f;
        public const float ExteriorEntranceAnchorLocalX = 0f;
        public const float ExteriorEntranceAnchorLocalZ = 22.05f;

        public static float ModelWidth =>
            SourceModelWidth * ExteriorModelScale;
        public static float ModelHeight =>
            SourceModelHeight * ExteriorModelScale;
        public static float ModelLength =>
            SourceModelLength * ExteriorModelScale;

        public static Vector3 ModelLocalSize =>
            new Vector3(ModelWidth, ModelHeight, ModelLength);

        /// <summary>
        /// XZ contract of ANCHOR_Exterior.Entrance in the presentation
        /// prefab. The Catholic basilica has one central west door; local
        /// +Z points out through that door. This is the prefab's own
        /// unscaled local position - see
        /// <see cref="ExteriorEntranceModelOffset"/> for the placed one.
        /// </summary>
        public static Vector3 ExteriorEntranceAnchorLocalPosition =>
            new Vector3(
                ExteriorEntranceAnchorLocalX,
                0f,
                ExteriorEntranceAnchorLocalZ);

        /// <summary>
        /// The same anchor after the placer's uniform shrink: the offset
        /// from the model root to the visible door in world metres.
        /// </summary>
        public static Vector3 ExteriorEntranceModelOffset =>
            ExteriorEntranceAnchorLocalPosition * ExteriorModelScale;

        /// <summary>
        /// Returns null for a blueprint without a church precinct. A present
        /// precinct is strict: it must be one rectangular ChurchGround area
        /// with the authored southernmost west Street frontage.
        /// </summary>
        public static CityChurchPlan Create(CityLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            var surfaces = new List<CitySurfaceDescriptor>(8);
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = layout.Surfaces[index];
                if (surface.Feature == CityAreaFeatureKind.Church &&
                    surface.Kind == CitySurfaceKind.ChurchGround)
                {
                    surfaces.Add(surface);
                }
            }

            if (surfaces.Count == 0)
            {
                return null;
            }

            string areaId = surfaces[0].AreaId;
            int minimumX = int.MaxValue;
            int maximumX = int.MinValue;
            int minimumZ = int.MaxValue;
            int maximumZ = int.MinValue;
            Rect grounds = surfaces[0].WorldBounds;
            float groundTopY = surfaces[0].PhysicalTopY;
            var cells = new List<Vector2Int>(surfaces.Count);
            for (int index = 0; index < surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = surfaces[index];
                if (!string.Equals(
                        surface.AreaId,
                        areaId,
                        StringComparison.Ordinal) ||
                    Mathf.Abs(surface.PhysicalTopY - groundTopY) > 0.02f)
                {
                    throw new InvalidOperationException(
                        "The church must be one level, single-ID precinct.");
                }

                Vector2Int cell = surface.Cell;
                cells.Add(cell);
                minimumX = Mathf.Min(minimumX, cell.x);
                maximumX = Mathf.Max(maximumX, cell.x);
                minimumZ = Mathf.Min(minimumZ, cell.y);
                maximumZ = Mathf.Max(maximumZ, cell.y);
                grounds = Encapsulate(grounds, surface.WorldBounds);
            }

            if ((maximumX - minimumX + 1) *
                    (maximumZ - minimumZ + 1) != surfaces.Count)
            {
                throw new InvalidOperationException(
                    "The church precinct must be one solid rectangle.");
            }

            cells.Sort(CityAreaPlacement.CompareCellsRowMajor);
            CityOpenAreaAccessDescriptor access = FindAccess(
                layout,
                areaId);
            var expectedCell = new Vector2Int(minimumX, minimumZ);
            RoadEdge expectedFrontage = RoadEdge.ForCellFrontage(
                expectedCell,
                Vector2Int.left);
            if (access.Cell != expectedCell ||
                access.StreetSideDirection != Vector2Int.left ||
                access.FrontageEdge != expectedFrontage ||
                access.OutwardNormal != Vector3.right ||
                !layout.HasRoad(expectedFrontage) ||
                layout.GetPathKind(expectedFrontage) != CityPathKind.Street)
            {
                throw new InvalidOperationException(
                    "The church requires its southernmost west Street " +
                    "frontage after road generation.");
            }

            Vector3 altarDirection = access.OutwardNormal.normalized;
            Vector3 entranceOutward = -altarDirection;
            Quaternion modelRotation = Quaternion.LookRotation(
                entranceOutward,
                Vector3.up);

            float modelWest = grounds.xMin + StreetSetback;
            // The nave is laid on the frontage's own axis so the walk
            // from the street runs straight at the door, the way every
            // other City entrance approach does. It still keeps its
            // clearance from the cemetery to the south.
            float southernmost = grounds.yMin + MinimumCemeteryClearance;
            float northernmost = grounds.yMax - ModelWidth;
            float modelSouth = northernmost < southernmost
                ? southernmost
                : Mathf.Clamp(
                    access.Center.z - ModelWidth * 0.5f,
                    southernmost,
                    northernmost);
            var modelFootprint = new Rect(
                modelWest,
                modelSouth,
                ModelLength,
                ModelWidth);
            if (!Contains(grounds, modelFootprint))
            {
                throw new InvalidOperationException(
                    "The placed church does not fit its grounds.");
            }

            float modelBaseY = groundTopY + FoundationTopAboveGround;
            Vector3 modelRootPosition = new Vector3(
                modelFootprint.center.x,
                modelBaseY,
                modelFootprint.center.y);
            var foundationBounds = new Bounds(
                new Vector3(
                    modelFootprint.center.x,
                    groundTopY + FoundationTopAboveGround -
                    FoundationHeight * 0.5f,
                    modelFootprint.center.y),
                new Vector3(
                    modelFootprint.width,
                    FoundationHeight,
                    modelFootprint.height));
            var colliderBounds = new Bounds(
                new Vector3(
                    modelFootprint.center.x,
                    modelBaseY + ModelHeight * 0.5f,
                    modelFootprint.center.y),
                new Vector3(
                    modelFootprint.width,
                    ModelHeight,
                    modelFootprint.height));

            Vector3 transformedEntranceAnchor = modelRootPosition +
                modelRotation * ExteriorEntranceModelOffset;
            // The door sits on the church ground itself, NOT on the
            // forecourt paving laid over it. The paving has no collider,
            // so a dock measured from its top floats above every height
            // the hero can actually reach - and the door action refuses
            // any dock further than InteractionVerticalTolerance from
            // where he stands, which is how this door came to show its
            // prompt and then do nothing at all when pressed.
            var doorGround = new Vector3(
                transformedEntranceAnchor.x,
                groundTopY,
                transformedEntranceAnchor.z);
            if (Mathf.Abs(doorGround.z - modelFootprint.center.y) > 0.001f)
            {
                throw new InvalidOperationException(
                    "The Catholic basilica door must stay centered on its " +
                    "west facade.");
            }

            Vector3 dock = doorGround +
                           entranceOutward * DoorDockOutwardDistance;
            dock.y = groundTopY + PlayerFactory.GroundedRootOffset;
            Vector3 interaction = doorGround +
                                  entranceOutward *
                                  InteractionOutwardDistance;
            interaction.y = groundTopY + InteractionHeight;
            Vector3 cityReturn = access.Center +
                                 altarDirection *
                                 CityReturnInsetFromFrontage;
            cityReturn.y = groundTopY + PlayerFactory.GroundedRootOffset;
            // Paved from the frontage the walk starts at all the way to
            // the door line, so the docked hero's whole capsule stands
            // inside it rather than on its lip.
            Rect approach = Rect.MinMaxRect(
                Mathf.Min(access.Center.x, doorGround.x),
                Mathf.Min(access.Center.z, doorGround.z) -
                ApproachWidth * 0.5f,
                Mathf.Max(access.Center.x, doorGround.x),
                Mathf.Max(access.Center.z, doorGround.z) +
                ApproachWidth * 0.5f);
            if (!Contains(grounds, approach))
            {
                throw new InvalidOperationException(
                    "The church approach must stay on church ground.");
            }

            float cemeteryClearance = ResolveCemeteryClearance(
                layout,
                modelFootprint,
                approach);
            var plan = new CityChurchPlan(
                areaId,
                cells,
                grounds,
                groundTopY,
                access,
                modelRootPosition,
                modelRotation,
                modelFootprint,
                foundationBounds,
                colliderBounds,
                approach,
                entranceOutward,
                altarDirection,
                doorGround,
                interaction,
                dock,
                cityReturn,
                cemeteryClearance);
            ValidateOrThrow(layout, plan);
            return plan;
        }

        public static void ValidateOrThrow(
            CityLayout layout,
            CityChurchPlan plan)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (plan.Cells.Count == 0 ||
                !Contains(plan.Grounds, plan.ModelFootprint) ||
                !Contains(plan.Grounds, plan.ApproachBounds) ||
                plan.CemeteryClearance < MinimumCemeteryClearance - 0.001f ||
                Vector3.Dot(
                    plan.ModelRotation * Vector3.forward,
                    plan.EntranceOutwardDirection) < 0.999f ||
                Mathf.Abs(plan.AltarDirection.sqrMagnitude - 1f) > 0.001f)
            {
                throw new InvalidOperationException(
                    "The church plan violates its site or orientation contract.");
            }

            if ((plan.EntranceOutwardDirection + plan.AltarDirection)
                    .sqrMagnitude > 0.0001f ||
                !plan.ApproachBounds.Contains(
                    new Vector2(
                        plan.ReturnPosition.x,
                        plan.ReturnPosition.z)) ||
                !plan.ApproachBounds.Contains(
                    new Vector2(
                        plan.DoorDockPosition.x,
                        plan.DoorDockPosition.z)))
            {
                throw new InvalidOperationException(
                    "The church entrance, approach and return drifted apart.");
            }

            Vector3 transformedEntranceAnchor = plan.ModelRootPosition +
                plan.ModelRotation * ExteriorEntranceModelOffset;
            Vector2 anchorXZ = new Vector2(
                transformedEntranceAnchor.x,
                transformedEntranceAnchor.z);
            Vector2 doorXZ = new Vector2(
                plan.DoorGroundPosition.x,
                plan.DoorGroundPosition.z);
            if (Vector2.Distance(anchorXZ, doorXZ) > 0.001f ||
                Mathf.Abs(
                    plan.DoorGroundPosition.z -
                    plan.ModelFootprint.center.y) > 0.001f)
            {
                throw new InvalidOperationException(
                    "The church action door drifted from the exterior " +
                    "prefab entrance anchor.");
            }

            if (Vector3.Distance(
                    plan.DoorAction.InteractionPosition,
                    plan.InteractionPosition) > 0.001f ||
                Vector3.Distance(
                    plan.DoorAction.EntryRootPosition,
                    plan.DoorDockPosition) > 0.001f ||
                Vector3.Distance(
                    plan.DoorAction.ExitRootPosition,
                    plan.DoorDockPosition) > 0.001f ||
                Vector3.Dot(
                    plan.DoorAction.EntryFacingDirection,
                    plan.AltarDirection) < 0.999f)
            {
                throw new InvalidOperationException(
                    "The church door action must dock at the visible " +
                    "central entrance.");
            }

            // The door action refuses any dock the hero cannot already
            // be standing on, and the church ground is one flat slab, so
            // both docks are the grounded root height over it or the
            // prompt appears and pressing it does nothing.
            float grounded = plan.GroundTopY +
                             PlayerFactory.GroundedRootOffset;
            if (Mathf.Abs(plan.DoorDockPosition.y - grounded) >
                    PlayerMotor.InteractionVerticalTolerance ||
                Mathf.Abs(plan.ReturnPosition.y - grounded) >
                    PlayerMotor.InteractionVerticalTolerance)
            {
                throw new InvalidOperationException(
                    "The church door dock and City return must stand on " +
                    "the church ground the hero can actually reach.");
            }
        }

        private static CityOpenAreaAccessDescriptor FindAccess(
            CityLayout layout,
            string areaId)
        {
            bool found = false;
            CityOpenAreaAccessDescriptor selected = default;
            for (int index = 0;
                 index < layout.OpenAreaAccesses.Count;
                 index++)
            {
                CityOpenAreaAccessDescriptor access =
                    layout.OpenAreaAccesses[index];
                if (access.Feature != CityAreaFeatureKind.Church ||
                    !string.Equals(
                        access.AreaId,
                        areaId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (found)
                {
                    throw new InvalidOperationException(
                        "A church precinct must have exactly one access.");
                }

                selected = access;
                found = true;
            }

            if (!found)
            {
                throw new InvalidOperationException(
                    "The church precinct has no Street access.");
            }

            return selected;
        }

        private static float ResolveCemeteryClearance(
            CityLayout layout,
            Rect modelFootprint,
            Rect approach)
        {
            bool found = false;
            Rect cemetery = default;
            Rect cemeteryGate = default;
            bool hasGate = false;
            for (int index = 0; index < layout.Surfaces.Count; index++)
            {
                CitySurfaceDescriptor surface = layout.Surfaces[index];
                if (surface.Kind != CitySurfaceKind.CemeteryGround)
                {
                    continue;
                }

                cemetery = found
                    ? Encapsulate(cemetery, surface.WorldBounds)
                    : surface.WorldBounds;
                found = true;
            }

            for (int index = 0;
                 index < layout.OpenAreaAccesses.Count;
                 index++)
            {
                CityOpenAreaAccessDescriptor access =
                    layout.OpenAreaAccesses[index];
                if (access.Feature != CityAreaFeatureKind.Cemetery)
                {
                    continue;
                }

                cemeteryGate = access.ApproachBounds;
                hasGate = true;
                break;
            }

            if (!found)
            {
                return float.PositiveInfinity;
            }

            float clearance = modelFootprint.yMin - cemetery.yMax;
            if (clearance < MinimumCemeteryClearance - 0.001f ||
                (hasGate && approach.Overlaps(cemeteryGate)))
            {
                throw new InvalidOperationException(
                    "The church footprint or approach conflicts with the " +
                    "unchanged cemetery and its gate.");
            }

            return clearance;
        }

        private static Rect Encapsulate(Rect first, Rect second)
        {
            return Rect.MinMaxRect(
                Mathf.Min(first.xMin, second.xMin),
                Mathf.Min(first.yMin, second.yMin),
                Mathf.Max(first.xMax, second.xMax),
                Mathf.Max(first.yMax, second.yMax));
        }

        private static bool Contains(Rect outer, Rect inner)
        {
            const float tolerance = 0.001f;
            return inner.xMin >= outer.xMin - tolerance &&
                   inner.xMax <= outer.xMax + tolerance &&
                   inner.yMin >= outer.yMin - tolerance &&
                   inner.yMax <= outer.yMax + tolerance;
        }

    }
}

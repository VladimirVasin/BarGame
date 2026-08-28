using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Materialises the church-yard plan exclusively from Blender-authored
    /// City misc meshes. Runtime geometry is limited to invisible collision
    /// proxies; surfaces, furniture and planting are imported presentation.
    /// </summary>
    public static class CityChurchCourtyardWorldBuilder
    {
        public const string RootName = "Church Courtyard";
        public const float SurfaceTopAboveGround = 0.012f;
        public const float AuthoredSurfaceHeight = 0.04f;

        private static readonly Color Stone =
            new Color(0.40f, 0.39f, 0.35f);
        private static readonly Color Gravel =
            new Color(0.37f, 0.34f, 0.29f);
        private static readonly Color Lawn =
            new Color(0.18f, 0.31f, 0.17f);
        private static readonly Color Foliage =
            new Color(0.14f, 0.30f, 0.16f);
        private static readonly Color Bark =
            new Color(0.22f, 0.14f, 0.08f);
        private static readonly Color Timber =
            new Color(0.35f, 0.22f, 0.12f);
        private static readonly Color Iron =
            new Color(0.16f, 0.17f, 0.15f);
        private static readonly Color BedStone =
            new Color(0.42f, 0.42f, 0.38f);
        private static readonly Color Flowers =
            new Color(0.48f, 0.22f, 0.24f);

        public static GameObject Build(
            Transform parent,
            CityChurchCourtyardPlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                return null;
            }

            CityMiscAssetProvider provider =
                CityMiscAssetProvider.LoadOrThrow();
            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent, false);

            int styleCount = (int)BatchStyle.Flowers + 1;
            var batches = new List<RuntimeMeshPlacement>[styleCount];
            for (int index = 0; index < styleCount; index++)
            {
                batches[index] = new List<RuntimeMeshPlacement>();
            }

            for (int index = 0; index < plan.Surfaces.Count; index++)
            {
                AppendSurface(provider, plan, plan.Surfaces[index], batches);
            }

            for (int index = 0; index < plan.Fixtures.Count; index++)
            {
                AppendFixture(provider, plan.Fixtures[index], batches);
            }

            for (int index = 0; index < styleCount; index++)
            {
                List<RuntimeMeshPlacement> placements = batches[index];
                if (placements.Count == 0)
                {
                    continue;
                }

                BatchStyle style = (BatchStyle)index;
                Color color = ResolveColor(style);
                GameObject batch = RuntimePrimitiveFactory.CreateCombinedMeshes(
                    $"Church Courtyard {style}",
                    root,
                    placements,
                    color,
                    false,
                    ResolveTileSize(style),
                    ResolveUvMode(style));
                ApplyAppearance(batch.GetComponent<Renderer>(), style, color);
            }

            BuildCollision(root, plan);
            return root.gameObject;
        }

        public static void AppendBenchSeats(
            CityChurchCourtyardPlan plan,
            ICollection<CityBenchSeat> target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (plan == null)
            {
                return;
            }

            for (int index = 0; index < plan.Fixtures.Count; index++)
            {
                CityChurchCourtyardFixtureDescriptor fixture =
                    plan.Fixtures[index];
                if (fixture.Kind != CityChurchCourtyardFixtureKind.Bench)
                {
                    continue;
                }

                target.Add(new CityBenchSeat(
                    fixture.Id,
                    fixture.GroundPosition + Vector3.up * 0.49f,
                    1.60f,
                    0.42f,
                    plan.GroundTopY,
                    fixture.Rotation * Vector3.forward));
            }
        }

        private static void AppendSurface(
            CityMiscAssetProvider provider,
            CityChurchCourtyardPlan plan,
            CityChurchCourtyardSurfaceDescriptor surface,
            IReadOnlyList<List<RuntimeMeshPlacement>> batches)
        {
            int variant = (int)surface.Kind;
            CityMiscMeshPart part = provider.GetPartOrThrow(
                CityMiscKind.ChurchCourtyardSurface,
                variant,
                0);
            Rect bounds = surface.Bounds;
            batches[(int)ResolveSurfaceStyle(surface.Kind)].Add(
                new RuntimeMeshPlacement(
                    part.Mesh,
                    new Vector3(
                        bounds.center.x,
                        plan.GroundTopY + SurfaceTopAboveGround -
                        AuthoredSurfaceHeight,
                        bounds.center.y),
                    Quaternion.identity,
                    new Vector3(bounds.width, 1f, bounds.height)));
        }

        private static void AppendFixture(
            CityMiscAssetProvider provider,
            CityChurchCourtyardFixtureDescriptor fixture,
            IReadOnlyList<List<RuntimeMeshPlacement>> batches)
        {
            CityMiscKind kind = ResolveKind(fixture.Kind);
            int partCount = CityMiscAssetProvider.GetPartCount(kind);
            for (int index = 0; index < partCount; index++)
            {
                CityMiscMeshPart part = provider.GetPartOrThrow(
                    kind,
                    fixture.Variant,
                    index);
                BatchStyle style = ResolveFixtureStyle(
                    fixture.Kind,
                    part.Role);
                batches[(int)style].Add(new RuntimeMeshPlacement(
                    part.Mesh,
                    fixture.GroundPosition,
                    fixture.Rotation,
                    fixture.Scale));
            }
        }

        private static void BuildCollision(
            Transform root,
            CityChurchCourtyardPlan plan)
        {
            var blockers = new List<RuntimeOrientedBox>(plan.Fixtures.Count);
            for (int index = 0; index < plan.Fixtures.Count; index++)
            {
                CityChurchCourtyardFixtureDescriptor fixture =
                    plan.Fixtures[index];
                Rect bounds = fixture.BlockerBounds;
                float height = ResolveCollisionHeight(fixture.Kind);
                blockers.Add(new RuntimeOrientedBox(
                    new Vector3(
                        bounds.center.x,
                        plan.GroundTopY + height * 0.5f,
                        bounds.center.y),
                    Quaternion.identity,
                    new Vector3(bounds.width, height, bounds.height)));
            }

            if (blockers.Count == 0)
            {
                return;
            }

            GameObject collision =
                RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                    "Church Courtyard Collision",
                    root,
                    blockers,
                    Color.black,
                    true);
            Renderer renderer = collision.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.enabled = false;
            }
        }

        private static CityMiscKind ResolveKind(
            CityChurchCourtyardFixtureKind kind)
        {
            switch (kind)
            {
                case CityChurchCourtyardFixtureKind.Shrub:
                    return CityMiscKind.ChurchCourtyardShrub;
                case CityChurchCourtyardFixtureKind.FlowerBed:
                    return CityMiscKind.ChurchCourtyardFlowerBed;
                case CityChurchCourtyardFixtureKind.Bench:
                    return CityMiscKind.CemeteryBench;
                case CityChurchCourtyardFixtureKind.Tree:
                    return CityMiscKind.ParkTree;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private static BatchStyle ResolveSurfaceStyle(
            CityChurchCourtyardSurfaceKind kind)
        {
            switch (kind)
            {
                case CityChurchCourtyardSurfaceKind.Stone:
                    return BatchStyle.Stone;
                case CityChurchCourtyardSurfaceKind.Gravel:
                    return BatchStyle.Gravel;
                case CityChurchCourtyardSurfaceKind.Lawn:
                    return BatchStyle.Lawn;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private static BatchStyle ResolveFixtureStyle(
            CityChurchCourtyardFixtureKind kind,
            CityMiscMeshRole role)
        {
            if (kind == CityChurchCourtyardFixtureKind.Tree)
            {
                return role == CityMiscMeshRole.Bark
                    ? BatchStyle.Bark
                    : BatchStyle.Foliage;
            }

            if (kind == CityChurchCourtyardFixtureKind.Bench)
            {
                return role == CityMiscMeshRole.Timber
                    ? BatchStyle.Timber
                    : BatchStyle.Iron;
            }

            if (kind == CityChurchCourtyardFixtureKind.FlowerBed)
            {
                if (role == CityMiscMeshRole.Masonry)
                {
                    return BatchStyle.BedStone;
                }

                return role == CityMiscMeshRole.Residential
                    ? BatchStyle.Flowers
                    : BatchStyle.Foliage;
            }

            return BatchStyle.Foliage;
        }

        private static float ResolveCollisionHeight(
            CityChurchCourtyardFixtureKind kind)
        {
            switch (kind)
            {
                case CityChurchCourtyardFixtureKind.Shrub:
                    return 0.82f;
                case CityChurchCourtyardFixtureKind.FlowerBed:
                    return 0.34f;
                case CityChurchCourtyardFixtureKind.Bench:
                    return 1.30f;
                case CityChurchCourtyardFixtureKind.Tree:
                    return 2.75f;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private static Color ResolveColor(BatchStyle style)
        {
            switch (style)
            {
                case BatchStyle.Stone:
                    return Stone;
                case BatchStyle.Gravel:
                    return Gravel;
                case BatchStyle.Lawn:
                    return Lawn;
                case BatchStyle.Foliage:
                    return Foliage;
                case BatchStyle.Bark:
                    return Bark;
                case BatchStyle.Timber:
                    return Timber;
                case BatchStyle.Iron:
                    return Iron;
                case BatchStyle.BedStone:
                    return BedStone;
                case BatchStyle.Flowers:
                    return Flowers;
                default:
                    throw new ArgumentOutOfRangeException(nameof(style));
            }
        }

        private static float ResolveTileSize(BatchStyle style)
        {
            switch (style)
            {
                case BatchStyle.Stone:
                    return CityParkSurfaceAppearance.GetRecipe(
                        CityParkSurfaceKind.Plaza).MetersPerTile;
                case BatchStyle.Gravel:
                    return CityCemeterySurfaceAppearance.GetRecipe(
                        CityCemeterySurfaceKind.Gravel).MetersPerTile;
                case BatchStyle.Lawn:
                    return CityParkSurfaceAppearance.GetRecipe(
                        CityParkSurfaceKind.Lawn).MetersPerTile;
                case BatchStyle.Bark:
                    return CityParkSurfaceAppearance.GetRecipe(
                        CityParkSurfaceKind.Bark).MetersPerTile;
                case BatchStyle.Timber:
                    return CityParkSurfaceAppearance.GetRecipe(
                        CityParkSurfaceKind.Timber).MetersPerTile;
                case BatchStyle.Iron:
                    return CityParkSurfaceAppearance.GetRecipe(
                        CityParkSurfaceKind.PaintedMetal).MetersPerTile;
                case BatchStyle.BedStone:
                    return CityParkSurfaceAppearance.GetRecipe(
                        CityParkSurfaceKind.Stone).MetersPerTile;
                case BatchStyle.Flowers:
                case BatchStyle.Foliage:
                    return CityParkSurfaceAppearance.GetRecipe(
                        CityParkSurfaceKind.Foliage).MetersPerTile;
                default:
                    throw new ArgumentOutOfRangeException(nameof(style));
            }
        }

        private static RuntimeWorldUvMode ResolveUvMode(BatchStyle style)
        {
            return style == BatchStyle.Stone ||
                   style == BatchStyle.Gravel ||
                   style == BatchStyle.Lawn
                ? RuntimeWorldUvMode.XZPlanar
                : RuntimeWorldUvMode.BoxProjected;
        }

        private static void ApplyAppearance(
            Renderer renderer,
            BatchStyle style,
            Color color)
        {
            switch (style)
            {
                case BatchStyle.Gravel:
                    CityCemeterySurfaceAppearance.ApplyCombined(
                        renderer,
                        CityCemeterySurfaceKind.Gravel,
                        color);
                    return;
                case BatchStyle.Stone:
                    CityParkSurfaceAppearance.ApplyCombined(
                        renderer,
                        CityParkSurfaceKind.Plaza,
                        color);
                    return;
                case BatchStyle.Lawn:
                    CityParkSurfaceAppearance.ApplyCombined(
                        renderer,
                        CityParkSurfaceKind.Lawn,
                        color);
                    return;
                case BatchStyle.Bark:
                    CityParkSurfaceAppearance.ApplyCombined(
                        renderer,
                        CityParkSurfaceKind.Bark,
                        color);
                    return;
                case BatchStyle.Timber:
                    CityParkSurfaceAppearance.ApplyCombined(
                        renderer,
                        CityParkSurfaceKind.Timber,
                        color);
                    return;
                case BatchStyle.Iron:
                    CityParkSurfaceAppearance.ApplyCombined(
                        renderer,
                        CityParkSurfaceKind.PaintedMetal,
                        color);
                    return;
                case BatchStyle.BedStone:
                    CityParkSurfaceAppearance.ApplyCombined(
                        renderer,
                        CityParkSurfaceKind.Stone,
                        color);
                    return;
                case BatchStyle.Flowers:
                case BatchStyle.Foliage:
                    CityParkSurfaceAppearance.ApplyCombined(
                        renderer,
                        CityParkSurfaceKind.Foliage,
                        color);
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(style));
            }
        }

        private enum BatchStyle
        {
            Stone = 0,
            Gravel = 1,
            Lawn = 2,
            Foliage = 3,
            Bark = 4,
            Timber = 5,
            Iron = 6,
            BedStone = 7,
            Flowers = 8
        }
    }
}

using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One pressure footprint on a deformable bed surface, in the surface's
    /// local space. Depth is the part's actual penetration under the rest
    /// plane, already clamped by the caller — the surface follows the body's
    /// underside rather than guessing weights.
    /// </summary>
    public readonly struct HomeBedDepressionSource
    {
        public HomeBedDepressionSource(
            float localX,
            float localZ,
            float halfExtentX,
            float halfExtentZ,
            float depth)
        {
            LocalX = localX;
            LocalZ = localZ;
            HalfExtentX = halfExtentX;
            HalfExtentZ = halfExtentZ;
            Depth = depth;
        }

        public float LocalX { get; }
        public float LocalZ { get; }
        public float HalfExtentX { get; }
        public float HalfExtentZ { get; }
        public float Depth { get; }
    }

    /// <summary>
    /// Tuning for the bedding spring. Denting is fast so the surface keeps
    /// up with a lowering body; recovery is slow so the dent visibly
    /// refills after he gets up — the "thick cloth" read.
    /// </summary>
    public readonly struct HomeBedSurfaceDepressionSettings
    {
        private HomeBedSurfaceDepressionSettings(
            float dentTauSeconds,
            float recoverTauSeconds,
            float edgeBandMeters,
            float footprintMarginMeters,
            float rimBulgeRatio)
        {
            DentTauSeconds = dentTauSeconds;
            RecoverTauSeconds = recoverTauSeconds;
            EdgeBandMeters = edgeBandMeters;
            FootprintMarginMeters = footprintMarginMeters;
            RimBulgeRatio = rimBulgeRatio;
        }

        /// <summary>~90% of the way down in 0.25 s.</summary>
        public float DentTauSeconds { get; }

        /// <summary>~95% of the way back up in 1.5 s.</summary>
        public float RecoverTauSeconds { get; }

        /// <summary>
        /// Distance from the pinned border ring over which the dent eases
        /// in from zero to full depth.
        /// </summary>
        public float EdgeBandMeters { get; }

        /// <summary>
        /// How far past a part's footprint the dent skirt reaches before
        /// fading to nothing.
        /// </summary>
        public float FootprintMarginMeters { get; }

        /// <summary>
        /// Soft bedding does not only sink under a body — the displaced
        /// material bulges UP around it. The bulge is what actually reads
        /// from the gameplay camera: a lit welt around a shaded hollow,
        /// and a humped surface line where a dent alone changes nothing.
        /// Expressed as a fraction of the local dent depth.
        /// </summary>
        public float RimBulgeRatio { get; }

        /// <summary>
        /// The mattress: a wide skirt, because a dent that hugs the body
        /// exactly is hidden by the body itself — what the player actually
        /// sees is the bowl sloping down around the silhouette.
        /// </summary>
        public static HomeBedSurfaceDepressionSettings Default =>
            new HomeBedSurfaceDepressionSettings(
                0.10f,
                0.5f,
                0.15f,
                0.18f,
                0.65f);

        /// <summary>
        /// The pillow: compact — a wide skirt would sink the whole small
        /// pillow instead of cupping the head.
        /// </summary>
        public static HomeBedSurfaceDepressionSettings Compact =>
            new HomeBedSurfaceDepressionSettings(
                0.10f,
                0.5f,
                0.10f,
                0.12f,
                0.55f);
    }

    /// <summary>
    /// Deterministic per-vertex depression state for one grid surface.
    /// Pure math over a fixed-step integrator, so EditMode tests can
    /// construct and step it with no scene — the same split the cemetery
    /// act models use.
    /// </summary>
    public sealed class HomeBedSurfaceDepressionModel
    {
        public const float FixedStep = 1f / 120f;
        public const int MaximumSources = 8;
        private const float ChangeEpsilon = 0.00001f;

        private readonly HomeBedSurfaceDepressionSettings settings;
        private readonly float sizeX;
        private readonly float sizeZ;
        private readonly float cellX;
        private readonly float cellZ;
        private readonly float maxDepth;
        private readonly float dentAlpha;
        private readonly float recoverAlpha;
        private readonly float[] depths;
        private readonly float[] targets;
        private readonly HomeBedDepressionSource[] sources =
            new HomeBedDepressionSource[MaximumSources];
        private int sourceCount;
        private float bodyWeight;
        private float accumulator;
        private bool hasShadowRect;
        private float shadowMinX;
        private float shadowMinZ;
        private float shadowMaxX;
        private float shadowMaxZ;
        private float shadowMaxDepth;

        public HomeBedSurfaceDepressionModel(
            HomeBedSurfaceDepressionSettings depressionSettings,
            float surfaceSizeX,
            float surfaceSizeZ,
            int gridColumns,
            int gridRows,
            float maximumDepth)
        {
            settings = depressionSettings;
            sizeX = Mathf.Max(0.01f, surfaceSizeX);
            sizeZ = Mathf.Max(0.01f, surfaceSizeZ);
            Columns = Mathf.Max(1, gridColumns);
            Rows = Mathf.Max(1, gridRows);
            cellX = sizeX / Columns;
            cellZ = sizeZ / Rows;
            maxDepth = Mathf.Max(0f, maximumDepth);
            dentAlpha = 1f - Mathf.Exp(
                -FixedStep / Mathf.Max(0.001f, settings.DentTauSeconds));
            recoverAlpha = 1f - Mathf.Exp(
                -FixedStep / Mathf.Max(0.001f, settings.RecoverTauSeconds));
            depths = new float[VertexCount];
            targets = new float[VertexCount];
        }

        public int Columns { get; }
        public int Rows { get; }
        public int VertexCountX => Columns + 1;
        public int VertexCountZ => Rows + 1;
        public int VertexCount => VertexCountX * VertexCountZ;
        public float MaximumDepth => maxDepth;
        public float BodyWeight => bodyWeight;

        public float GetDepth(int columnIndex, int rowIndex)
        {
            return depths[
                (rowIndex * VertexCountX) + columnIndex];
        }

        public void SetBodyWeight(float weight)
        {
            bodyWeight = Mathf.Clamp01(weight);
        }

        /// <summary>
        /// Caps the dent inside a local rectangle. The pillow's rigid box
        /// is embedded in the mattress; denting the mattress under it
        /// deeper than that embed would open a gap beneath the pillow.
        /// </summary>
        public void SetShadowRect(
            float minX,
            float minZ,
            float maxX,
            float maxZ,
            float maximumDepthInside)
        {
            hasShadowRect = true;
            shadowMinX = minX;
            shadowMinZ = minZ;
            shadowMaxX = maxX;
            shadowMaxZ = maxZ;
            shadowMaxDepth = Mathf.Max(0f, maximumDepthInside);
        }

        public void SetSources(
            HomeBedDepressionSource[] pressureSources,
            int count)
        {
            sourceCount = Mathf.Clamp(
                count,
                0,
                Mathf.Min(
                    MaximumSources,
                    pressureSources?.Length ?? 0));
            for (int index = 0; index < sourceCount; index++)
            {
                sources[index] = pressureSources[index];
            }
        }

        /// <summary>
        /// Steps the spring toward the current targets. Returns true when
        /// any vertex moved beyond noise, so the owner only rewrites the
        /// mesh on frames that actually changed it.
        /// </summary>
        public bool Advance(float deltaTime)
        {
            RefreshTargets();
            accumulator += Mathf.Clamp(deltaTime, 0f, 0.5f);
            bool changed = false;
            while (accumulator + 0.000001f >= FixedStep)
            {
                accumulator -= FixedStep;
                for (int index = 0; index < depths.Length; index++)
                {
                    float target = targets[index];
                    float current = depths[index];
                    float alpha = target > current
                        ? dentAlpha
                        : recoverAlpha;
                    float next = current + ((target - current) * alpha);
                    if (Mathf.Abs(next - current) > ChangeEpsilon)
                    {
                        changed = true;
                    }

                    depths[index] = next;
                }
            }

            return changed;
        }

        /// <summary>
        /// Jumps straight to equilibrium. The opening begins its sleep in
        /// the loop without ever entering, and its close-up must find the
        /// dent already made rather than watch it grow.
        /// </summary>
        public void SnapToTarget()
        {
            RefreshTargets();
            for (int index = 0; index < depths.Length; index++)
            {
                depths[index] = targets[index];
            }

            accumulator = 0f;
        }

        public void ResetToRest()
        {
            for (int index = 0; index < depths.Length; index++)
            {
                depths[index] = 0f;
            }

            accumulator = 0f;
        }

        /// <summary>Bilinear depth at a local XZ point, clamped to the grid.</summary>
        public float SampleDepth(float localX, float localZ)
        {
            float gridX = Mathf.Clamp(
                (localX + (sizeX * 0.5f)) / cellX,
                0f,
                Columns);
            float gridZ = Mathf.Clamp(
                (localZ + (sizeZ * 0.5f)) / cellZ,
                0f,
                Rows);
            int column = Mathf.Min((int)gridX, Columns - 1);
            int row = Mathf.Min((int)gridZ, Rows - 1);
            float tX = gridX - column;
            float tZ = gridZ - row;
            float bottom = Mathf.Lerp(
                GetDepth(column, row),
                GetDepth(column + 1, row),
                tX);
            float top = Mathf.Lerp(
                GetDepth(column, row + 1),
                GetDepth(column + 1, row + 1),
                tX);
            return Mathf.Lerp(bottom, top, tZ);
        }

        private void RefreshTargets()
        {
            float margin = Mathf.Max(
                0.01f,
                settings.FootprintMarginMeters);
            float band = Mathf.Max(0.01f, settings.EdgeBandMeters);
            for (int row = 0; row < VertexCountZ; row++)
            {
                float z = (row * cellZ) - (sizeZ * 0.5f);
                for (int column = 0; column < VertexCountX; column++)
                {
                    float x = (column * cellX) - (sizeX * 0.5f);
                    int index = (row * VertexCountX) + column;
                    if (column == 0 ||
                        row == 0 ||
                        column == Columns ||
                        row == Rows)
                    {
                        // The pinned ring never moves: the side faces are
                        // welded to it, so the box silhouette stays whole.
                        targets[index] = 0f;
                        continue;
                    }

                    float deepest = 0f;
                    float highestBulge = 0f;
                    for (int sourceIndex = 0;
                         sourceIndex < sourceCount;
                         sourceIndex++)
                    {
                        HomeBedDepressionSource source =
                            sources[sourceIndex];
                        float excessX = Mathf.Max(
                            0f,
                            Mathf.Abs(x - source.LocalX) -
                            source.HalfExtentX) / margin;
                        float excessZ = Mathf.Max(
                            0f,
                            Mathf.Abs(z - source.LocalZ) -
                            source.HalfExtentZ) / margin;
                        float skirt = Mathf.Sqrt(
                            (excessX * excessX) +
                            (excessZ * excessZ));
                        if (skirt >= 1f)
                        {
                            continue;
                        }

                        // One continuous profile with a zero crossing:
                        // the hollow falls to nothing by ZeroCross of the
                        // skirt, and past it the displaced bedding humps
                        // UP. Overlapping curves would let the dent's own
                        // tail smother its welt everywhere.
                        const float zeroCross = 0.55f;
                        if (skirt < zeroCross)
                        {
                            float dent = source.Depth *
                                (1f - Smooth01(skirt / zeroCross));
                            if (dent > deepest)
                            {
                                deepest = dent;
                            }
                        }
                        else
                        {
                            float rim = Smooth01(
                                (skirt - zeroCross) /
                                (1f - zeroCross));
                            float bulge = source.Depth *
                                settings.RimBulgeRatio *
                                (4f * rim * (1f - rim));
                            if (bulge > highestBulge)
                            {
                                highestBulge = bulge;
                            }
                        }
                    }

                    // Where one part's dent overlaps another's welt band,
                    // the pressing body wins over the neighbouring bulge.
                    float signedDepth = deepest > 0.0005f
                        ? deepest
                        : -highestBulge;

                    float borderDistance = Mathf.Min(
                        Mathf.Min(
                            x + (sizeX * 0.5f),
                            (sizeX * 0.5f) - x),
                        Mathf.Min(
                            z + (sizeZ * 0.5f),
                            (sizeZ * 0.5f) - z));
                    float envelope = Smooth01(
                        Mathf.Clamp01(borderDistance / band));
                    float target = Mathf.Min(
                        maxDepth,
                        signedDepth * envelope * bodyWeight);

                    // Padded by one cell so a bilinear sample anywhere
                    // inside the true rectangle only ever reads capped
                    // vertices — an uncapped neighbour just outside would
                    // leak depth across the border.
                    if (hasShadowRect &&
                        x >= shadowMinX - cellX &&
                        x <= shadowMaxX + cellX &&
                        z >= shadowMinZ - cellZ &&
                        z <= shadowMaxZ + cellZ)
                    {
                        target = Mathf.Min(target, shadowMaxDepth);
                    }

                    targets[index] = target;
                }
            }
        }

        private static float Smooth01(float amount)
        {
            float clamped = Mathf.Clamp01(amount);
            return clamped * clamped * (3f - (2f * clamped));
        }
    }
}

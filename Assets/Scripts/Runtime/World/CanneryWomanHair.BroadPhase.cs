using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CanneryWomanHair
    {
        /// <summary>Contains the complete interpolated ellipse used by ResolveOtherStrands.</summary>
        private Vector3 ConservativeRingExtent(int i)
        {
            Vector3 firstWidth = ringWidths[i], lastWidth = ringWidths[i + 1];
            float radius = Mathf.Max(firstWidth.magnitude, lastWidth.magnitude) + SurfaceClearance;
            float depth = Mathf.Max(ringDepths[i].magnitude, ringDepths[i + 1].magnitude) + SurfaceClearance;
            float maximumHeight = Mathf.Max(ringHeightLengths[i], ringHeightLengths[i + 1]);
            // Radial and cap clearances are independent in the narrow phase.
            float isotropic = Mathf.Max(radius, depth) + maximumHeight + SurfaceClearance;
            // Account for float roundoff when the actor is far from world zero.
            Vector3 positionSize = Vector3.Max(Abs(ringCenters[i]), Abs(ringCenters[i + 1]));
            float roundoff = .000001f + .00000047683716f * Mathf.Max(positionSize.x, Mathf.Max(positionSize.y, positionSize.z));

            Vector3 axis = ringCenters[i + 1] - ringCenters[i];
            float axisSquared = axis.sqrMagnitude;
            if (axisSquared <= .000001f) return Vector3.one * (isotropic + roundoff);
            axis /= Mathf.Sqrt(axisSquared);
            Vector3 firstProjected = firstWidth - axis * Vector3.Dot(firstWidth, axis);
            Vector3 lastProjected = lastWidth - axis * Vector3.Dot(lastWidth, axis);
            float firstSquared = firstProjected.sqrMagnitude, lastSquared = lastProjected.sqrMagnitude;
            // Below this projection size, or near opposition, normalization
            // can cross Unity's zero-vector threshold. Use an enclosing sphere.
            if (firstSquared <= .00000001f || lastSquared <= .00000001f)
                return Vector3.one * (isotropic + roundoff);
            Vector3 firstUnit = firstProjected / Mathf.Sqrt(firstSquared);
            Vector3 lastUnit = lastProjected / Mathf.Sqrt(lastSquared);
            float cosine = Mathf.Clamp(Vector3.Dot(firstUnit, lastUnit), -1f, 1f);
            if (cosine <= -.95f) return Vector3.one * (isotropic + roundoff);

            // The projection of Lerp(width0,width1,t) is a positive linear
            // combination of these two vectors. Its normalized direction
            // traverses their minor arc, irrespective of their unequal lengths.
            Vector3 middleWidth = (firstUnit + lastUnit).normalized;
            Vector3 middleDepth = Vector3.Cross(axis, middleWidth).normalized;
            float sineHalf = Mathf.Sqrt(Mathf.Max(0f, (1f - cosine) * .5f));
            Vector3 planeLimit = new Vector3(
                Mathf.Sqrt(Mathf.Max(0f, 1f - axis.x * axis.x)),
                Mathf.Sqrt(Mathf.Max(0f, 1f - axis.y * axis.y)),
                Mathf.Sqrt(Mathf.Max(0f, 1f - axis.z * axis.z)));
            Vector3 widthComponent = Vector3.Min(planeLimit, Abs(middleWidth) + Abs(middleDepth) * sineHalf);
            Vector3 depthComponent = Vector3.Min(planeLimit, Abs(middleDepth) + Abs(middleWidth) * sineHalf);
            // For each coordinate, |e1(t)| <= |middle1| + |middle2| sin(half).
            // The ellipse support is sqrt((r*e1)^2 + (d*e2)^2). Endpoint
            // maximum lengths bound the interpolated r and d by convexity.
            float radiusSquared = radius * radius, depthSquared = depth * depth;
            Vector3 extent = new Vector3(
                Mathf.Sqrt(radiusSquared * widthComponent.x * widthComponent.x + depthSquared * depthComponent.x * depthComponent.x),
                Mathf.Sqrt(radiusSquared * widthComponent.y * widthComponent.y + depthSquared * depthComponent.y * depthComponent.y),
                Mathf.Sqrt(radiusSquared * widthComponent.z * widthComponent.z + depthSquared * depthComponent.z * depthComponent.z));
            int segment = i % 13;
            float cap = segment == 0 ? ringHeightLengths[i] + SurfaceClearance :
                segment == 11 ? ringHeightLengths[i + 1] + SurfaceClearance : 0f;
            extent += Abs(axis) * cap;
            // The isotropic envelope remains an independent upper
            // bound, useful when the separate component bounds overlap.
            return Vector3.Min(extent, Vector3.one * isotropic) + Vector3.one * roundoff;
        }
    }
}

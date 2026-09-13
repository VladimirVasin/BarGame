using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CanneryWomanHair
    {
        private Vector3[] bodyPlaneNormals;
        private float[] bodyPlaneScales;
        private Matrix4x4[][] bodySampleMatrices;
        private Vector3[][] bodyUnitPoints;
        private Matrix4x4[] bodyPreviousWorldToUnit;
        private Vector3[] bodyUnitMinimum, bodyUnitMaximum, bodyMinimum, bodyMaximum;
        private float[] bodyPlaneSupport;
        private float[] bodyPlaneOffsets;
        private Vector3 bodyPlaneOrigin;
        private bool[] bodySupportValid, bodyRigidSections;
        private const int BodyPlaneCount = 26;
        private const float BodyMeasurementPadding = .002f;

        // Cached import measurements, never runtime mesh reads. Complete
        // triangles belong to overlapping sections, so no triangle can bridge
        // an unmeasured gap between authored shirt rings.
        private void ConfigureMeasuredBody(VillageResidentPresentation motion)
        {
            var parts = new Dictionary<string, SkinnedMeshRenderer>();
            foreach (SkinnedMeshRenderer renderer in motion.ModelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                parts[renderer.name] = renderer;
            var proxies = new List<BodyProxy>();
            proxies.Add(Fit(new[] { parts["GEO_Head"], parts["GEO_FaceSurface"], parts["HAIR_NapeScalp"] },
                "head", float.NegativeInfinity, float.PositiveInfinity, false));
            proxies.Add(Fit(new[] { parts["GEO_Neck"] }, "neck", float.NegativeInfinity, float.PositiveInfinity, false));
            for (int section = 0; section < 5; section++)
            {
                float center = 1.09f + section * .08f;
                proxies.Add(Fit(new[] { parts["CLO_LongSleeveTop"] }, "chest", center - .061f, center + .061f, true));
            }
            foreach (string side in new[] { "L", "R" })
            {
                SkinnedMeshRenderer sleeve = parts["CLO_Sleeve." + side];
                proxies.Add(Fit(new[] { sleeve }, "chest", float.NegativeInfinity, float.PositiveInfinity, false, "chest"));
                proxies.Add(Fit(new[] { sleeve }, "upper_arm." + side, float.NegativeInfinity, float.PositiveInfinity, false, "upper_arm." + side));
                proxies.Add(Fit(new[] { sleeve, parts["CLO_Cuff." + side] }, "forearm." + side,
                    float.NegativeInfinity, float.PositiveInfinity, false, "forearm." + side));
            }
            body = proxies.ToArray();
            bodySupportValid = null;

            BodyProxy Fit(SkinnedMeshRenderer[] renderers, string referenceBone, float minimum, float maximum, bool joined, string influence = null)
            {
                var bones = new List<Transform>(); var samples = new List<BodySample>();
                Bounds bounds = new Bounds(); bool started = false;
                foreach (SkinnedMeshRenderer renderer in renderers)
                {
                    Mesh mesh = renderer.sharedMesh;
                    Vector3[] vertices = mesh.vertices;
                    BoneWeight[] weights = mesh.boneWeights;
                    Matrix4x4[] bindposes = mesh.bindposes;
                    Transform[] sourceBones = renderer.bones;
                    Matrix4x4 toActor = transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                    var selected = new HashSet<int>();
                    int[] triangles = mesh.triangles;
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                        if (influence != null && !Influenced(a) && !Influenced(b) && !Influenced(c)) continue;
                        float ya = toActor.MultiplyPoint3x4(vertices[a]).y, yb = toActor.MultiplyPoint3x4(vertices[b]).y,
                            yc = toActor.MultiplyPoint3x4(vertices[c]).y;
                        if (Mathf.Max(ya, Mathf.Max(yb, yc)) < minimum || Mathf.Min(ya, Mathf.Min(yb, yc)) > maximum) continue;
                        selected.Add(a); selected.Add(b); selected.Add(c);
                    }
                    bool Influenced(int index)
                    {
                        BoneWeight weight = weights[index];
                        return (weight.weight0 > .00001f && sourceBones[weight.boneIndex0].name == influence) ||
                            (weight.weight1 > .00001f && sourceBones[weight.boneIndex1].name == influence) ||
                            (weight.weight2 > .00001f && sourceBones[weight.boneIndex2].name == influence) ||
                            (weight.weight3 > .00001f && sourceBones[weight.boneIndex3].name == influence);
                    }
                    foreach (int vertex in selected)
                    {
                        Vector3 actorPoint = toActor.MultiplyPoint3x4(vertices[vertex]);
                        if (!started) { bounds = new Bounds(actorPoint, Vector3.zero); started = true; }
                        else bounds.Encapsulate(actorPoint);
                        BoneWeight weight = weights[vertex];
                        int Bone(int source, float amount)
                        {
                            if (amount <= 0f) return 0;
                            int index = bones.IndexOf(sourceBones[source]);
                            if (index < 0) { index = bones.Count; bones.Add(sourceBones[source]); }
                            return index;
                        }
                        Vector3 Local(int source, float amount) => amount > 0f ? bindposes[source].MultiplyPoint3x4(vertices[vertex]) : Vector3.zero;
                        samples.Add(new BodySample {
                            A = Local(weight.boneIndex0, weight.weight0), B = Local(weight.boneIndex1, weight.weight1),
                            C = Local(weight.boneIndex2, weight.weight2), D = Local(weight.boneIndex3, weight.weight3),
                            BoneA = Bone(weight.boneIndex0, weight.weight0), BoneB = Bone(weight.boneIndex1, weight.weight1),
                            BoneC = Bone(weight.boneIndex2, weight.weight2), BoneD = Bone(weight.boneIndex3, weight.weight3),
                            Weights = new Vector4(weight.weight0, weight.weight1, weight.weight2, weight.weight3)
                        });
                    }
                }
                if (!started) throw new InvalidOperationException("Empty measured hair/body contact section.");
                Transform bone = CityPedestrianHandProps.FindSocket(motion.ModelRoot, referenceBone);
                Vector3 radii = bounds.extents + Vector3.one * BodyMeasurementPadding;
                Matrix4x4 world = transform.localToWorldMatrix * Matrix4x4.TRS(bounds.center, Quaternion.identity, radii);
                var planes = new List<BodyPlane>(BodyPlaneCount);
                for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                for (int z = -1; z <= 1; z++)
                    if (x != 0 || y != 0 || z != 0) planes.Add(new BodyPlane { Normal = new Vector3(x, y, z).normalized });
                return new BodyProxy { Name = renderers[0].name + "/" + referenceBone, Bone = bone,
                    UnitToBone = bone.worldToLocalMatrix * world, JoinedSection = joined,
                    SampleBones = bones.ToArray(), Samples = samples.ToArray(), Planes = planes.ToArray() };
            }
        }

        private void EnsureBodyState()
        {
            if (bodyToWorld != null && bodyToWorld.Length == body.Length && bodySupportValid != null) return;
            bodyToWorld = new Matrix4x4[body.Length]; worldToBody = new Matrix4x4[body.Length];
            bodyBounds = new Bounds[body.Length]; bodyMinimumRadii = new float[body.Length];
            bodyPlaneNormals = new Vector3[body.Length * BodyPlaneCount]; bodyPlaneScales = new float[bodyPlaneNormals.Length];
            bodyPlaneSupport = new float[bodyPlaneNormals.Length];
            bodyPlaneOffsets = new float[bodyPlaneNormals.Length];
            bodySampleMatrices = new Matrix4x4[body.Length][]; bodyUnitPoints = new Vector3[body.Length][];
            bodyPreviousWorldToUnit = new Matrix4x4[body.Length];
            bodyUnitMinimum = new Vector3[body.Length]; bodyUnitMaximum = new Vector3[body.Length];
            bodyMinimum = new Vector3[body.Length]; bodyMaximum = new Vector3[body.Length];
            bodySupportValid = new bool[body.Length]; bodyRigidSections = new bool[body.Length];
            for (int i = 0; i < body.Length; i++)
            {
                bodySampleMatrices[i] = new Matrix4x4[body[i].SampleBones.Length];
                bodyUnitPoints[i] = new Vector3[body[i].Samples.Length];
                bodyRigidSections[i] = body[i].SampleBones.Length == 1 && body[i].SampleBones[0] == body[i].Bone;
            }
        }

        private void UpdateBodyPlanes()
        {
            float padding = BodyMeasurementPadding * transform.lossyScale.x;
            // Keep the world plane calculation near the character rather
            // than subtracting large world coordinates for every contact.
            bodyPlaneOrigin = transform.position;
            for (int volume = 0; volume < body.Length; volume++)
            {
                Matrix4x4[] matrices = bodySampleMatrices[volume];
                bool rebuild = !bodySupportValid[volume];
                if (rebuild || !bodyRigidSections[volume])
                {
                    // A rigid section's unit geometry is invariant under its
                    // reference bone. Mixed sections reuse a fit only when
                    // every contributing matrix is exactly unchanged.
                    rebuild |= !bodyPreviousWorldToUnit[volume].Equals(worldToBody[volume]);
                    for (int i = 0; i < matrices.Length; i++)
                    {
                        Matrix4x4 current = body[volume].SampleBones[i].localToWorldMatrix;
                        rebuild |= !matrices[i].Equals(current);
                        matrices[i] = current;
                    }
                }
                if (rebuild) RebuildBodySupport(volume, matrices);
                float minimumRadius = Mathf.Min(bodyToWorld[volume].MultiplyVector(Vector3.right).magnitude,
                    Mathf.Min(bodyToWorld[volume].MultiplyVector(Vector3.up).magnitude, bodyToWorld[volume].MultiplyVector(Vector3.forward).magnitude));
                bodyMinimumRadii[volume] = minimumRadius;
                Vector3 unitCenter = (bodyUnitMinimum[volume] + bodyUnitMaximum[volume]) * .5f;
                Vector3 unitExtents = (bodyUnitMaximum[volume] - bodyUnitMinimum[volume]) * .5f +
                    Vector3.one * ((padding + SurfaceClearance) / Mathf.Max(.00001f, minimumRadius));
                Vector3 extents = Abs(bodyToWorld[volume].MultiplyVector(Vector3.right * unitExtents.x)) +
                    Abs(bodyToWorld[volume].MultiplyVector(Vector3.up * unitExtents.y)) +
                    Abs(bodyToWorld[volume].MultiplyVector(Vector3.forward * unitExtents.z));
                Vector3 center = bodyToWorld[volume].MultiplyPoint3x4(unitCenter);
                bodyBounds[volume] = new Bounds(center, extents * 2f);
                bodyMinimum[volume] = center - extents; bodyMaximum[volume] = center + extents;
                Matrix4x4 normalMatrix = worldToBody[volume].transpose;
                Vector3 unitOrigin = worldToBody[volume].MultiplyPoint3x4(bodyPlaneOrigin);
                for (int plane = 0; plane < BodyPlaneCount; plane++)
                {
                    int index = volume * BodyPlaneCount + plane;
                    BodyPlane surface = body[volume].Planes[plane];
                    Vector3 normal = normalMatrix.MultiplyVector(surface.Normal);
                    float scale = 1f / Mathf.Max(.00001f, normal.magnitude);
                    bodyPlaneScales[index] = scale; bodyPlaneNormals[index] = normal * scale;
                    surface.Distance = bodyPlaneSupport[index] + padding / scale;
                    body[volume].Planes[plane] = surface;
                    bodyPlaneOffsets[index] = (Vector3.Dot(surface.Normal, unitOrigin) - surface.Distance) * scale;
                }
            }
        }

        private void RebuildBodySupport(int volume, Matrix4x4[] matrices)
        {
            Vector3[] points = bodyUnitPoints[volume];
            Vector3 minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            for (int i = 0; i < points.Length; i++)
            {
                BodySample point = body[volume].Samples[i];
                Vector3 world = Vector3.zero;
                if (point.Weights.x > 0f) world += matrices[point.BoneA].MultiplyPoint3x4(point.A) * point.Weights.x;
                if (point.Weights.y > 0f) world += matrices[point.BoneB].MultiplyPoint3x4(point.B) * point.Weights.y;
                if (point.Weights.z > 0f) world += matrices[point.BoneC].MultiplyPoint3x4(point.C) * point.Weights.z;
                if (point.Weights.w > 0f) world += matrices[point.BoneD].MultiplyPoint3x4(point.D) * point.Weights.w;
                Vector3 unit = worldToBody[volume].MultiplyPoint3x4(world);
                points[i] = unit; minimum = Vector3.Min(minimum, unit); maximum = Vector3.Max(maximum, unit);
            }
            bodyUnitMinimum[volume] = minimum; bodyUnitMaximum[volume] = maximum;
            for (int plane = 0; plane < BodyPlaneCount; plane++)
            {
                Vector3 normal = body[volume].Planes[plane].Normal;
                float distance = float.NegativeInfinity;
                foreach (Vector3 point in points) distance = Mathf.Max(distance, Vector3.Dot(normal, point));
                bodyPlaneSupport[volume * BodyPlaneCount + plane] = distance;
            }
            bodyPreviousWorldToUnit[volume] = worldToBody[volume]; bodySupportValid[volume] = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ContainsBodyBounds(Vector3 point, int volume) => ContainsBounds(point, bodyMinimum[volume], bodyMaximum[volume]);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IntersectsBodyBounds(Vector3 minimum, Vector3 maximum, int volume) =>
            bodyMinimum[volume].x <= maximum.x && bodyMaximum[volume].x >= minimum.x &&
            bodyMinimum[volume].y <= maximum.y && bodyMaximum[volume].y >= minimum.y &&
            bodyMinimum[volume].z <= maximum.z && bodyMaximum[volume].z >= minimum.z;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float PlaneDistance(Vector3 relative, int index) =>
            Vector3.Dot(bodyPlaneNormals[index], relative) + bodyPlaneOffsets[index];

        private bool InsideBodyVolume(Vector3 point, int volume, float clearance)
        {
            if (!ContainsBodyBounds(point, volume)) return false;
            Vector3 relative = point - bodyPlaneOrigin;
            for (int plane = 0; plane < BodyPlaneCount; plane++)
            {
                float distance = PlaneDistance(relative, volume * BodyPlaneCount + plane);
                if (distance >= clearance) return false;
            }
            return true;
        }

        private Vector3 ResolveBodyVolume(Vector3 point, int volume, float clearance)
        {
            if (!ContainsBodyBounds(point, volume)) return point;
            Vector3 relative = point - bodyPlaneOrigin;
            float closest = float.NegativeInfinity; int best = -1;
            for (int plane = 0; plane < BodyPlaneCount; plane++)
            {
                BodyPlane surface = body[volume].Planes[plane];
                float distance = PlaneDistance(relative, volume * BodyPlaneCount + plane);
                if (distance >= clearance) return point;
                if (body[volume].JoinedSection && Mathf.Abs(surface.Normal.y) > .95f) continue;
                if (distance > closest) { closest = distance; best = plane; }
            }
            return best < 0 ? point : point + bodyPlaneNormals[volume * BodyPlaneCount + best] * (clearance - closest);
        }

        private Vector3 ResolveBodyVolumeCached(Vector3 point, Vector3 relative, int volume, float clearance,
            byte[] separatingPlanes, int pair)
        {
            int first = volume * BodyPlaneCount, remembered = separatingPlanes[pair] - 1;
            bool rememberedValid = (uint)remembered < BodyPlaneCount;
            float rememberedDistance = rememberedValid ? PlaneDistance(relative, first + remembered) : 0f;
            // The cache stores a candidate plane, never a previous result.
            // Recheck it against the current pose and the current point.
            if (rememberedValid && rememberedDistance >= clearance) return point;
            if (!ContainsBodyBounds(point, volume)) return point;
            float closest = float.NegativeInfinity; int best = -1;
            for (int plane = 0; plane < BodyPlaneCount; plane++)
            {
                float distance = rememberedValid && plane == remembered
                    ? rememberedDistance : PlaneDistance(relative, first + plane);
                if (distance >= clearance)
                {
                    separatingPlanes[pair] = (byte)(plane + 1);
                    return point;
                }
                if (body[volume].JoinedSection && Mathf.Abs(body[volume].Planes[plane].Normal.y) > .95f) continue;
                // Preserve the ordinary loop order, including its tie break,
                // when the point really needs the full contact projection.
                if (distance > closest) { closest = distance; best = plane; }
            }
            separatingPlanes[pair] = 0;
            return best < 0 ? point : point + bodyPlaneNormals[first + best] * (clearance - closest);
        }

        private bool BodyBoundsSeparated(Vector3 minimumRelative, Vector3 maximumRelative, int volume,
            float clearance, byte[] separatingPlanes, int pair)
        {
            int first = volume * BodyPlaneCount, remembered = separatingPlanes[pair] - 1;
            bool rememberedValid = (uint)remembered < BodyPlaneCount;
            Vector3 magnitude = Vector3.Max(Abs(minimumRelative), Abs(maximumRelative));
            if (rememberedValid && BodyPlaneSeparatesBounds(minimumRelative, maximumRelative, magnitude, first + remembered, clearance))
                return true;
            for (int plane = 0; plane < BodyPlaneCount; plane++)
            {
                if (rememberedValid && plane == remembered) continue;
                if (!BodyPlaneSeparatesBounds(minimumRelative, maximumRelative, magnitude, first + plane, clearance)) continue;
                separatingPlanes[pair] = (byte)(plane + 1);
                return true;
            }
            separatingPlanes[pair] = 0;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool BodyPlaneSeparatesBounds(Vector3 minimum, Vector3 maximum, Vector3 magnitude, int index, float clearance)
        {
            Vector3 normal = bodyPlaneNormals[index];
            // A linear function reaches its AABB minimum at this exact corner.
            // If that minimum is outside, every vertex and edge in the group is.
            Vector3 corner = new Vector3(normal.x < 0f ? maximum.x : minimum.x,
                normal.y < 0f ? maximum.y : minimum.y, normal.z < 0f ? maximum.z : minimum.z);
            float minimumDistance = PlaneDistance(corner, index);
            float termMagnitude = Vector3.Dot(Abs(normal), magnitude) + Mathf.Abs(bodyPlaneOffsets[index]) + Mathf.Abs(clearance);
            // Broad phase may retain a boundary group; it must never reject
            // one due to rounding of the three products and their sum.
            float roundoff = .00000095367432f * termMagnitude;
            return minimumDistance >= clearance + roundoff;
        }
    }
}

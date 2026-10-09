using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Consumes authored closed tissue cells on the production skin. Cells
    /// change volume in place; they never acquire a transform or rigid body.</summary>
    internal sealed class CombatTorsoDamageSurface : IDisposable
    {
        private sealed class Cell
        {
            internal Vector3 Centre;
            internal Vector3 PosedCentre;
            internal uint PosedVersion;
            internal BoneWeight Weight;
            internal float Loss;
            internal float Shrink;
        }

        private readonly SkinnedMeshRenderer skin;
        private readonly Mesh template;
        private readonly bool flesh;
        private readonly Cell[] cells;
        private readonly int[] cellForVertex;
        private readonly Vector3[] inner;
        private readonly float[] outer;
        private readonly int[][] triangles;
        private readonly List<int>[] remaining;
        private readonly Matrix4x4[] poses;
        private readonly Matrix4x4[] bind;
        private readonly Transform[] bones;
        private readonly int[] usedBones;
        private readonly Matrix4x4[] worldPoses;
        private readonly Bounds[] influenceBounds;
        private readonly float minimumWeight, maximumWeight;
        private readonly List<Vector3> baseline = new List<Vector3>();
        private readonly List<Vector3> baselineNormals = new List<Vector3>();
        private readonly List<Vector4> baselineTangents = new List<Vector4>();
        private readonly Vector3[] vertices;
        private Bounds worldBounds;
        private int visibleCellCount;
        private bool poseCaptured;
        private uint poseVersion;
        private Mesh input, owned;
        private uint inputVersion = uint.MaxValue, damageVersion, renderedDamageVersion = uint.MaxValue;

        internal uint GeometryVersion { get; private set; }
        internal bool HasDamage { get; private set; }
        internal bool HasGeometry { get; private set; }
        internal bool IsRenderingOwnedMesh => owned != null && skin != null && skin.sharedMesh == owned;
        internal int DepletedCellCount { get; private set; }
        internal int CellCount => cells.Length;
        internal int CellTests { get; private set; }
        internal int PoseCaptures { get; private set; }
        internal int SurfaceRejects { get; private set; }
        internal int TopologyBuilds { get; private set; }

        internal CombatTorsoDamageSurface(SkinnedMeshRenderer skin, bool flesh)
        {
            this.skin = skin; this.flesh = flesh; template = input = skin.sharedMesh;
            var labels = new List<Vector2>(); var targets = new List<Vector4>(); var centres = new List<Vector4>();
            template.GetUVs(1, labels); template.GetUVs(4, targets); template.GetUVs(6, centres);
            if (labels.Count != template.vertexCount || targets.Count != labels.Count || centres.Count != labels.Count)
                throw new InvalidOperationException("Torso surface lacks authored erosion cells: " + skin.name);
            int count = 0;
            foreach (Vector2 label in labels) count = Mathf.Max(count, Mathf.RoundToInt(label.x) + 1);
            cells = new Cell[count]; cellForVertex = new int[labels.Count];
            inner = new Vector3[labels.Count]; outer = new float[labels.Count]; vertices = new Vector3[labels.Count];
            baseline.Capacity = labels.Count;
            if (!flesh) { baselineNormals.Capacity = labels.Count; baselineTangents.Capacity = labels.Count; }
            BoneWeight[] weights = template.boneWeights;
            for (int i = 0; i < labels.Count; i++)
            {
                int cell = cellForVertex[i] = Mathf.RoundToInt(labels[i].x);
                if (cell < 0 || cell >= count) throw new InvalidOperationException("Invalid torso cell: " + skin.name);
                cells[cell] ??= new Cell { Centre = centres[i], Weight = weights[i] };
                inner[i] = targets[i]; outer[i] = labels[i].y;
            }
            foreach (Cell cell in cells)
                if (cell == null) throw new InvalidOperationException("Noncontiguous torso cells: " + skin.name);
            triangles = new int[template.subMeshCount][]; remaining = new List<int>[triangles.Length];
            for (int sub = 0; sub < triangles.Length; sub++)
            {
                triangles[sub] = template.GetTriangles(sub); remaining[sub] = new List<int>(triangles[sub].Length);
                for (int i = 0; i < triangles[sub].Length; i += 3)
                    if (cellForVertex[triangles[sub][i]] != cellForVertex[triangles[sub][i + 1]] ||
                        cellForVertex[triangles[sub][i]] != cellForVertex[triangles[sub][i + 2]])
                        throw new InvalidOperationException("Torso face crosses authored cells: " + skin.name);
            }
            bones = skin.bones; bind = template.bindposes;
            poses = new Matrix4x4[bones.Length]; worldPoses = new Matrix4x4[bones.Length];
            influenceBounds = new Bounds[bones.Length];
            var measured = new bool[bones.Length];
            minimumWeight = float.PositiveInfinity;
            foreach (Cell cell in cells)
            {
                BoneWeight w = cell.Weight;
                float total = w.weight0 + w.weight1 + w.weight2 + w.weight3;
                minimumWeight = Mathf.Min(minimumWeight, total); maximumWeight = Mathf.Max(maximumWeight, total);
                Include(w.boneIndex0, w.weight0); Include(w.boneIndex1, w.weight1);
                Include(w.boneIndex2, w.weight2); Include(w.boneIndex3, w.weight3);
                void Include(int bone, float weight)
                {
                    if (weight <= 0f) return;
                    if (!measured[bone]) { influenceBounds[bone] = new Bounds(cell.Centre, Vector3.zero); measured[bone] = true; }
                    else influenceBounds[bone].Encapsulate(cell.Centre);
                }
            }
            var used = new List<int>();
            for (int i = 0; i < measured.Length; i++) if (measured[i]) used.Add(i);
            usedBones = used.ToArray(); HasGeometry = !flesh;
        }

        private void CapturePose()
        {
            bool changed = !poseCaptured;
            foreach (int bone in usedBones)
            {
                Matrix4x4 world = bones[bone].localToWorldMatrix;
                if (poseCaptured && worldPoses[bone].Equals(world)) continue;
                worldPoses[bone] = world; poses[bone] = world * bind[bone]; changed = true;
            }
            if (!changed) return;
            unchecked { poseVersion++; }
            if (poseVersion == 0)
            {
                foreach (Cell cell in cells) cell.PosedVersion = 0;
                poseVersion = 1;
            }
            bool measured = false;
            foreach (int bone in usedBones)
            {
                Matrix4x4 matrix = poses[bone]; Bounds local = influenceBounds[bone]; Vector3 e = local.extents;
                var candidate = new Bounds(matrix.MultiplyPoint3x4(local.center), new Vector3(
                    Mathf.Abs(matrix.m00) * e.x + Mathf.Abs(matrix.m01) * e.y + Mathf.Abs(matrix.m02) * e.z,
                    Mathf.Abs(matrix.m10) * e.x + Mathf.Abs(matrix.m11) * e.y + Mathf.Abs(matrix.m12) * e.z,
                    Mathf.Abs(matrix.m20) * e.x + Mathf.Abs(matrix.m21) * e.y + Mathf.Abs(matrix.m22) * e.z) * 2f);
                if (!measured) { worldBounds = candidate; measured = true; }
                else { worldBounds.Encapsulate(candidate.min); worldBounds.Encapsulate(candidate.max); }
            }
            if (measured)
            {
                // Every weighted centre is a convex combination of these bounds.
                // Include normalization error and a small float-rounding margin.
                Vector3 min = worldBounds.min, max = worldBounds.max;
                worldBounds.SetMinMax(Vector3.Min(min * minimumWeight, min * maximumWeight),
                    Vector3.Max(max * minimumWeight, max * maximumWeight));
                worldBounds.Expand(.00002f);
            }
            else worldBounds = default;
            poseCaptured = true; PoseCaptures++;
        }

        private Vector3 WorldCentre(Cell cell)
        {
            if (cell.PosedVersion == poseVersion) return cell.PosedCentre;
            BoneWeight w = cell.Weight;
            Vector3 value = poses[w.boneIndex0].MultiplyPoint3x4(cell.Centre) * w.weight0;
            if (w.weight1 > 0f) value += poses[w.boneIndex1].MultiplyPoint3x4(cell.Centre) * w.weight1;
            if (w.weight2 > 0f) value += poses[w.boneIndex2].MultiplyPoint3x4(cell.Centre) * w.weight2;
            if (w.weight3 > 0f) value += poses[w.boneIndex3].MultiplyPoint3x4(cell.Centre) * w.weight3;
            cell.PosedVersion = poseVersion;
            return cell.PosedCentre = value;
        }

        internal bool ClosestSurfacePoint(Vector3 point, ref float distance, ref Vector3 closest)
        {
            CapturePose(); bool found = false;
            if (worldBounds.SqrDistance(point) > distance) { SurfaceRejects++; return false; }
            foreach (Cell cell in cells)
            {
                CellTests++;
                Vector3 candidate = WorldCentre(cell); float squared = (candidate - point).sqrMagnitude;
                if (squared >= distance) continue;
                distance = squared; closest = candidate; found = true;
            }
            return found;
        }

        internal bool Apply(Vector3 point, float radius, float amount)
        {
            if (amount <= 0f || radius <= 0f) return false;
            CapturePose(); bool changed = false;
            float radiusSquared = radius * radius;
            if (worldBounds.SqrDistance(point) > radiusSquared) { SurfaceRejects++; return false; }
            float scale = Mathf.Max(.001f, skin.transform.lossyScale.magnitude / Mathf.Sqrt(3f));
            foreach (Cell cell in cells)
            {
                if (cell.Loss >= 1f) continue;
                CellTests++;
                float squared = (WorldCentre(cell) - point).sqrMagnitude;
                if (squared > radiusSquared) continue;
                float normalized = Mathf.Sqrt(squared) / radius;
                if (normalized >= 1f) continue;
                // A broad centre and a soft boundary form one widening wound.
                // Rest-space variation keeps the edge irregular and stable across reload/pose.
                float edge = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.3f, 1f, normalized));
                Vector3 c = cell.Centre * scale;
                float irregular = .92f + .08f * Mathf.Sin(c.x * 91f + c.y * 73f + c.z * 117f);
                float next = Mathf.Min(1f, cell.Loss + amount * edge * irregular);
                if (next <= cell.Loss) continue;
                if (cell.Loss < 1f && next >= 1f) DepletedCellCount++;
                if (flesh)
                {
                    bool wasVisible = cell.Loss >= .25f && cell.Loss < 1f;
                    bool visible = next >= .25f && next < 1f;
                    if (wasVisible != visible) visibleCellCount += visible ? 1 : -1;
                }
                cell.Loss = next; changed = true;
            }
            if (!changed) return false;
            HasDamage = true; unchecked { damageVersion++; }
            return true;
        }

        // Source cloth owns its mesh first; erosion composes over that same snapshot.
        internal void PrepareSource()
        { if (skin != null && owned != null && skin.sharedMesh == owned) skin.sharedMesh = input; }

        // Exterior surfaces will be drawn as soon as damage begins. Prepare their
        // private mesh at loading time without changing the live renderer binding.
        internal void PrepareExteriorMesh()
        { if (!flesh) EnsureOwnedMesh(); }

        private void EnsureOwnedMesh()
        {
            if (owned != null) return;
            owned = UnityEngine.Object.Instantiate(template); owned.name = template.name + " Local Erosion";
            owned.hideFlags = HideFlags.HideAndDontSave; owned.MarkDynamic();
        }

        internal void Refresh(uint sourceVersion)
        {
            Mesh current = skin.sharedMesh;
            if (current == owned) current = input;
            if (!HasDamage) return;
            bool topologyChanged = renderedDamageVersion != damageVersion;
            bool changed = current != input || inputVersion != sourceVersion || topologyChanged;
            input = current;
            if (flesh && visibleCellCount == 0)
            {
                // Weak distant contacts still consume tissue, but no flesh is
                // visible yet. Keep the damage without cloning/uploading an
                // invisible mesh; fully consumed tissue keeps its bone exposure.
                HasGeometry = false;
                if (changed) unchecked { GeometryVersion++; }
                inputVersion = sourceVersion; renderedDamageVersion = damageVersion;
                return;
            }
            if (owned == null)
            {
                EnsureOwnedMesh(); changed = topologyChanged = true;
            }
            if (changed)
            {
                input.GetVertices(baseline);
                foreach (Cell cell in cells)
                    cell.Shrink = flesh ? Mathf.Pow(Mathf.Clamp01((cell.Loss - .25f) / .75f), .8f) * .98f :
                        Mathf.Clamp01(cell.Loss / .25f) * .12f;
                for (int i = 0; i < vertices.Length; i++)
                {
                    float shrink = cells[cellForVertex[i]].Shrink;
                    if (flesh) shrink *= outer[i];
                    vertices[i] = Vector3.Lerp(baseline[i], inner[i], shrink);
                }
                owned.vertices = vertices;
                if (topologyChanged)
                {
                    TopologyBuilds++; HasGeometry = false;
                    for (int sub = 0; sub < triangles.Length; sub++)
                    {
                        List<int> kept = remaining[sub]; kept.Clear(); int[] authored = triangles[sub];
                        for (int i = 0; i < authored.Length; i += 3)
                        {
                            float loss = cells[cellForVertex[authored[i]]].Loss;
                            bool visible = flesh ? loss >= .25f && loss < 1f : loss < .25f;
                            if (!visible) continue;
                            kept.Add(authored[i]); kept.Add(authored[i + 1]); kept.Add(authored[i + 2]);
                        }
                        owned.SetTriangles(kept, sub, false); HasGeometry |= kept.Count > 0;
                    }
                }
                if (flesh) owned.RecalculateNormals();
                else
                {
                    input.GetNormals(baselineNormals); owned.SetNormals(baselineNormals);
                    input.GetTangents(baselineTangents);
                    if (baselineTangents.Count == vertices.Length) owned.SetTangents(baselineTangents);
                }
                owned.RecalculateBounds();
                inputVersion = sourceVersion; renderedDamageVersion = damageVersion; unchecked { GeometryVersion++; }
            }
            if (skin.sharedMesh != owned) skin.sharedMesh = owned;
            skin.localBounds = input.bounds;
        }

        internal void Reset()
        {
            foreach (Cell cell in cells) cell.Loss = 0f;
            PrepareSource(); HasDamage = false; HasGeometry = !flesh; DepletedCellCount = 0;
            visibleCellCount = 0;
            renderedDamageVersion = uint.MaxValue; unchecked { damageVersion++; GeometryVersion++; }
        }

        public void Dispose()
        {
            PrepareSource();
            if (owned != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(owned);
                else UnityEngine.Object.DestroyImmediate(owned);
            }
            owned = null;
        }
    }
}

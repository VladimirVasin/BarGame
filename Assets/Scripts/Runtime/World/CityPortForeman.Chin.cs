using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityPortForeman
    {
        private static readonly string[] ChinPartNames =
            { "GEO_ForemanChin", "GEO_ForemanJowl.L", "GEO_ForemanJowl.R" };
        private static readonly string[] ChinShapeNames =
            { "ChinDrop", "ChinLift", "ChinLeft", "ChinRight", "ChinForward", "ChinBack" };
        private readonly ForemanChinDynamics chinDynamics = new ForemanChinDynamics();
        private SkinnedMeshRenderer[] chinRenderers = Array.Empty<SkinnedMeshRenderer>();
        private int[,] chinShapes;
        private Quaternion chinHeadReference = Quaternion.identity;
        private double chinReferenceSeconds = double.NaN;

        /// <summary>Normalized left/down/forward displacement, separate from the rigid head pose.</summary>
        public Vector3 ChinDisplacement
        {
            get
            {
                ForemanChinDisplacement value = chinDynamics.Current;
                return new Vector3((float)value.X, (float)value.Y, (float)value.Z);
            }
        }
        public bool ChinMotionActive => chinDynamics.IsMoving;

        private void InitializeChin()
        {
            var found = new SkinnedMeshRenderer[ChinPartNames.Length];
            var shapes = new int[ChinPartNames.Length, ChinShapeNames.Length];
            for (int part = 0; part < found.Length; part++)
            {
                foreach (Renderer renderer in renderers)
                    if (renderer != null && renderer.name == ChinPartNames[part])
                    { found[part] = renderer as SkinnedMeshRenderer; break; }
                if (found[part] == null || found[part].sharedMesh == null)
                    throw new InvalidOperationException("Foreman chin requires its authored skinned part " + ChinPartNames[part] + ".");
                for (int shape = 0; shape < ChinShapeNames.Length; shape++)
                {
                    shapes[part, shape] = ResolveChinShape(found[part].sharedMesh, ChinShapeNames[shape]);
                    if (shapes[part, shape] < 0)
                        throw new InvalidOperationException("Foreman chin part " + ChinPartNames[part] +
                            " lacks blendshape " + ChinShapeNames[shape] + ". Rebuild the authored foreman assets.");
                }
            }
            chinRenderers = found; chinShapes = shapes;
            ResetChin();
        }

        private static int ResolveChinShape(Mesh mesh, string name)
        {
            int exact = mesh.GetBlendShapeIndex(name);
            if (exact >= 0) return exact;
            int match = -1;
            for (int index = 0; index < mesh.blendShapeCount; index++)
            {
                if (!mesh.GetBlendShapeName(index).EndsWith("." + name, StringComparison.Ordinal)) continue;
                if (match >= 0)
                    throw new InvalidOperationException("Ambiguous foreman chin blendshape " + name + " on " + mesh.name + ".");
                match = index;
            }
            return match;
        }

        private void UpdateChin(double lifeSeconds)
        {
            if (Head == null || chinRenderers.Length != ChinPartNames.Length ||
                double.IsNaN(lifeSeconds) || double.IsInfinity(lifeSeconds) || lifeSeconds == chinReferenceSeconds) return;
            Quaternion currentHead = Quaternion.Inverse(transform.rotation) * Head.rotation;
            double step = lifeSeconds - chinReferenceSeconds;
            if (double.IsNaN(chinReferenceSeconds) || step < 0d || step > ForemanChinDynamics.MaximumStepSeconds)
                chinHeadReference = currentHead;
            chinReferenceSeconds = lifeSeconds;
            // The imported bone's rest axes need not face forward and may sit at an Euler
            // singularity. Remove that orientation first; the remaining small turn is measured
            // in actor axes. Initial sampling and seeks rebase without a false head impulse.
            Vector3 headAngles = (currentHead * Quaternion.Inverse(chinHeadReference)).eulerAngles;
            // A carrot bite can change the sprite mouth but cannot energize this speech motion.
            // After ownership ends the physical velocity remains, then naturally settles.
            chinDynamics.Advance(lifeSeconds, CurrentSpeechFace.Mouth, speechFaceOwner != null || IsSpeaking,
                headAngles.x, headAngles.y);
            ApplyChinWeights();
        }

        private void ResetChin()
        {
            chinDynamics.Reset();
            chinHeadReference = Quaternion.identity;
            chinReferenceSeconds = double.NaN;
            ApplyChinWeights();
        }

        private void ApplyChinWeights()
        {
            if (chinShapes == null) return;
            ForemanChinDisplacement displacement = chinDynamics.Current;
            for (int part = 0; part < chinRenderers.Length; part++)
            {
                SkinnedMeshRenderer renderer = chinRenderers[part];
                if (renderer == null) continue;
                Set(0, displacement.Y); Set(1, -displacement.Y);
                Set(2, displacement.X); Set(3, -displacement.X);
                Set(4, displacement.Z); Set(5, -displacement.Z);
                void Set(int shape, double amount) => renderer.SetBlendShapeWeight(chinShapes[part, shape],
                    Mathf.Clamp01((float)amount) * 100f);
            }
        }
    }
}

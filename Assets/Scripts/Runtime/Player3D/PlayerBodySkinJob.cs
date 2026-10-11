using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace BarPromenade
{
    // Pose the immutable corrected source in the contact bone's coordinates.
    // Keep the authored weight order and the same affine point operation.
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true, DisableSafetyChecks = true)]
    internal struct PlayerBodySkinJob : IJob
    {
        [ReadOnly] public NativeArray<Vector3> SourcePoints;
        [ReadOnly] public NativeArray<BoneWeight> Weights;
        [ReadOnly] public NativeArray<Matrix4x4> Matrices;
        public NativeArray<Vector3> PosedVertices;

        [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true, DisableSafetyChecks = true)]
        public static void RunSynchronous(ref PlayerBodySkinJob job) => job.Execute();

        public void Execute()
        {
            for (int i = 0; i < SourcePoints.Length; i++)
            {
                Vector3 point = SourcePoints[i];
                BoneWeight weight = Weights[i];
                Vector3 result = Matrices[weight.boneIndex0].MultiplyPoint3x4(point) * weight.weight0;
                if (weight.weight1 > 0f) result += Matrices[weight.boneIndex1].MultiplyPoint3x4(point) * weight.weight1;
                if (weight.weight2 > 0f) result += Matrices[weight.boneIndex2].MultiplyPoint3x4(point) * weight.weight2;
                if (weight.weight3 > 0f) result += Matrices[weight.boneIndex3].MultiplyPoint3x4(point) * weight.weight3;
                PosedVertices[i] = result;
            }
        }
    }
}

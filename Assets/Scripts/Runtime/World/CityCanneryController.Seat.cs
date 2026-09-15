using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityCanneryController
    {
        private string driverSeatTrousers;
        private bool driverSeatFitReady;
        private Vector2 driverSeatFit, driverReverseSeatFit;
        private string driverBenchTrousers;
        private bool driverBenchFitReady;
        private Vector2 driverBenchFit;
        public Vector2 DriverSeatFit => driverSeatFit;
        public Vector2 DriverReverseSeatFit => driverReverseSeatFit;
        public Vector2 DriverBenchFit => driverBenchFit;

        // The truck anchor is the cushion surface, not the pelvis joint.
        // Resolve the visible garment against its flat top once per trousers
        // choice. Both driving poses keep their authored wheel/pedal targets.
        private void EnsureDriverSeatFit()
        {
            VillageResidentPresentation actor = workers[4];
            string trousers = actor.GetComponent<NpcWardrobe>().GetEquippedItem("trousers") ?? "body";
            if (driverSeatFitReady && driverSeatTrousers == trousers) return;
            string part = trousers == "body" ? "GEO_LegsUpper" :
                "CLO_Trousers_" + trousers.Substring("trousers.".Length) + "Upper";
            var skin = Require(actor.ModelRoot, part).GetComponent<SkinnedMeshRenderer>();
            var surface = new DriverSeatSurface(skin);
            var joints = new[] { driverPelvis, driverThighs[0], driverShins[0], driverFeet[0],
                driverThighs[1], driverShins[1], driverFeet[1] };
            var positions = new Vector3[joints.Length];
            var rotations = new Quaternion[joints.Length];
            for (int i = 0; i < joints.Length; i++)
            { positions[i] = joints[i].localPosition; rotations[i] = joints[i].localRotation; }
            Vector3 rootPosition = actor.transform.position;
            Quaternion rootRotation = actor.transform.rotation;
            try
            {
                driverSeatFit = Fit(0f);
                driverReverseSeatFit = Fit(1f);
                driverSeatTrousers = trousers;
                driverSeatFitReady = true;
            }
            finally
            {
                actor.transform.SetPositionAndRotation(rootPosition, rootRotation);
                RestoreJoints();
            }

            void RestoreJoints()
            {
                for (int i = 0; i < joints.Length; i++)
                { joints[i].localPosition = positions[i]; joints[i].localRotation = rotations[i]; }
            }
            Vector2 Fit(float lean)
            {
                Vector2 best = default;
                float bestScore = float.PositiveInfinity;
                for (int forwardStep = 0; forwardStep <= 20; forwardStep++)
                for (int liftStep = 0; liftStep <= 16; liftStep++)
                {
                    float forward = forwardStep * .01f, lift = liftStep * .01f;
                    float score = forward * forward + lift * lift;
                    if (score >= bestScore) continue;
                    actor.transform.SetPositionAndRotation(Truck.position, Truck.rotation);
                    RestoreJoints();
                    Vector3 target = driverSeat.position + Truck.up * lift + Truck.forward * forward +
                        (-Truck.right * .12f + Truck.forward * .06f) * lean;
                    actor.transform.position += target - driverPelvis.position;
                    driverPelvis.rotation = Quaternion.AngleAxis(12f * lean, Truck.forward) * driverPelvis.rotation;
                    bool feetMatch = true;
                    for (int i = 0; i < 2; i++)
                    {
                        Vector3 pedal = driverFoot.position + Truck.right * (i == 0 ? -.14f : .14f);
                        LimbTwoBoneIk.Solve(driverThighs[i], driverShins[i], driverFeet[i], pedal,
                            driverFeet[i].rotation, driverThighs[i].position + Truck.forward * .65f,
                            1f, .995f, true);
                        feetMatch &= Vector3.Distance(driverFeet[i].position, pedal) <= .015f;
                    }
                    if (!feetMatch) continue;
                    float contact = surface.Measure(driverSeat.position, Truck.right, Truck.up, Truck.forward);
                    if (contact < -.010f || contact > .015f) continue;
                    bestScore = score; best = new Vector2(forward, lift);
                }
                if (float.IsPositiveInfinity(bestScore))
                    throw new InvalidOperationException("The delivery driver's visible trousers cannot fit the truck seat and pedals.");
                return best;
            }
        }

        private Vector3 DriverSeatTarget(float lean)
        {
            Vector2 fit = Vector2.Lerp(driverSeatFit, driverReverseSeatFit, lean);
            return driverSeat.position + Truck.forward * fit.x + Truck.up * fit.y +
                (-Truck.right * .12f + Truck.forward * .06f) * lean;
        }

        private void EnsureDriverBenchFit()
        {
            VillageResidentPresentation actor = workers[4];
            string trousers = actor.GetComponent<NpcWardrobe>().GetEquippedItem("trousers") ?? "body";
            if (driverBenchFitReady && driverBenchTrousers == trousers) return;
            string part = trousers == "body" ? "GEO_LegsUpper" :
                "CLO_Trousers_" + trousers.Substring("trousers.".Length) + "Upper";
            var surface = new DriverSeatSurface(Require(actor.ModelRoot, part).GetComponent<SkinnedMeshRenderer>());
            actor.Apply(VillageResidentAction.SewingEnter, DriverSitSeconds);
            var joints = new[] { driverPelvis, driverThighs[0], driverShins[0], driverFeet[0],
                driverThighs[1], driverShins[1], driverFeet[1] };
            var positions = new Vector3[joints.Length];
            var rotations = new Quaternion[joints.Length];
            for (int i = 0; i < joints.Length; i++)
            { positions[i] = joints[i].localPosition; rotations[i] = joints[i].localRotation; }
            float bestScore = float.PositiveInfinity;
            for (int forwardStep = -8; forwardStep <= 12; forwardStep++)
            for (int liftStep = 4; liftStep <= 24; liftStep++)
            {
                float forward = forwardStep * .01f, lift = liftStep * .01f;
                float score = forward * forward + (lift - .08f) * (lift - .08f);
                if (score >= bestScore) continue;
                for (int i = 0; i < joints.Length; i++)
                { joints[i].localPosition = positions[i]; joints[i].localRotation = rotations[i]; }
                driverPelvis.position = DriverBenchSeatContact + actor.transform.up * lift + actor.transform.forward * forward;
                bool feetMatch = true;
                for (int i = 0; i < 2; i++)
                {
                    LimbTwoBoneIk.Solve(driverThighs[i], driverShins[i], driverFeet[i], driverPlantedFeet[i],
                        driverPlantedFootRotations[i], driverThighs[i].position + Plan.Right * .65f, 1f, 1f, true);
                    feetMatch &= Vector3.Distance(driverFeet[i].position, driverPlantedFeet[i]) <= .015f;
                }
                if (!feetMatch) continue;
                float contact = surface.Measure(DriverBenchSeatContact, actor.transform.right,
                    actor.transform.up, actor.transform.forward, .20f);
                if (contact < -.010f || contact > .015f) continue;
                bestScore = score; driverBenchFit = new Vector2(forward, lift);
            }
            if (float.IsPositiveInfinity(bestScore))
                throw new InvalidOperationException("The delivery driver's visible trousers cannot fit the bench and planted feet.");
            driverBenchTrousers = trousers;
            driverBenchFitReady = true;
            // The caller resamples the requested enter/exit frame after the
            // endpoint measurement, before applying the cached displacement.
        }

        // Same cached bind-pose skinning as the character contact surfaces:
        // no GPU readback, temporary rendered mesh, or frame-by-frame bake.
        private sealed class DriverSeatSurface
        {
            private readonly Vector3[] vertices, posed;
            private readonly BoneWeight[] weights;
            private readonly Matrix4x4[] bindPoses, matrices;
            private readonly Transform[] bones;
            private readonly int[] triangles;
            private readonly Vector3[] clipA = new Vector3[8], clipB = new Vector3[8];

            public DriverSeatSurface(SkinnedMeshRenderer skin)
            {
                Mesh mesh = skin.sharedMesh;
                vertices = mesh.vertices; weights = mesh.boneWeights; bindPoses = mesh.bindposes;
                bones = skin.bones; triangles = mesh.triangles;
                posed = new Vector3[vertices.Length]; matrices = new Matrix4x4[bones.Length];
            }

            public float Measure(Vector3 origin, Vector3 right, Vector3 up, Vector3 forward, float halfDepth = .28f)
            {
                for (int i = 0; i < bones.Length; i++) matrices[i] = bones[i].localToWorldMatrix * bindPoses[i];
                for (int i = 0; i < vertices.Length; i++)
                {
                    BoneWeight w = weights[i]; Vector3 v = vertices[i];
                    Vector3 world = matrices[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0 +
                        matrices[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1 +
                        matrices[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2 +
                        matrices[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3 - origin;
                    posed[i] = new Vector3(Vector3.Dot(world, right), Vector3.Dot(world, up), Vector3.Dot(world, forward));
                }
                float minimum = float.PositiveInfinity;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    clipA[0] = posed[triangles[i]]; clipA[1] = posed[triangles[i + 1]]; clipA[2] = posed[triangles[i + 2]];
                    int count = Clip(clipA, 3, clipB, 0, 1f, .23f);
                    count = Clip(clipB, count, clipA, 0, -1f, .23f);
                    count = Clip(clipA, count, clipB, 2, 1f, halfDepth);
                    count = Clip(clipB, count, clipA, 2, -1f, halfDepth);
                    for (int p = 0; p < count; p++) minimum = Mathf.Min(minimum, clipA[p].y);
                }
                return minimum;
            }

            private static int Clip(Vector3[] input, int count, Vector3[] output, int axis, float sign, float edge)
            {
                if (count == 0) return 0;
                int written = 0; Vector3 previous = input[count - 1];
                float before = edge - previous[axis] * sign;
                for (int i = 0; i < count; i++)
                {
                    Vector3 current = input[i]; float after = edge - current[axis] * sign;
                    if ((before >= 0f) != (after >= 0f))
                        output[written++] = Vector3.LerpUnclamped(previous, current, before / (before - after));
                    if (after >= 0f) output[written++] = current;
                    previous = current; before = after;
                }
                return written;
            }
        }
    }
}

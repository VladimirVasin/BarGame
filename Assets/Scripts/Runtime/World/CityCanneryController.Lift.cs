using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityCanneryController
    {
        private const float LiftGroundClearance = .005f;
        private const float LiftGuideRollerRadius = .025f;
        private Transform liftCarriage;
        private Vector3 liftCarriageDock;
        private Quaternion liftCarriageRest;
        private readonly Transform[] liftRams = new Transform[2];
        private readonly Transform[] liftFoldBarrels = new Transform[2];
        private readonly Transform[] liftFoldRods = new Transform[2];
        private readonly Transform[] liftFoldBases = new Transform[2];
        private readonly Transform[] liftFoldTips = new Transform[2];
        private readonly Vector3[] liftRamDocks = new Vector3[2];
        private readonly Quaternion[] liftRamRest = new Quaternion[2];
        private readonly Quaternion[] liftFoldBarrelRest = new Quaternion[2];
        private readonly Quaternion[] liftFoldRodRest = new Quaternion[2];
        private readonly Quaternion[] liftFoldAuthoredAim = new Quaternion[2];
        private readonly Vector3[] lowLiftContacts = new Vector3[3];
        private readonly Vector3[] lowLiftTruckPositions = new Vector3[3];
        private readonly Quaternion[] lowLiftTruckRotations = new Quaternion[3];
        private readonly bool[] lowLiftMeasured = new bool[3];
        private Vector3[] liftGroundingVertices;
        private float minimumLiftTravel;
        public float CurrentLiftTravel => currentLiftTravel;

        private void CreateLiftMechanism()
        {
            liftCarriage = Require(Truck, "MOVE_LiftCarriage");
            liftCarriageDock = Truck.InverseTransformPoint(liftCarriage.position);
            liftCarriageRest = Quaternion.Inverse(Truck.rotation) * liftCarriage.rotation;
            minimumLiftTravel = float.NegativeInfinity;
            for (int i = 0; i < 2; i++)
            {
                string side = i == 0 ? "Left" : "Right";
                liftRams[i] = Require(Truck, "MOVE_LiftRam" + side);
                liftRamDocks[i] = Truck.InverseTransformPoint(liftRams[i].position);
                liftRamRest[i] = Quaternion.Inverse(Truck.rotation) * liftRams[i].rotation;
                liftFoldBarrels[i] = Require(Truck, "MOVE_LiftFoldBarrel" + side);
                liftFoldRods[i] = Require(Truck, "MOVE_LiftFoldRod" + side);
                liftFoldBases[i] = Require(liftCarriage, "ANCHOR_LiftFoldBase" + side);
                liftFoldTips[i] = Require(lift, "ANCHOR_LiftFoldTip" + side);
                liftFoldBarrelRest[i] = Quaternion.Inverse(Truck.rotation) * liftFoldBarrels[i].rotation;
                liftFoldRodRest[i] = Quaternion.Inverse(Truck.rotation) * liftFoldRods[i].rotation;
                liftFoldAuthoredAim[i] = Quaternion.Inverse(Truck.rotation) *
                    LiftLinkAim(liftFoldTips[i].position - liftFoldBases[i].position);
                Vector3 rail = Truck.InverseTransformPoint(Require(Truck, "ANCHOR_LiftRailBottom" + side).position);
                Vector3 guide = Truck.InverseTransformPoint(Require(liftCarriage, "ANCHOR_LiftGuideLower" + side).position);
                minimumLiftTravel = Mathf.Max(minimumLiftTravel, rail.y + LiftGuideRollerRadius - guide.y);
            }
            ApplyLiftMechanism(0f);
            var points = new List<Vector3>();
            AppendLiftGroundingVertices(lift, points);
            AppendLiftGroundingVertices(liftCarriage, points);
            for (int i = 0; i < 2; i++)
            {
                AppendLiftGroundingVertices(liftRams[i], points);
                AppendLiftGroundingVertices(liftFoldBarrels[i], points);
                AppendLiftGroundingVertices(liftFoldRods[i], points);
            }
            liftGroundingVertices = points.ToArray();
        }

        private void AppendLiftGroundingVertices(Transform part, List<Vector3> points)
        {
            foreach (MeshFilter filter in part.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || filter.name.StartsWith("COL_", System.StringComparison.Ordinal)) continue;
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                    points.Add(Truck.InverseTransformPoint(filter.transform.TransformPoint(vertex)));
            }
        }

        private Quaternion LiftLinkAim(Vector3 direction)
        {
            Vector3 axis = direction.normalized;
            return Quaternion.LookRotation(Vector3.Cross(Truck.right, axis), axis);
        }

        private void ApplyLiftMechanism(float travel)
        {
            Vector3 stroke = Truck.up * travel;
            liftCarriage.SetPositionAndRotation(Truck.TransformPoint(liftCarriageDock) + stroke,
                Truck.rotation * liftCarriageRest);
            for (int i = 0; i < 2; i++)
            {
                liftRams[i].SetPositionAndRotation(Truck.TransformPoint(liftRamDocks[i]) + stroke,
                    Truck.rotation * liftRamRest[i]);
                Vector3 from = liftFoldBases[i].position, to = liftFoldTips[i].position;
                Quaternion aim = LiftLinkAim(to - from) * Quaternion.Inverse(liftFoldAuthoredAim[i]);
                // The importer bakes each authored MOVE rotation into its
                // children. Apply only the change from that measured aim.
                liftFoldBarrels[i].SetPositionAndRotation(from, aim * liftFoldBarrelRest[i]);
                liftFoldRods[i].SetPositionAndRotation(to, aim * liftFoldRodRest[i]);
            }
        }

        private Vector3 MeasureLowLift()
        {
            int site = TrolleySite;
            if (lowLiftMeasured[site] && (lowLiftTruckPositions[site] - Truck.position).sqrMagnitude < .000001f &&
                Quaternion.Angle(lowLiftTruckRotations[site], Truck.rotation) < .001f) return lowLiftContacts[site];
            float lower = minimumLiftTravel, upper = LiftPivotTravel(HighLift);
            if (LiftClearanceAt(upper) < LiftGroundClearance)
                throw new System.InvalidOperationException("The upper lift position intersects the receiving surface.");
            if (LiftClearanceAt(lower) < LiftGroundClearance)
            {
                // All parts slide on the truck's own vertical axis. Sampling
                // their displaced XZ keeps the uphill underside above paving.
                for (int i = 0; i < 18; i++)
                {
                    float candidate = (lower + upper) * .5f;
                    if (LiftClearanceAt(candidate) < LiftGroundClearance) lower = candidate;
                    else upper = candidate;
                }
                lower = upper;
            }
            Vector3 contact = HighLift + Truck.up * (lower - LiftPivotTravel(HighLift));
            lowLiftMeasured[site] = true;
            lowLiftTruckPositions[site] = Truck.position;
            lowLiftTruckRotations[site] = Truck.rotation;
            return lowLiftContacts[site] = contact;
        }

        private float LiftClearanceAt(float travel)
        {
            float clearance = float.PositiveInfinity;
            Vector3 stroke = Truck.up * travel;
            foreach (Vector3 vertex in liftGroundingVertices)
            {
                Vector3 point = Truck.TransformPoint(vertex) + stroke;
                clearance = Mathf.Min(clearance, point.y - HandlingGroundHeight(point));
            }
            return clearance;
        }
    }
}

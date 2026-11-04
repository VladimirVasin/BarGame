using UnityEngine;

namespace BarPromenade
{
    // Adds garment support and limb contacts to the resident's authored seated
    // loop. The same rig and targets serve plank benches and sprung bus seats.
    internal sealed class CityPedestrianSeatedPose
    {
        private readonly CityPedestrianAssetRegistry registry;
        private readonly CityPedestrianShoeContacts shoes;
        private readonly CitySeatedCushionContact cushion;
        private readonly Transform[] joints = new Transform[12];
        private readonly Quaternion[] before = new Quaternion[12];
        private readonly AnatomicalHandFrame[] palms = new AnatomicalHandFrame[2];
        private readonly float[] padDepth = new float[2];
        private readonly Vector3[][] palmPads = new Vector3[2][];
        private readonly SkinnedMeshRenderer[] measuredPalms = new SkinnedMeshRenderer[2];
        private readonly Vector3[] rests = new Vector3[2], restNormals = new Vector3[2];
        private static readonly float[] RestLocations = { .68f, .77f, .83f, .58f, .47f, .36f };
        private readonly Transform spine;
        private Quaternion beforeSpine;
        private readonly float[] thighLengths = new float[2];
        private readonly NpcHandPose fingers;
        private readonly DefaultNpcBodyShape bodyShape;
        private int shapeVersion;
        private const float MeasurementInterval = 1f / 15f;
        private float untilMeasurement;
        private Transform seat;
        private Quaternion localBasis;
        private float floorBelowSeat;
        private bool applied;

        internal bool IsBound => seat != null;
        internal Quaternion Basis => seat.rotation * localBasis;
        internal float Correction { get; private set; }
        internal float PelvisForward { get; private set; }

        internal CityPedestrianSeatedPose(CityPedestrianAssetRegistry registry, CityPedestrianShoeContacts shoes)
        {
            this.registry = registry;
            this.shoes = shoes;
            cushion = new CitySeatedCushionContact(registry);
            fingers = registry.GetComponentInChildren<NpcHandPose>(true);
            spine = CityPedestrianHandProps.FindSocket(registry.ModelRoot, "spine");
            bodyShape = registry.GetComponentInChildren<DefaultNpcBodyShape>(true);
            for (int side = 0; side < 2; side++)
            {
                Transform foot = side == 0 ? registry.LeftFoot : registry.RightFoot;
                int i = side * 6;
                joints[i] = foot.parent.parent; joints[i + 1] = foot.parent; joints[i + 2] = foot;
                var hand = CityPedestrianHandProps.FindSocket(registry.ModelRoot, side == 0 ? "hand.L" : "hand.R");
                joints[i + 3] = hand.parent.parent; joints[i + 4] = hand.parent; joints[i + 5] = hand;
                thighLengths[side] = Vector3.Distance(joints[i].position, joints[i + 1].position);
            }
        }

        internal bool Bind(Transform anchor, Vector3 up, Vector3 forward, Vector3 floor)
        {
            if (fingers == null || shoes == null || !shoes.Measure()) return false;
            forward = Vector3.ProjectOnPlane(forward, up).normalized;
            if (forward.sqrMagnitude < .9f) return false;
            localBasis = Quaternion.Inverse(anchor.rotation) * Quaternion.LookRotation(forward, up);
            floorBelowSeat = Vector3.Dot(anchor.position - floor, up);
            // The knee needs to clear the front edge before the lower leg
            // drops. Shorter residents sit a little farther forward instead of
            // lifting their buttocks until a steeply sloped thigh clears it.
            float femur = Mathf.Min(thighLengths[0], thighLengths[1]);
            float shin = Vector3.Distance(joints[1].position, joints[2].position);
            float kneeHeight = shoes.Height(0) - floorBelowSeat + .985f * shin;
            float drop = Mathf.Clamp(.29f * femur - kneeHeight, 0f, .65f * femur);
            float reach = Mathf.Sqrt(femur * femur - drop * drop);
            PelvisForward = Mathf.Clamp(.38f - reach, -.065f, .075f);
            for (int side = 0; side < 2; side++)
            {
                palms[side] = fingers.GetAnatomicalFrame(side == 0);
                var wrist = joints[side * 6 + 5];
                Vector3 centre = wrist.position + wrist.rotation * palms[side].PalmCentreInHand;
                Vector3 towardObject = wrist.rotation * palms[side].PalmNormalInHand;
                string suffix = side == 0 ? "Palm.L" : "Palm.R";
                foreach (var renderer in registry.Renderers)
                    if (renderer is SkinnedMeshRenderer palm && palm.enabled && palm.gameObject.activeInHierarchy &&
                        palm.name.EndsWith(suffix, System.StringComparison.Ordinal))
                    {
                        if (palm == measuredPalms[side]) break;
                        Vector3[] points = CityPedestrianShoeContacts.SkinWorldVertices(palm);
                        padDepth[side] = 0f;
                        for (int v = 0; v < points.Length; v++)
                        {
                            Vector3 offset = points[v] - centre;
                            padDepth[side] = Mathf.Max(padDepth[side], Vector3.Dot(offset, towardObject));
                            points[v] = Quaternion.Inverse(wrist.rotation) * offset;
                        }
                        palmPads[side] = points;
                        measuredPalms[side] = palm;
                        break;
                    }
                if (palmPads[side] == null) return false;
            }
            cushion.Bind();
            seat = anchor;
            Correction = 0f;
            untilMeasurement = 0f;
            return true;
        }

        internal void Apply(float elapsed)
        {
            Quaternion basis = Basis;
            Vector3 up = basis * Vector3.up, forward = basis * Vector3.forward, right = basis * Vector3.right;
            for (int i = 0; i < joints.Length; i++) before[i] = joints[i].localRotation;
            applied = true;
            if (spine != null)
            {
                beforeSpine = spine.localRotation;
                spine.rotation = Quaternion.AngleAxis(17f, right) * spine.rotation;
            }
            untilMeasurement -= Mathf.Max(0f, elapsed);
            bool garmentChanged = cushion.Bind();
            int version = bodyShape != null ? bodyShape.SelectionVersion : 0;
            bool measure = garmentChanged || version != shapeVersion || untilMeasurement <= 0f;
            if (version != shapeVersion) shoes.Measure(force: true);
            shapeVersion = version;
            registry.ModelRoot.position += up * Correction;
            PlaceFeet(basis, up, forward, right);
            if (measure)
            {
                // Planting the boots changes hip/knee angles and the shape of the
                // trousers against the cushion. Measure the *replanted* pose;
                // normal idle samples start at the previous solution and finish
                // on the first check. The cap bounds work when changing outfits.
                for (int pass = 0; pass < 4; pass++)
                {
                    float residual = cushion.MeasureCorrection(seat.position, up, forward);
                    float offset = cushion.ForwardForRearSupport(PelvisForward, residual);
                    if (offset > .001f)
                    {
                        PelvisForward += offset;
                        registry.ModelRoot.position += forward * offset;
                        PlaceFeet(basis, up, forward, right);
                        continue;
                    }
                    if (Mathf.Abs(residual) <= .0015f) break;
                    Correction += residual;
                    registry.ModelRoot.position += up * residual;
                    PlaceFeet(basis, up, forward, right);
                }
                untilMeasurement = MeasurementInterval;
            }
            for (int side = 0; side < 2; side++)
            {
                int i = side * 6;
                float outward = side == 0 ? -1f : 1f;
                // The reachable part of a bulky trouser leg lies higher than
                // an interpolated hip/knee bone line. Use the upper fabric
                // triangle and the actual thickness of the worn palm/glove.
                if (measure) SelectRest(side, basis);
                Vector3 rest = seat.position + basis * rests[side];
                Vector3 normal = basis * restNormals[side];
                Quaternion handRotation = palms[side].ResolveHandRotation(-normal,
                    Vector3.ProjectOnPlane(forward, normal));
                Vector3 wrist = palms[side].ResolveHandPosition(rest, handRotation);
                Vector3 hint = joints[i + 3].position + outward * right * .27f + forward * .10f - up * .29f;
                LimbTwoBoneIk.Solve(joints[i + 3], joints[i + 4], joints[i + 5], wrist, handRotation, hint);
            }
        }

        private void SelectRest(int side, Quaternion basis)
        {
            int i = side * 6;
            Quaternion inverse = Quaternion.Inverse(basis);
            Vector3 hip = inverse * (joints[i].position - seat.position);
            Vector3 knee = inverse * (joints[i + 1].position - seat.position);
            float reach = .975f * LimbTwoBoneIk.ChainLength(joints[i + 3], joints[i + 4], joints[i + 5]);
            float best = float.PositiveInfinity;
            rests[side] = Vector3.Lerp(hip, knee, .5f) + Vector3.up * (.23f * thighLengths[side]);
            restNormals[side] = Vector3.up;
            foreach (float t in RestLocations)
            {
                Vector3 point = Vector3.Lerp(hip, knee, t);
                if (!cushion.TryThighTop(point.x, point.z, out point.y, out Vector3 normal, out float thighSide) ||
                    normal.y < .45f || (side == 0 ? thighSide : -thighSide) < .5f) continue;
                normal = Vector3.Slerp(Vector3.up, normal, .75f).normalized;
                point += normal * (padDepth[side] + .001f);
                Vector3 worldNormal = basis * normal;
                Quaternion rotation = palms[side].ResolveHandRotation(-worldNormal,
                    Vector3.ProjectOnPlane(basis * Vector3.forward, worldNormal));
                // A flared or folded trouser panel can rise under one edge of
                // the palm even though its centre lies on the leg. Rest the
                // complete worn hand pad on the highest cloth it covers.
                Quaternion localHand = inverse * rotation;
                float lift = float.NegativeInfinity;
                foreach (Vector3 pad in palmPads[side])
                {
                    Vector3 vertex = point + localHand * pad;
                    if (cushion.TryThighTop(vertex.x, vertex.z, out float top, out _))
                        lift = Mathf.Max(lift, top - vertex.y);
                }
                if (float.IsNegativeInfinity(lift)) continue;
                point.y += lift + .001f;
                Vector3 wrist = palms[side].ResolveHandPosition(seat.position + basis * point, rotation);
                float miss = Mathf.Max(0f, Vector3.Distance(joints[i + 3].position, wrist) - reach);
                float cost = miss + Mathf.Abs(lift) * .25f;
                if (cost >= best) continue;
                best = cost; rests[side] = point; restNormals[side] = normal;
                if (miss < .0001f && Mathf.Abs(lift) < .008f) break;
            }
        }

        private void PlaceFeet(Quaternion basis, Vector3 up, Vector3 forward, Vector3 right)
        {
            for (int side = 0; side < 2; side++)
            {
                int i = side * 6;
                float outward = side == 0 ? -1f : 1f;
                Transform thigh = joints[i], shin = joints[i + 1], foot = joints[i + 2];
                float femur = Vector3.Distance(thigh.position, shin.position);
                float tibia = Vector3.Distance(shin.position, foot.position);
                float hipHeight = Vector3.Dot(thigh.position - seat.position, up);
                float floorAnkle = shoes.Height(side) - floorBelowSeat;
                // Start from the skeleton and the ground. A lower seat lets a
                // longer leg rest with the boot a little under the knee. A high
                // seat lets a short thigh slope down to the knee beyond the edge.
                float kneeHeight = Mathf.Min(hipHeight + .025f * femur, floorAnkle + .985f * tibia);
                float kneeDrop = Mathf.Clamp(kneeHeight - hipHeight, -.65f * femur, .25f * femur);
                float spread = .055f * femur;
                float run = Mathf.Sqrt(Mathf.Max(0f, femur * femur - kneeDrop * kneeDrop - spread * spread));
                Vector3 knee = thigh.position + forward * run + up * kneeDrop + right * (outward * spread);
                thigh.rotation = Quaternion.FromToRotation(shin.position - thigh.position, knee - thigh.position) * thigh.rotation;
                float drop = Mathf.Clamp(hipHeight + kneeDrop - floorAnkle, 0f, tibia);
                float heelOffset = Mathf.Sqrt(Mathf.Max(0f, tibia * tibia - drop * drop));
                Vector3 shinTarget = -up * drop - forward * heelOffset;
                shin.rotation = Quaternion.FromToRotation(foot.position - shin.position, shinTarget) * shin.rotation;
                foot.rotation = basis * shoes.Rotation(side);
            }
        }

        internal void RestoreBones()
        {
            if (!applied) return;
            if (spine != null) spine.localRotation = beforeSpine;
            for (int i = 0; i < joints.Length; i++) joints[i].localRotation = before[i];
            applied = false;
        }

        internal void Release()
        {
            RestoreBones();
            seat = null;
            Correction = 0f;
        }
    }
}

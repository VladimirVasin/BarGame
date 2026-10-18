using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatActor
    {
        // Anatomical capsules cover the complete retained rig, including the
        // otherwise non-damaging folded leg, left hand and extended hook arm.
        // The independent shield head fits its authored .40m bind-scale sphere.
        // Reusing the old elongated worker skull envelope locks it at the gantry.
        private static readonly int[] CraneLimbParents = { -1, 0, 1, 2, -1, 2, 2, 5, 6, 7, 8, 9, 10, 0, 0, 13, 14, 15, 16 };
        private static readonly float[] CraneLimbRadii = { .23f, .23f, .24f, .13f, .40f, .13f, .13f, .12f, .12f, .11f, .11f, .12f, .12f, .15f, .15f, .12f, .12f, .10f, .10f };
        private readonly Transform[] craneClearanceBones = new Transform[19];
        private readonly Vector3[] craneCandidateBones = new Vector3[19];
        private readonly Vector3[] craneSafePositions = new Vector3[19], craneSafeScales = new Vector3[19];
        private readonly Quaternion[] craneSafeRotations = new Quaternion[19];
        private Collider[] craneClearanceBroadphase = new Collider[32];
        private Bounds craneHeadBounds, craneBridgeBounds;
        private bool craneMachineBoundsReady;
        private bool craneSafePoseReady;
        internal string CraneBlockedAnatomy { get; private set; }

        private void InitializeCraneClearance()
        {
            for (int i = 0; i < craneClearanceBones.Length; i++) craneClearanceBones[i] = RequireCraneBone(CraneAnatomyBoneNames[i]);
        }

        private static float CraneLimbRadius(int index) => CraneLimbRadii[index] * CombatCraneAssetProvider.BodyPoseScale;

        private void KeepCranePoseClear()
        {
            // Translation uses the previously rendered anatomy. Check the new
            // sampled pose too: a long limb must not animate through a rack or
            // rail even when the load command itself did not move this step.
            if (craneSafePoseReady && !CraneSuspensionClear(transform.position, transform.rotation, craneCommand))
            {
                for (int i = 0; i < craneClearanceBones.Length; i++)
                {
                    craneClearanceBones[i].SetLocalPositionAndRotation(craneSafePositions[i], craneSafeRotations[i]);
                    craneClearanceBones[i].localScale = craneSafeScales[i];
                }
                Array.Clear(craneRopeSlack, 0, craneRopeSlack.Length);
                if (CranePhase == CombatCranePhase.Striking) StopCraneStrike();
                return;
            }
            for (int i = 0; i < craneClearanceBones.Length; i++)
            {
                craneSafePositions[i] = craneClearanceBones[i].localPosition;
                craneSafeRotations[i] = craneClearanceBones[i].localRotation;
                craneSafeScales[i] = craneClearanceBones[i].localScale;
            }
            craneSafePoseReady = true;
        }

        private static Bounds CraneRendererBounds(GameObject model)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("Crane model has no rendered geometry.");
            Bounds result = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) result.Encapsulate(renderers[i].bounds);
            return result;
        }

        private void MeasureCraneMachineBounds()
        {
            craneHeadBounds = CraneRendererBounds(craneSuspensionHead);
            craneHeadBounds.center -= craneSuspensionHead.transform.position;
            // The lower feed cassette turns with the hanging body. Retain a
            // conservative yaw envelope without remeasuring renderers per tick.
            float swivelRadius = Mathf.Max(craneHeadBounds.extents.x + Mathf.Abs(craneHeadBounds.center.x),
                craneHeadBounds.extents.z + Mathf.Abs(craneHeadBounds.center.z));
            craneHeadBounds.center = new Vector3(0f, craneHeadBounds.center.y, 0f);
            craneHeadBounds.extents = new Vector3(swivelRadius, craneHeadBounds.extents.y, swivelRadius);
            craneBridgeBounds = CraneRendererBounds(craneBridge);
            craneBridgeBounds.center -= craneBridge.transform.position;
            // The rotating warehouse fan has no collision body. Its authored
            // housing/rotor begins at9.6m; the entire gantry must stay below it.
            if (craneHeadBounds.max.y > .28f || craneBridgeBounds.max.y > .28f)
                throw new InvalidOperationException("Crane exceeds the declared fan clearance.");
            craneMachineBoundsReady = true;
        }

        private bool CraneWorldOverlap(Vector3 a, Vector3 b, float radius)
        {
            float fraction = float.PositiveInfinity; Vector3 point = default, normal = default;
            GatherWorldSegment(a, b, radius, ref fraction, ref point, ref normal);
            return fraction <= 1f;
        }

        private bool CraneWorldSweep(Vector3 a, Vector3 b, float radius)
        {
            float fraction = float.PositiveInfinity; Vector3 point = default, normal = default;
            GatherWorldSweep(a, b, radius, ref fraction, ref point, ref normal);
            return fraction <= 1f;
        }

        private bool CraneBoxOverlaps(Bounds bounds)
        {
            int count;
            do
            {
                count = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents, craneClearanceBroadphase,
                    Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
                if (count < craneClearanceBroadphase.Length) break;
                Array.Resize(ref craneClearanceBroadphase, craneClearanceBroadphase.Length * 2);
            } while (true);
            for (int i = 0; i < count; i++) if (IsContactWorld(craneClearanceBroadphase[i])) return true;
            return false;
        }

        private bool CraneSuspensionClear(Vector3 wanted, Quaternion rotation, Vector3 carriage)
        {
            CraneBlockedAnatomy = null;
            Quaternion turn = rotation * Quaternion.Inverse(transform.rotation);
            Vector3 headDelta = new Vector3(carriage.x - CraneAnchorPosition.x, 0f, carriage.z - CraneAnchorPosition.z);
            Bounds broad = new Bounds(wanted + Vector3.up, Vector3.one * .1f);
            for (int i = 0; i < craneCandidateBones.Length; i++)
            {
                Vector3 point = wanted + turn * (craneClearanceBones[i].position - transform.position);
                craneCandidateBones[i] = point;
                broad.Encapsulate(new Bounds(point, Vector3.one * (CraneLimbRadius(i) * 2f)));
                broad.Encapsulate(craneClearanceBones[i].position);
            }
            for (int i = 0; i < craneFeeds.Length; i++)
            {
                Vector3 feed = CraneAnchorPosition + headDelta + turn * (craneFeeds[i].position - CraneAnchorPosition);
                Vector3 end = wanted + turn * (craneAttachments[i].position - transform.position);
                if (end.y >= feed.y - .03f)
                { CraneBlockedAnatomy = CombatCraneAssetProvider.SuspensionBoneNames[i]; return false; }
                broad.Encapsulate(feed);
            }
            Vector3 hook = wanted + turn * (craneHookCentre.position - transform.position);
            Vector3 leftHook = wanted + turn * (craneLeftHookCentre.position - transform.position);
            broad.Encapsulate(new Bounds(hook, Vector3.one * .36f)); broad.Encapsulate(new Bounds(leftHook, Vector3.one * .28f));
            Vector3 stowedEye = default; Quaternion stowedRotation = Quaternion.identity;
            bool stowed = craneHookFrame != null && CraneHookPhase == CombatCraneHookPhase.Stowed;
            if (stowed)
            {
                GetCraneStowedHookPose(out stowedEye, out stowedRotation);
                stowedEye = wanted + turn * (stowedEye - transform.position); stowedRotation = turn * stowedRotation;
                broad.Encapsulate(new Bounds(stowedEye + stowedRotation * (Vector3.up * .3f), Vector3.one * .75f));
            }
            broad.Expand(.35f);
            // Most travel is in empty air. One bounded NonAlloc broadphase
            // avoids issuing every limb/cable query in those empty substeps.
            if (CraneBoxOverlaps(broad))
            {
                if (stowed && CraneHookWorldFraction(craneHookFrame.transform.position, stowedEye, stowedRotation) <= 1f) return false;
                if (CraneWorldOverlap(craneCandidateBones[12], hook, .18f) || CraneWorldSweep(craneHookCentre.position, hook, .18f) ||
                    CraneWorldOverlap(craneCandidateBones[11], leftHook, .14f) || CraneWorldSweep(craneLeftHookCentre.position, leftHook, .14f)) return false;
                for (int i = 0; i < craneCandidateBones.Length; i++)
                {
                    int parent = CraneLimbParents[i];
                    Vector3 a = parent < 0 ? craneCandidateBones[i] : craneCandidateBones[parent];
                    if (CraneWorldOverlap(a, craneCandidateBones[i], CraneLimbRadius(i)) ||
                        CraneWorldSweep(craneClearanceBones[i].position, craneCandidateBones[i], CraneLimbRadius(i)))
                    { CraneBlockedAnatomy = CraneAnatomyBoneNames[i]; return false; }
                }
                for (int i = 0; i < craneFeeds.Length; i++)
                {
                    Vector3 oldEnd = craneAttachments[i].position;
                    Vector3 end = wanted + turn * (oldEnd - transform.position);
                    Vector3 feed = CraneAnchorPosition + headDelta + turn * (craneFeeds[i].position - CraneAnchorPosition);
                    for (int segment = 0; segment < CraneCableSegments; segment++)
                        if (CraneWorldOverlap(CraneRopePoint(i, segment / (float)CraneCableSegments, feed, end),
                            CraneRopePoint(i, (segment + 1f) / CraneCableSegments, feed, end), .025f)) return false;
                    // Cable endpoints move differently during a hoist. Sample
                    // their swept ruled surface at bounded metre intervals.
                    int samples = Mathf.CeilToInt(Vector3.Distance(feed, end) / .4f);
                    for (int sample = 0; sample <= samples; sample++)
                    {
                        float t = samples == 0 ? 0f : (float)sample / samples;
                        if (CraneWorldSweep(CraneRopePoint(i, t), CraneRopePoint(i, t, feed, end), .025f)) return false;
                    }
                }
            }
            if (!craneMachineBoundsReady) MeasureCraneMachineBounds();
            Bounds head = craneHeadBounds; head.center += CraneAnchorPosition + headDelta;
            Bounds bridge = craneBridgeBounds; bridge.center += new Vector3(0f, CraneAnchorPosition.y, carriage.z);
            for (int i = 0; i < craneCandidateBones.Length; i++)
            {
                int parent = CraneLimbParents[i];
                Vector3 from = parent < 0 ? craneCandidateBones[i] : craneCandidateBones[parent], delta = craneCandidateBones[i] - from;
                if (CombatHurtboxes.IntersectsSegment(head, from, delta, CraneLimbRadius(i)) ||
                    CombatHurtboxes.IntersectsSegment(bridge, from, delta, CraneLimbRadius(i)))
                { CraneBlockedAnatomy = CraneAnatomyBoneNames[i]; return false; }
            }
            if (CraneHookPhase is CombatCraneHookPhase.Outbound or CombatCraneHookPhase.Holding or CombatCraneHookPhase.Returning)
            {
                Vector3 exit = wanted + turn * (CraneHookCableExit - transform.position);
                if (!CranePathVisible(exit, craneHookEye)) return false;
            }
            return !CraneBoxOverlaps(head) && !CraneBoxOverlaps(bridge);
        }
    }
}

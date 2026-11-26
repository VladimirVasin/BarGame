using System;
using UnityEngine;

namespace BarPromenade
{
    internal sealed class CityJanitorWorker
    {
        internal readonly VillageResidentPresentation Body;
        internal readonly Transform Broom, Bristles;
        internal readonly CityJanitorLeaves Leaves;
        private readonly Transform lowerGrip, spine, leftShoulder, rightShoulder;
        private readonly Transform[] contactBones;
        private readonly Quaternion[] contactRotations;
        private readonly NpcHandPose hands;
        private readonly Quaternion broomBasis;
        private Vector3 floorNormal;
        private float sweepTime, leafStrokeStart, pace, nextPose, tucked;
        internal float Age { get; private set; }
        internal float Duration { get; private set; }
        internal bool Active => Body.gameObject.activeSelf;
        internal Vector3 Feet => Body.transform.position;

        internal CityJanitorWorker(Transform parent, string characterId, Transform template)
        {
            Body = DefaultNpcFactory.CreateForCharacter(parent, characterId);
            NpcFootstepSources.Unregister(Body.transform);
            CityPortCrew.AlignWorkerModelWithPlacement(Body);
            hands = Body.GetComponent<NpcHandPose>();
            Leaves = new CityJanitorLeaves(Body.transform);
            spine = CityPedestrianHandProps.FindSocket(Body.ModelRoot, "spine");
            Broom = UnityEngine.Object.Instantiate(template.gameObject, Body.transform, false).transform;
            Broom.name = "Janitor Broom";
            // This hand-held prop changes in LateUpdate with the skinned fingers. An instance
            // property block keeps its moving matrix on the per-renderer path in URP.
            var timber = Broom.GetComponent<MeshRenderer>();
            var finish = new MaterialPropertyBlock();
            int tint = Shader.PropertyToID("_BaseColor");
            finish.SetColor(tint, timber.sharedMaterial.GetColor(tint));
            timber.SetPropertyBlock(finish);
            Broom.localScale = template.lossyScale;
            broomBasis = template.rotation;
            lowerGrip = CityPedestrianHandProps.FindSocket(Broom, "BroomLowerGrip");
            Bristles = CityPedestrianHandProps.FindSocket(Broom, "BroomFoot");
            if (hands == null || spine == null || Bristles == null || lowerGrip == null)
                throw new InvalidOperationException("City janitors require the shared hand rig and the imported broom grips and foot.");
            // Keep the imported timber and straw finish, with a handle sized to the closed fingers.
            Vector3 longAxis = Broom.InverseTransformPoint(Bristles.position).normalized;
            float width = hands.CylinderRadius * 2f / .030f;
            Broom.localScale = Vector3.Scale(Broom.localScale, new Vector3(
                Mathf.Lerp(width, 1f, Mathf.Abs(longAxis.x)), Mathf.Lerp(width, 1f, Mathf.Abs(longAxis.y)),
                Mathf.Lerp(width, 1f, Mathf.Abs(longAxis.z))));
            Transform Joint(string name) => CityPedestrianHandProps.FindSocket(Body.ModelRoot, name);
            leftShoulder = Joint("upper_arm.L");
            rightShoulder = Joint("upper_arm.R");
            contactBones = new[] { spine, leftShoulder, rightShoulder, Joint("forearm.L"), Joint("forearm.R"),
                Joint("hand.L"), Joint("hand.R"), Body.Head };
            contactRotations = new Quaternion[contactBones.Length];
        }

        internal void Arrive(Vector3 feet, Vector3 normal, Vector3 forward, float shift, float speed, float duration)
        {
            Body.transform.SetPositionAndRotation(feet, Quaternion.LookRotation(forward));
            Body.gameObject.SetActive(true);
            floorNormal = normal;
            sweepTime = shift; pace = speed; Duration = duration;
            leafStrokeStart = shift <= 2f ? 2f : 20f;
            Leaves.Place(feet, normal, forward);
            Age = tucked = nextPose = 0f;
            PoseAt(sweepTime, 0f);
        }

        internal void Advance(float delta, float distanceSquared)
        {
            Age += delta;
            tucked = Mathf.MoveTowards(tucked, distanceSquared < 1.7f * 1.7f ? 1f : 0f, delta * 2f);
            if (tucked == 0f) sweepTime += delta * pace;
            else if (sweepTime >= leafStrokeStart + 1f && sweepTime < leafStrokeStart + 1.67f)
                // A loose leaf still lands if the worker draws the broom out of a passer-by's way.
                sweepTime = Mathf.Min(sweepTime + delta * pace, leafStrokeStart + 1.67f);
            if (Age < nextPose)
            {
                for (int i = 0; i < contactBones.Length; i++) contactBones[i].localRotation = contactRotations[i];
                return;
            }
            nextPose = Age + (distanceSquared < 16f * 16f ? 1f / 30f : 1f / 12f);
            PoseAt(sweepTime, tucked);
        }

        internal void PoseAt(float time, float tuck)
        {
            // Three broad strokes, two short finishing strokes and a pause on the planted shaft.
            float t = Mathf.Repeat(time, 18f);
            bool finishing = t >= 6f;
            float wave = finishing ? Mathf.Repeat((t - 6f) / 1.4f, 1f) : Mathf.Repeat(t / 2f, 1f);
            float work = Mathf.SmoothStep(0f, 1f, t / .65f) *
                (1f - Mathf.SmoothStep(0f, 1f, (t - 8.2f) / 1.1f)) * (1f - tuck);
            float stroke = -Mathf.Cos(wave * 2f * Mathf.PI);
            float lift = wave < .5f ? 0f : Mathf.Sin((wave - .5f) * 2f * Mathf.PI);
            Vector3 rest = new Vector3(-.055f, 0f, .22f);
            Vector3 path = new Vector3(stroke * (finishing ? .15f : .36f), .075f * lift,
                .40f + .055f * stroke);
            float leafTime = time - leafStrokeStart;
            Leaves.Sample(leafTime);
            bool recovering = leafTime >= 1f && leafTime < 4f;
            if (recovering) path = CityJanitorLeaves.RecoveryStroke(leafTime);
            Vector3 local = Vector3.Lerp(rest, path, work);
            Vector3 target = Feet + Body.transform.rotation * local;
            target.y = Feet.y - (floorNormal.x * (target.x - Feet.x) + floorNormal.z * (target.z - Feet.z)) /
                floorNormal.y + local.y;
            Vector3 look = recovering ? Leaves.LooseLeaf.position : target;
            Body.ApplyLocomotion(0f, false, time, Vector3.Lerp(Feet + Body.transform.forward * 4f, look, work));
            Quaternion upright = spine.rotation;
            spine.rotation = Quaternion.AngleAxis(7f + 20f * work, Body.transform.right) * upright;
            Vector3 shoulders = (rightShoulder.position + leftShoulder.position) * .5f;
            Vector3 gripLine = shoulders + Body.transform.forward * .19f - Vector3.up * .25f;
            Vector3 shaft = (gripLine - target).normalized;
            Quaternion baseRotation = Body.transform.rotation * broomBasis;
            Broom.SetPositionAndRotation(target, Quaternion.FromToRotation(Vector3.up, shaft) * baseRotation);
            Broom.position += target - Bristles.position;
            // Hands slide up the shaft as the back straightens between bouts.
            float gripSlide = Mathf.Clamp(Vector3.Dot(gripLine - Broom.position, shaft), .29f, .425f);
            Vector3 upper = Broom.position + shaft * gripSlide, lower = lowerGrip.position + shaft * gripSlide;

            Pose right = hands.GetSocketPose(false, upper, shaft, -Body.transform.right + Body.transform.forward * .35f);
            Pose left = hands.GetSocketPose(true, lower, shaft, Body.transform.right + Body.transform.forward * .35f);
            for (int i = 0; i < 8; i++)
            {
                float lean = 7f + 20f * work + i * 4f;
                spine.rotation = Quaternion.AngleAxis(lean, Body.transform.right) * upright;
                Body.ApplyHandContacts(right.position, left.position, right.rotation, left.rotation);
                if ((Body.RightGrip.position - right.position).sqrMagnitude < .000025f &&
                    (Body.LeftGrip.position - left.position).sqrMagnitude < .000025f) break;
            }
            hands.SetGrip(false, 1f);
            hands.SetGrip(true, 1f);
            if (recovering)
            {
                float watch = Mathf.SmoothStep(0f, 1f, Mathf.Min((leafTime - 1f) / .37f, (4f - leafTime) / .42f));
                Body.Head.rotation = Quaternion.AngleAxis(12f * work * watch, Body.transform.right) * Body.Head.rotation;
            }
            for (int i = 0; i < contactBones.Length; i++) contactRotations[i] = contactBones[i].localRotation;
        }

        internal void Leave() => Body.gameObject.SetActive(false);
    }
}

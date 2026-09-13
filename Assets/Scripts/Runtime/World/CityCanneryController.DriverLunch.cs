using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CityCanneryController
    {
        private const float DriverLunchTakeSeconds = 2.4f;
        private const float DriverLunchPocketContact = 1.2f;
        private Vector3 driverLunchGripLocal, driverLunchBiteLocal;
        private Vector3 driverLunchFingerLocal, driverLunchPalmLocal;
        private Transform driverLunchJaw;

        public Transform DriverLunch { get; private set; }
        /// <summary>The bite hold: the real cut face touches the mouth socket.</summary>
        public bool DriverEating { get; private set; }
        public bool DriverLunchVisible => DriverLunch != null && DriverLunch.gameObject.activeInHierarchy;
        public Vector3 DriverLunchMouthContact => DriverLunch != null
            ? DriverLunch.TransformPoint(driverLunchBiteLocal) : Vector3.zero;
        public Vector3 DriverLunchHandContact => DriverLunch != null
            ? DriverLunch.TransformPoint(driverLunchGripLocal) : Vector3.zero;

        private void CreateDriverLunch()
        {
            DriverLunch = SupermarketProductModelResources.Instantiate(InventoryItemId.DayOldLoaf,
                transform, new Vector3(.18f, .09f, .105f), "Driver Lunch");
            var registry = DriverLunch.GetComponentInChildren<SupermarketProductAssetRegistry>();
            if (registry == null) throw new InvalidOperationException("The driver's bread has no authored product bounds.");
            Bounds bread = default, cut = default;
            bool hasBread = false, hasCut = false;
            foreach (SupermarketProductPartBinding part in registry.Parts)
            {
                MeshFilter filter = part.Renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                    throw new InvalidOperationException("The driver's bread lost its measured product mesh.");
                // Product meshes deliberately disable CPU vertex access in
                // player builds. Their imported local bounds remain available.
                Bounds meshBounds = filter.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 vertex = meshBounds.center + Vector3.Scale(meshBounds.extents, new Vector3(
                        (corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                    Vector3 point = DriverLunch.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                    if (hasBread) bread.Encapsulate(point); else { bread = new Bounds(point, Vector3.zero); hasBread = true; }
                    if (part.Role != "crumb") continue;
                    if (hasCut) cut.Encapsulate(point); else { cut = new Bounds(point, Vector3.zero); hasCut = true; }
                }
            }
            if (!hasBread || !hasCut || bread.size.x > .181f || bread.size.y > .091f || bread.size.z > .106f)
                throw new InvalidOperationException("The driver's personal bread lacks its small cut-end contract.");
            // The pale cut end is an actual separate authored mesh, facing -X.
            // The measured contact is on its outer face, not a guessed mouth
            // position or the loaf's unrelated top/rim bounds.
            driverLunchBiteLocal = new Vector3(cut.min.x, cut.center.y, cut.center.z);
            // The grip socket sits inside the mitten. Keep the food centre on
            // its volar side, between the existing thumb and finger geometry.
            driverLunchGripLocal = new Vector3(bread.center.x + bread.extents.x * .38f,
                bread.center.y - .055f, bread.center.z);
            VillageResidentPresentation actor = workers[4];
            Transform hand = driverTrolleyHands[1];
            driverLunchFingerLocal = hand.InverseTransformDirection(actor.RightGrip.position - hand.position).normalized;
            driverLunchPalmLocal = Vector3.ProjectOnPlane(hand.InverseTransformDirection(actor.transform.forward),
                driverLunchFingerLocal).normalized;
            if (driverLunchFingerLocal.sqrMagnitude < .9f || driverLunchPalmLocal.sqrMagnitude < .9f)
                throw new InvalidOperationException("The driver's lunch requires a measured mitten/socket frame.");
            driverLunchJaw = Require(actor.ModelRoot, "face.mouth");
            HideDriverLunch();
        }

        private void HideDriverLunch()
        {
            DriverEating = false;
            if (DriverLunch != null) DriverLunch.gameObject.SetActive(false);
        }

        /// <summary>Called after the seated body sample. Absolute lunch time
        /// owns take, bite, rest and the reverse handoff into the same pocket.</summary>
        private void ApplyDriverLunch(float elapsed, float stowProgress = 0f)
        {
            if (DriverLunch == null) return;
            elapsed = Mathf.Max(0f, elapsed);
            float stow = Mathf.Clamp01(stowProgress);
            VillageResidentPresentation actor = workers[4];
            Vector3 up = actor.transform.up, forward = actor.transform.forward, right = actor.transform.right;
            Transform hand = driverTrolleyHands[1];
            Quaternion seatedHandRotation = hand.rotation;
            Vector3 seatedRight = actor.RightGrip.position;
            Vector3 pocketGrip = driverPelvis.position + right * .185f + up * .10f + forward * .10f;
            Vector3 lapGrip = driverPelvis.position + right * .19f + up * .13f + forward * .38f;
            Quaternion eatingRotation = Quaternion.LookRotation(-right, up);
            Quaternion pocketRotation = Quaternion.LookRotation(-right, forward);
            Pose pocket = LunchPoseAtGrip(pocketGrip, pocketRotation);
            Pose lap = LunchPoseAtGrip(lapGrip, eatingRotation);

            float cycle = Mathf.Repeat(Mathf.Max(0f, elapsed - DriverLunchTakeSeconds), 12f);
            float lift = elapsed < DriverLunchTakeSeconds ? 0f :
                cycle < .8f ? Ease(cycle / .8f) : cycle < 1.65f ? 1f :
                cycle < 2.4f ? 1f - Ease((cycle - 1.65f) / .75f) : 0f;
            float take = Ease(elapsed / DriverLunchPocketContact);
            bool held = elapsed >= DriverLunchPocketContact;
            bool biting = elapsed >= DriverLunchTakeSeconds && cycle >= .8f && cycle <= 1.65f;
            // Look is resolved before the mouth contact so even the small
            // ordinary head movement cannot slide the cut end through lips.
            ApplyCrewLook(actor, driverMouth.position + forward * 1.8f - up * (.45f * (1f - lift)),
                .28f * take * (1f - stow));
            if (stow <= 0f && elapsed >= DriverLunchTakeSeconds && cycle >= .8f && cycle < 4.2f)
            {
                float chew = Mathf.Sin((cycle - .8f) * Mathf.PI * 5f);
                driverLunchJaw.position -= up * (.003f * chew * chew);
            }
            Pose mouth = new Pose(driverMouth.position - eatingRotation * driverLunchBiteLocal, eatingRotation);
            Pose food = elapsed < DriverLunchTakeSeconds
                ? LunchBlend(pocket, lap, Ease((elapsed - DriverLunchPocketContact) / DriverLunchPocketContact))
                : LunchBlend(lap, mouth, lift);
            Vector3 handTarget = held ? food.position + food.rotation * driverLunchGripLocal :
                Vector3.Lerp(seatedRight, pocketGrip, take);
            Quaternion gripRotation = held ? food.rotation : pocket.rotation;
            float wristWeight = take;

            if (stow > 0f)
            {
                // Start at the exact last eating pose, including a raised
                // bite. Only after reaching the pocket is the remainder
                // concealed and the empty hand returned to the seated pose.
                float returnToPocket = Ease(stow / .65f);
                food = LunchBlend(food, pocket, returnToPocket);
                handTarget = food.position + food.rotation * driverLunchGripLocal;
                gripRotation = food.rotation;
                held = stow < .65f;
                biting = false;
                if (stow >= .65f)
                {
                    float release = Ease((stow - .65f) / .35f);
                    handTarget = Vector3.Lerp(pocketGrip, seatedRight, release);
                    wristWeight = 1f - release;
                }
            }

            Vector3 fingers = gripRotation * Vector3.left;
            Vector3 palm = gripRotation * Vector3.up;
            Quaternion grasp = Quaternion.LookRotation(fingers, palm) *
                Quaternion.Inverse(Quaternion.LookRotation(driverLunchFingerLocal, driverLunchPalmLocal));
            hand.rotation = Quaternion.Slerp(seatedHandRotation, grasp, wristWeight);
            // A bite keeps the elbow low beside the ribs. The ordinary work
            // contact pole spreads it sideways for broad two-handed loads.
            Transform shoulder = driverTrolleyShoulders[1];
            Vector3 wrist = handTarget - (actor.RightGrip.position - hand.position);
            LimbTwoBoneIk.Solve(shoulder, driverTrolleyForearms[1], hand, wrist, hand.rotation,
                shoulder.position + right * .18f - up * .50f + forward * .08f, wristWeight, .995f, true);
            if (held && Vector3.Distance(actor.RightGrip.position, handTarget) > .025f)
            {
                WorkerHandsMatch = false;
                LastCrewContactFailure = actor.name + ": lunch grip=" +
                    Vector3.Distance(actor.RightGrip.position, handTarget).ToString("F3");
            }
            DriverLunch.SetPositionAndRotation(food.position, food.rotation);
            DriverLunch.gameObject.SetActive(held);
            DriverEating = held && biting;
        }

        private Pose LunchPoseAtGrip(Vector3 grip, Quaternion rotation) =>
            new Pose(grip - rotation * driverLunchGripLocal, rotation);

        private static Pose LunchBlend(Pose first, Pose last, float weight) => new Pose(
            Vector3.Lerp(first.position, last.position, weight), Quaternion.Slerp(first.rotation, last.rotation, weight));
    }
}

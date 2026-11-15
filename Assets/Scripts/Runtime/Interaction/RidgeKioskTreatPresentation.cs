using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Hero serves the drinks and the table shot holds until both men pick theirs up.</summary>
    internal sealed class RidgeKioskTreatPresentation : IDialogueIntroduction
    {
        private const float Draw = .24f, Lift = .55f, OverTable = .84f, Contact = 1.08f, End = 1.50f;
        private const float ContactTolerance = .023f;
        private const float PickupTimeout = 7f;
        private readonly Transform hero, top, spine, upper, lower, wrist, pelvis;
        private readonly NpcHandPose hands;
        private readonly RidgeKioskCourtLife owner;
        private readonly BarPatronDrinkProp[] drinks;
        private readonly Func<bool> transfer, pickedUp;
        private readonly NpcHeroUpperBodyPose.Binding pose;
        private float elapsed;
        private int serving;
        private bool captured, committed, released;

        internal RidgeKioskTreatPresentation(RidgeKioskCourtLife owner, Player3DAssetRegistry registry,
            Transform hero, Transform top, BarPatronDrinkProp first, BarPatronDrinkProp second,
            Func<bool> transfer, Func<bool> pickedUp)
        {
            this.owner = owner;
            this.hero = hero;
            this.top = top;
            this.transfer = transfer;
            this.pickedUp = pickedUp;
            drinks = new[] { first, second };
            pose = new NpcHeroUpperBodyPose.Binding(registry.ModelRoot);
            hands = registry.GetComponent<NpcHandPose>();
            pelvis = registry.Anchors.Pelvis;
            spine = CityPedestrianHandProps.FindSocket(registry.ModelRoot, "spine");
            upper = Bone(Player3DAnatomicalPart.RightUpperArm);
            lower = Bone(Player3DAnatomicalPart.RightForearm);
            wrist = Bone(Player3DAnatomicalPart.RightHand);
            Transform Bone(Player3DAnatomicalPart part) =>
                registry.TryGetPart(part, out var binding) ? binding.Bone : null;
        }

        internal bool IsReady => hands && pelvis && spine && upper && lower && wrist;

        public bool Present(float seconds, out bool failed)
        {
            failed = false;
            if (released || !IsReady) { failed = true; return false; }
            if (serving >= drinks.Length)
            {
                if (pickedUp()) return true;
                failed = (elapsed += seconds) >= PickupTimeout;
                return false;
            }
            BarPatronDrinkProp drink = drinks[serving];
            if (!captured)
            {
                captured = hands.TryCaptureGrip(owner, false, drink.GripKind, exclusive: true);
                if (!captured) { failed = true; return false; }
            }

            float previous = elapsed;
            elapsed = Mathf.Min(End, elapsed + seconds);
            if (previous < Draw) elapsed = Mathf.Min(elapsed, Draw);
            else if (previous < Contact) elapsed = Mathf.Min(elapsed, Contact);
            pose.Capture();

            Quaternion rotation = Quaternion.LookRotation(-hero.forward, Vector3.up);
            AnatomicalHandFrame anatomy = hands.GetAnatomicalFrame(false, drink.GripKind);
            Vector3 targetBase = RidgeKioskCourtPlan.GiftPoint(top.position, serving);
            Vector3 target = targetBase + rotation * (drink.GripLocalPosition - drink.BaseLocalPosition);
            Vector3 pocket = pelvis.position + hero.right * .19f + hero.forward * .12f + Vector3.up * .13f;
            Vector3 lift = targetBase + hero.right * (.17f - Vector3.Dot(targetBase - top.position, hero.right)) -
                hero.forward * .31f;
            lift.y = Mathf.Max(target.y + .20f, top.position.y + .28f);
            Vector3 raised = target + Vector3.up * .14f;
            float lean = Ramp(Lift - .12f, Contact - .12f) * (1f - Ramp(Contact + .09f, End - .12f));
            Vector3 tow = Vector3.ProjectOnPlane(target - spine.position, Vector3.up).normalized;
            float reachLean = serving == 0 ? 34f : 52f;
            spine.rotation = Quaternion.AngleAxis(reachLean * lean, Vector3.Cross(Vector3.up, tow)) * spine.rotation;

            Vector3 goal = elapsed <= Draw ? pocket : elapsed <= Lift ? Vector3.Lerp(pocket, lift, Ramp(Draw, Lift)) :
                elapsed <= OverTable ? Vector3.Lerp(lift, raised, Ramp(Lift, OverTable)) :
                elapsed <= Contact ? Vector3.Lerp(raised, target, Ramp(OverTable, Contact)) :
                elapsed <= Contact + .16f ? Vector3.Lerp(target, raised, Ramp(Contact, Contact + .16f)) :
                Vector3.Lerp(raised, lift, Ramp(Contact + .16f, End - .06f));
            Pose contact = anatomy.ResolveCylinderPose(goal, Vector3.up, -(rotation * Vector3.forward));
            float weight = Ramp(0f, Draw) * (1f - Ramp(Contact + .20f, End));
            LimbTwoBoneIk.Solve(upper, lower, wrist, contact.position, contact.rotation,
                upper.position + hero.right * .22f - Vector3.up * .27f, weight, .99f, true);
            hands.SetOwnedGrip(owner, false, Ramp(Draw - .08f, Draw) * (1f - Ramp(Contact, Contact + .08f)));
            Vector3 grip = wrist.position + wrist.rotation * anatomy.CylinderCentreInHand;
            if (elapsed >= Draw && elapsed <= Contact)
            {
                drink.Root.SetPositionAndRotation(drink.RootPositionAtGrip(grip, rotation), rotation);
                if (!drink.Root.gameObject.activeSelf) drink.Root.gameObject.SetActive(true);
            }

            pose.IncludeContactCorrections();

            if (elapsed == Contact)
            {
                if (Vector3.Distance(grip, target) > ContactTolerance)
                {
                    GameLog.Info("kiosk", "treat_contact_missed",
                        GameLog.Field("serving", serving),
                        GameLog.Field("error_m", Vector3.Distance(grip, target)));
                    failed = true;
                    return false;
                }
                drink.Root.SetPositionAndRotation(targetBase - rotation * drink.BaseLocalPosition, rotation);
                if (serving == 1 && !committed)
                {
                    committed = transfer();
                    if (!committed) { failed = true; return false; }
                }
            }

            if (elapsed < End) return false;
            pose.Restore();
            hands.ReleaseGrip(owner, false);
            captured = false;
            serving++;
            elapsed = 0f;
            return false;
        }

        public void Release()
        {
            if (released) return;
            released = true;
            pose.Restore();
            if (captured && hands) hands.ReleaseGrip(owner, false);
            if (committed) return;
            foreach (BarPatronDrinkProp drink in drinks)
                if (drink.Root)
                {
                    drink.Root.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(drink.Root.gameObject);
                }
        }

        private float Ramp(float from, float to) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, elapsed));

    }
}

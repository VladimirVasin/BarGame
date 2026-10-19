using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Warehouse-local staging and admission; the shared narrative session owns all presentation.</summary>
    public static class WarehouseValveInspection
    {
        public const string PromptKey = "interaction.warehouse_valve";
        public const string LockedKey = "combat.warehouse.valve.locked";
        public const string WheelPivotName = "Warehouse Valve Wheel Pivot";
        public const string ChainPivotName = "Warehouse Valve Chain Pivot";
        public const string LockPivotName = "Warehouse Valve Lock Pivot";
        public const float AttemptSeconds = 1.2f;

        public static NarrativeInteraction Create(GameObject control, WarehouseArenaPlan plan, CombatTestRoot owner)
        {
            if (control == null || plan == null) throw new ArgumentNullException();
            var subject = new GameObject("Warehouse Valve Focus");
            subject.transform.SetParent(control.transform, false);
            Bounds bounds = default;
            bool measured = false;
            foreach (MeshRenderer renderer in control.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.name.StartsWith("DriveRiser", StringComparison.Ordinal) ||
                    renderer.name.StartsWith("GateDriveLink", StringComparison.Ordinal) ||
                    renderer.name.StartsWith("RiserBrackets", StringComparison.Ordinal)) continue;
                // Imported centimetre roots retain their exact world transform and scale.
                // This groups existing meshes for optical framing; it authors no geometry.
                renderer.transform.SetParent(subject.transform, true);
                if (!measured) { bounds = renderer.bounds; measured = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (!measured) throw new InvalidOperationException("The valve inspection needs its visible wheel and lock.");
            Vector3 floor = control.transform.TransformPoint(plan.GateControlApproach - plan.GateControlPosition);
            // Include the landing strand in the same uninterrupted close-up.
            bounds.Encapsulate(control.transform.TransformPoint(new Vector3(-.72f, .025f, -.85f)));
            bounds.Encapsulate(control.transform.TransformPoint(new Vector3(.98f, .15f, -.5f)));
            Vector3 facing = bounds.center - floor;
            facing.y = 0f;
            var staging = NarrativeStagingPlan.Standing(floor, Quaternion.LookRotation(facing), bounds,
                Vector3.right, NarrativeCameraMode.ObjectCloseUp, new Vector3(.12f, 1.2f, -1f).normalized,
                authoredFocusBoundsOnly: true);
            var definition = new NarrativeInteractionDefinition("warehouse-valve", PromptKey,
                new[] { new NarrativePage(LockedKey, compact: true) }, requiresRunningGameTime: false);
            var host = new GameObject("Warehouse Valve Inspection");
            host.transform.SetParent(control.transform, false);
            var inspection = host.AddComponent<NarrativeInteraction>();
            inspection.Configure(definition, subject.transform, staging, floor + Vector3.up * .85f);
            inspection.InspectionAttemptSeconds = AttemptSeconds;
            var gate = control.AddComponent<WarehouseGateController>();
            BindAttempt(control, subject.transform, inspection, gate);
            inspection.Admission = interactor => owner != null && owner.IsInitialized &&
                owner.Player.Interactor == interactor &&
                owner.Hero != null && owner.Hero.CanBeginStandingInspection && !gate.IsOpen;
            bool ownsFocus = false, previousFocus = false;
            inspection.PreparingPlayer += interactor =>
            {
                inspection.Activity = gate.IsUnlocked || GameSessionState.HasInventoryItem(InventoryItemId.WarehouseGateKey) ? gate : null;
                previousFocus = owner.Hero.CombatFocused;
                ownsFocus = true;
                owner.Hero.PrepareStandingInspection();
            };
            inspection.RestoringPlayer += interactor =>
            {
                if (!ownsFocus) return;
                ownsFocus = false;
                if (owner != null && owner.Hero != null)
                    owner.Hero.RestoreStandingInspectionFocus(previousFocus);
            };
            return inspection;
        }

        private static void BindAttempt(GameObject control, Transform subject, NarrativeInteraction inspection, WarehouseGateController gate)
        {
            Transform wheelAnchor = WarehouseArenaAssetProvider.FindAnchor(control, "Wheel");
            Transform lockAnchor = WarehouseArenaAssetProvider.FindAnchor(control, "Lock");
            Transform wheel = CreatePivot(subject, WheelPivotName, wheelAnchor.position, control.transform.rotation);
            // The existing chain closes around the fixed lug at this authored offset from Wheel.
            // Its attachment stays fixed while the lower loop and lock are pulled forward.
            Vector3 attachment = wheelAnchor.position + control.transform.TransformVector(new Vector3(.23f, .5f, .09f));
            Transform chain = CreatePivot(subject, ChainPivotName, attachment, control.transform.rotation);
            Transform padlock = CreatePivot(chain, LockPivotName, lockAnchor.position, control.transform.rotation);
            int wheelParts = 0, chainParts = 0, lockParts = 0;
            foreach (MeshRenderer renderer in subject.GetComponentsInChildren<MeshRenderer>(true))
            {
                Transform pivot;
                if (renderer.name.StartsWith("WheelRim", StringComparison.Ordinal) ||
                    renderer.name.StartsWith("WheelSpokes", StringComparison.Ordinal) ||
                    renderer.name.StartsWith("WheelHub", StringComparison.Ordinal))
                { pivot = wheel; wheelParts++; }
                else if (renderer.name.StartsWith("YellowChain", StringComparison.Ordinal))
                { pivot = chain; chainParts++; }
                else if (renderer.name.StartsWith("Padlock", StringComparison.Ordinal) ||
                    renderer.name.StartsWith("Keyhole", StringComparison.Ordinal))
                { pivot = padlock; lockParts++; }
                else continue;
                // Preserve imported centimetre scale, complete world pose and existing shared materials.
                renderer.transform.SetParent(pivot, true);
            }
            if (wheelParts != 3 || chainParts != 1 || lockParts != 4)
                throw new InvalidOperationException("The valve attempt needs its rim, spokes, hub, linked chain and complete padlock.");
            var restWheel = new RestPose(wheel);
            var restChain = new RestPose(chain);
            var restLock = new RestPose(padlock);
            inspection.AttemptProgress += progress =>
            {
                if (!gate.IsUnlocked) SampleAttempt(progress, restWheel, restChain, restLock);
            };
            SampleAttempt(0f, restWheel, restChain, restLock);
            gate.Initialize(inspection, wheel, chain, padlock);
        }

        private static Transform CreatePivot(Transform parent, string name, Vector3 position, Quaternion rotation)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(parent, false);
            pivot.SetPositionAndRotation(position, rotation);
            return pivot;
        }

        private static void SampleAttempt(float progress, RestPose wheel, RestPose chain, RestPose padlock)
        {
            // Shared session time is the only clock. Absolute samples stay deterministic through
            // pauses/hitches, and its completion/cancellation/disable path always sends zero.
            if (float.IsNaN(progress) || float.IsInfinity(progress) || progress <= 0f || progress >= 1f)
            {
                wheel.Restore(); chain.Restore(); padlock.Restore();
                return;
            }
            float pull = PullPulse(progress, .06f, .50f) + .7f * PullPulse(progress, .53f, .91f);
            float shudder = pull * Mathf.Sin(progress * Mathf.PI * 18f);
            if (wheel.Target != null)
                wheel.Target.localRotation = wheel.Rotation * Quaternion.AngleAxis(8f * pull + .4f * shudder, Vector3.forward);
            if (chain.Target != null)
                chain.Target.localRotation = chain.Rotation * Quaternion.Euler(2.6f * pull + .25f * shudder,
                    0f, 2.2f * pull + .35f * shudder);
            if (padlock.Target != null)
                padlock.Target.localRotation = padlock.Rotation * Quaternion.AngleAxis(-6f * pull - .75f * shudder, Vector3.forward);
        }

        private static float PullPulse(float progress, float start, float end)
        {
            if (progress <= start || progress >= end) return 0f;
            float pulse = Mathf.Sin((progress - start) / (end - start) * Mathf.PI);
            return pulse * pulse;
        }

        private readonly struct RestPose
        {
            public readonly Transform Target;
            public readonly Quaternion Rotation;
            private readonly Vector3 position, scale;

            public RestPose(Transform target)
            {
                Target = target; position = target.localPosition;
                Rotation = target.localRotation; scale = target.localScale;
            }

            public void Restore()
            {
                if (Target == null) return;
                Target.SetLocalPositionAndRotation(position, Rotation);
                Target.localScale = scale;
            }
        }
    }
}

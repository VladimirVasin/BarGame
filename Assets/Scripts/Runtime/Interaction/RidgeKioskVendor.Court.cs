using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class RidgeKioskVendor
    {
        private enum VendorPhase { Counter, ToDoor, Open, Cross, Close, ToDestination, Smoke, FinishSmoke }
        private const float DoorSeconds = 1.10f, WalkingSpeed = .85f;
        private RidgeKioskStation courtStation;
        private RidgeKioskCourtWorld courtWorld;
        private Transform door;
        private CharacterController sellerBody;
        private readonly NpcAttentionHeadLayer customerLook = new();
        private VendorPhase phase;
        private Vector3 counter, insideDoor, outsideDoor;
        private bool returning;
        private double nextBreak = double.NaN, previousMinute = double.NaN;
        private float elapsed, gait, idleClock, smokeTime, handoverWeight;
        private Vector3? handoverPoint;
        private NpcHeroUpperBodyPose.Binding smokePose;
        private AnimationClip smokeClip;
        private PlayerAnimatedInteractionDefinition smokeDefinition;
        private HomeBalconySmokingExhaleEffect exhale;
        private CityPedestrianHandPropRegistry cigarette;
        internal bool AtCounter => phase == VendorPhase.Counter;
        internal bool IsSmoking => phase == VendorPhase.Smoke || phase == VendorPhase.FinishSmoke;

        internal void ConfigureCourt(RidgeKioskCourtWorld court, Transform hinge, RidgeKioskStation station)
        {
            courtStation = station;
            courtWorld = court;
            door = hinge;
            counter = Actor.transform.position;
            insideDoor = kiosk.TransformPoint(new Vector3(-.84f, .08f, -.21f));
            outsideDoor = kiosk.TransformPoint(new Vector3(-2.22f, 0f, -.21f));
            sellerBody = Actor.gameObject.AddComponent<CharacterController>();
            sellerBody.height = 1.69f;
            sellerBody.center = Vector3.up * .845f;
            sellerBody.radius = .235f;
            sellerBody.skinWidth = .024f;
            sellerBody.stepOffset = .16f;
            sellerBody.minMoveDistance = 0f;
            customerLook.Bind(Actor.transform, Actor.Head, Actor.Animator);
            smokeDefinition = CityBalconySmokerPresentation.CreateAnimationDefinition();
            var hero = Player3DResources.LoadPrefab().GetComponent<Player3DAssetRegistry>();
            if (!hero.TryGetAnimation(smokeDefinition.LoopClipName, out var binding) || !binding.Clip)
                throw new InvalidOperationException("The kiosk seller needs the shared smoking clip.");
            smokeClip = binding.Clip;
            Transform mouth = CityPedestrianHandProps.FindSocket(Actor.ModelRoot, CityPedestrianHandProps.MouthSocketName);
            exhale = Actor.gameObject.AddComponent<HomeBalconySmokingExhaleEffect>();
            exhale.Initialize(mouth, smokeDefinition);
            exhale.EnableManualBurstMode();
            Transform socket = CityPedestrianHandProps.FindSocket(Actor.ModelRoot, CityPedestrianHandPropId.Cigarette);
            if (!socket)
            {
                Transform source = CityPedestrianHandProps.FindSocket(hero.ModelRoot, CityPedestrianHandPropId.Cigarette);
                Transform hand = CityPedestrianHandProps.FindSocket(Actor.ModelRoot, BarPatronWorldBuilder.RightHandBoneName);
                socket = new GameObject(source.name).transform;
                socket.SetParent(hand, false);
                socket.SetLocalPositionAndRotation(source.localPosition, source.localRotation);
            }
            Material material = Actor.ModelRoot.GetComponentInChildren<Renderer>().sharedMaterial;
            cigarette = CityPedestrianHandProps.Attach(socket, CityPedestrianHandPropId.Cigarette, material, 0);
            cigarette.gameObject.SetActive(false);
            smokePose = new NpcHeroUpperBodyPose.Binding(Actor.ModelRoot);
        }

        internal void SetHandover(Vector3? worldPoint, float weight)
        {
            handoverPoint = worldPoint;
            handoverWeight = weight;
        }

        internal void AdvanceCourtVendor(float step)
        {
            if (step <= 0f) return;
            double now = GameSessionState.GameDayIndex * 1440d + GameSessionState.GameTimeOfDayMinutes;
            bool changedTime = !double.IsNaN(previousMinute) && (now < previousMinute || now - previousMinute > 5d);
            previousMinute = now;
            if (changedTime) ResetCourtVendor();
            if (AtCounter)
            {
                courtStation.ServePendingHero();
                if (courtStation.HasCustomerNearby || double.IsNaN(nextBreak))
                    nextBreak = now + 8d + (GameSessionState.CitySeed & 7);
                if (GameSessionState.IsGameTimeRunning && now >= nextBreak)
                {
                    returning = false;
                    Enter(VendorPhase.ToDoor);
                    NpcFootstepSources.Register(Actor.transform);
                }
                else { PresentAtCounter(step); return; }
            }

            elapsed += step;
            switch (phase)
            {
                case VendorPhase.ToDoor:
                    if (WalkTo(returning ? outsideDoor : insideDoor, step)) Enter(VendorPhase.Open);
                    break;
                case VendorPhase.Open:
                    if (MoveDoor(true, step)) Enter(VendorPhase.Cross);
                    break;
                case VendorPhase.Cross:
                    if (WalkTo(returning ? insideDoor : outsideDoor, step)) Enter(VendorPhase.Close);
                    break;
                case VendorPhase.Close:
                    if (MoveDoor(false, step)) Enter(VendorPhase.ToDestination);
                    break;
                case VendorPhase.ToDestination:
                    if (!returning && courtStation.HasCustomerNearby) { ReturnToCounter(); break; }
                    if (WalkTo(returning ? counter : courtWorld.Plan.SmokePoint, step))
                    {
                        if (returning)
                        {
                            if (!Face(kiosk.forward, step)) break;
                            nextBreak = double.NaN;
                            NpcFootstepSources.Unregister(Actor.transform);
                            Enter(VendorPhase.Counter);
                        }
                        else if (Face(-kiosk.forward, step))
                        {
                            cigarette.gameObject.SetActive(true);
                            smokeTime = 0f;
                            Enter(VendorPhase.Smoke);
                        }
                    }
                    break;
                case VendorPhase.Smoke:
                    PresentSmoke(step, Mathf.Clamp01(elapsed / .35f));
                    if (elapsed >= 15f || courtStation.HasCustomerNearby) Enter(VendorPhase.FinishSmoke);
                    break;
                case VendorPhase.FinishSmoke:
                    PresentSmoke(step, 1f - Mathf.Clamp01(elapsed / .35f));
                    if (elapsed >= .35f) ReturnToCounter();
                    break;
            }
        }

        private void PresentAtCounter(float step)
        {
            idleClock += step;
            if (!streetCamera) streetCamera = Camera.main;
            Transform focus = Session.IsActive && Session.HeroHead ? Session.HeroHead : courtStation.Customer;
            if (!focus && Time.time < nextIdleSample) return;
            float distance = streetCamera ? (streetCamera.transform.position - transform.position).sqrMagnitude : 0f;
            nextIdleSample = Time.time + (focus || distance < 100f ? 0f : distance < 900f ? .12f : .55f);
            customerLook.Restore();
            float listen = focus ? 1f : .35f + .22f * Mathf.Sin(idleClock * .46f);
            Actor.ApplyIdleVariation(idleClock, true, listen);
            Vector3 rest = kiosk.TransformPoint(new Vector3(.24f, 1.05f, .92f));
            Vector3 palm = handoverPoint.HasValue ? Vector3.Lerp(rest, handoverPoint.Value, handoverWeight) : rest;
            Actor.ApplyHandContacts(palm, null, Mathf.Lerp(.77f, 1f, handoverWeight));
            customerLook.SetFocus(focus ? focus.position : null);
            customerLook.Apply(step);
        }

        private bool WalkTo(Vector3 targetPosition, float step)
        {
            customerLook.Restore();
            Vector3 start = Actor.transform.position;
            Vector3 offset = targetPosition - start;
            Vector3 direction = Vector3.ProjectOnPlane(offset, Vector3.up);
            if (direction.sqrMagnitude > .0025f && !Face(direction, step))
            { Actor.ApplyLocomotion(0f, false, gait); return false; }
            sellerBody.Move(Vector3.ClampMagnitude(offset, WalkingSpeed * step));
            float travel = Vector3.ProjectOnPlane(Actor.transform.position - start, Vector3.up).magnitude;
            gait += travel / WalkingSpeed;
            Actor.ApplyLocomotion(travel / step, false, gait);
            return (targetPosition - Actor.transform.position).sqrMagnitude < .0036f;
        }

        private bool Face(Vector3 direction, float step)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
            Actor.transform.rotation = Quaternion.RotateTowards(Actor.transform.rotation, targetRotation, 300f * step);
            return Quaternion.Angle(Actor.transform.rotation, targetRotation) < 5f;
        }

        private bool MoveDoor(bool opening, float step)
        {
            Vector3 normal = returning == opening ? kiosk.right : -kiosk.right;
            Face(normal, step);
            float t = Mathf.Clamp01(elapsed / DoorSeconds);
            float swing = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.20f, .82f, t));
            door.localRotation = Quaternion.Euler(0f, -96f * (opening ? swing : 1f - swing), 0f);
            Vector3 handle = door.TransformPoint(new Vector3(-.063f, 1.01f, .687f));
            Actor.ApplyDoor(opening ? VillageResidentAction.DoorOpen : VillageResidentAction.DoorClose,
                t * VillageResidentPresentation.DoorDurationSeconds, handle);
            return t >= 1f;
        }

        private void PresentSmoke(float step, float weight)
        {
            float previous = smokeTime;
            smokeTime = Mathf.Repeat(previous + step, exhale.LoopDurationSeconds);
            float clipTime = CityBalconySmokerPresentation.ResolveClipProgress(smokeDefinition, smokeTime) * smokeClip.length;
            Actor.ApplyIdleVariation(gait + elapsed, true, 0f);
            courtWorld.HeroActions.Apply(smokePose, smokeClip, clipTime, weight, additive: false);
            if (phase == VendorPhase.Smoke && (previous <= exhale.BurstTimeSeconds && smokeTime > exhale.BurstTimeSeconds ||
                smokeTime < previous && smokeTime > exhale.BurstTimeSeconds)) exhale.EmitManualBurst();
        }

        private void ReturnToCounter()
        {
            smokePose?.Restore();
            cigarette.gameObject.SetActive(false);
            exhale.StopAndClear();
            returning = true;
            Enter(VendorPhase.ToDoor);
        }

        private void Enter(VendorPhase value) { phase = value; elapsed = 0f; }

        private void ResetCourtVendor()
        {
            if (!courtStation) return;
            customerLook.Restore();
            customerLook.Clear();
            smokePose?.Restore();
            exhale?.StopAndClear();
            if (cigarette) cigarette.gameObject.SetActive(false);
            handoverPoint = null;
            handoverWeight = 0f;
            nextBreak = double.NaN;
            NpcFootstepSources.Unregister(Actor.transform);
            if (sellerBody) sellerBody.enabled = false;
            if (Actor && kiosk) Actor.transform.SetPositionAndRotation(counter, kiosk.rotation);
            if (sellerBody) sellerBody.enabled = true;
            if (door) door.localRotation = Quaternion.identity;
            Enter(VendorPhase.Counter);
        }
    }
}

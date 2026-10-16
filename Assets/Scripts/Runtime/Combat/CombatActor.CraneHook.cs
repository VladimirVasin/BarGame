using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum CombatCraneHookPhase { Stowed, Outbound, Holding, Returning, Released }

    public sealed partial class CombatActor
    {
        public const float CraneHookMaximumLength = 6f;
        private GameObject craneHookFrame, craneCuffFrame;
        private Transform craneHookEyeAnchor, craneHookImpact, craneCableExit, craneAttackCable;
        private Vector3 craneAttackCableScale, craneHookEye, craneHookVelocity, craneHookInitialVelocity;
        private Quaternion craneHookRotation;
        private readonly Vector3[] craneHookContactFrom = new Vector3[5], craneHookContactTo = new Vector3[5];
        private SkinnedMeshRenderer[] craneHiddenHands;
        private Rigidbody craneHookBody, craneCuffBody;
        private Collider craneHookCollider, craneCuffCollider;
        private Vector3 craneDroppedVelocity, craneDroppedSpin, craneDroppedCuffVelocity, craneDroppedCuffSpin;
        private bool craneHookWorldBlocked, craneDropPaused, craneCuffDropped;
        private float craneHookTime, craneHookDuration, craneHookHold;
        private Vector3 craneHookWorldPoint, craneHookWorldNormal;
        public CombatCraneHookPhase CraneHookPhase { get; private set; } = CombatCraneHookPhase.Stowed;
        public Vector3 CraneHookPosition => craneHookImpact != null ? craneHookImpact.position : transform.position;
        public Vector3 CraneHookEyePosition => craneHookEyeAnchor != null ? craneHookEyeAnchor.position : transform.position;
        public Vector3 CraneHookCableExit => craneCableExit != null ? craneCableExit.position : craneHand.position;
        public float CraneHookCableLength => Vector3.Distance(CraneHookCableExit, CraneHookEyePosition);
        public Vector3 CraneHookAimDirection { get; private set; }
        public Vector3 CraneHookFlightOrigin { get; private set; }
        public int CraneHookThrowCount { get; private set; }
        internal Transform GetCraneHookTransform() => craneHookFrame.transform;
        internal Transform GetCraneHookCableTransform() => craneAttackCable;
        internal bool CraneRightHandVisible
        {
            get { foreach (Renderer hand in craneHiddenHands) if (hand.enabled && hand.gameObject.activeInHierarchy) return true; return false; }
        }

        private static Vector3 CraneTargetPoint(CombatActor target) => target.transform.position + Vector3.up * 1.2f;

        private void InitializeCraneHook()
        {
            var hidden = new List<SkinnedMeshRenderer>();
            foreach (SkinnedMeshRenderer skin in DamageRigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (Array.IndexOf(CombatCraneAssetProvider.RightHandRendererNames, skin.name) >= 0)
                { skin.enabled = false; hidden.Add(skin); }
            craneHiddenHands = hidden.ToArray();
            if (craneHiddenHands.Length == 0) throw new InvalidOperationException("Crane replacement could not locate the ordinary right hand.");
            craneHookFrame = new GameObject("Crane hook metre frame"); craneHookFrame.transform.SetParent(craneSuspensionHost.transform, false);
            GameObject hook = CombatCraneAssetProvider.CreateHook(craneHookFrame.transform);
            craneHookEyeAnchor = CityPedestrianHandProps.FindSocket(hook.transform, CombatCraneAssetProvider.HookCableAnchor);
            craneHookImpact = CityPedestrianHandProps.FindSocket(hook.transform, CombatCraneAssetProvider.HookImpactAnchor);
            craneCuffFrame = new GameObject("Crane wrist metre frame"); craneCuffFrame.transform.SetParent(craneSuspensionHost.transform, false);
            GameObject cuff = CombatCraneAssetProvider.CreateWristSocket(craneCuffFrame.transform);
            craneCableExit = CityPedestrianHandProps.FindSocket(cuff.transform, CombatCraneAssetProvider.WristCableAnchor);
            if (craneHookEyeAnchor == null || craneHookImpact == null || craneCableExit == null) throw new InvalidOperationException("Missing crane hook attachment.");
            craneAttackCable = CombatCraneAssetProvider.CreateCableSegment(craneSuspensionHost.transform).transform;
            craneAttackCable.name = "Crane attack cable"; craneAttackCableScale = craneAttackCable.localScale;
            craneHookBody = craneHookFrame.AddComponent<Rigidbody>(); craneHookBody.isKinematic = true; craneHookBody.useGravity = false;
            craneHookBody.mass = CombatCraneAssetProvider.HookMassKilograms;
            var hookCollider = craneHookFrame.AddComponent<CapsuleCollider>(); hookCollider.direction = 1;
            hookCollider.center = Vector3.up * .3f; hookCollider.height = .62f; hookCollider.radius = .12f;
            craneHookCollider = hookCollider; craneHookCollider.enabled = false;
            craneCuffBody = craneCuffFrame.AddComponent<Rigidbody>(); craneCuffBody.isKinematic = true; craneCuffBody.useGravity = false;
            craneCuffBody.mass = 1f;
            var cuffCollider = craneCuffFrame.AddComponent<SphereCollider>(); cuffCollider.radius = .09f;
            craneCuffCollider = cuffCollider; craneCuffCollider.enabled = false;
        }

        internal bool IsCraneReplacedHandSurface(SkinnedMeshRenderer source) => IsCrane &&
            Array.IndexOf(CombatCraneAssetProvider.RightHandRendererNames, source.name) >= 0;

        private void ResetCraneHook()
        {
            if (craneHookFrame == null) return;
            CombatCorpseTraversal.ReleaseCollider(craneHookCollider); CombatCorpseTraversal.ReleaseCollider(craneCuffCollider);
            craneHookBody.isKinematic = craneCuffBody.isKinematic = true;
            craneHookBody.useGravity = craneCuffBody.useGravity = false;
            craneHookCollider.enabled = craneCuffCollider.enabled = false;
            craneHookFrame.SetActive(true); craneCuffFrame.SetActive(true); craneAttackCable.gameObject.SetActive(true);
            craneDropPaused = craneCuffDropped = false; CraneHookPhase = CombatCraneHookPhase.Stowed;
            CraneHookThrowCount = 0; craneHookTime = craneHookHold = 0f;
            craneHookWorldBlocked = false; craneHookVelocity = Vector3.zero;
            foreach (Renderer hand in craneHiddenHands) hand.enabled = false;
        }

        private void LaunchCraneHook()
        {
            Vector3 target = contactTarget != null ? CraneTargetPoint(contactTarget) : CraneHookCableExit + craneDirection * 4f;
            Vector3 direction = (target - CraneHookCableExit).normalized;
            if (direction.sqrMagnitude < .5f) direction = transform.forward;
            CraneHookAimDirection = craneDirection = direction;
            craneHookEye = CraneHookFlightOrigin = CraneHookCableExit + direction * .12f;
            Vector3 targetEye = target - direction * CombatCraneAssetProvider.HookImpactOffsetMetres;
            targetEye = CraneHookCableExit + Vector3.ClampMagnitude(targetEye - CraneHookCableExit, CraneHookMaximumLength);
            craneHookDuration = Mathf.Clamp(Vector3.Distance(craneHookEye, targetEye) / 11f, .2f, .7f);
            craneHookInitialVelocity = (targetEye - craneHookEye) / craneHookDuration + Vector3.up * (4.905f * craneHookDuration);
            craneHookVelocity = craneHookInitialVelocity; craneHookTime = craneHookHold = 0f;
            craneHookRotation = Quaternion.FromToRotation(Vector3.up, direction);
            CraneHookPhase = CombatCraneHookPhase.Outbound; CraneHookThrowCount++;
            craneContactValid = false; craneHookWorldBlocked = false;
            craneSwayVelocity -= Vector3.ProjectOnPlane(direction, Vector3.up) * .65f;
            craneRelease.clip = CombatCraneAssetProvider.LoadStartSound(); craneRelease.pitch = 1.4f; craneRelease.Play();
        }

        private void AdvanceCraneHook(float seconds)
        {
            if (CraneHookPhase == CombatCraneHookPhase.Holding) { craneHookHold += seconds; return; }
            if (CraneHookPhase is not (CombatCraneHookPhase.Outbound or CombatCraneHookPhase.Returning)) return;
            bool outbound = CraneHookPhase == CombatCraneHookPhase.Outbound;
            Vector3 from = craneHookEye, wanted;
            if (outbound)
            {
                craneHookTime += seconds;
                float time = Mathf.Min(craneHookTime, craneHookDuration + .08f);
                wanted = CraneHookFlightOrigin + craneHookInitialVelocity * time + Vector3.down * (4.905f * time * time);
                craneHookVelocity = craneHookInitialVelocity + Vector3.down * (9.81f * time);
                wanted = CraneHookCableExit + Vector3.ClampMagnitude(wanted - CraneHookCableExit, CraneHookMaximumLength - .08f);
            }
            else wanted = Vector3.MoveTowards(from, CraneHookCableExit + Vector3.down * .1f, seconds * 8f);
            float fraction = CraneHookWorldFraction(from, wanted, craneHookRotation);
            craneHookWorldBlocked = fraction <= 1f;
            if (craneHookWorldBlocked) wanted = Vector3.Lerp(from, wanted, Mathf.Max(0f, fraction - .005f));
            craneHookEye = wanted;
            if (outbound)
            {
                collectSweep = true; sweepFrom = Mathf.Max(0f, State.AttackElapsed - seconds); sweepTo = State.AttackElapsed; pendingSequence = State.AttackSequence;
                if (craneHookWorldBlocked || craneHookTime >= craneHookDuration + .08f || CraneHookCableLength >= CraneHookMaximumLength - .02f)
                { CraneHookPhase = CombatCraneHookPhase.Holding; craneHookHold = 0f; craneSwayVelocity += Vector3.ProjectOnPlane(CraneHookAimDirection, Vector3.up) * .4f; }
            }
            else if (!craneHookWorldBlocked && Vector3.Distance(craneHookEye, CraneHookCableExit) < .13f)
            { CraneHookPhase = CombatCraneHookPhase.Stowed; craneWinch.Stop(); }
        }

        private float CraneHookWorldFraction(Vector3 from, Vector3 to, Quaternion rotation)
        {
            float fraction = float.PositiveInfinity; Vector3 point = default, normal = default;
            for (int i = -1; i < CombatCraneAssetProvider.HookContactPoints.Length; i++)
            {
                Vector3 offset = rotation * (i < 0 ? Vector3.up * .1f : CombatCraneAssetProvider.HookContactPoints[i]);
                float radius = i < 0 ? .08f : CombatCraneAssetProvider.HookContactRadii[i];
                GatherWorldSegment(to + offset, to + offset, radius, ref fraction, ref point, ref normal);
                GatherWorldSweep(from + offset, to + offset, radius, ref fraction, ref point, ref normal);
            }
            if (fraction <= 1f) { craneHookWorldPoint = point; craneHookWorldNormal = normal; }
            return fraction;
        }

        private void BeginCraneHookReturn()
        {
            CraneHookPhase = CombatCraneHookPhase.Returning; collectSweep = false;
            craneWinch.pitch = 1.12f; if (!craneWinch.isPlaying) craneWinch.Play();
        }

        private void StopCraneHookFlight()
        {
            if (CraneHookPhase == CombatCraneHookPhase.Outbound)
            { CraneHookPhase = CombatCraneHookPhase.Holding; craneHookHold = 0f; }
        }

        private void PresentCraneHook()
        {
            foreach (Renderer hand in craneHiddenHands) hand.enabled = false;
            PresentCraneCuff();
            if (CraneHookPhase == CombatCraneHookPhase.Released) return;
            if (CraneHookPhase == CombatCraneHookPhase.Stowed)
            {
                GetCraneStowedHookPose(out craneHookEye, out craneHookRotation);
            }
            craneHookFrame.transform.SetPositionAndRotation(craneHookEye, craneHookRotation);
            Vector3 delta = CraneHookEyePosition - CraneHookCableExit;
            craneAttackCable.SetPositionAndRotation(CraneHookCableExit, Quaternion.FromToRotation(Vector3.up, delta));
            Vector3 scale = craneAttackCableScale; scale.y *= delta.magnitude; craneAttackCable.localScale = scale;
        }

        private void PresentCraneCuff()
        {
            if (craneCuffDropped) return;
            Vector3 direction = CraneAiming ? craneDirection : craneHand.TransformDirection(Vector3.up);
            craneCuffFrame.transform.SetPositionAndRotation(craneHand.position, Quaternion.FromToRotation(Vector3.up, direction));
        }

        private void GetCraneStowedHookPose(out Vector3 eye, out Quaternion rotation)
        {
            Vector3 cuffDirection = CraneAiming ? craneDirection : craneHand.TransformDirection(Vector3.up);
            Vector3 exit = craneHand.position + cuffDirection.normalized * CombatCraneAssetProvider.WristCableExitMetres;
            float weight = CranePhase == CombatCranePhase.Signal ? Mathf.SmoothStep(0f, 1f, craneClock / CraneSignalSeconds) : 0f;
            Vector3 direction = Vector3.Slerp(Vector3.down, craneDirection, weight).normalized;
            eye = exit + direction * .1f;
            rotation = Quaternion.FromToRotation(Vector3.up, direction);
        }

        private void CaptureCraneContactPose()
        {
            for (int i = 0; i < craneHookContactTo.Length; i++)
            {
                Vector3 point = craneHookFrame.transform.TransformPoint(CombatCraneAssetProvider.HookContactPoints[i]);
                craneHookContactFrom[i] = craneContactValid ? craneHookContactTo[i] : point; craneHookContactTo[i] = point;
            }
            craneContactValid = true;
        }

        private void SweepCraneContact(float from, float to, int sequence, List<Contact> pending)
        {
            if (!CraneAttached || !craneContactValid || craneStrikeStopped || sequence != State.AttackSequence) return;
            CombatActor nearest = null; CombatHurtboxes.Hit bodyHit = default; float fraction = float.PositiveInfinity;
            for (int i = 0; i < craneHookContactTo.Length; i++)
                for (int p = 0; p < ContactParticipantCount; p++)
                {
                    CombatActor target = ContactParticipantAt(p);
                    if (target == this || !CanDamageParticipant(target) || target.State.IsDefeated) continue;
                    if (target.Hurtboxes.SweepSphere(craneHookContactFrom[i], craneHookContactTo[i], CombatCraneAssetProvider.HookContactRadii[i], CraneHookAimDirection, out var found) && found.Fraction < fraction)
                    { nearest = target; bodyHit = found; fraction = found.Fraction; }
                }
            if (nearest != null && CranePathVisible(CraneHookCableExit, bodyHit.Point) && State.TryRegisterHit(nearest.GetEntityId().GetHashCode(), sequence))
            {
                pending.Add(new Contact(this, nearest, bodyHit.Point, bodyHit.Normal, CraneHookAimDirection, bodyHit.Location,
                    bodyHit.Part, bodyHit.LocalPoint, weaponSpeed: craneHookVelocity.magnitude, time: Mathf.Lerp(from, to, fraction), bodyRegion: bodyHit.DamageRegion, bodyPatch: bodyHit.DamagePatch));
                StopCraneHookFlight();
            }
            else if (craneHookWorldBlocked)
                pending.Add(Contact.Obstacle(this, null, craneHookWorldPoint, craneHookWorldNormal, CraneHookAimDirection, to, false));
        }

        private void ReleaseCraneHook()
        {
            CraneHookPhase = CombatCraneHookPhase.Released; craneAttackCable.gameObject.SetActive(false);
            craneHookBody.isKinematic = false; craneHookBody.useGravity = true; craneHookCollider.enabled = true;
            craneHookBody.linearVelocity = Vector3.ClampMagnitude(craneHookVelocity + locomotionVelocity, 12f);
            CombatCorpseTraversal.RegisterCollider(this, craneHookCollider, detached: true);
            if (!BodyDamage.CanUseRightHand)
            {
                craneCuffDropped = true;
                craneCuffBody.isKinematic = false; craneCuffBody.useGravity = true; craneCuffCollider.enabled = true;
                CombatCorpseTraversal.RegisterCollider(this, craneCuffCollider, detached: true);
            }
        }

        private void FreezeCraneHook(bool frozen)
        {
            if (craneHookBody == null || CraneHookPhase != CombatCraneHookPhase.Released || frozen == craneDropPaused) return;
            if (frozen)
            {
                craneDroppedVelocity = craneHookBody.linearVelocity; craneDroppedSpin = craneHookBody.angularVelocity; craneHookBody.isKinematic = true;
                if (craneCuffDropped)
                { craneDroppedCuffVelocity = craneCuffBody.linearVelocity; craneDroppedCuffSpin = craneCuffBody.angularVelocity; craneCuffBody.isKinematic = true; }
            }
            else
            {
                craneHookBody.isKinematic = false; craneHookBody.linearVelocity = craneDroppedVelocity; craneHookBody.angularVelocity = craneDroppedSpin;
                if (craneCuffDropped)
                { craneCuffBody.isKinematic = false; craneCuffBody.linearVelocity = craneDroppedCuffVelocity; craneCuffBody.angularVelocity = craneDroppedCuffSpin; }
            }
            craneDropPaused = frozen;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatZombieLoad
    {
        private sealed class Part
        {
            internal CombatZombieLoad Owner;
            internal Transform Bone, Outward;
            internal Vector3 RestPosition, RestScale, Velocity, AngularVelocity;
            internal Quaternion RestRotation;
            internal BoxCollider Attached, Loose;
            internal Rigidbody Body;
            internal CombatZombieMaterialContact Sound;
            internal bool Detached, Core, OwnsRamIgnore, Retired;
            internal float Health = 40f, Age;
        }

        private static readonly Dictionary<Collider, Part> partOwners = new Dictionary<Collider, Part>();
        private readonly List<Part> boards = new List<Part>(32), core = new List<Part>(8);
        private Transform debrisRoot;
        private SkinnedMeshRenderer woodSkin;
        private bool partsFrozen, simulationFrozen;
        private int boundsFrame = -1;
        internal int BoardCount => boards.Count;
        internal int DetachedBoardCount { get; private set; }
        internal BoxCollider GetBoardCollider(int index) => boards[index].Attached;
        internal Transform GetBoardBone(int index) => boards[index].Bone;
        internal Rigidbody GetBoardBody(int index) => boards[index].Body;
        internal bool IsBoardDetached(int index) => boards[index].Detached;
        internal Vector3 CrateDimensions
        {
            get
            {
                Transform frame = envelopeCollider.transform; Vector3 size = envelopeCollider.size;
                return new Vector3(frame.TransformVector(Vector3.right * size.x).magnitude,
                    frame.TransformVector(Vector3.up * size.y).magnitude, frame.TransformVector(Vector3.forward * size.z).magnitude);
            }
        }
        internal bool ContainsTorso => new Bounds(envelopeCollider.center, envelopeCollider.size)
            .Contains(envelopeCollider.transform.InverseTransformPoint(chest.position));
        internal Bounds AttachedBounds
        {
            get
            {
                Bounds result = Collider.bounds;
                foreach (Part part in core) result.Encapsulate(part.Attached.bounds);
                foreach (Part part in boards) if (!part.Detached) result.Encapsulate(part.Attached.bounds);
                return result;
            }
        }

        internal static bool TryReceiveHit(Collider shape, Vector3 point, Vector3 impulse, float damage)
        {
            if (shape == null || !partOwners.TryGetValue(shape, out Part part) || part.Detached ||
                !float.IsFinite(damage) || damage <= 0f) return false;
            part.Owner.owner.NotifyZombieWoodHit();
            if (part.Core) return true;
            part.Health -= damage * part.Owner.owner.State.Settings.IncomingDamageScale;
            if (part.Health <= 0f) part.Owner.DetachBoard(part, point, impulse);
            return true;
        }

        // Authored limb contacts use the same retained boards, real holes and
        // debris bodies as weapon damage. A previously broken panel stays gone.
        internal bool BreakMimicPanel(int boardNumber, Vector3 worldDirection)
        {
            string name = "Board" + boardNumber.ToString("D2");
            foreach (Part part in boards)
            {
                if (part.Bone.name != name) continue;
                if (part.Detached) return false;
                Vector3 outward = worldDirection.normalized;
                Vector3 point = part.Attached.transform.TransformPoint(part.Attached.center);
                part.Health = 0f;
                DetachBoard(part, point + Vector3.up * .025f,
                    (outward * 18f + Vector3.up * 3f) * part.Body.mass);
                return true;
            }
            throw new InvalidOperationException("Missing authored mimic panel " + name);
        }

        internal void BindMaterialAudio(CombatZombieAudio audio)
        {
            foreach (Part part in boards)
            {
                part.Sound = part.Body.gameObject.AddComponent<CombatZombieMaterialContact>();
                part.Sound.Initialize(audio, true);
            }
            foreach (Fish fish in fishParts)
            {
                fish.Sound = fish.Body.gameObject.AddComponent<CombatZombieMaterialContact>();
                fish.Sound.Initialize(audio, false);
            }
        }

        private void InitializeParts()
        {
            debrisRoot = new GameObject("Porter retained debris").transform;
            debrisRoot.SetParent(owner.transform.parent, false);
            foreach (SkinnedMeshRenderer skin in Model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (skin.name == "ZombieCrateMesh") woodSkin = skin;
            if (woodSkin == null) throw new InvalidOperationException("Porter requires a skinned wooden skeleton.");
            foreach (Transform bone in Model.GetComponentsInChildren<Transform>(true))
            {
                if (Numbered(bone.name, "Board")) boards.Add(CreatePart(bone, false));
                else if (Numbered(bone.name, "Core")) core.Add(CreatePart(bone, true));
            }
            if (boards.Count < 8 || boards.Count > 64 || core.Count == 0 || core.Count > 16)
                throw new InvalidOperationException("Porter requires a bounded authored wooden skeleton.");
            Collider = core[0].Attached;
        }

        private static bool Numbered(string name, string prefix) => name.StartsWith(prefix, StringComparison.Ordinal) &&
            name.Length == prefix.Length + 2 && char.IsDigit(name[prefix.Length]) && char.IsDigit(name[prefix.Length + 1]);

        private static void ReadBounds(Transform bone, string minName, string maxName, out Vector3 centre, out Vector3 size)
        {
            Transform min = CityPedestrianHandProps.FindSocket(bone, minName), max = CityPedestrianHandProps.FindSocket(bone, maxName);
            if (min == null || max == null) throw new InvalidOperationException("Porter part lacks measured bounds: " + minName);
            Vector3 a = bone.InverseTransformPoint(min.position), b = bone.InverseTransformPoint(max.position);
            centre = (a + b) * .5f; size = new Vector3(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y), Mathf.Abs(a.z - b.z));
        }

        private Part CreatePart(Transform bone, bool isCore)
        {
            ReadBounds(bone, bone.name + "BoundsMin", bone.name + "BoundsMax", out Vector3 centre, out Vector3 size);
            var part = new Part { Owner = this, Bone = bone, Core = isCore, RestPosition = bone.localPosition,
                RestRotation = bone.localRotation, RestScale = bone.localScale,
                Outward = CityPedestrianHandProps.FindSocket(bone, bone.name + "Outward") };
            part.Attached = bone.gameObject.AddComponent<BoxCollider>();
            part.Attached.center = centre; part.Attached.size = size;
            RegisterPartShape(part.Attached, part, false); IgnoreOwn(part.Attached);
            if (!isCore)
            {
                if (part.Outward == null) throw new InvalidOperationException("A breakable board requires its outward marker.");
                Transform proxy = new GameObject("Porter " + bone.name + " physics").transform;
                proxy.SetParent(debrisRoot, false);
                proxy.SetPositionAndRotation(bone.position, bone.rotation); proxy.localScale = bone.lossyScale;
                part.Loose = proxy.gameObject.AddComponent<BoxCollider>();
                part.Loose.center = centre; part.Loose.size = size; part.Loose.enabled = false;
                part.Body = proxy.gameObject.AddComponent<Rigidbody>();
                part.Body.mass = Mathf.Clamp(Vector3.Scale(size, bone.lossyScale).magnitude * .7f, .25f, 1.4f);
                part.Body.isKinematic = true; part.Body.useGravity = true;
                part.Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                part.Body.linearDamping = .25f; part.Body.angularDamping = .6f; part.Body.maxAngularVelocity = 16f;
                RegisterPartShape(part.Loose, part, true); IgnoreOwn(part.Loose);
            }
            return part;
        }

        private void RegisterPartShape(Collider shape, Part part, bool detached)
        { loadColliders.Add(shape); partOwners.Add(shape, part); CombatCorpseTraversal.RegisterCollider(owner, shape, detached); }

        private void IgnoreOwn(Collider shape)
        {
            foreach (Collider original in ownColliders) if (original != null && original != shape) Physics.IgnoreCollision(shape, original);
        }

        private void ResetParts()
        {
            partsFrozen = simulationFrozen = false; DetachedBoardCount = 0; boundsFrame = -1;
            debrisRoot.gameObject.SetActive(true);
            foreach (Part part in boards)
            {
                part.Sound?.ResetContacts();
                part.Body.isKinematic = true; part.Loose.enabled = false; part.Detached = part.Retired = false; part.Health = 40f; part.Age = 0f;
                part.Bone.SetLocalPositionAndRotation(part.RestPosition, part.RestRotation); part.Bone.localScale = part.RestScale;
                part.Attached.enabled = true;
            }
            foreach (Part part in core) part.Attached.enabled = true;
            RefreshContacts();
        }

        private void RefreshContacts()
        {
            contactSegments.Clear();
            for (int i = 0; i < authoredSegments.Length; i++)
                if (railOwners[i] != null && !railOwners[i].Detached) contactSegments.Add(authoredSegments[i]);
        }

        private void DetachBoard(Part part, Vector3 point, Vector3 impulse)
        {
            if (part.Detached) return;
            Vector3 normal = (part.Outward.position - part.Bone.position).normalized;
            ScheduleSpill(part, normal);
            part.Detached = true; DetachedBoardCount++; part.Age = 0f;
            RestoreRam(part); part.Attached.enabled = false;
            part.Body.transform.localScale = part.Bone.lossyScale;
            part.Body.position = part.Bone.position; part.Body.rotation = part.Bone.rotation;
            part.Loose.enabled = true; part.Body.isKinematic = false;
            Vector3 push = float.IsFinite(impulse.sqrMagnitude) ? Vector3.ClampMagnitude(impulse * .18f, 8f) : Vector3.zero;
            part.Body.linearVelocity = Vector3.ClampMagnitude((PhysicsBody.isKinematic ? owner.AchievedPlanarVelocity : PhysicsBody.GetPointVelocity(point)) + push / part.Body.mass, 8f);
            part.Body.angularVelocity = Vector3.ClampMagnitude(Vector3.Cross(point - part.Body.worldCenterOfMass, push) * 8f, 12f);
            IgnoreOwn(part.Loose);
            foreach (Part other in boards) { Physics.IgnoreCollision(part.Loose, other.Attached); if (other != part) Physics.IgnoreCollision(part.Loose, other.Loose); }
            foreach (Part other in core) Physics.IgnoreCollision(part.Loose, other.Attached);
            foreach (Fish fish in fishParts) if (fish != null) Physics.IgnoreCollision(part.Loose, fish.Shape);
            CombatCorpseTraversal.NotifyColliderEnabled(part.Loose);
            if (partsFrozen) Hold(part.Body, ref part.Velocity, ref part.AngularVelocity, true);
            RefreshContacts();
        }

        private void IgnoreRam(Part part)
        {
            if (Physics.GetIgnoreCollision(part.Attached, ramProxy)) return;
            Physics.IgnoreCollision(part.Attached, ramProxy); part.OwnsRamIgnore = true;
        }

        private void RestoreRam(Part part)
        {
            if (ramProxy != null && part.OwnsRamIgnore)
            { Physics.IgnoreCollision(part.Attached, ramProxy, false); CombatCorpseTraversal.NotifyColliderEnabled(part.Attached); }
            part.OwnsRamIgnore = false;
        }

        internal void SetSimulationFrozen(bool value)
        { simulationFrozen = value; FreezeParts(value || PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused); }

        private void FreezeParts(bool value)
        {
            if (partsFrozen == value) return;
            partsFrozen = value;
            foreach (Part part in boards) if (part.Detached && part.Age < 8f) Hold(part.Body, ref part.Velocity, ref part.AngularVelocity, value);
            foreach (Fish fish in fishParts) if (fish != null && fish.Released && fish.Age < 8f)
                Hold(fish.Body, ref fish.Velocity, ref fish.AngularVelocity, value);
        }

        private static void Hold(Rigidbody body, ref Vector3 velocity, ref Vector3 angular, bool value)
        {
            if (value)
            { if (!body.isKinematic) { velocity = body.linearVelocity; angular = body.angularVelocity; body.isKinematic = true; } }
            else { body.isKinematic = false; body.linearVelocity = velocity; body.angularVelocity = angular; }
        }

        private void Update()
        {
            if (owner == null || Model == null || !Model.gameObject.activeInHierarchy) return;
            RefreshChangedMotorCollisionExclusions();
            FreezeParts(simulationFrozen || PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused);
        }

        private void LateUpdate()
        {
            if (Model == null || !Model.gameObject.activeInHierarchy) return;
            RestoreReleasedPoses();
            int frame = (int)(Time.unscaledTime * (woodSkin.isVisible || catchSkin.isVisible ? 30f : 10f));
            if (frame == boundsFrame) return;
            boundsFrame = frame; UpdatePartBounds();
        }

        private void UpdatePartBounds()
        {
            Bounds wood = default; bool any = false;
            foreach (Part part in core) EncapsulatePart(woodSkin.transform, part.Bone, part.Attached.center, part.Attached.size, ref wood, ref any);
            foreach (Part part in boards) if (!part.Retired) EncapsulatePart(woodSkin.transform, part.Bone, part.Attached.center, part.Attached.size, ref wood, ref any);
            woodSkin.localBounds = wood;
            Bounds catchBounds = ReleasedFishCount < 8 ? attachedCatchBounds : default;
            any = ReleasedFishCount < 8;
            foreach (Fish fish in fishParts) if (fish != null && fish.Released && !fish.Retired)
                EncapsulatePart(catchSkin.transform, fish.Root, fish.Centre, fish.Size * 2f, ref catchBounds, ref any);
            if (any) catchSkin.localBounds = catchBounds;
        }

        private static void EncapsulatePart(Transform renderer, Transform frame, Vector3 centre, Vector3 size, ref Bounds bounds, ref bool any)
        {
            Vector3 half = size * .5f;
            Matrix4x4 toRenderer = renderer.worldToLocalMatrix * frame.localToWorldMatrix;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 local = centre + Vector3.Scale(half, new Vector3((corner & 1) == 0 ? -1f : 1f,
                    (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                Vector3 point = toRenderer.MultiplyPoint3x4(local);
                if (!any) { bounds = new Bounds(point, Vector3.zero); any = true; } else bounds.Encapsulate(point);
            }
        }

        private void ReleaseParts()
        {
            foreach (Part part in core) Unregister(part.Attached);
            foreach (Part part in boards) { Unregister(part.Attached); Unregister(part.Loose); }
            foreach (Fish fish in fishParts) if (fish != null) CombatCorpseTraversal.ReleaseCollider(fish.Shape);
        }

        private static void Unregister(Collider shape)
        { loadColliders.Remove(shape); partOwners.Remove(shape); CombatCorpseTraversal.ReleaseCollider(shape); }
    }
}

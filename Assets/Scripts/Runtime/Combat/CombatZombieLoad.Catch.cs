using System;
using UnityEngine;

namespace BarPromenade
{
    public sealed partial class CombatZombieLoad
    {
        private sealed class Fish
        {
            internal Transform Root;
            internal Vector3 RestPosition, RestScale, Centre, Size, Velocity, AngularVelocity, Exit, Normal, GapUp;
            internal Vector3 PreviousExitOpening, PreviousExitNormal;
            internal Quaternion RestRotation;
            internal readonly Transform[] Bones = new Transform[3];
            internal readonly Vector3[] RestPositions = new Vector3[3], SamplePositions = new Vector3[3];
            internal readonly Quaternion[] RestRotations = new Quaternion[3], SampleRotations = new Quaternion[3];
            internal Rigidbody Body;
            internal CombatZombieMaterialContact Sound;
            internal CapsuleCollider Shape;
            internal bool Released, Pending, Retired, Exiting, ExitAligned;
            internal float Delay, Age, ExitAlignmentTolerance;
            internal int Pulse = -1;
        }

        private readonly Fish[] fishParts = new Fish[8];
        private bool catchSpilledOnFall;
        internal int ReleasedFishCount { get; private set; }
        internal int SettledFishCount { get; private set; }
        internal Transform GetFishRoot(int index) => fishParts[index].Root;
        internal Rigidbody GetFishBody(int index) => fishParts[index].Body;
        internal bool IsFishReleased(int index) => fishParts[index].Released;

        private void InitializeReleasedCatch()
        {
            for (int i = 0; i < fishParts.Length; i++)
            {
                string name = "Fish" + (i + 1).ToString("D2");
                Transform root = CityPedestrianHandProps.FindSocket(Model, name);
                if (root == null) throw new InvalidOperationException("Porter catch requires independent motion root " + name);
                ReadBounds(root, name + "BoundsMin", name + "BoundsMax", out Vector3 centre, out Vector3 size);
                var fish = new Fish { Root = root, RestPosition = root.localPosition, RestRotation = root.localRotation,
                    RestScale = root.localScale, Centre = centre, Size = size };
                string[] suffixes = { "Body", "Head", "Tail" };
                for (int bone = 0; bone < suffixes.Length; bone++)
                {
                    Transform animated = CityPedestrianHandProps.FindSocket(root, "Catch" + (i + 1) + suffixes[bone]);
                    if (animated == null) throw new InvalidOperationException("Porter fish lacks its retained deformation bones.");
                    fish.Bones[bone] = animated;
                    fish.RestPositions[bone] = fish.SamplePositions[bone] = animated.localPosition;
                    fish.RestRotations[bone] = fish.SampleRotations[bone] = animated.localRotation;
                }
                Transform proxy = new GameObject("Porter fish " + (i + 1) + " physics").transform;
                proxy.SetParent(debrisRoot, false); proxy.SetPositionAndRotation(root.position, root.rotation); proxy.localScale = root.lossyScale;
                fish.Shape = proxy.gameObject.AddComponent<CapsuleCollider>();
                // The rounded trunk owns contact; deforming fins and tail must
                // not turn the full animation envelope into a rigid corner.
                int lengthAxis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
                fish.Shape.direction = lengthAxis; fish.Shape.height = size[lengthAxis] * .76f;
                fish.Shape.radius = Mathf.Min(size[(lengthAxis + 1) % 3], size[(lengthAxis + 2) % 3]) * .32f;
                fish.Shape.enabled = false;
                fish.Shape.contactOffset = .002f;
                fish.Body = proxy.gameObject.AddComponent<Rigidbody>(); fish.Body.mass = .7f;
                fish.Body.isKinematic = true; fish.Body.useGravity = true;
                fish.Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                fish.Body.linearDamping = .3f; fish.Body.angularDamping = .7f; fish.Body.maxAngularVelocity = 14f;
                IgnoreOwn(fish.Shape);
                for (int previous = 0; previous < i; previous++) Physics.IgnoreCollision(fish.Shape, fishParts[previous].Shape);
                CombatCorpseTraversal.RegisterCollider(owner, fish.Shape, detached: true);
                fishParts[i] = fish;
            }
        }

        private void ResetReleasedCatch()
        {
            ReleasedFishCount = SettledFishCount = 0; catchSpilledOnFall = false;
            foreach (Fish fish in fishParts)
            {
                fish.Sound?.ResetContacts();
                fish.Body.isKinematic = true; fish.Body.useGravity = true; fish.Body.constraints = RigidbodyConstraints.None;
                fish.Shape.enabled = false;
                fish.Released = fish.Pending = fish.Retired = fish.Exiting = fish.ExitAligned = false; fish.Age = fish.Delay = 0f; fish.Pulse = -1;
                fish.Root.SetLocalPositionAndRotation(fish.RestPosition, fish.RestRotation); fish.Root.localScale = fish.RestScale;
            }
        }

        private void CaptureFishSample()
        {
            foreach (Fish fish in fishParts) if (fish != null)
                for (int i = 0; i < fish.Bones.Length; i++)
                { fish.SamplePositions[i] = fish.Bones[i].localPosition; fish.SampleRotations[i] = fish.Bones[i].localRotation; }
        }

        private void ScheduleSpill(Part board, Vector3 outward)
        {
            Vector3 opening = board.Attached.transform.TransformPoint(board.Attached.center);
            Fish nearest = null; float distance = float.PositiveInfinity;
            foreach (Fish fish in fishParts)
            {
                if (fish.Released || fish.Pending) continue;
                float candidate = (fish.Root.position - opening).sqrMagnitude;
                if (candidate >= distance) continue;
                nearest = fish; distance = candidate;
            }
            if (nearest == null) return;
            nearest.Pending = true; nearest.Delay = .12f;
            nearest.Normal = Model.InverseTransformDirection(outward);
            // The longest tangential board axis spans the opening. Keep the
            // fish's thinner body axis across its short edge while it squeezes
            // out; fins keep their authored deformation and do not own contact.
            Transform frame = board.Attached.transform;
            Vector3 size = Vector3.Scale(board.Attached.size, frame.lossyScale);
            Vector3 longAxis = Vector3.zero; float longest = -1f;
            for (int axis = 0; axis < 3; axis++)
            {
                Vector3 direction = frame.TransformDirection(axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward);
                if (Mathf.Abs(Vector3.Dot(direction, outward)) > .5f || Mathf.Abs(size[axis]) <= longest) continue;
                longAxis = direction; longest = Mathf.Abs(size[axis]);
            }
            Vector3 gapUp = Vector3.Cross(outward, longAxis).normalized;
            nearest.GapUp = Model.InverseTransformDirection(gapUp);
            // Adjacent slats need not be symmetric about the removed board:
            // the head closure narrows one side of the first front rail's hole.
            // Centre the trunk in the actual free interval, not the old timber.
            float halfFishLength = nearest.Shape.height * .5f * Mathf.Abs(nearest.Root.lossyScale[nearest.Shape.direction]);
            float below = float.NegativeInfinity, above = float.PositiveInfinity;
            foreach (Part other in boards) if (other != board && !other.Detached)
                BoundSpillOpening(other, board, opening, outward, longAxis, gapUp, halfFishLength, ref below, ref above);
            foreach (Part other in core)
                BoundSpillOpening(other, board, opening, outward, longAxis, gapUp, halfFishLength, ref below, ref above);
            nearest.ExitAlignmentTolerance = .006f;
            if (float.IsFinite(below) && float.IsFinite(above))
            {
                opening += gapUp * ((below + above) * .5f);
                Vector3 scale = nearest.Root.lossyScale;
                int axis = nearest.Shape.direction;
                float radius = nearest.Shape.radius * Mathf.Max(Mathf.Abs(scale[(axis + 1) % 3]), Mathf.Abs(scale[(axis + 2) % 3]));
                nearest.ExitAlignmentTolerance = Mathf.Clamp(((above - below) * .5f - radius) * .5f, .0005f, .006f);
            }
            nearest.Exit = Model.InverseTransformPoint(opening);
        }

        private static void BoundSpillOpening(Part other, Part removed, Vector3 opening, Vector3 outward,
            Vector3 across, Vector3 up, float halfFishLength, ref float below, ref float above)
        {
            BoxCollider shape = other.Attached;
            Vector3 offset = shape.transform.TransformPoint(shape.center) - opening;
            if (Mathf.Abs(Vector3.Dot(offset, across)) > halfFishLength + PartHalfSpan(shape, across) ||
                Mathf.Abs(Vector3.Dot(offset, outward)) > PartHalfSpan(shape, outward) + PartHalfSpan(removed.Attached, outward) + .025f) return;
            float centre = Vector3.Dot(offset, up), half = PartHalfSpan(shape, up);
            if (centre + half < 0f) below = Mathf.Max(below, centre + half);
            else if (centre - half > 0f) above = Mathf.Min(above, centre - half);
        }

        private static float PartHalfSpan(BoxCollider shape, Vector3 direction)
        {
            Transform frame = shape.transform;
            Vector3 half = shape.size * .5f;
            return Mathf.Abs(Vector3.Dot(frame.TransformVector(Vector3.right * half.x), direction)) +
                Mathf.Abs(Vector3.Dot(frame.TransformVector(Vector3.up * half.y), direction)) +
                Mathf.Abs(Vector3.Dot(frame.TransformVector(Vector3.forward * half.z), direction));
        }

        private void ReleaseFish(Fish fish, Vector3 opening, Vector3 outward, bool throughHole = false)
        {
            if (fish.Released) return;
            fish.Released = true; fish.Pending = false; fish.Age = 0f; ReleasedFishCount++;
            fish.Body.transform.localScale = fish.Root.lossyScale;
            // The same simulation step reads COM before the next physics sync.
            // Set the physical pose, not just its pending Transform pose.
            fish.Body.position = fish.Root.position; fish.Body.rotation = fish.Root.rotation;
            fish.Body.isKinematic = false; fish.Shape.enabled = true;
            fish.Exiting = throughHole;
            fish.ExitAligned = false;
            fish.PreviousExitOpening = opening;
            fish.PreviousExitNormal = outward;
            fish.Body.useGravity = !throughHole;
            fish.Body.constraints = throughHole ? RigidbodyConstraints.FreezeRotation : RigidbodyConstraints.None;
            if (throughHole)
            {
                Vector3 gapUp = Model.TransformDirection(fish.GapUp);
                Vector3 size = fish.Size;
                int longest = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
                int thinnest = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
                Vector3 localLong = longest == 0 ? Vector3.right : longest == 1 ? Vector3.up : Vector3.forward;
                Vector3 localThin = thinnest == 0 ? Vector3.right : thinnest == 1 ? Vector3.up : Vector3.forward;
                Quaternion basis = Quaternion.Inverse(Quaternion.LookRotation(Vector3.Cross(localLong, localThin), localThin));
                Quaternion closest = fish.Body.rotation; float angle = float.PositiveInfinity;
                for (int forward = -1; forward <= 1; forward += 2)
                    for (int up = -1; up <= 1; up += 2)
                    {
                        Quaternion candidate = Quaternion.LookRotation(outward * forward, gapUp * up) * basis;
                        float turn = Quaternion.Angle(fish.Body.rotation, candidate);
                        if (turn < angle) { angle = turn; closest = candidate; }
                    }
                fish.Body.rotation = closest;
            }
            Vector3 inherited = PhysicsBody.isKinematic ? owner.AchievedPlanarVelocity : PhysicsBody.GetPointVelocity(fish.Root.position);
            Vector3 direction = opening - fish.Root.position;
            fish.Body.linearVelocity = inherited + Vector3.ClampMagnitude(direction * 3.5f, 2.8f) + outward * .8f + Vector3.up * .6f;
            fish.Body.angularVelocity = throughHole ? Vector3.zero : new Vector3(2f, -1f, 3f);
            IgnoreOwn(fish.Shape); CombatCorpseTraversal.NotifyColliderEnabled(fish.Shape);
            foreach (Fish other in fishParts) if (other != fish) Physics.IgnoreCollision(fish.Shape, other.Shape);
            foreach (Part board in boards) Physics.IgnoreCollision(fish.Shape, board.Loose);
            if (partsFrozen) Hold(fish.Body, ref fish.Velocity, ref fish.AngularVelocity, true);
        }

        private void AdvanceReleasedParts(float seconds)
        {
            if (!catchSpilledOnFall && (owner.IsKnockedDown || owner.IsRagdollActive || owner.IsBodyGrounded || owner.State.IsDefeated))
            {
                catchSpilledOnFall = true;
                // The open rim releases the remaining original fish on any fall.
                foreach (Fish fish in fishParts)
                {
                    if (!fish.Released) ReleaseFish(fish, fish.Root.position + Vector3.up * .25f, Vector3.up);
                    else if (fish.Exiting) FinishFishExit(fish);
                }
            }
            foreach (Part part in boards) if (part.Detached && part.Age < 8f)
            {
                part.Age += seconds;
                if (part.Age >= 8f)
                { part.Age = 8f; part.Body.isKinematic = true; }
            }
            SettledFishCount = 0;
            for (int index = 0; index < fishParts.Length; index++)
            {
                Fish fish = fishParts[index];
                if (fish.Pending)
                {
                    fish.Delay -= seconds;
                    if (fish.Delay <= 0f) ReleaseFish(fish, Model.TransformPoint(fish.Exit), Model.TransformDirection(fish.Normal), true);
                }
                if (!fish.Released) continue;
                fish.Age = Mathf.Min(8f, fish.Age + seconds);
                if (fish.Exiting)
                {
                    Vector3 opening = Model.TransformPoint(fish.Exit), normal = Model.TransformDirection(fish.Normal);
                    Vector3 openingVelocity = (opening - fish.PreviousExitOpening) / seconds;
                    Vector3 normalVelocity = (normal - fish.PreviousExitNormal) / seconds;
                    fish.PreviousExitOpening = opening; fish.PreviousExitNormal = normal;
                    Vector3 centre = fish.Body.worldCenterOfMass;
                    if (Vector3.Dot(centre - opening, normal) > .23f || fish.Age >= .9f)
                        FinishFishExit(fish);
                    else if (!fish.Body.isKinematic)
                    {
                        // Align inside the crate before pushing out. A diagonal
                        // escape from a lower fish root otherwise wedges its
                        // rounded trunk against the still-solid slat below.
                        // Both stages move the same body with full wood contact.
                        Vector3 offset = centre - opening;
                        if (!fish.ExitAligned && Vector3.ProjectOnPlane(offset, normal).sqrMagnitude < fish.ExitAlignmentTolerance * fish.ExitAlignmentTolerance &&
                            Vector3.Dot(offset, normal) < -.16f) fish.ExitAligned = true;
                        float stageOffset = fish.ExitAligned ? .28f : -.24f;
                        Vector3 target = opening + normal * stageOffset;
                        // Track measured rig motion without a spring's steady
                        // lag, even while the owner walks. Use the same stage
                        // for both frames so changing stages adds no impulse.
                        Vector3 targetVelocity = Vector3.ClampMagnitude(openingVelocity + normalVelocity * stageOffset, 3.2f);
                        fish.Body.linearVelocity = Vector3.ClampMagnitude(targetVelocity + (target - centre) * 12f, 3.2f);
                        fish.Body.angularVelocity = Vector3.zero;
                    }
                }
                int pulse = Mathf.FloorToInt(fish.Age / .37f);
                if (!fish.Exiting && fish.Age < 4f && pulse != fish.Pulse && !fish.Body.isKinematic)
                {
                    fish.Pulse = pulse;
                    float strength = 1f - fish.Age / 5f;
                    float side = ((index + pulse) & 1) == 0 ? 1f : -1f;
                    fish.Body.linearVelocity = Vector3.ClampMagnitude(fish.Body.linearVelocity +
                        new Vector3(side * .34f, .65f + .09f * (index % 3), -.23f * side) * strength, 4f);
                    fish.Body.angularVelocity = Vector3.ClampMagnitude(fish.Body.angularVelocity +
                        new Vector3(2.2f * side, .7f, -2.8f * side) * strength, 11f);
                }
                if (fish.Age >= 8f)
                { fish.Age = 8f; fish.Body.isKinematic = true; SettledFishCount++; }
            }
            RestoreReleasedPoses();
        }

        private static void FinishFishExit(Fish fish)
        {
            fish.Exiting = false; fish.Body.useGravity = true; fish.Body.constraints = RigidbodyConstraints.None;
            if (!fish.Body.isKinematic) fish.Body.angularVelocity = new Vector3(2f, -1f, 3f);
        }

        private void RestoreReleasedPoses()
        {
            foreach (Part part in boards) if (part.Detached)
            {
                if (!part.Retired && part.Age >= 8f && (part.Body.position - owner.transform.position).sqrMagnitude > 144f)
                { part.Retired = true; part.Loose.enabled = false; part.Bone.localScale = Vector3.zero; }
                part.Bone.SetPositionAndRotation(part.Retired ? Model.position : part.Body.position, part.Body.rotation);
            }
            foreach (Fish fish in fishParts) if (fish != null && fish.Released)
            {
                if (!fish.Retired && fish.Age >= 8f && (fish.Body.position - owner.transform.position).sqrMagnitude > 144f)
                { fish.Retired = true; fish.Shape.enabled = false; fish.Root.localScale = Vector3.zero; }
                fish.Root.SetPositionAndRotation(fish.Retired ? Model.position : fish.Body.position, fish.Body.rotation);
                float weight = Mathf.Clamp01(5f - fish.Age);
                for (int i = 0; i < fish.Bones.Length; i++)
                {
                    fish.Bones[i].localPosition = Vector3.Lerp(fish.RestPositions[i], fish.SamplePositions[i], weight);
                    fish.Bones[i].localRotation = Quaternion.Slerp(fish.RestRotations[i], fish.SampleRotations[i], weight);
                }
            }
        }
    }
}

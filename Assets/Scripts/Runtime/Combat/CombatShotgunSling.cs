using System;
using Unity.Collections;
using Unity.Profiling;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The imported webbing is a soft chain anchored to this shotgun's two swivels.</summary>
    [DefaultExecutionOrder(380)]
    [DisallowMultipleComponent]
    public sealed class CombatShotgunSling : MonoBehaviour
    {
        public const string RibbonName = "Ribbon__Webbing";
        public const int MaximumFrameSubsteps = 2;
        public long TotalPresentTicks { get; private set; }
        public long TotalPresentCalls { get; private set; }
        public long LastPresentTicks { get; private set; }
        public long TotalSimulationSteps { get; private set; }
        public int LastSimulationSteps { get; private set; }
        internal static bool NativeProjectionSupported => PlayerScarfBodyContacts.NativeArithmeticMode == 1;
        private static readonly ProfilerMarker PresentMarker = new ProfilerMarker("BarPromenade.WeaponCarry.Sling.Present");
        private static readonly ProfilerMarker PoseMarker = new ProfilerMarker("BarPromenade.WeaponCarry.Sling.Pose");
        private static readonly ProfilerMarker SolveMarker = new ProfilerMarker("BarPromenade.WeaponCarry.Sling.Solve");
        private static readonly ProfilerMarker ContactMarker = new ProfilerMarker("BarPromenade.WeaponCarry.Sling.Contacts");
        private static readonly ProfilerMarker MeasureMarker = new ProfilerMarker("BarPromenade.WeaponCarry.Sling.Measure");
        private static readonly ProfilerMarker SeedMarker = new ProfilerMarker("BarPromenade.WeaponCarry.Sling.Seed");
        private static readonly ProfilerMarker RenderMarker = new ProfilerMarker("BarPromenade.WeaponCarry.Sling.Render");
        private static readonly ProfilerMarker ProjectionMarker = new ProfilerMarker("BarPromenade.WeaponCarry.Sling.Projection");
        private static readonly ProfilerMarker TargetsMarker = new ProfilerMarker("BarPromenade.WeaponCarry.Sling.Targets");
        private static readonly ProfilerMarker SnapshotMarker = new ProfilerMarker("BarPromenade.WeaponCarry.Sling.Snapshot");
        private static readonly ProfilerMarker ProjectionWorkspaceMarker = new ProfilerMarker("BarPromenade.WeaponCarry.Sling.Projection.Workspace");
        // Grasp just before the material midpoint: the foreend leg needs more
        // length while the left hand carries the loop over the shoulder.
        private const int Segments = 80, Links = 40, HandPoint = 18;
        private const float HalfWidth = .0125f, HalfThickness = .001f;
        private const float StepSeconds = 1f / 120f, MaximumElapsed = StepSeconds * MaximumFrameSubsteps;
        private const float LinkTolerance = .00025f, ContactTolerance = .001f;
        private const int ContactCacheSlots = 16;
        private const int ContactCacheBankSize = (Links + 1) * ContactCacheSlots;
        internal const int SteadyProjectionIterationBudget = 64;
        internal const int SteadyLengthRepairIterationBudget = 64;
        private const int FinalProjectionReserve = 32;
        internal int SteadyProjectionIterations { get; private set; }
        internal int SteadyLengthRepairIterations { get; private set; }
        public float AuthoredLength => 1.40f;
        public float PresentedLength { get; private set; }
        public float PresentedStockToHandLength { get; private set; }
        public float PresentedHandToForeendLength { get; private set; }
        public int HandCenterlineIndex => HandPoint * 2;
        public float MaximumLinkError { get; private set; }
        public float MaximumContactError { get; private set; }
        public float ShoulderAttachmentAmount => shoulderAmount;
        public float LeftHandAttachmentAmount => handAmount;
        public int MaximumLinkErrorIndex { get; private set; }
        public int MaximumContactErrorIndex { get; private set; }
        public Vector3 MaximumLinkErrorWorldCentre { get; private set; }
        public Vector3 MaximumContactErrorWorldPoint { get; private set; }
        public Vector3 HandTargetWorld => handTarget;
        public Vector3 HandAxisWorld => handAxis;
        public float StockToHandDistance => Vector3.Distance(from, handTarget);
        public float HandToForeendDistance => Vector3.Distance(handTarget, to);
        public float StockToHandMaterialLength => AuthoredLength * HandPoint / Links;
        public float HandToForeendMaterialLength => AuthoredLength * (Links - HandPoint) / Links;
        public float StockBodyContactError { get; private set; }
        public float ForeendBodyContactError { get; private set; }
        public float HardGripBodyContactError { get; private set; }
        public MeshFilter Ribbon => ribbon;
        public int CenterlinePointCount => centres.Length;
        public Vector3 WorldCenterlinePoint(int index) => centres[index];

        private CombatActor owner;
        private Player3DAssetRegistry registry;
        private PlayerEquipmentContacts equipment;
        private PlayerScarfBodyContacts bodyContacts;
        private NpcHandPose hands;
        private CombatDroppedWeaponPickup pickup;
        private Transform shoulder, oppositeShoulder, stock, foreend;
        private Vector3 stockOffset, foreendOffset;
        private MeshFilter ribbon;
        private Mesh source, deformedMesh;
        private Vector2[] alongWidth, thickness;
        private Vector3[] vertices;
        private readonly Vector3[] points = new Vector3[Links + 1], previous = new Vector3[Links + 1];
        private readonly Vector3[] contactDirections = new Vector3[Links + 1];
        private readonly Vector3[] contactInputs = new Vector3[ContactCacheBankSize * 2];
        private readonly Vector3[] contactOutputs = new Vector3[ContactCacheBankSize * 2];
        private readonly bool[] contactValid = new bool[ContactCacheBankSize * 2];
        private readonly float[] inverseMass = new float[Links + 1];
        private readonly Vector3[] centres = new Vector3[Segments + 1];
        private readonly Vector3[] sides = new Vector3[Segments + 1], normals = new Vector3[Segments + 1];
        private readonly RaycastHit[] floorHits = new RaycastHit[12];
        private Vector3 bodyRight, bodyUp, bodyForward, from, to, shoulderTarget, handTarget, handAxis;
        private Vector3 frontChest, frontAbdomen, rightSide;
        private float chestRouteLength, abdomenRouteLength, sideRouteLength, frontRouteLength;
        private float graspAbdomenLength, graspSideLength, graspRouteLength;
        private Vector3 lastRootPosition, lastGunPosition, lastFrom, lastTo, lastShoulder, lastHand;
        private Quaternion lastRootRotation, lastGunRotation;
        private Plane floor;
        private bool initialized, resetPending = true, wasAttached, wasInspecting, wasPaused, hasFloor;
        private bool contactsRegistered;
        private float shoulderAmount, handAmount, lastShoulderAmount, lastHandAmount, previousStep = StepSeconds;
        private int shoulderPoint = 9, simulatedFrame = -1;
        private ProjectionWorkArena projectionWork, retainedProjectionWork;
        internal int ProjectionWorkspaceAllocations { get; private set; }

        private static ProjectionWorkScope CreateProjectionWorkArena(CombatShotgunSling owner, int count)
        {
            using var profile = ProjectionWorkspaceMarker.Auto();
            if (owner == null) return new ProjectionWorkScope(null, new ProjectionWorkArena(count, Allocator.TempJob));
            if (owner.projectionWork != null)
                throw new InvalidOperationException("Sling projection scopes cannot overlap.");
            if (owner.retainedProjectionWork != null &&
                owner.retainedProjectionWork.NativeSupported != NativeProjectionSupported)
            {
                owner.retainedProjectionWork.Dispose();
                owner.retainedProjectionWork = null;
            }
            if (owner.retainedProjectionWork == null)
            {
                owner.retainedProjectionWork = new ProjectionWorkArena(count, Allocator.Persistent);
                owner.ProjectionWorkspaceAllocations++;
            }
            owner.projectionWork = owner.retainedProjectionWork;
            return new ProjectionWorkScope(owner, owner.projectionWork);
        }

        // A rendered pass borrows its owner's buffers; diagnostic invocations
        // retain the original temporary lifetime. Neither path changes the solve.
        private readonly struct ProjectionWorkScope : IDisposable
        {
            private readonly CombatShotgunSling owner;
            private readonly ProjectionWorkArena work;
            internal ProjectionWorkScope(CombatShotgunSling owner, ProjectionWorkArena work)
            { this.owner = owner; this.work = work; }
            internal void SetMasses(float[] source) => work.SetMasses(source);
            internal bool Project(Vector3[] source, CombatSlingProjectionJob.Parameters settings) => work.Project(source, settings);
            public void Dispose()
            {
                if (ReferenceEquals(owner, null)) work.Dispose();
                else if (ReferenceEquals(owner.projectionWork, work)) owner.projectionWork = null;
            }
        }

        private sealed class ProjectionWorkArena : IDisposable
        {
            private readonly bool nativeSupported;
            internal bool NativeSupported => nativeSupported;
            private float[] managedMasses;
            private NativeArray<Vector3> points;
            private NativeArray<float> inverseMass;

            public ProjectionWorkArena(int count, Allocator allocator)
            {
                nativeSupported = NativeProjectionSupported;
                try
                {
                    if (nativeSupported)
                    {
                        points = new NativeArray<Vector3>(count, allocator, NativeArrayOptions.UninitializedMemory);
                        inverseMass = new NativeArray<float>(count, allocator, NativeArrayOptions.UninitializedMemory);
                    }
                }
                catch { Dispose(); throw; }
            }

            public void SetMasses(float[] source)
            {
                managedMasses = source;
                if (nativeSupported) NativeArray<float>.Copy(source, 0, inverseMass, 0, source.Length);
            }

            public bool Project(Vector3[] source, CombatSlingProjectionJob.Parameters settings)
            {
                using var profile = ProjectionMarker.Auto();
                if (!nativeSupported)
                {
                    RunManagedProjectionGroup(source, managedMasses, settings);
                    return false;
                }
                NativeArray<Vector3>.Copy(source, 0, points, 0, source.Length);
                var job = new CombatSlingProjectionJob { Points = points, InverseMass = inverseMass, Settings = settings };
                CombatSlingProjectionJob.RunSynchronous(ref job);
                NativeArray<Vector3>.Copy(points, 0, source, 0, source.Length);
                return job.ManagedExecution == 0;
            }

            public void Dispose()
            {
                using var profile = ProjectionWorkspaceMarker.Auto();
                if (inverseMass.IsCreated) inverseMass.Dispose();
                if (points.IsCreated) points.Dispose();
            }
        }

        internal void Initialize(CombatActor actor, Player3DAssetRegistry rig, CombatWeaponCarryAssetProvider.Mount mount)
        {
            owner = actor; registry = rig;
            hands = rig.Anchors.RightGrip.GetComponentInParent<NpcHandPose>();
            shoulder = CityPedestrianHandProps.FindSocket(rig.Animator.transform, "upper_arm.L");
            oppositeShoulder = CityPedestrianHandProps.FindSocket(rig.Animator.transform, "upper_arm.R");
            foreach (Transform part in mount.Root.GetComponentsInChildren<Transform>(true))
            {
                if (part.name == "SlingStock") stock = part;
                else if (part.name == "SlingForeend") foreend = part;
            }
            foreach (MeshFilter filter in mount.Root.GetComponentsInChildren<MeshFilter>(true))
                if (filter.name == RibbonName) { ribbon = filter; break; }
            if (stock == null || foreend == null || ribbon == null || ribbon.sharedMesh == null || shoulder == null)
                throw new InvalidOperationException("The shotgun sling needs its authored ribbon, swivel anchors and production left shoulder.");
            stockOffset = mount.Root.transform.InverseTransformPoint(stock.position);
            foreendOffset = mount.Root.transform.InverseTransformPoint(foreend.position);
            source = ribbon.sharedMesh;
            alongWidth = source.uv; thickness = source.uv2;
            if (alongWidth.Length != source.vertexCount || thickness.Length != source.vertexCount)
                throw new InvalidOperationException("The imported shotgun ribbon lacks its arclength/width/thickness coordinates.");
            vertices = new Vector3[source.vertexCount];
            deformedMesh = Instantiate(source);
            deformedMesh.name = "Shotgun Sling Deformed";
            deformedMesh.hideFlags = HideFlags.DontSave;
            deformedMesh.MarkDynamic();
            ribbon.sharedMesh = deformedMesh;
            // Imported grip-local geometry belongs to the original gun in the
            // hand, on the back, on the floor and in the shared item screen.
            for (int child = mount.Root.transform.childCount - 1; child >= 0; child--)
            {
                Transform geometry = mount.Root.transform.GetChild(child);
                if (geometry != mount.WeaponDock) geometry.SetParent(transform, false);
            }
            // The primary coat shell omits sewn pockets/flaps. Its clearance
            // covers the ribbon edges and underside without sampling cloth output.
            bodyContacts = new PlayerScarfBodyContacts(rig, useMeshSupportPlanes: true,
                includeEquipment: false, poseSourceSkin: true, pointQuerySlots: Links + 1,
                jacketSurfacePadding: .013f + HalfThickness + HalfWidth * .5f);
        }

        private void LateUpdate() => Present();

        internal void RequestPresent()
        {
            // Combat samples several poses per render frame. Cloth consumes
            // the final live rig in its late pass, before jacket contacts.
            // Detachment releases stale hero contacts immediately.
            if (contactsRegistered && (owner == null || !owner.isActiveAndEnabled || !isActiveAndEnabled ||
                !transform.IsChildOf(owner.transform))) ReleaseContacts();
        }

        internal void Present()
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            LastSimulationSteps = 0;
            try { PresentCore(); }
            finally
            {
                LastPresentTicks = System.Diagnostics.Stopwatch.GetTimestamp() - started;
                TotalPresentTicks += LastPresentTicks;
                TotalPresentCalls++;
            }
        }

        private void PresentCore()
        {
            if (owner == null || registry == null || ribbon == null || deformedMesh == null) return;
            using var profile = PresentMarker.Auto();
            if (pickup == null) pickup = GetComponent<CombatDroppedWeaponPickup>();
            bool inspecting = pickup != null && pickup.IsPresenting;
            bool attached = owner.isActiveAndEnabled && transform.IsChildOf(owner.transform) && !inspecting;
            bool paused = PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused;
            SteadyProjectionIterations = 0;
            SteadyLengthRepairIterations = 0;
            if (paused && initialized)
            {
                wasPaused = true;
                simulatedFrame = Time.frameCount;
                return;
            }
            ReadTargets(attached);
            bool teleported = initialized && ((owner.transform.position - lastRootPosition).sqrMagnitude > .3025f ||
                (transform.position - lastGunPosition).sqrMagnitude > .3025f ||
                Quaternion.Angle(owner.transform.rotation, lastRootRotation) > 110f);
            bool changedOwnership = initialized && (attached != wasAttached || inspecting != wasInspecting);
            bool coldReset = !initialized || resetPending || teleported || changedOwnership;
            if (attached)
            {
                using var poseProfile = PoseMarker.Auto();
                bodyContacts.UpdatePose();
            }
            if (!attached && !inspecting) MeasureFloor(); else hasFloor = false;
            using PlayerScarfBodyContacts.RayQueryScope nativeQueries = attached ? bodyContacts.BeginNativeQueries() : default;
            using var nativeProjection = CreateProjectionWorkArena(this, Links + 1);
            // Pose, escape directions, grip cavity and floor are fixed for this
            // presentation. Repeated exact inputs can reuse their projection,
            // while every solver/friction iteration still runs normally.
            Array.Clear(contactValid, 0, contactValid.Length);
            if (coldReset)
            {
                lastFrom = from; lastTo = to; lastShoulder = shoulderTarget; lastHand = handTarget;
                lastShoulderAmount = shoulderAmount; lastHandAmount = handAmount;
                Seed(attached);
                resetPending = false; initialized = true;
            }
            else if (wasPaused)
            {
                Array.Copy(points, previous, points.Length);
                previousStep = StepSeconds;
            }
            bool finalPinsConverged = false;
            if (inspecting)
            {
                // The pickup presenter moves one gun and its own strap
                // together. A camera flight must not inject cloth energy.
                if (!coldReset)
                {
                    Quaternion turn = transform.rotation * Quaternion.Inverse(lastGunRotation);
                    for (int i = 0; i <= Links; i++)
                        points[i] = transform.position + turn * (points[i] - lastGunPosition);
                }
                Array.Copy(points, previous, points.Length);
            }
            else if (simulatedFrame != Time.frameCount)
            {
                float elapsed = Mathf.Clamp(Time.deltaTime, 0f, MaximumElapsed);
                int steps = elapsed > 0f ? Mathf.Min(MaximumFrameSubsteps, Mathf.CeilToInt(elapsed / StepSeconds)) : 0;
                LastSimulationSteps = steps;
                TotalSimulationSteps += steps;
                float seconds = steps > 0 ? elapsed / steps : StepSeconds;
                int substepIterations = steps > 0 ? Mathf.Min(16,
                    (SteadyProjectionIterationBudget - FinalProjectionReserve) / (steps * 4) * 4) : 0;
                for (int step = 0; step < steps; step++)
                {
                    float alpha = (step + 1f) / steps;
                    SetMasses(Mathf.Lerp(lastShoulderAmount, shoulderAmount, alpha), Mathf.Lerp(lastHandAmount, handAmount, alpha));
                    Integrate(seconds);
                    AttractToPins(alpha, seconds);
                    finalPinsConverged = Constrain(attached, alpha, substepIterations, measureMetrics: false, countSteady: true);
                }
                simulatedFrame = Time.frameCount;
            }
            // Reconcile the final live pins after the interpolated physics
            // steps, without advancing gravity again. The last substep already
            // uses these same pins; a converged chain needs no repeated solve.
            bool changedMasses = SetMasses(shoulderAmount, handAmount);
            if (!finalPinsConverged || changedMasses)
                Constrain(attached, 1f, SteadyProjectionIterationBudget - SteadyProjectionIterations,
                    measureMetrics: false, countSteady: true);
            RepairMaterialLength();
            // Keep the warm chain for the next frame if a contact/length seam
            // has not converged. Its targets and final hard pins are already
            // current; repeated fitting must not consume the whole frame.
            MeasureConstraintErrors(attached, out _);
            using (SnapshotMarker.Auto())
            {
                lastFrom = from; lastTo = to; lastShoulder = shoulderTarget; lastHand = handTarget;
                lastShoulderAmount = shoulderAmount; lastHandAmount = handAmount;
                lastRootPosition = owner.transform.position; lastRootRotation = owner.transform.rotation;
                lastGunPosition = transform.position; lastGunRotation = transform.rotation;
                wasAttached = attached; wasInspecting = inspecting; wasPaused = false;
            }
            Render(attached);
        }

        private void ReadTargets(bool attached)
        {
            using var profile = TargetsMarker.Auto();
            from = transform.TransformPoint(stockOffset); to = transform.TransformPoint(foreendOffset);
            stock.position = from; foreend.position = to;
            bodyRight = oppositeShoulder != null ? (oppositeShoulder.position - shoulder.position).normalized : owner.transform.right;
            bodyUp = (registry.Anchors.Head.position - registry.Anchors.Chest.position).normalized;
            bodyForward = Vector3.Cross(bodyRight, bodyUp).normalized;
            shoulderTarget = shoulder.position + bodyUp * .07f - bodyRight * .012f + bodyForward * .04f;
            frontChest = registry.Anchors.Chest.position + bodyForward * .15f - bodyRight * .06f;
            frontAbdomen = registry.Anchors.Pelvis.position + bodyUp * .18f + bodyForward * .14f + bodyRight * .13f;
            rightSide = registry.Anchors.Pelvis.position + bodyUp * .13f + bodyRight * .22f - bodyForward * .02f;
            chestRouteLength = Vector3.Distance(shoulderTarget, frontChest);
            abdomenRouteLength = chestRouteLength + Vector3.Distance(frontChest, frontAbdomen);
            sideRouteLength = abdomenRouteLength + Vector3.Distance(frontAbdomen, rightSide);
            frontRouteLength = sideRouteLength + Vector3.Distance(rightSide, to);
            handTarget = hands != null ? hands.CylinderCentre(true) : registry.Anchors.LeftGrip.position;
            handAxis = hands != null ? hands.CylinderAxis(true) : registry.Anchors.LeftGrip.up;
            graspAbdomenLength = Vector3.Distance(handTarget, frontAbdomen);
            graspSideLength = graspAbdomenLength + Vector3.Distance(frontAbdomen, rightSide);
            graspRouteLength = graspSideLength + Vector3.Distance(rightSide, to);
            shoulderAmount = attached && owner.CarriedWeaponLocation(CombatWeaponId.Shotgun) == CombatWeaponCarryLocation.Stowed ? 1f : 0f;
            handAmount = 0f;
            if (attached && owner.IsSwitchingWeapon && owner.WeaponCarryActionWeapon == CombatWeaponId.Shotgun)
            {
                shoulderAmount = CombatWeaponCarryAssetProvider.SlingShoulderAmount(owner.WeaponCarryActionIsDraw, owner.WeaponCarryActionProgress);
                handAmount = CombatWeaponCarryAssetProvider.SlingLeftHandAmount(owner.WeaponCarryActionIsDraw, owner.WeaponCarryActionProgress);
            }
            if ((lastShoulderAmount <= .001f && shoulderAmount > .001f) || !initialized)
                shoulderPoint = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(from, shoulderTarget) / (AuthoredLength / Links)) + 1, 3, HandPoint - 3);
            UpdateContactDirections();
        }

        private void UpdateContactDirections()
        {
            Vector3 chest = registry.Anchors.Chest.position;
            for (int i = 0; i <= Links; i++)
            {
                Vector3 guide = Vector3.Lerp(from, to, i / (float)Links);
                Vector3 shoulderGuide = i <= shoulderPoint ? Vector3.Lerp(from, shoulderTarget, i / (float)shoulderPoint) :
                    SampleFrontRoute((i - shoulderPoint) / (float)(Links - shoulderPoint));
                guide = Vector3.Lerp(guide, shoulderGuide, shoulderAmount);
                Vector3 graspGuide;
                if (i <= HandPoint)
                {
                    graspGuide = Vector3.Lerp(from, handTarget, i / (float)HandPoint);
                    Vector3 overShoulder = i <= shoulderPoint ? Vector3.Lerp(from, shoulderTarget, i / (float)shoulderPoint) :
                        Vector3.Lerp(shoulderTarget, handTarget, (i - shoulderPoint) / (float)(HandPoint - shoulderPoint));
                    graspGuide = Vector3.Lerp(graspGuide, overShoulder, shoulderAmount);
                }
                else
                {
                    float progress = (i - HandPoint) / (float)(Links - HandPoint);
                    graspGuide = Vector3.Lerp(Vector3.Lerp(handTarget, to, progress), SampleGraspTail(progress), shoulderAmount);
                }
                guide = Vector3.Lerp(guide, graspGuide, handAmount);
                // These directions choose a coherent exterior side when a
                // point touches overlapping body/arm envelopes. They are not
                // positions or shape constraints: free webbing still falls,
                // bends and carries inertia on its actual attachment span.
                Vector3 outward = Vector3.ProjectOnPlane(guide - chest, bodyUp);
                contactDirections[i] = outward.sqrMagnitude > .0004f ? outward.normalized : bodyRight;
            }
        }

        private Vector3 SampleFrontRoute(float progress)
        {
            // Seed the continuous loop around the front and the opposite flank.
            // Distances allocate material independently of the hand's grasp index;
            // these guides choose contact sides, never pin fabric to the abdomen.
            float distance = progress * frontRouteLength;
            if (distance <= chestRouteLength)
                return Vector3.Lerp(shoulderTarget, frontChest, distance / Mathf.Max(chestRouteLength, .0001f));
            if (distance <= abdomenRouteLength)
                return Vector3.Lerp(frontChest, frontAbdomen,
                    (distance - chestRouteLength) / Mathf.Max(abdomenRouteLength - chestRouteLength, .0001f));
            if (distance <= sideRouteLength)
                return Vector3.Lerp(frontAbdomen, rightSide,
                    (distance - abdomenRouteLength) / Mathf.Max(sideRouteLength - abdomenRouteLength, .0001f));
            return Vector3.Lerp(rightSide, to,
                (distance - sideRouteLength) / Mathf.Max(frontRouteLength - sideRouteLength, .0001f));
        }

        private Vector3 SampleGraspTail(float progress)
        {
            float distance = progress * graspRouteLength;
            if (distance <= graspAbdomenLength)
                return Vector3.Lerp(handTarget, frontAbdomen, distance / Mathf.Max(graspAbdomenLength, .0001f));
            if (distance <= graspSideLength)
                return Vector3.Lerp(frontAbdomen, rightSide,
                    (distance - graspAbdomenLength) / Mathf.Max(graspSideLength - graspAbdomenLength, .0001f));
            return Vector3.Lerp(rightSide, to,
                (distance - graspSideLength) / Mathf.Max(graspRouteLength - graspSideLength, .0001f));
        }

        private void Seed(bool attached)
        {
            using var profile = SeedMarker.Auto();
            for (int i = 0; i <= Links; i++)
            {
                float t = i / (float)Links;
                Vector3 point;
                if (shoulderAmount > .001f)
                {
                    if (i <= shoulderPoint) point = Vector3.Lerp(from, shoulderTarget, i / (float)shoulderPoint);
                    else point = SampleFrontRoute((i - shoulderPoint) / (float)(Links - shoulderPoint));
                }
                else point = Vector3.Lerp(from, to, t) + Vector3.down * (.5f * Mathf.Sin(Mathf.PI * t));
                points[i] = previous[i] = point;
            }
            SetMasses(shoulderAmount, handAmount);
            Constrain(attached, 1f, 160);
            // Settle gravity at discontinuities, never to an authored beam.
            // Subsequent motion retains the chain's actual inertia.
            for (int step = 0; step < 24; step++)
            {
                Integrate(StepSeconds);
                AttractToPins(1f, StepSeconds);
                Constrain(attached, 1f, 24);
            }
            Constrain(attached, 1f, 160, 224);
            Array.Copy(points, previous, points.Length);
            previousStep = StepSeconds;
            Array.Clear(normals, 0, normals.Length);
        }

        private bool SetMasses(float shoulderWeight, float handWeight)
        {
            float shoulderMass = shoulderWeight >= .999f ? 0f : 1f;
            float handMass = handWeight >= .999f ? 0f : 1f;
            bool changed = inverseMass[shoulderPoint] != shoulderMass || inverseMass[HandPoint] != handMass;
            for (int i = 0; i <= Links; i++) inverseMass[i] = 1f;
            inverseMass[0] = inverseMass[Links] = 0f;
            inverseMass[shoulderPoint] = shoulderMass;
            inverseMass[HandPoint] = handMass;
            return changed;
        }

        private void Integrate(float seconds)
        {
            float damping = Mathf.Exp(-3.8f * seconds);
            Vector3 gravityStep = Physics.gravity * (seconds * seconds);
            for (int i = 1; i < Links; i++)
            {
                Vector3 point = points[i];
                Vector3 velocity = Vector3.ClampMagnitude((point - previous[i]) / previousStep, 2.2f);
                points[i] += velocity * (seconds * damping) + gravityStep;
                previous[i] = point;
            }
            previousStep = seconds;
        }

        private void AttractToPins(float alpha, float seconds)
        {
            Attract(shoulderPoint, Vector3.Lerp(lastShoulder, shoulderTarget, alpha),
                Mathf.Lerp(lastShoulderAmount, shoulderAmount, alpha));
            Attract(HandPoint, Vector3.Lerp(lastHand, handTarget, alpha),
                Mathf.Lerp(lastHandAmount, handAmount, alpha));

            void Attract(int point, Vector3 target, float weight)
            {
                if (weight <= 0f || inverseMass[point] == 0f) return;
                // A partial grasp is a spring, not a pin reapplied on every
                // solver iteration. Its strength depends on elapsed time;
                // releasing the hand therefore releases real material slack.
                float rate = 36f * weight / Mathf.Max(.001f, 1f - weight);
                points[point] = Vector3.Lerp(points[point], target, 1f - Mathf.Exp(-rate * seconds));
            }
        }

        private bool Constrain(bool attached, float alpha, int iterations, int extraIterations = 0,
            bool measureMetrics = true, bool countSteady = false)
        {
            using var profile = SolveMarker.Auto();
            float rest = AuthoredLength / Links;
            var settings = ProjectionSettings(alpha);
            projectionWork?.SetMasses(inverseMass);
            for (int iteration = 0; iteration < iterations + extraIterations; iteration += 4)
            {
                // Project contacts before lengths. The last contact projection
                // must not leave stretched links in the displayed ribbon.
                ResolveContacts(attached);
                settings.StartIteration = iteration;
                settings.IterationCount = Mathf.Min(4, iterations + extraIterations - iteration);
                if (projectionWork != null) projectionWork.Project(points, settings);
                else RunManagedProjectionGroup(points, inverseMass, settings);
                if (countSteady) SteadyProjectionIterations += settings.IterationCount;
                if (((iteration + settings.IterationCount - 1) & 7) == 7 && LengthsConverged(rest))
                {
                    if (!measureMetrics)
                    {
                        if (ContactsConverged(attached)) return true;
                        continue;
                    }
                    MeasureConstraintErrors(attached, out float length);
                    if (MaximumLinkError <= LinkTolerance &&
                        Mathf.Abs(length - AuthoredLength) <= .006f && MaximumContactError <= ContactTolerance) return true;
                }
            }
            // Hard attachments have zero inverse mass, so they already remain
            // exact during both projections. No final soft pull may add strain.
            if (!measureMetrics) return LengthsConverged(rest) && ContactsConverged(attached);
            MeasureConstraintErrors(attached, out float finalLength);
            return MaximumLinkError <= LinkTolerance && Mathf.Abs(finalLength - AuthoredLength) <= .006f &&
                MaximumContactError <= ContactTolerance;
        }

        private CombatSlingProjectionJob.Parameters ProjectionSettings(float alpha) =>
            new CombatSlingProjectionJob.Parameters
            {
                Rest = AuthoredLength / Links, ShoulderPoint = shoulderPoint, HandPoint = HandPoint,
                PinnedFrom = Vector3.Lerp(lastFrom, from, alpha), PinnedTo = Vector3.Lerp(lastTo, to, alpha),
                PinnedShoulder = Vector3.Lerp(lastShoulder, shoulderTarget, alpha), PinnedHand = Vector3.Lerp(lastHand, handTarget, alpha),
                PinShoulder = Mathf.Lerp(lastShoulderAmount, shoulderAmount, alpha) >= .999f,
                PinHand = Mathf.Lerp(lastHandAmount, handAmount, alpha) >= .999f,
                FromToAxis = to - from, BodyForward = bodyForward
            };

        private void RepairMaterialLength()
        {
            float rest = AuthoredLength / Links;
            if (LengthsConverged(rest)) return;
            using var profile = SolveMarker.Auto();
            var settings = ProjectionSettings(1f);
            projectionWork?.SetMasses(inverseMass);
            for (int iteration = 0; iteration < SteadyLengthRepairIterationBudget; iteration += 8)
            {
                // The contact-aware solve is already bounded. Restore material
                // length without repeating a blocked seam's body projection.
                settings.StartIteration = iteration;
                settings.IterationCount = 8;
                if (projectionWork != null) projectionWork.Project(points, settings);
                else RunManagedProjectionGroup(points, inverseMass, settings);
                SteadyLengthRepairIterations += settings.IterationCount;
                if (LengthsConverged(rest)) return;
            }
        }

        private static bool RunNativeProjectionGroup(Vector3[] points, float[] inverseMass, CombatSlingProjectionJob.Parameters settings)
        {
            using var work = CreateProjectionWorkArena(null, points.Length);
            work.SetMasses(inverseMass);
            return work.Project(points, settings);
        }

        private static void RunManagedProjectionGroup(Vector3[] points, float[] inverseMass, CombatSlingProjectionJob.Parameters settings)
        {
            int links = points.Length - 1;
            for (int iteration = settings.StartIteration; iteration < settings.StartIteration + settings.IterationCount; iteration++)
            {
                points[0] = settings.PinnedFrom;
                points[links] = settings.PinnedTo;
                if (settings.PinShoulder) points[settings.ShoulderPoint] = settings.PinnedShoulder;
                if (settings.PinHand) points[settings.HandPoint] = settings.PinnedHand;
                bool reverse = (iteration & 1) != 0;
                LimitMaterialDistances(points, inverseMass, settings.Rest, reverse);
                for (int link = 0; link < links; link++)
                {
                    int a = reverse ? links - 1 - link : link, b = a + 1;
                    Vector3 delta = points[b] - points[a];
                    float length = delta.magnitude, mass = inverseMass[a] + inverseMass[b];
                    if (mass <= 0f) continue;
                    Vector3 correction;
                    if (length <= .000001f)
                    {
                        Vector3 axis = a > 0 ? points[a] - points[a - 1] : settings.FromToAxis;
                        if (axis.sqrMagnitude < .000001f) axis = settings.BodyForward;
                        correction = -axis.normalized * (settings.Rest / mass);
                    }
                    else correction = delta * ((length - settings.Rest) / (length * mass));
                    points[a] += correction * inverseMass[a];
                    points[b] -= correction * inverseMass[b];
                }
            }
        }

        private bool LengthsConverged(float rest)
        {
            float total = 0f;
            for (int i = 0; i < Links; i++)
            {
                float length = Vector3.Distance(points[i], points[i + 1]);
                if (Mathf.Abs(length - rest) > LinkTolerance) return false;
                total += length;
            }
            return Mathf.Abs(total - AuthoredLength) <= .006f;
        }

        private bool ContactsConverged(bool attached)
        {
            for (int i = 1; i < Links; i++)
            {
                if (inverseMass[i] == 0f) continue;
                if (!(Vector3.Distance(points[i], ContactPoint(points[i], attached, i)) <= ContactTolerance)) return false;
            }
            return true;
        }

        private static void LimitMaterialDistances(Vector3[] points, float[] inverseMass, float rest, bool reverse)
        {
            // Each free point is at most its material arclength from either
            // end of its pinned span. This accelerates stretch propagation
            // through contacts without imposing a shape on slack webbing.
            int first = 0;
            for (int last = 1; last < points.Length; last++)
            {
                if (inverseMass[last] != 0f) continue;
                for (int offset = 1; offset < last - first; offset++)
                {
                    int point = reverse ? last - offset : first + offset;
                    if (reverse)
                    {
                        Limit(point, last, (last - point) * rest);
                        Limit(point, first, (point - first) * rest);
                    }
                    else
                    {
                        Limit(point, first, (point - first) * rest);
                        Limit(point, last, (last - point) * rest);
                    }
                }
                first = last;
            }

            void Limit(int point, int pin, float maximum)
            {
                Vector3 delta = points[point] - points[pin];
                if (delta.sqrMagnitude <= maximum * maximum) return;
                points[point] = points[pin] + delta.normalized * maximum;
            }
        }

        private void MeasureConstraintErrors(bool attached, out float length)
        {
            using var profile = MeasureMarker.Auto();
            length = 0f;
            MaximumLinkError = MaximumContactError = 0f;
            MaximumLinkErrorIndex = MaximumContactErrorIndex = -1;
            MaximumLinkErrorWorldCentre = MaximumContactErrorWorldPoint = Vector3.zero;
            StockBodyContactError = Vector3.Distance(points[0], ContactPoint(points[0], attached, 0));
            ForeendBodyContactError = Vector3.Distance(points[Links], ContactPoint(points[Links], attached, Links));
            HardGripBodyContactError = attached && inverseMass[HandPoint] == 0f ?
                Vector3.Distance(points[HandPoint], ContactPoint(points[HandPoint], true, HandPoint)) : 0f;
            float rest = AuthoredLength / Links;
            for (int i = 0; i < Links; i++)
            {
                float link = Vector3.Distance(points[i], points[i + 1]);
                length += link;
                float error = Mathf.Abs(link - rest);
                if (error <= MaximumLinkError) continue;
                MaximumLinkError = error;
                MaximumLinkErrorIndex = i;
                MaximumLinkErrorWorldCentre = (points[i] + points[i + 1]) * .5f;
            }
            for (int i = 1; i < Links; i++)
            {
                if (inverseMass[i] == 0f) continue;
                Vector3 contact = ContactPoint(points[i], attached, i);
                float error = Vector3.Distance(points[i], contact);
                if (error <= MaximumContactError) continue;
                MaximumContactError = error;
                MaximumContactErrorIndex = i;
                MaximumContactErrorWorldPoint = points[i];
            }
        }

        private void ResolveContacts(bool attached)
        {
            using var profile = ContactMarker.Auto();
            for (int i = 1; i < Links; i++)
            {
                if (inverseMass[i] == 0f) continue;
                Vector3 point = ContactPoint(points[i], attached, i);
                Vector3 correction = point - points[i];
                points[i] = point;
                // Friction prevents contact corrections becoming stored velocity.
                if (correction.sqrMagnitude > .00000001f) previous[i] += correction * .9f;
            }
        }

        private Vector3 ContactPoint(Vector3 point, bool attached, int materialPoint)
        {
            int hash = point.x.GetHashCode();
            hash = unchecked(hash * 397) ^ point.y.GetHashCode();
            hash = unchecked(hash * 397) ^ point.z.GetHashCode();
            int slot = materialPoint * ContactCacheSlots + (hash & (ContactCacheSlots - 1));
            if (contactValid[slot] && contactInputs[slot].Equals(point)) return contactOutputs[slot];
            // Keep the previous exact input when two solver positions hash to
            // one bucket. Both entries expire with the same frozen pose above.
            int previousSlot = slot + ContactCacheBankSize;
            if (contactValid[previousSlot] && contactInputs[previousSlot].Equals(point)) return contactOutputs[previousSlot];
            Vector3 input = point;
            if (attached)
            {
                // The convex palm envelope closes the real finger cavity.
                // Only the small authored gripping cylinder is open to the
                // held webbing; torso, sleeve and all other anatomy still act.
                Player3DAnatomicalPart? excluded = null;
                if (hands != null && handAmount > 0f)
                {
                    Vector3 delta = point - handTarget;
                    float along = Vector3.Dot(delta, handAxis);
                    Vector3 radial = delta - handAxis * along;
                    if (Mathf.Abs(along) <= .04f && radial.sqrMagnitude <= hands.CylinderRadius * hands.CylinderRadius)
                        excluded = Player3DAnatomicalPart.LeftHand;
                }
                bodyContacts.ResolvePointOutward(ref point, contactDirections[materialPoint], excluded, materialPoint);
            }
            if (hasFloor)
            {
                float distance = floor.GetDistanceToPoint(point);
                if (distance < HalfThickness + .002f) point += floor.normal * (HalfThickness + .002f - distance);
            }
            if (contactValid[slot])
            {
                contactInputs[previousSlot] = contactInputs[slot];
                contactOutputs[previousSlot] = contactOutputs[slot];
                contactValid[previousSlot] = true;
            }
            contactInputs[slot] = input;
            contactOutputs[slot] = point;
            contactValid[slot] = true;
            return point;
        }

        private void MeasureFloor()
        {
            hasFloor = false;
            Vector3 origin = transform.position + Vector3.up * .25f;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, floorHits, AuthoredLength + .5f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float closest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = floorHits[i];
                if (hit.transform.IsChildOf(transform) || hit.transform.IsChildOf(owner.transform) ||
                    hit.normal.y < .5f || hit.distance >= closest) continue;
                closest = hit.distance; floor = new Plane(hit.normal, hit.point); hasFloor = true;
            }
        }

        private void Render(bool attached)
        {
            using var profile = RenderMarker.Auto();
            Vector3 chest = attached ? registry.Anchors.Chest.position : Vector3.zero;
            Vector3 gunForward = transform.forward, gunRight = transform.right, gunUp = transform.up;
            Vector3 palmNormal = hands != null && handAmount > 0f ? hands.PalmNormal(true) : Vector3.zero;
            PresentedLength = PresentedStockToHandLength = PresentedHandToForeendLength = 0f;
            for (int i = 0; i <= Segments; i++)
            {
                int link = Mathf.Min(Links - 1, i / 2);
                centres[i] = Vector3.Lerp(points[link], points[link + 1], i * .5f - link);
                if (i > 0)
                {
                    float length = Vector3.Distance(centres[i - 1], centres[i]);
                    PresentedLength += length;
                    if (i <= HandCenterlineIndex) PresentedStockToHandLength += length;
                    else PresentedHandToForeendLength += length;
                }
            }
            for (int i = 0; i <= Segments; i++)
            {
                Vector3 tangent = centres[Mathf.Min(Segments, i + 1)] - centres[Mathf.Max(0, i - 1)];
                if (tangent.sqrMagnitude < .000001f) tangent = gunForward;
                tangent.Normalize();
                Vector3 preferred = attached ? centres[i] - chest : gunRight;
                if (attached) preferred -= bodyUp * Vector3.Dot(preferred, bodyUp);
                if (Mathf.Abs(i - HandPoint * 2) < 5 && handAmount > 0f && hands != null)
                    preferred = Vector3.Lerp(preferred.normalized, palmNormal, handAmount);
                Vector3 normal = Vector3.ProjectOnPlane(preferred, tangent).normalized;
                if (normal.sqrMagnitude < .5f) normal = Vector3.ProjectOnPlane(gunUp, tangent).normalized;
                if (normal.sqrMagnitude < .5f) normal = Vector3.ProjectOnPlane(
                    Mathf.Abs(tangent.y) < .9f ? Vector3.up : Vector3.right, tangent).normalized;
                if (normals[i].sqrMagnitude > .5f)
                    normal = Vector3.ProjectOnPlane(Vector3.Lerp(normals[i], normal, .18f), tangent).normalized;
                Vector3 side = Vector3.Cross(normal, tangent).normalized;
                if (i > 0 && Vector3.Dot(sides[i - 1], side) < 0f) { side = -side; normal = -normal; }
                sides[i] = side; normals[i] = normal;
            }
            Matrix4x4 worldToRibbon = ribbon.transform.worldToLocalMatrix;
            for (int i = 0; i < vertices.Length; i++)
            {
                float sample = Mathf.Clamp01(alongWidth[i].x) * Segments;
                int first = Mathf.Min(Segments - 1, Mathf.FloorToInt(sample));
                float blend = sample - first;
                Vector3 centre = Vector3.Lerp(centres[first], centres[first + 1], blend);
                Vector3 side = Vector3.Lerp(sides[first], sides[first + 1], blend).normalized;
                Vector3 normal = Vector3.Lerp(normals[first], normals[first + 1], blend).normalized;
                vertices[i] = worldToRibbon.MultiplyPoint3x4(centre + side * (HalfWidth * alongWidth[i].y) +
                    normal * (HalfThickness * thickness[i].x));
            }
            deformedMesh.vertices = vertices;
            deformedMesh.RecalculateNormals(); deformedMesh.RecalculateBounds();
            if (equipment == null) equipment = registry.GetComponent<PlayerEquipmentContacts>();
            if (equipment == null) return;
            if (attached)
            {
                equipment.SetRibbonContacts(this, centres, HalfWidth, HalfThickness);
                contactsRegistered = true;
            }
            else if (contactsRegistered)
            {
                equipment.ClearRibbonContacts(this);
                contactsRegistered = false;
            }
        }

        internal void ReleaseContacts()
        {
            if (contactsRegistered && equipment != null) equipment.ClearRibbonContacts(this);
            contactsRegistered = false;
            resetPending = true;
        }
        private void OnDisable() => ReleaseContacts();
        private void OnDestroy()
        {
            bodyContacts?.Dispose();
            bodyContacts = null;
            retainedProjectionWork?.Dispose();
            retainedProjectionWork = projectionWork = null;
            if (equipment != null) equipment.Unregister(this);
            if (ribbon != null && ribbon.sharedMesh == deformedMesh) ribbon.sharedMesh = source;
            if (deformedMesh != null) Destroy(deformedMesh);
        }
    }
}

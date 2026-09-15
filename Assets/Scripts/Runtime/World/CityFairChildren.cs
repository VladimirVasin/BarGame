using System;
using UnityEngine;

namespace BarPromenade
{
    public enum CityFairChildPhase { Waiting, WalkingIn, Entering, Looping, Exiting, WalkingOut }

    /// <summary>Three local visitors. No city pedestrian graph, shop transactions or player rig ownership.</summary>
    [DefaultExecutionOrder(210)]
    [DisallowMultipleComponent]
    public sealed class CityFairChildren : MonoBehaviour
    {
        public const float BodyRadius = .16f;
        private sealed class Visitor
        {
            public Vector3[] Route;
            public Vector3 Facing;
            public CityFairChildPhase Phase;
            public int Waypoint, Completed;
            public float Seconds, WalkSeconds;
            public float StopSeconds;
            public bool Moving, Turning, Settling;
            public bool Claimed;
            public CapsuleCollider Body;
            public Vector3? RightTarget, LeftTarget;
        }

        private readonly RaycastHit[] hits = new RaycastHit[32];
        private readonly Collider[] overlaps = new Collider[32];
        private readonly Visitor[] visitors = new Visitor[3];
        private CityFairPlan plan;
        private Transform table, car;
        private Transform[] wheels;
        private Quaternion[] wheelRest;
        private Vector3 carGrip;
        private bool initialized;
        public CityFairChildPresentation[] Actors { get; private set; } = Array.Empty<CityFairChildPresentation>();
        public int ActorCount => Actors.Length;
        public float ElapsedSeconds { get; private set; }
        public Transform Table => table;
        public Transform Car => car;
        public string BenchId => plan.Benches[1].Id;
        public CityFairChildPhase Phase(int index) => visitors[index].Phase;
        public int CompletedCycles(int index) => visitors[index].Completed;
        public bool OwnsBench => initialized && visitors[2].Claimed;
        public Vector3? RightTarget(int index) => visitors[index].RightTarget;
        public Vector3? LeftTarget(int index) => visitors[index].LeftTarget;
        public Vector3[] Route(int index) => (Vector3[])visitors[index].Route.Clone();

        public static CityFairChildren Build(Transform parent, CityFairPlan fair)
        {
            if (fair == null || !fair.IsEnabled) throw new ArgumentException("Children require the grounded fair.", nameof(fair));
            var host = new GameObject("Fair Children");
            host.transform.SetParent(parent, false);
            var result = host.AddComponent<CityFairChildren>();
            result.plan = fair;
            result.table = CityFairChildAssetProvider.CreateProp("LowTable", host.transform).transform;
            result.table.SetPositionAndRotation(fair.ChildTablePosition, fair.ChildTableRotation);
            foreach (MeshFilter mesh in result.table.GetComponentsInChildren<MeshFilter>(true))
                if (mesh.sharedMesh != null) mesh.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh.sharedMesh;
            result.car = CityFairChildAssetProvider.CreateProp("WoodenCar", host.transform).transform;
            result.carGrip = result.car.InverseTransformPoint(CityFairChildAssetProvider.FindPart(result.car.gameObject, "SOCKET_Grip").position);
            result.wheels = new Transform[4];
            result.wheelRest = new Quaternion[4];
            string[] wheelNames = { "WheelPivot_FL", "WheelPivot_FR", "WheelPivot_RL", "WheelPivot_RR" };
            for (int i = 0; i < 4; i++)
            {
                result.wheels[i] = CityFairChildAssetProvider.FindPart(result.car.gameObject, wheelNames[i]);
                result.wheelRest[i] = result.wheels[i].localRotation;
            }
            result.Actors = new CityFairChildPresentation[3];
            CityFairStall toyStall = fair.Stalls[3];
            Vector3 tableRoot = fair.ChildTablePosition;
            Vector3 benchDock = fair.Benches[1].SeatTopCenter + Vector3.forward * .54f;
            Vector3[][] routes =
            {
                new[] { new Vector3(fair.Bounds.xMax - .46f, 0f, toyStall.Position.z + .40f),
                    new Vector3(toyStall.Position.x + 1.66f, 0f, toyStall.Position.z - .54f) },
                new[] { tableRoot + new Vector3(-.65f, 0f, -.90f),
                    tableRoot + new Vector3(0f, 0f, -.70f), tableRoot + new Vector3(0f, 0f, -.52f) },
                new[] { new Vector3(fair.Bounds.xMax - .80f, 0f, fair.BoundaryZ + .10f),
                    new Vector3(fair.Bounds.xMax - .90f, 0f, fair.BoundaryZ + 1.50f), benchDock }
            };
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < routes[i].Length; j++) routes[i][j] = result.Ground(routes[i][j]);
                result.ValidateRoute(routes[i]);
                GameObject actor = CityFairChildAssetProvider.Create(i, host.transform);
                actor.name = "Fair Child " + (i == 0 ? "Raincoat - wooden toys" : i == 1 ? "Quilted - toy car" : "Vest - bench");
                result.Actors[i] = CityFairChildPresentation.Attach(actor);
                var body = actor.AddComponent<CapsuleCollider>();
                body.height = 1.22f;
                body.radius = BodyRadius;
                body.center = Vector3.up * .65f;
                result.visitors[i] = new Visitor { Route = routes[i], Body = body,
                    Facing = i == 0 ? Vector3.left : Vector3.forward };
            }
            result.initialized = true;
            result.Restart();
            return result;
        }

        private void Restart()
        {
            ElapsedSeconds = 0f;
            for (int i = 0; i < Actors.Length; i++)
            {
                Visitor visitor = visitors[i];
                visitor.Phase = CityFairChildPhase.Waiting;
                visitor.Seconds = -i * 2.5f;
                visitor.Waypoint = 1;
                visitor.WalkSeconds = 0f;
                visitor.Completed = 0;
                visitor.StopSeconds = 0f;
                visitor.Settling = false;
                visitor.Claimed = false;
                visitor.Body.enabled = true;
                Actors[i].gameObject.SetActive(true);
                Actors[i].transform.SetPositionAndRotation(visitor.Route[0], Quaternion.LookRotation(visitor.Facing));
                Actors[i].ResetPose();
                NpcFootstepSources.Register(Actors[i].transform);
                Present(i);
            }
            PlaceCar(0f);
        }

        private void LateUpdate()
        {
            if (!initialized || GameTimeScaleRuntime.IsPaused || Time.deltaTime <= 0f) return;
            float delta = Mathf.Min(Time.deltaTime, .10f);
            ElapsedSeconds += delta;
            for (int i = 0; i < Actors.Length; i++)
            {
                Advance(i, delta);
                Present(i);
            }
        }

        private void Advance(int index, float delta)
        {
            Visitor visitor = visitors[index];
            visitor.Moving = visitor.Turning = false;
            visitor.Seconds += delta;
            if (visitor.Phase == CityFairChildPhase.Waiting)
            {
                if (visitor.Seconds < 3.5f + index) return;
                if (index == 2 && CityBenchSeatClaims.IsClaimed(BenchId)) return;
                SetPhase(visitor, CityFairChildPhase.WalkingIn);
                visitor.Waypoint = 1;
            }
            if (visitor.Phase == CityFairChildPhase.WalkingIn || visitor.Phase == CityFairChildPhase.WalkingOut)
            {
                Transform actor = Actors[index].transform;
                Vector3 destination = visitor.Route[visitor.Waypoint];
                Vector3 direction = Vector3.ProjectOnPlane(destination - actor.position, Vector3.up);
                if (direction.sqrMagnitude > .000025f)
                {
                    Quaternion desired = Quaternion.LookRotation(direction);
                    visitor.Turning = Quaternion.Angle(actor.rotation, desired) > 5f;
                    actor.rotation = Quaternion.RotateTowards(actor.rotation, desired, 145f * delta);
                    if (Quaternion.Angle(actor.rotation, desired) > 20f) return;
                    Vector3 next = Ground(Vector3.MoveTowards(actor.position, destination, .62f * delta));
                    if (Blocked(visitor, actor.position, next)) return;
                    actor.position = next;
                    visitor.Moving = true;
                    visitor.WalkSeconds += delta;
                    return;
                }
                bool entering = visitor.Phase == CityFairChildPhase.WalkingIn;
                bool atEnd = entering ? visitor.Waypoint == visitor.Route.Length - 1 : visitor.Waypoint == 0;
                if (!atEnd) { visitor.Waypoint += entering ? 1 : -1; return; }
                Quaternion facing = Quaternion.LookRotation(visitor.Facing);
                visitor.Turning = Quaternion.Angle(actor.rotation, facing) > .1f;
                actor.rotation = Quaternion.RotateTowards(actor.rotation, facing, 145f * delta);
                if (Quaternion.Angle(actor.rotation, facing) > .1f) return;
                visitor.Settling = true;
                visitor.StopSeconds += delta;
                if (visitor.StopSeconds < CityFairChildPresentation.Duration(CityFairChildAction.Stop)) return;
                if (entering)
                {
                    if (index == 2)
                    {
                        if (!TryClaimBench(visitor))
                        {
                            SetPhase(visitor, CityFairChildPhase.WalkingOut);
                            visitor.Waypoint = visitor.Route.Length - 2;
                            return;
                        }
                        visitor.Body.enabled = false;
                    }
                    SetPhase(visitor, CityFairChildPhase.Entering);
                }
                else SetPhase(visitor, CityFairChildPhase.Waiting);
            }
            else if (visitor.Phase == CityFairChildPhase.Entering)
            {
                if (visitor.Seconds >= CityFairChildPresentation.Duration(Action(index, CityFairChildPhase.Entering)))
                    SetPhase(visitor, CityFairChildPhase.Looping);
            }
            else if (visitor.Phase == CityFairChildPhase.Looping)
            {
                // Whole authored cycles keep the last loop pose continuous with its exit.
                float duration = CityFairChildPresentation.Duration(Action(index, CityFairChildPhase.Looping));
                if (visitor.Seconds >= duration * (index == 0 ? 3f : index == 1 ? 3f : 4f))
                    SetPhase(visitor, CityFairChildPhase.Exiting);
            }
            else if (visitor.Phase == CityFairChildPhase.Exiting &&
                visitor.Seconds >= CityFairChildPresentation.Duration(Action(index, CityFairChildPhase.Exiting)))
            {
                visitor.Completed++;
                visitor.Body.enabled = true;
                ReleaseBench(visitor);
                SetPhase(visitor, CityFairChildPhase.WalkingOut);
                visitor.Waypoint = visitor.Route.Length - 2;
            }
        }

        private bool TryClaimBench(Visitor visitor)
        {
            Vector3 dock = visitor.Route[visitor.Route.Length - 1];
            // A hero approaching an unclaimed plank also gets right of way.
            if (Blocked(visitor, dock, dock)) return false;
            visitor.Claimed = CityBenchSeatClaims.TryClaim(BenchId, visitor);
            return visitor.Claimed;
        }

        private void ReleaseBench(Visitor visitor)
        {
            if (!visitor.Claimed) return;
            CityBenchSeatClaims.Release(BenchId, visitor);
            visitor.Claimed = false;
        }

        private void Present(int index)
        {
            Visitor visitor = visitors[index];
            CityFairChildPresentation actor = Actors[index];
            bool walking = visitor.Phase == CityFairChildPhase.WalkingIn || visitor.Phase == CityFairChildPhase.WalkingOut;
            bool busy = visitor.Phase == CityFairChildPhase.Entering || visitor.Phase == CityFairChildPhase.Looping || visitor.Phase == CityFairChildPhase.Exiting;
            CityFairChildAction action = busy ? Action(index, visitor.Phase) : walking && visitor.Settling ? CityFairChildAction.Stop :
                walking && visitor.Moving ? CityFairChildAction.Walk : walking && visitor.Turning ? CityFairChildAction.Turn : CityFairChildAction.Idle;
            float sampleSeconds = walking && visitor.Settling ? visitor.StopSeconds : walking && visitor.Moving ? visitor.WalkSeconds : Mathf.Max(0f, visitor.Seconds);
            CityFairChildAction gesture = index == 1 ? CityFairChildAction.AdjustCap : CityFairChildAction.AdjustSleeve;
            if (!busy && !walking && index > 0 && visitor.Seconds >= 1f && visitor.Seconds < 1f + CityFairChildPresentation.Duration(gesture))
            { action = gesture; sampleSeconds = visitor.Seconds - 1f; }
            actor.Sample(action, sampleSeconds);
            visitor.RightTarget = null;
            visitor.LeftTarget = null;
            if (index == 2 && busy)
            {
                float weight = BusyWeight(index);
                Vector3 final = plan.Benches[1].SeatTopCenter + Vector3.up * .07f;
                Vector3 authored = actor.transform.TransformPoint(new Vector3(0f, .57f, -.54f));
                actor.ModelRoot.position += (final - authored) * weight;
            }
            else
            {
                actor.ApplyFootContacts(plan.SampleGroundY(actor.LeftFoot.position), plan.SampleGroundY(actor.RightFoot.position));
            }
            if (index == 0 && busy)
            {
                CityFairStall stall = plan.Stalls[3];
                visitor.RightTarget = stall.Position + stall.Rotation * new Vector3(-1.38f, .966f, .37f);
                visitor.LeftTarget = stall.Position + stall.Rotation * new Vector3(-1.38f, .966f, .71f);
                actor.ApplyHandContacts(visitor.RightTarget, visitor.LeftTarget, BusyWeight(index));
                actor.ApplyLook(stall.Position + stall.Rotation * new Vector3(-.85f, 1.06f, .57f), BusyWeight(index));
            }
            else if (index == 1)
            {
                float seconds = visitor.Phase == CityFairChildPhase.Looping ? visitor.Seconds : 0f;
                PlaceCar(seconds);
                if (busy)
                {
                    visitor.RightTarget = car.TransformPoint(carGrip);
                    visitor.LeftTarget = table.TransformPoint(new Vector3(-.15f, .565f, -.20f));
                    actor.ApplyHandContacts(visitor.RightTarget, visitor.LeftTarget, BusyWeight(index));
                    actor.ApplyLook(car.position, BusyWeight(index));
                }
            }
            else if (index == 2 && busy)
                actor.ApplyLook(plan.OrganPosition + Vector3.up * 1.1f, .25f * BusyWeight(index));
        }

        private float BusyWeight(int index)
        {
            Visitor visitor = visitors[index];
            if (visitor.Phase == CityFairChildPhase.Looping) return 1f;
            float duration = CityFairChildPresentation.Duration(Action(index, visitor.Phase));
            float weight = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(visitor.Seconds / duration));
            return visitor.Phase == CityFairChildPhase.Exiting ? 1f - weight : weight;
        }

        private void PlaceCar(float seconds)
        {
            if (car == null || table == null) return;
            float duration = CityFairChildPresentation.Duration(CityFairChildAction.ToyLoop);
            float phase = Mathf.Repeat(seconds, duration) / duration;
            float roll = .08f * Mathf.Sin(phase * Mathf.PI * 2f);
            float lift = phase < .50f ? 0f : .065f * Mathf.Sin((phase - .50f) * Mathf.PI * 2f);
            float turn = phase < .50f ? 0f : 24f * Mathf.Sin((phase - .50f) * Mathf.PI * 2f);
            car.SetPositionAndRotation(table.TransformPoint(new Vector3(.09f, .55f + lift, -.12f + roll)),
                table.rotation * Quaternion.Euler(0f, turn, 0f));
            for (int i = 0; i < wheels.Length; i++)
                wheels[i].localRotation = wheelRest[i] * Quaternion.AngleAxis(roll / .029f * Mathf.Rad2Deg, Vector3.right);
        }

        private bool Blocked(Visitor visitor, Vector3 start, Vector3 end)
        {
            Vector3 bottom = end + Vector3.up * .25f;
            Vector3 top = end + Vector3.up * 1.08f;
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, BodyRadius + .01f, overlaps, ~0, QueryTriggerInteraction.Ignore);
            if (count == overlaps.Length) return true;
            for (int i = 0; i < count; i++)
                if (overlaps[i] != visitor.Body) return true;
            Vector3 delta = end - start;
            if (delta.sqrMagnitude < .000001f) return false;
            count = Physics.CapsuleCastNonAlloc(start + Vector3.up * .25f, start + Vector3.up * 1.08f,
                BodyRadius, delta.normalized, hits, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (hits[i].collider != visitor.Body) return true;
            return count == hits.Length;
        }

        private Vector3 Ground(Vector3 point)
        {
            point.y = plan.SampleGroundY(point);
            return point;
        }

        private void ValidateRoute(Vector3[] route)
        {
            for (int segment = 1; segment < route.Length; segment++)
                for (int sample = 0; sample <= 24; sample++)
                {
                    Vector3 point = Vector3.Lerp(route[segment - 1], route[segment], sample / 24f);
                    Rect body = CityFairPlanner.Footprint(point, BodyRadius * 2f, BodyRadius * 2f);
                    if (!plan.Contains(point)) throw new InvalidOperationException("A fair child route leaves the fair.");
                    foreach (Rect clear in plan.ClearPaths)
                        if (body.Overlaps(clear)) throw new InvalidOperationException("A fair child route occupies a clear path.");
                    foreach (Rect obstacle in plan.Obstacles)
                        if (body.Overlaps(obstacle)) throw new InvalidOperationException("A fair child route intersects a prop.");
                }
        }

        private static CityFairChildAction Action(int index, CityFairChildPhase phase)
        {
            if (index == 0) return phase == CityFairChildPhase.Entering ? CityFairChildAction.GoodsEnter :
                phase == CityFairChildPhase.Exiting ? CityFairChildAction.GoodsExit : CityFairChildAction.GoodsLoop;
            if (index == 1) return phase == CityFairChildPhase.Entering ? CityFairChildAction.ToyEnter :
                phase == CityFairChildPhase.Exiting ? CityFairChildAction.ToyExit : CityFairChildAction.ToyLoop;
            return phase == CityFairChildPhase.Entering ? CityFairChildAction.SitEnter :
                phase == CityFairChildPhase.Exiting ? CityFairChildAction.SitExit : CityFairChildAction.SitLoop;
        }

        private static void SetPhase(Visitor visitor, CityFairChildPhase phase)
        {
            visitor.Phase = phase;
            visitor.Seconds = 0f;
            visitor.Settling = false;
            visitor.StopSeconds = 0f;
        }

        private void OnEnable() { if (initialized) Restart(); }
        private void OnDisable()
        {
            if (!initialized) return;
            foreach (Visitor visitor in visitors) ReleaseBench(visitor);
            foreach (CityFairChildPresentation actor in Actors)
            {
                if (actor == null) continue;
                NpcFootstepSources.Unregister(actor.transform);
                actor.gameObject.SetActive(false);
            }
            PlaceCar(0f);
        }
    }
}

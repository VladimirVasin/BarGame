using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal sealed class RidgeBasketballCourt : MonoBehaviour
    {
        internal enum Phase { Gathering, Fetch, Pickup, Carry, Throw, Flight, Rebound, Leaving }
        private const float Radius = RidgeBasketballPlan.BallRadius;
        private const float ReleaseAt = .75f, FlightTime = .87f, DropTime = .52f, BounceTime = .62f;
        private const float WalkSpeed = 1.40f;
        private const float RimStandoff = .68f;
        private readonly CityPedestrianPresentation[] players = new CityPedestrianPresentation[2];
        private readonly CityAmbientWalkPlan.Entry[] entrances = new CityAmbientWalkPlan.Entry[2];
        private readonly Vector3[] returnPoints = new Vector3[2];
        private readonly AnimatorCullingMode[] previousCulling = new AnimatorCullingMode[2];
        private readonly bool[][] previousSkinUpdates = new bool[2][];
        private CityHillsideGroundPlan terrain;
        private CityHillsideStreetRoute street;
        private Transform ball, net;
        private Vector3 line, wait, basket, resting, release, contact, bounce;
        private System.Random random;
        private bool hasEntrances, leaving;
        private int shooter, outcome;
        private float seconds, stride;
        internal Phase Current { get; private set; }
        internal int Attempts { get; private set; }
        internal bool Made => outcome == 0;
        internal int Shooter => shooter;
        internal Transform Ball => ball;
        internal Vector3 Rim => basket;
        internal CityPedestrianPresentation Player(int seat) => players[seat];
        internal int FreeSeat => !isActiveAndEnabled || leaving ? -1 : !players[0] ? 0 : !players[1] ? 1 : -1;

        internal static void Build(Transform parent, CityLayout layout, RidgeBasketballPlan plan)
        {
            var root = new GameObject("Ridge courtyard basketball").transform;
            root.SetParent(parent, false);
            var court = root.gameObject.AddComponent<RidgeBasketballCourt>();
            court.terrain = CityHillsideGroundPlan.ForLayout(layout);
            foreach (var road in CityHillsideStreetPlan.ForLayout(layout).Routes)
                if (road.Bank == CityTerraceTransportBank.East) court.street = road;
            root.position = court.Floor(new Vector3(plan.Wall.x, 0, plan.Wall.y));
            var asset = Resources.Load<GameObject>("City/Playground/RidgeBasketball");
            if (!asset) throw new InvalidOperationException("Missing courtyard basketball equipment.");
            Transform model = Instantiate(asset, root, false).transform;
            foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>()) Finish(renderer);

            var board = CityGlassReturnWorld.NormalizePivot(root, "Board");
            var stripe = CityGlassReturnWorld.NormalizePivot(root, "Stripe");
            court.ball = CityGlassReturnWorld.NormalizePivot(root, "Ball");
            court.net = CityGlassReturnWorld.NormalizePivot(root, "Net");
            // Pivots are placed in world metres irrespective of the FBX root's units.
            board.position = root.position + Vector3.up * 2.30f;
            court.basket = board.position + Vector3.back * RimStandoff;
            court.net.position = court.basket;
            court.line = court.Floor(root.position + Vector3.back * 3.49f);
            court.wait = court.Floor(court.line + new Vector3(1.67f, 0, .42f));
            stripe.position = court.Floor(court.line + Vector3.forward * .17f) + Vector3.up * .006f;
            var along = court.Floor(stripe.position + Vector3.right * .51f) - court.Floor(stripe.position - Vector3.right * .51f);
            var across = court.Floor(stripe.position + Vector3.forward * .1f) - court.Floor(stripe.position - Vector3.forward * .1f);
            stripe.rotation = Quaternion.LookRotation(across, Vector3.Cross(across, along).normalized);
            court.random = new System.Random(unchecked(layout.Seed ^ 0x437128));
            court.ResetBall();
        }

        private static void Finish(Renderer renderer)
        {
            switch (renderer.name)
            {
                case "BoardTimber":
                    CityParkSurfaceAppearance.ApplyCombined(renderer, CityParkSurfaceKind.Timber, new Color(.59f,.53f,.43f));
                    return;
                case "HoopSteel":
                case "BoltHeads":
                    CityParkSurfaceAppearance.ApplyCombined(renderer, CityParkSurfaceKind.PaintedMetal,
                        renderer.name == "HoopSteel" ? new Color(.65f,.33f,.16f) : new Color(.27f,.26f,.235f));
                    return;
            }
            var tint = renderer.name switch {
                "BallRubber" => new Color(.61f,.30f,.14f), "BallChannels" => new Color(.13f,.10f,.075f),
                "CordKnots" => new Color(.65f,.62f,.52f), "ChalkDust" => new Color(.66f,.65f,.58f),
                _ => throw new InvalidOperationException("Unknown basketball finish " + renderer.name)
            };
            renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", tint);
            properties.SetColor("_Color", tint);
            properties.SetFloat("_Smoothness", renderer.name == "BallRubber" ? .13f : .065f);
            renderer.SetPropertyBlock(properties);
        }

        private Vector3 Floor(Vector3 p)
        {
            var xz = new Vector2(p.x, p.z);
            if (street != null && street.TrySampleTop(xz, out float streetTop, out _)) p.y = streetTop;
            else if (terrain.TrySampleTop(xz, out float ground)) p.y = ground;
            else throw new InvalidOperationException("No basketball footing at " + p);
            return p;
        }

        internal bool TryEntrance(CityAmbientWalkPlan walks, List<CityAmbientWalkPlan.Entry> buffer,
            int seat, out CityAmbientWalkPlan.Entry entry)
        {
            if (!hasEntrances)
            {
                for (int i = 0; i < 2; i++)
                {
                    Vector3 preferred = line + new Vector3(i == 0 ? -1.6f : 2f, 0, -5.65f);
                    walks.CollectEntries(preferred, 12f, buffer);
                    float best = 16f;
                    bool found = false;
                    foreach (var candidate in buffer)
                    {
                        if (!walks.AllowsChild(candidate.Link) ||
                            !walks.LinkId(candidate.Link).StartsWith("hill:East:pavement:", StringComparison.Ordinal)) continue;
                        float distance = walks.ProjectDistance(candidate.Link, preferred);
                        Vector3 point = walks.Sample(candidate.Link, distance, out _);
                        float gap = (new Vector2(point.x-preferred.x,point.z-preferred.z)).sqrMagnitude;
                        if (gap >= best) continue;
                        best = gap;
                        entrances[i] = new CityAmbientWalkPlan.Entry(candidate.Link, distance, point);
                        found = true;
                    }
                    if (!found) { entry = default; return false; }
                }
                hasEntrances = true;
            }
            entry = entrances[seat];
            return true;
        }

        internal void Join(int seat, CityPedestrianPresentation body)
        {
            players[seat] = body;
            returnPoints[seat] = body.transform.position;
            var rig = body.Registry;
            previousCulling[seat] = rig.Animator.cullingMode;
            rig.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (previousSkinUpdates[seat] == null || previousSkinUpdates[seat].Length < rig.Renderers.Count)
                previousSkinUpdates[seat] = new bool[rig.Renderers.Count];
            for (int part = 0; part < rig.Renderers.Count; part++)
                if (rig.Renderers[part] is SkinnedMeshRenderer skin)
                {
                    previousSkinUpdates[seat][part] = skin.forceMatrixRecalculationPerRender;
                    skin.forceMatrixRecalculationPerRender = true;
                }
        }

        internal bool OnCourt(CityPedestrianPresentation body) => players[0] == body || players[1] == body;

        internal void Remove(CityPedestrianPresentation body)
        {
            for (int i = 0; i < 2; i++)
                if (players[i] == body)
                {
                    var rig = body.Registry;
                    for (int part = 0; part < rig.Renderers.Count; part++)
                        if (rig.Renderers[part] is SkinnedMeshRenderer skin)
                            skin.forceMatrixRecalculationPerRender = previousSkinUpdates[i][part];
                    rig.Animator.cullingMode = previousCulling[i];
                    body.ClearAuthoredAction();
                    players[i] = null;
                    leaving = true;
                }
            if (!players[0] && !players[1]) ResetBall();
        }

        private void OnDisable()
        {
            for (int i = 0; i < players.Length; i++) if (players[i]) Remove(players[i]);
        }

        private void ResetBall()
        {
            shooter = 0;
            leaving = false;
            resting = Floor(line + Vector3.forward * .29f) + Vector3.up * Radius;
            ball.position = resting;
            net.rotation = Quaternion.identity;
            Set(Phase.Gathering);
        }

        internal void Advance(float dt, bool daylight)
        {
            if (!players[0] && !players[1]) return;
            leaving |= !daylight;
            if (leaving && Current != Phase.Leaving && (Current == Phase.Gathering || Current == Phase.Fetch ||
                Current == Phase.Carry || !players[0] || !players[1])) BeginLeaving();
            seconds += dt;
            if (Current == Phase.Leaving)
            {
                for (int i = 0; i < 2; i++)
                {
                    if (players[i] && Move(players[i], returnPoints[i], dt)) Remove(players[i]);
                }
                return;
            }
            var active = players[shooter];
            var next = players[1-shooter];
            if (Current == Phase.Gathering)
            {
                bool firstReady = active && Move(active, line, dt);
                bool nextReady = next && Move(next, wait, dt);
                if (firstReady && nextReady) Set(Phase.Fetch);
                return;
            }

            if (Move(next, wait, dt)) Watch(next, ball.position, dt);
            switch (Current)
            {
                case Phase.Fetch:
                    // Two different lanes: the previous shooter steps aside as the next runs up.
                    Vector3 pick = Floor(resting - Vector3.forward * .29f);
                    if (Move(active, pick, dt) && Face(active, Quaternion.identity, dt)) Set(Phase.Pickup);
                    break;
                case Phase.Pickup:
                    Pose(active, CityFairChildAction.BallPickup, seconds);
                    if (seconds >= .75f) Hold(active, CityFairChildAction.BallPickup, seconds);
                    if (seconds >= 1.5f) { stride = 0f; Set(Phase.Carry); }
                    break;
                case Phase.Carry:
                    if (Move(active, line, dt, carrying: true) && Face(active, Quaternion.identity, dt)) Set(Phase.Throw);
                    Pose(active, CityFairChildAction.BallCarry, stride);
                    Hold(active, CityFairChildAction.BallCarry, stride);
                    break;
                case Phase.Throw:
                    Pose(active, CityFairChildAction.BallThrow, Mathf.Min(seconds, ReleaseAt));
                    Hold(active, CityFairChildAction.BallThrow, Mathf.Min(seconds, ReleaseAt));
                    if (seconds >= ReleaseAt) ReleaseShot();
                    break;
                case Phase.Flight:
                    Pose(active, CityFairChildAction.BallThrow, ReleaseAt+seconds);
                    float t = Mathf.Clamp01(seconds/FlightTime);
                    // Ease the horizontal arrival at the ring so a make drops through the net.
                    ball.position = Vector3.Lerp(release, contact, Made ? t*(2f-t) : t) +
                        Vector3.up * (4.9f*FlightTime*FlightTime*t*(1f-t));
                    Spin(dt);
                    if (seconds >= FlightTime) Set(Phase.Rebound);
                    break;
                case Phase.Rebound:
                    Rebound(active, dt);
                    if (seconds >= DropTime+BounceTime+.18f)
                    {
                        active.ClearAuthoredAction();
                        shooter = 1-shooter;
                        if (leaving) BeginLeaving(); else Set(Phase.Fetch);
                    }
                    break;
            }
        }

        private void ReleaseShot()
        {
            release = ball.position;
            Attempts++;
            // One independent draw at release: one make, four equally likely misses.
            outcome = random.Next(5);
            float side = outcome == 1 ? -.265f : outcome == 2 ? .265f : outcome == 3 ? .43f : -.14f;
            contact = outcome switch {
                0 => basket,
                3 => basket + new Vector3(side,.12f,RimStandoff-.10f-Radius),
                4 => basket + new Vector3(side,-.26f,-.68f),
                _ => basket + new Vector3(side,.105f,0f)
            };
            bounce = Floor(basket + new Vector3(Made ? 0 : side*1.6f,0,Made ? -.09f : -1.15f)) + Vector3.up*Radius;
            resting = Floor(bounce + new Vector3(Made ? .31f : side*.7f,0,-.84f)) + Vector3.up*Radius;
            Set(Phase.Flight);
        }

        private void Rebound(CityPedestrianPresentation active, float dt)
        {
            if (Made)
            {
                Pose(active, CityFairChildAction.BasketJump, seconds);
                float shake = Mathf.Clamp01(seconds/.48f);
                net.rotation = Quaternion.Euler(9f*Mathf.Sin(shake*Mathf.PI*2f)*(1f-shake),0,0);
            }
            else active.Advance(dt,false);
            if (seconds <= DropTime)
            {
                float t = seconds/DropTime;
                ball.position = Vector3.Lerp(contact,bounce,t) + Vector3.up *
                    ((Made ? .42f : .78f)*4f*t*(1f-t));
            }
            else
            {
                float t = Mathf.Clamp01((seconds-DropTime)/BounceTime);
                ball.position = Vector3.Lerp(bounce,resting,t) + Vector3.up*(1.65f*t*(1f-t));
            }
            Spin(dt);
        }

        private void Spin(float dt) => ball.Rotate(Vector3.right, 250f*dt, Space.World);

        private void BeginLeaving()
        {
            foreach (var child in players) if (child) child.ClearAuthoredAction();
            resting = Floor(ball.position) + Vector3.up*Radius;
            ball.position = resting;
            net.rotation = Quaternion.identity;
            Set(Phase.Leaving);
        }

        private void Set(Phase phase) { Current = phase; seconds = 0; }

        private static void Pose(CityPedestrianPresentation body, CityFairChildAction action, float time)
        {
            float duration = CityFairChildActions.Duration(action);
            float phase = CityFairChildActions.IsLoop(action) ? Mathf.Repeat(time/duration,1f) : Mathf.Clamp01(time/duration);
            CityPlaygroundVisit.SamplePose(body, action, phase, body.transform.position, body.transform.rotation, false);
        }

        private void Hold(CityPedestrianPresentation body, CityFairChildAction action, float time)
        {
            var pose = CityFairChildActions.Sample(action,time);
            ball.position = body.transform.TransformPoint((pose.LeftGrip+pose.RightGrip)*.5f);
        }

        private static bool Face(CityPedestrianPresentation body, Quaternion rotation, float dt)
        {
            var actor = body.transform;
            actor.rotation = Quaternion.RotateTowards(actor.rotation, rotation, 330f*dt);
            return Quaternion.Angle(actor.rotation,rotation) < 1f;
        }

        private static void Watch(CityPedestrianPresentation body, Vector3 point, float dt)
        {
            var direction = Vector3.ProjectOnPlane(point-body.transform.position,Vector3.up);
            if (direction.sqrMagnitude > .001f) Face(body,Quaternion.LookRotation(direction),dt);
            body.Advance(dt,false);
        }

        private bool Move(CityPedestrianPresentation body, Vector3 target, float dt, bool carrying = false)
        {
            Transform actor = body.transform;
            Vector3 delta = Vector3.ProjectOnPlane(target-actor.position,Vector3.up);
            if (delta.sqrMagnitude < .0004f) return true;
            if (!Face(body, Quaternion.LookRotation(delta),dt))
            {
                if (!carrying) body.Advance(dt,false);
                return false;
            }
            Vector3 previous = actor.position;
            Vector3 step = Floor(previous + Vector3.ClampMagnitude(delta,WalkSpeed*dt));
            if (!CityPlaygroundVisit.ClearPoint(step)) { if (!carrying) body.Advance(dt,false); return false; }
            actor.position = step;
            if (carrying) stride += (step-previous).magnitude / CityFairChildActions.StreetCycleMetres;
            else body.AdvanceTravelled(dt,true,step-previous);
            return (step-target).sqrMagnitude < .0004f;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum VillageWomanStage
    {
        Waiting, WalkingToPickup, PickingUp, Carrying, PuttingDown,
        WalkingToRest, Resting, WalkingToStack, Working, WalkingWithLog, LoadingLog, PlacingLog
    }

    /// <summary>Local households with finite stock and scene-owned presentation.</summary>
    [DefaultExecutionOrder(350)]
    public sealed partial class AlpineVillageLifeController : MonoBehaviour
    {
        private readonly List<Transform> baskets = new List<Transform>();
        private readonly List<Collider> solids = new List<Collider>();
        private readonly bool[] greeted = new bool[6];
        private readonly float[] speechRemaining = new float[6];
        private readonly float[] speechCooldown = new float[6];
        private readonly NpcSpeaker[] speakers = new NpcSpeaker[6];
        private Transform hero;
        private NpcSpeechBubbleView bubbles;
        private AlpineVillageWalkableArea walkable;
        private Transform lid;
        private float stageTime;
        private float stationTime;
        private float walkTime;
        private float walkSpeed;
        private Vector3[] route;
        private int routeIndex;
        private VillageWomanStage routeEnd;
        private Quaternion destinationFacing;
        private bool basketAttached;
        private int stationBeat = -1;
        private VillageLifeAudio sound;

        public AlpineVillageLifePlan Plan { get; private set; }
        public VillageResidentPresentation Woman { get; private set; }
        public VillageResidentPresentation StationWorker { get; private set; }
        public IReadOnlyList<Transform> Baskets => baskets;
        public IReadOnlyList<Collider> SolidProps => solids;
        public VillageWomanStage WomanStage { get; private set; }
        public int DeliveredBaskets => GameSessionState.VillageHousehold.DeliveredBasketCount;
        public bool IsWomanBlocked { get; private set; }
        public string LastGreetingKey { get; private set; } = string.Empty;
        public Transform CarriedBasket => basketAttached ? baskets[workBasket] : null;

        public static AlpineVillageLifeController Create(Transform parent,
            AlpineVillagePlan village, AlpineVillageWalkableArea walkable,
            Transform player, Camera camera,
            IReadOnlyDictionary<string, VillageResidentDoor> doors = null,
            AlpineVillageSnowTreading snow = null)
        {
            var host = new GameObject("Village Household Life");
            host.transform.SetParent(parent, false);
            try
            {
                var life = host.AddComponent<AlpineVillageLifeController>();
                life.Plan = AlpineVillageLifePlan.Create(village);
                life.hero = player;
                life.walkable = walkable;
                life.BuildProps();
                var library = VillageResidentLibrary.Load();
                if (library == null || !library.IsComplete)
                    throw new InvalidOperationException("Village resident library has not been imported.");
                life.Woman = library.Create(VillageResidentRole.WoodWoman, host.transform);
                life.StationWorker = library.Create(VillageResidentRole.StationWorker, host.transform);
                life.Woman.transform.SetPositionAndRotation(life.Plan.Dock(life.Plan.Pickups[0]),
                    Quaternion.LookRotation(-life.Plan.Forward));
                life.StationWorker.transform.SetPositionAndRotation(life.Plan.StationWork,
                    Quaternion.LookRotation(-life.Plan.StationForward));
                life.Woman.Apply(VillageResidentAction.Idle, 0f);
                life.StationWorker.Apply(VillageResidentAction.Idle, 0f);
                life.bubbles = host.AddComponent<NpcSpeechBubbleView>();
                life.bubbles.Initialize(camera, player);
                life.InstallResident(life.StationWorker, 0, "village-station-worker");
                life.InstallResident(life.Woman, 1, "village-wood-woman");
                life.sound = host.AddComponent<VillageLifeAudio>();
                life.sound.Initialize();
                life.InitializeFirewood();
                life.InitializeNeighbours(library, doors, snow);
                return life;
            }
            catch
            {
                host.SetActive(false);
                Destroy(host);
                throw;
            }
        }

        private void BuildProps()
        {
            Quaternion facing = Quaternion.LookRotation(Plan.Forward);
            Place(VillageLifePropKind.WoodShelter, Plan.Shelter, facing, true);
            Transform stack = Place(VillageLifePropKind.LogStack, Plan.Stack, facing, true);
            BuildLooseLogs(stack);
            Transform block = Place(VillageLifePropKind.ChoppingBlock, Plan.Block, facing, true);
            Quaternion axeRotation = facing * Quaternion.Euler(0f, 0f, 165f);
            Vector3 axeRest = block.TransformPoint(VillageLifePropLibrary.GetAnchor(
                VillageLifePropKind.ChoppingBlock, "AxeRest"));
            Place(VillageLifePropKind.Axe, axeRest - Vector3.up * 0.02f -
                axeRotation * VillageLifePropLibrary.GetAnchor(VillageLifePropKind.Axe, "Blade"),
                axeRotation, false);
            var paths = AlpineVillagePathPlanner.Create(Plan.Village);
            float sledSnow = AlpineVillageSnowDrift.SampleDepth(Plan.Village, paths,
                new Vector2(Plan.Sled.x, Plan.Sled.z));
            Place(VillageLifePropKind.UtilitySled,
                Plan.Sled + Vector3.up * Mathf.Max(0f, sledSnow - 0.03f), facing, true);
            for (int i = 0; i < AlpineVillageLifePlan.BasketCount; i++)
            {
                Place(VillageLifePropKind.BasketStand, Plan.Pickups[i], facing, true);
                Place(VillageLifePropKind.BasketStand, Plan.Deliveries[i], facing, true);
                Transform basket = Place(VillageLifePropKind.Basket,
                    Plan.Pickups[i] + Vector3.up * AlpineVillageLifePlan.StandHeight,
                    Quaternion.LookRotation(-Plan.Forward), false);
                basket.name = "Firewood Basket " + i;
                baskets.Add(basket);
            }
            Quaternion stationFacing = Quaternion.LookRotation(Plan.StationForward);
            Transform crate = Place(VillageLifePropKind.StationCrate, Plan.StationCrate, stationFacing, true);
            lid = VillageLifePropLibrary.Create(VillageLifePropKind.StationLid, crate).transform;
            lid.localPosition = VillageLifePropLibrary.GetAnchor(VillageLifePropKind.StationCrate, "LidHinge");
            lid.localRotation = Quaternion.identity;
            stationStrap = VillageLifePropLibrary.Create(VillageLifePropKind.StationStrap, crate).transform;
            var strap = stationStrap;
            strap.localPosition = VillageLifePropLibrary.GetAnchor(VillageLifePropKind.StationCrate, "StrapRoot");
        }

        private Transform Place(VillageLifePropKind kind, Vector3 position, Quaternion rotation, bool solid)
        {
            GameObject instance = VillageLifePropLibrary.Create(kind, transform);
            instance.transform.SetPositionAndRotation(position, rotation);
            if (solid)
            {
                // Only actual meshes collide: the shelter remains open between its posts.
                foreach (var mesh in instance.GetComponentsInChildren<MeshFilter>())
                {
                    var collider = mesh.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = mesh.sharedMesh;
                    solids.Add(collider);
                }
            }
            return instance.transform;
        }

        private void InstallResident(VillageResidentPresentation actor, int index, string voice)
        {
            speakers[index] = new NpcSpeaker(actor, actor.Head, voice, NpcEarshotProfile.Conversation);
            bubbles.DeclareSpeaker(speakers[index]);
            var body = actor.gameObject.AddComponent<CapsuleCollider>();
            // The broad body proxy starts above the low supports. A full-width
            // capsule around the feet would overlap a stand the actual boots clear.
            body.center = Vector3.up * 1.06f;
            body.height = 1.15f;
            body.radius = 0.20f;
            var trigger = new GameObject("Greeting");
            trigger.transform.SetParent(actor.transform, false);
            var reach = trigger.AddComponent<SphereCollider>();
            reach.isTrigger = true;
            reach.radius = 1.15f;
            reach.center = Vector3.up * 0.8f;
            trigger.AddComponent<VillageResidentGreeting>().Initialize(this, actor.Role);
        }

        private void Update() => Advance(Time.deltaTime);

        public void Advance(float deltaSeconds) => Advance(deltaSeconds,
            GameSessionState.GameTimeOfDayMinutes, GameWeatherRules.EvaluateCurrentGust(),
            GameWeatherRules.EvaluateCurrentWind().HorizontalDirection);

        public void Advance(float deltaSeconds, double minutes, float gust, Vector3 windDirection)
        {
            if (Plan == null || deltaSeconds <= 0f || float.IsNaN(deltaSeconds) ||
                float.IsInfinity(deltaSeconds) || GameTimeScaleRuntime.IsPaused ||
                SceneTransitionService.IsTransitioning) return;
            // Bound the integration step, but consume elapsed simulation time rather than losing it.
            float remaining = Mathf.Min(deltaSeconds, 0.25f);
            while (remaining > 0.0001f)
            {
                float dt = Mathf.Min(remaining, 1f / 60f);
                AdvanceStep(dt, minutes, gust, windDirection);
                remaining -= dt;
            }
        }

        private void AdvanceStep(float dt, double minutes, float gust, Vector3 windDirection)
        {
            for (int i = 0; i < 6; i++)
            {
                speechRemaining[i] = Mathf.Max(0f, speechRemaining[i] - dt);
                speechCooldown[i] = Mathf.Max(0f, speechCooldown[i] - dt);
            }
            AdvanceNeighbours(dt, minutes, gust, windDirection);
        }

        private void AdvanceWoman(float dt)
        {
            if (speechRemaining[1] > 0f)
            {
                Woman.Apply(VillageResidentAction.Idle, 0f, GreetingLook(1, Woman));
                return;
            }
            IsWomanBlocked = false;
            if (AdvanceFirewoodLoading(dt)) return;
            if (route != null)
            {
                AdvanceWalk(dt);
                return;
            }
            stageTime += dt;
            switch (WomanStage)
            {
                case VillageWomanStage.Waiting:
                    Woman.Apply(VillageResidentAction.Idle, stageTime);
                    if (stageTime >= 3f) BeginNextFirewoodJob();
                    break;
                case VillageWomanStage.PickingUp:
                    Woman.Apply(VillageResidentAction.Reach, stageTime);
                    if (stageTime >= VillageResidentPresentation.PickupContactSeconds) basketAttached = true;
                    FollowBasket();
                    if (stageTime >= Woman.ClipLength(VillageResidentAction.Reach))
                        StartWalk(ActiveFirewoodRoute(), VillageWomanStage.Carrying,
                            VillageWomanStage.PuttingDown, -Plan.Forward);
                    break;
                case VillageWomanStage.PuttingDown:
                    Woman.Apply(VillageResidentAction.Place, stageTime);
                    FollowBasket();
                    if (basketAttached && stageTime >= VillageResidentPresentation.PlaceContactSeconds)
                    {
                        basketAttached = false;
                        sound.PlayWood(baskets[workBasket].position);
                    }
                    if (stageTime >= Woman.ClipLength(VillageResidentAction.Place))
                    {
                        GameSessionState.VillageHousehold.TryDeliverBasket(workBasket, workDeliveryStand);
                        RestoreBasketToSupport(workBasket);
                        Thank(VillageResidentRole.WoodWoman, "village.life.wood.work", false);
                        if (SelectNextBasket())
                            StartWalk(new[] { Woman.transform.position, Plan.Yard(-1.5f, 3.5f),
                                Plan.Yard(2.5f, 3.5f), Plan.Dock(Plan.Pickups[workBasket]) },
                                VillageWomanStage.WalkingToPickup, VillageWomanStage.Waiting, -Plan.Forward);
                        else WalkToRest();
                    }
                    break;
                case VillageWomanStage.Resting:
                    Woman.Apply(VillageResidentAction.Idle, stageTime);
                    if (stageTime >= 9f && SelectNextBasket())
                    {
                        StartWalk(new[] { Woman.transform.position, Plan.Yard(0f, 3.5f),
                            Plan.Yard(2.5f, 3.5f), Plan.Dock(Plan.Pickups[workBasket]) },
                            VillageWomanStage.WalkingToPickup, VillageWomanStage.Waiting, -Plan.Forward);
                        break;
                    }
                    if (stageTime >= 9f)
                        StartWalk(new[] { Woman.transform.position, Plan.Yard(0f, 3.5f),
                            Plan.Yard(3.3f, 3.5f), Plan.Yard(3.3f, 2f), Plan.Yard(2.1f, 2f), Plan.Work }, VillageWomanStage.WalkingToStack,
                            VillageWomanStage.Working, -Plan.Forward);
                    break;
                case VillageWomanStage.Working:
                    Woman.Apply(VillageResidentAction.StationWork, stageTime);
                    if (stageTime >= 3f && stageTime - dt < 3f)
                        sound.PlayWood(Plan.Stack + Vector3.up * 0.82f);
                    if (stageTime >= Woman.ClipLength(VillageResidentAction.StationWork)) WalkToRest();
                    break;
            }
        }

        private void WalkToRest()
        {
            Vector3[] points = WomanStage == VillageWomanStage.Working
                ? new[] { Woman.transform.position, Plan.Yard(2.1f, 2f), Plan.Yard(3.3f, 2f),
                    Plan.Yard(3.3f, 3.5f), Plan.Yard(-0.8f, 3.5f), Plan.Rest }
                : new[] { Woman.transform.position, Plan.Yard(-0.8f, 3.5f), Plan.Rest };
            StartWalk(points, VillageWomanStage.WalkingToRest, VillageWomanStage.Resting, Plan.Forward);
        }

        private void StartWalk(Vector3[] points, VillageWomanStage moving,
            VillageWomanStage end, Vector3 facing)
        {
            route = points;
            routeIndex = 1;
            routeEnd = end;
            destinationFacing = Quaternion.LookRotation(facing);
            walkSpeed = 0f;
            walkTime = 0f;
            SetStage(moving);
        }

        private void AdvanceWalk(float dt)
        {
            Transform actor = Woman.transform;
            Vector3 delta = route[routeIndex] - actor.position;
            delta.y = 0f;
            float distance = delta.magnitude;
            Vector3 direction = distance > 0.001f ? delta / distance : actor.forward;
            bool last = routeIndex == route.Length - 1;
            Quaternion facing = distance < 0.025f && last ? destinationFacing : Quaternion.LookRotation(direction);
            actor.rotation = Quaternion.RotateTowards(actor.rotation, facing, 105f * dt);
            float angle = Quaternion.Angle(actor.rotation, facing);
            Vector3 proposed = actor.position + direction * Mathf.Min(distance, 0.82f * dt);
            IsWomanBlocked = ResidentBlocks(neighbours[1], proposed) || !walkable.Contains(proposed, 0.27f);
            float desiredSpeed = !IsWomanBlocked && angle < 20f && distance > 0.025f
                ? Mathf.Min(basketAttached ? 0.68f : 0.82f, distance * 2.2f) : 0f;
            walkSpeed = Mathf.MoveTowards(walkSpeed, desiredSpeed, dt * 2f);
            float step = IsWomanBlocked ? 0f : Mathf.Min(distance, walkSpeed * dt);
            if (step > 0f) actor.position = Plan.Ground(actor.position + direction * step);
            walkTime += dt * (walkSpeed / 0.82f);
            Woman.ApplyLocomotion(walkTime, walkSpeed, basketAttached || heldLog >= 0);
            FollowLoadingLog();
            FollowBasket();
            if (distance < 0.025f && (!last || angle < 1f))
            {
                if (!last) routeIndex++;
                else
                {
                    route = null;
                    SetStage(routeEnd);
                }
            }
        }

        private bool HeroBlocks(Vector3 current, Vector3 next)
        {
            if (hero == null) return false;
            Vector2 heroXZ = new Vector2(hero.position.x, hero.position.z);
            Vector2 nextXZ = new Vector2(next.x, next.z);
            Vector2 currentXZ = new Vector2(current.x, current.z);
            // A resident may walk away from somebody standing behind her.
            return Mathf.Abs(hero.position.y - current.y) < 1.8f &&
                Vector2.Distance(heroXZ, nextXZ) < 0.84f &&
                Vector2.Distance(heroXZ, nextXZ) <= Vector2.Distance(heroXZ, currentXZ) + 0.001f;
        }

        private void FollowBasket()
        {
            if (!basketAttached) return;
            Vector3 across = Woman.RightGrip.position - Woman.LeftGrip.position;
            Vector3 forward = Vector3.Cross(across.normalized, Vector3.up).normalized;
            Quaternion rotation = Quaternion.LookRotation(forward);
            Vector3 center = (Woman.LeftGrip.position + Woman.RightGrip.position) * 0.5f;
            baskets[workBasket].SetPositionAndRotation(center - rotation * Vector3.up * 0.43f, rotation);
        }

        private void AdvanceStation(float dt)
        {
            if (AdvanceStationHelp(dt)) return;
            if (speechRemaining[0] > 0f)
            {
                StationWorker.Apply(VillageResidentAction.Idle, 0f, GreetingLook(0, StationWorker));
                return;
            }
            stationTime += dt;
            float workLength = StationWorker.ClipLength(VillageResidentAction.StationWork);
            float phase = Mathf.Repeat(stationTime, workLength + 7f);
            bool working = phase >= 7f;
            float actionTime = working ? phase - 7f : phase;
            StationWorker.Apply(working ? VillageResidentAction.StationWork : VillageResidentAction.Idle, actionTime);
            // A short lid check ends closed; the same finite work clip releases the hands.
            float lift = working && actionTime >= 1.5f && actionTime <= 4.5f
                ? Mathf.Sin(Mathf.PI * (actionTime - 1.5f) / 3f) : 0f;
            lid.localRotation = Quaternion.Euler(-8f * lift * lift, 0f, 0f);
            int beat = Mathf.FloorToInt(stationTime / (workLength + 7f));
            if (working && actionTime >= 4.5f && stationBeat != beat)
            {
                stationBeat = beat;
                sound.PlayWood(Plan.StationCrate + Vector3.up * 0.8f);
            }
        }

        private void SetStage(VillageWomanStage value)
        {
            WomanStage = value;
            stageTime = 0f;
        }

        private Vector3 GreetingLook(int index, VillageResidentPresentation actor)
        {
            float elapsed = NpcSpeechBubbleView.VisibleSeconds - speechRemaining[index];
            float weight = Mathf.SmoothStep(0f, 1f,
                Mathf.Min(elapsed / 0.6f, speechRemaining[index] / 0.6f));
            return Vector3.Lerp(actor.Head.position + actor.transform.forward * 2f,
                hero.position + Vector3.up * 1.55f, weight);
        }

        public bool CanTalk(VillageResidentRole role)
        {
            int index = (int)role;
            if (speechCooldown[index] > 0f || SceneTransitionService.IsTransitioning) return false;
            if (workroom != null && workroom.CanTalk(role)) return true;
            if (neighbours.Count > index && (!neighbours[index].IsOutside ||
                neighbours[index].IsReactingToGust || neighbours[index].Steps.Count > 0))
                return neighbours[index].IsOutside && !neighbours[index].IsReactingToGust &&
                    neighbours[index].Task == VillageNeighbourTask.Wait && speechCooldown[index] <= 0f;
            if (index >= 2) return false;
            if (index == 0)
                return Mathf.Repeat(stationTime, StationWorker.ClipLength(VillageResidentAction.StationWork) + 7f) < 7f;
            return route == null && (WomanStage == VillageWomanStage.Waiting || WomanStage == VillageWomanStage.Resting);
        }

        public bool TryGreet(VillageResidentRole role)
        {
            if (!CanTalk(role) || hero == null) return false;
            int index = (int)role;
            VillageResidentPresentation actor = neighbours[index].Actor;
            bool indoors = workroom != null && workroom.CanTalk(role);
            if (Vector3.Distance(hero.position, actor.transform.position) > 5.2f ||
                (indoors ? !workroom.CanSeeResident(actor) : !AlpineVillagePathValidator.SegmentClearsAllFootprints(Plan.Village,
                    hero.position, actor.transform.position, 0.01f)) ||
                GameSessionState.IsRidingAVehicle) return false;
            string key = "village.life." + new[] { "station", "wood", "repair", "sewing", "snow", "visitor" }[index] + ".";
            key += greeted[index] ? "work" : "recognition";
            if (indoors && greeted[index]) key = workroom.WorkLine(role);
            if (!bubbles.Show(actor, LocalizationService.Get(key))) return false;
            greeted[index] = true;
            speechRemaining[index] = NpcSpeechBubbleView.VisibleSeconds;
            speechCooldown[index] = 16f;
            LastGreetingKey = key;
            return true;
        }

        private void TryAutomaticGreeting(VillageResidentRole role, VillageResidentPresentation actor)
        {
            int index = (int)role;
            if (!greeted[index] && hero != null &&
                Vector3.Distance(hero.position, actor.transform.position) < 4.8f) TryGreet(role);
        }

        public Vector3 ResidentPosition(VillageResidentRole role) => neighbours[(int)role].Actor.transform.position;
    }
}

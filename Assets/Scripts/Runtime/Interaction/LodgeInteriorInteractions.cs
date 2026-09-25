using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Authored furniture hooks; shared seating and narrative owners run every action.</summary>
    [DisallowMultipleComponent]
    public sealed class LodgeInteriorInteractions : MonoBehaviour
    {
        public const string ChairId = "lodge-stove-chair";
        public const string PhotographId = "lodge-photograph";
        public const string SkiEquipmentId = "lodge-ski-equipment";
        public const string GroupPhotographId = "lodge-group-photograph";
        public const float FloorY = .02f;
        public const float ChairSeatWidth = .46f;
        public const float ChairSeatDepth = .44f;

        public CityBenchSitInteraction Chair { get; private set; }
        public IReadOnlyList<CityBenchSitInteraction> LoungeChairs { get; private set; }
        public NarrativeInteraction Photograph { get; private set; }
        public NarrativeInteraction GroupPhotograph { get; private set; }
        public Transform GroupPhotographModel { get; private set; }
        public NarrativeInteraction SkiEquipment { get; private set; }
        private WorldItemFoundScreen photographScreen;
        private PlayerInteractor photographInteractor;

        public void Initialize(AlpineVillageRoot village)
        {
            if (village == null) throw new ArgumentNullException(nameof(village));
            CityBenchSitPlan seat = CreateChairPlan(transform);
            if (!village.World.WalkableArea.Contains(seat.EntryRootPosition, .34f))
                throw new InvalidOperationException("The lodge chair's entry dock is blocked.");
            Chair = CityBenchSitWorldBuilder.Build(transform, new[] { seat }, village.Player,
                village.CameraFollow.Camera)[0];
            var loungeSeats = new[] { CreateLoungeChairPlan(transform, 0), CreateLoungeChairPlan(transform, 1) };
            foreach (CityBenchSitPlan loungeSeat in loungeSeats)
                if (!village.World.WalkableArea.Contains(loungeSeat.EntryRootPosition, .34f))
                    throw new InvalidOperationException("The lodge lounge chair's entry dock is blocked: " + loungeSeat.Id);
            LoungeChairs = CityBenchSitWorldBuilder.Build(transform, loungeSeats, village.Player, village.CameraFollow.Camera);

            Transform photograph = Require("LodgePhotographImage");
            Photograph = BuildInspection(PhotographId, "interaction.lodge_photograph", "lodge.photograph.inspect",
                photograph, Require("LodgePhotoDock"), AlpineVillageNarrativeBuilder.RendererBounds(photograph),
                transform.forward, NarrativeCameraMode.DocumentCloseUp, -transform.right);

            Transform equipment = Require("LodgeSkiEquipment");
            SkiEquipment = BuildInspection(SkiEquipmentId, "interaction.lodge_ski_equipment", "lodge.ski_equipment.inspect",
                equipment, Require("LodgeSkiDock"), AlpineVillageNarrativeBuilder.RendererBounds(equipment),
                transform.right, NarrativeCameraMode.ObjectSide, -transform.forward);

            GroupPhotographModel = Require("LodgeGroupPhotograph");
            Transform groupImage = Require("LodgeGroupPhotographImage");
            GroupPhotograph = BuildInspection(GroupPhotographId, "interaction.lodge_group_photograph", "lodge.group_photograph.inspect",
                GroupPhotographModel, Require("LodgeGroupPhotoDock"), AlpineVillageNarrativeBuilder.RendererBounds(groupImage),
                transform.right, NarrativeCameraMode.DocumentCloseUp,
                (Require("LodgeGroupPhotoCameraFront").position - GroupPhotographModel.position).normalized);
            GroupPhotograph.CompletionActionKey = "narrative.controls.next";
            GroupPhotograph.Completed += BeginGroupPhotographPickup;
            RestoreGroupPhotographState();
        }

        public static CityBenchSitPlan CreateChairPlan(Transform lodge)
        {
            if (lodge == null) throw new ArgumentNullException(nameof(lodge));
            Vector3 seat = LodgeStovePlan.Require(lodge, "LodgeChairSeat").position;
            Vector3 facing = LodgeStovePlan.Require(lodge, "LodgeChairFacing").position - seat;
            var plan = new CityBenchSitPlan(new CityBenchSeat(ChairId, seat, ChairSeatWidth, ChairSeatDepth,
                lodge.TransformPoint(new Vector3(0f, FloorY, 0f)).y, facing,
                sitPromptKey: "interaction.lodge_chair"));
            if (!plan.IsPresent) throw new InvalidOperationException("Invalid authored lodge chair seat.");
            return plan;
        }

        public static CityBenchSitPlan CreateLoungeChairPlan(Transform lodge, int index)
        {
            if (lodge == null) throw new ArgumentNullException(nameof(lodge));
            if (index < 0 || index > 1) throw new ArgumentOutOfRangeException(nameof(index));
            string prefix = "LodgeLoungeChair" + (index + 1);
            Vector3 seat = LodgeStovePlan.Require(lodge, prefix + "Seat").position;
            Vector3 facing = LodgeStovePlan.Require(lodge, prefix + "Facing").position - seat;
            var plan = new CityBenchSitPlan(new CityBenchSeat("lodge-lounge-chair-" + (index + 1),
                seat, .54f, .52f, lodge.TransformPoint(new Vector3(0f, FloorY, 0f)).y, facing,
                sitPromptKey: "interaction.lodge_lounge_chair", frontApproachOnly: true));
            if (!plan.IsPresent) throw new InvalidOperationException("Invalid authored lounge chair seat.");
            return plan;
        }

        public void RestoreGroupPhotographState()
        {
            bool available = !GameSessionState.IsWorldItemCollected(GroupPhotographId);
            if (GroupPhotographModel != null) GroupPhotographModel.gameObject.SetActive(available);
            if (GroupPhotograph != null) GroupPhotograph.gameObject.SetActive(available);
        }

        private void BeginGroupPhotographPickup(PlayerInteractor interactor)
        {
            if (!isActiveAndEnabled || interactor == null || GroupPhotographModel == null ||
                GameSessionState.IsWorldItemCollected(GroupPhotographId) || SceneTransitionService.IsTransitioning)
                return;
            photographScreen = WorldItemFoundScreen.For(interactor);
            photographInteractor = interactor;
            if (photographScreen == null || !photographScreen.TryPresent(interactor,
                    InventoryItemId.LodgeGroupPhotograph, GroupPhotographModel,
                    () => isActiveAndEnabled && !SceneTransitionService.IsTransitioning &&
                        GameSessionState.TryCollectWorldItem(GroupPhotographId, InventoryItemId.LodgeGroupPhotograph),
                    FinishGroupPhotographPickup))
            {
                photographScreen = null;
                photographInteractor = null;
            }
        }

        private void FinishGroupPhotographPickup(bool taken)
        {
            photographScreen = null;
            PlayerInteractor interactor = photographInteractor;
            photographInteractor = null;
            if (this == null) return;
            RestoreGroupPhotographState();
            if (taken && interactor != null) interactor.ShowFeedback("lodge.group_photograph.taken", 2f);
        }

        private void OnDisable()
        {
            if (photographScreen != null) photographScreen.Abandon();
            photographScreen = null;
            photographInteractor = null;
        }

        private NarrativeInteraction BuildInspection(string id, string prompt, string text,
            Transform subject, Transform dock, Bounds focus, Vector3 cameraSide,
            NarrativeCameraMode cameraMode, Vector3 cameraFront)
        {
            Vector3 groundedFloor = dock.position;
            if (Mathf.Abs(transform.InverseTransformPoint(groundedFloor).y - FloorY) > .001f)
                throw new InvalidOperationException("The lodge inspection dock is not on its floor: " + id);
            Vector3 facing = focus.center - groundedFloor;
            facing.y = 0f;
            var definition = new NarrativeInteractionDefinition(id, prompt, new[] { new NarrativePage(text) });
            var staging = NarrativeStagingPlan.Standing(groundedFloor, Quaternion.LookRotation(facing), focus,
                cameraSide, cameraMode, cameraFront);
            var host = new GameObject(id);
            host.transform.SetParent(transform, false);
            var interaction = host.AddComponent<NarrativeInteraction>();
            interaction.Configure(definition, subject, staging, groundedFloor + Vector3.up * .85f);
            return interaction;
        }

        private Transform Require(string name) => LodgeStovePlan.Require(transform, name);
    }
}

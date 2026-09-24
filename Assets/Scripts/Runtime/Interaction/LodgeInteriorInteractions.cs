using System;
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
        public const float FloorY = .02f;
        public const float ChairSeatWidth = .46f;
        public const float ChairSeatDepth = .44f;

        public CityBenchSitInteraction Chair { get; private set; }
        public NarrativeInteraction Photograph { get; private set; }
        public NarrativeInteraction SkiEquipment { get; private set; }

        public void Initialize(AlpineVillageRoot village)
        {
            if (village == null) throw new ArgumentNullException(nameof(village));
            CityBenchSitPlan seat = CreateChairPlan(transform);
            if (!village.World.WalkableArea.Contains(seat.EntryRootPosition, .34f))
                throw new InvalidOperationException("The lodge chair's entry dock is blocked.");
            Chair = CityBenchSitWorldBuilder.Build(transform, new[] { seat }, village.Player,
                village.CameraFollow.Camera)[0];

            Transform photograph = Require("LodgePhotographImage");
            Photograph = BuildInspection(PhotographId, "interaction.lodge_photograph", "lodge.photograph.inspect",
                photograph, Require("LodgePhotoDock"), AlpineVillageNarrativeBuilder.RendererBounds(photograph),
                transform.forward, NarrativeCameraMode.DocumentCloseUp, -transform.right);

            Transform equipment = Require("LodgeSkiEquipment");
            SkiEquipment = BuildInspection(SkiEquipmentId, "interaction.lodge_ski_equipment", "lodge.ski_equipment.inspect",
                equipment, Require("LodgeSkiDock"), AlpineVillageNarrativeBuilder.RendererBounds(equipment),
                transform.right, NarrativeCameraMode.ObjectSide, -transform.forward);
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

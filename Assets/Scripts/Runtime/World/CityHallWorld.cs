using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    [DisallowMultipleComponent]
    public sealed class CityHallWorld : MonoBehaviour
    {
        internal const string InteriorResource = "City/Hall/CityHallVestibule";
        private Transform hour, minute;
        private Quaternion hourRest, minuteRest;
        private readonly VillageResidentPresentation[] guards = new VillageResidentPresentation[2];
        private Camera observer;
        private float nextPose;
        public int DisplayedMinute { get; private set; } = -1;
        public CityHallDoor Entrance { get; private set; }

        internal static void Build(Transform parent, CityHallPlan plan, List<Collider> shelters)
        {
            if (plan == null) return;
            Transform host = new GameObject("City Hall").transform;
            host.SetParent(parent, false);
            host.position = plan.Threshold;
            var hall = host.gameObject.AddComponent<CityHallWorld>();
            Transform model = PlaceModel("City/Hall/CityHallExterior", host);
            hall.hour = CityPedestrianHandProps.FindSocket(model, "HourHand");
            hall.minute = CityPedestrianHandProps.FindSocket(model, "MinuteHand");
            hall.hourRest = hall.hour.rotation;
            hall.minuteRest = hall.minute.rotation;
            hall.RefreshClock();
            foreach (var solid in plan.GroundSolids)
                Box(host, "Structure", solid.Center - host.position, solid.Size);
            Box(host, "Office floors", new Vector3(-6.92f, 12.07f, -6.40f), new Vector3(17.70f,16.5f,12.32f));
            Box(host, "Clock wing", new Vector3(-11.82f,11.75f,1.9f), new Vector3(7.88f,15.85f,8.62f));
            shelters.Add(Box(host, "Clock wing rain shelter", new Vector3(-11.82f,-.85f,2.98f),
                new Vector3(7.85f,9.3f,6.42f), true));
            Box(host, "Canopy", new Vector3(0,3.05f,.39f), new Vector3(3.46f,.37f,2.65f));
            shelters.Add(Box(host, "Canopy rain shelter", new Vector3(0,1.47f,.39f), new Vector3(3.44f,2.88f,2.63f), true));
            shelters.Add(Box(host, "Office soffit rain shelter", new Vector3(-6.92f,-.85f,-6.4f),
                new Vector3(17.65f,9.3f,12.28f), true));
            Fixture("CityHallEntrance", new Vector3(-.32f,2.70f,1.26f), new Vector3(-.24f,1.14f,-.11f));
            Fixture("CityHallGate", new Vector3(1.24f,2.70f,4.11f), new Vector3(-.58f,1.04f,1.22f));
            Fixture("CityHallDial", new Vector3(-16.94f,14.87f,2.33f), new Vector3(-16.21f,11.68f,3.39f));
            BuildPlaque(host);
            hall.Entrance = CityHallDoor.Build(host, plan.DoorAction, true);
            GameObject prop = Resources.Load<GameObject>(EastGuardAssetProvider.RifleResourcePath);
            for (int i = 0; i < hall.guards.Length; i++)
            {
                var actor = DefaultNpcFactory.CreateForCharacter(host, i == 0
                    ? DefaultNpcPopulation.CityHallGuardLeft : DefaultNpcPopulation.CityHallGuardRight);
                hall.guards[i] = actor;
                actor.transform.SetPositionAndRotation(plan.GuardStation(i), Quaternion.Euler(0,i == 0 ? 16f : -24f,0));
                CityPortCrew.AlignWorkerModelWithPlacement(actor);
                NpcFootstepSources.Unregister(actor.transform);
                var body = actor.gameObject.AddComponent<CapsuleCollider>();
                body.height = 1.68f; body.radius = .26f; body.center = Vector3.up * .86f;
                NpcNameplateTarget.Attach(actor.gameObject, "npc.name.east_guard", actor.Head, "city_hall_guard");
                var carry = new GameObject("Shoulder sling").transform;
                carry.SetParent(actor.transform, false);
                carry.localPosition = new Vector3(.20f,1.11f,.15f);
                carry.localRotation = Quaternion.Euler(-10,0,13);
                Instantiate(prop, carry, false);
                PlaceModel("City/Hall/CityHallSling", carry);
                carry.SetParent(CityPedestrianHandProps.FindSocket(actor.ModelRoot, "chest"), true);
            }

            void Fixture(string purpose, Vector3 source, Vector3 target)
            {
                var anchor = new GameObject(CityNightAtmosphere.IndustrialTaskLightPrefix + purpose).transform;
                anchor.SetParent(host, false);
                anchor.SetLocalPositionAndRotation(source, Quaternion.LookRotation(target - source));
            }
        }

        internal static Transform PlaceModel(string resource, Transform parent)
        {
            var model = Instantiate(Resources.Load<GameObject>(resource), parent, false);
            foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>())
            {
                int separator = renderer.name.LastIndexOf("__", StringComparison.Ordinal);
                string role = renderer.name.Substring(separator + 2);
                bool dial = renderer.name == "CityHallExteriorPlaster__Plaster";
                if (!renderer.name.EndsWith("LampGlass__SteelLight", StringComparison.Ordinal) && !dial)
                {
                    renderer.sharedMaterial = CityPortAssetProvider.GetSurfaceMaterial(role);
                    continue;
                }
                renderer.sharedMaterial = CityNightResources.EmissiveMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                if (resource == InteriorResource)
                    continue; // The public-room tubes have constant power.
                CityNightGlowRegistry.Register(renderer, dial
                    ? new Color(.72f,.74f,.66f) : new Color(1.05f,1.12f,1.02f));
            }
            return model.transform;
        }

        internal static BoxCollider Box(Transform parent, string name, Vector3 center, Vector3 size, bool trigger = false)
        {
            var host = new GameObject(name);
            host.transform.SetParent(parent, false);
            host.transform.localPosition = center;
            var box = host.AddComponent<BoxCollider>();
            box.size = size;
            box.isTrigger = trigger;
            return box;
        }

        private static void BuildPlaque(Transform parent)
        {
            var host = new GameObject("Enamel door plate");
            host.transform.SetParent(parent, false);
            host.transform.localPosition = new Vector3(-1.16f,1.58f,-.125f);
            host.transform.localRotation = Quaternion.Euler(0,180,0);
            var label = host.AddComponent<TextMeshPro>();
            label.font = CemeteryPlaqueFont.Get();
            label.text = "ГОРОДСКОЕ\nУПРАВЛЕНИЕ";
            label.fontSize = .57f;
            label.color = new Color(.84f,.84f,.76f);
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.rectTransform.sizeDelta = new Vector2(.61f,.26f);
        }

        private void OnEnable()
        {
            if (hour != null) RefreshClock();
        }

        private void LateUpdate()
        {
            RefreshClock();
            if (GameTimeScaleRuntime.IsPaused || Time.time < nextPose) return;
            if (observer == null || !observer.isActiveAndEnabled) observer = Camera.main;
            bool near = observer != null && (observer.transform.position - transform.position).sqrMagnitude < 42f * 42f;
            nextPose = Time.time + (near ? .10f : .65f);
            if (!near) return;
            float idleTime = Time.time;
            for (int i = 0; i < guards.Length; i++) guards[i].ApplyIdleVariation(idleTime + i * 3.7f, true, 0);
        }

        internal void RefreshClock()
        {
            int time = GameSessionState.GameMinuteOfDay;
            if (time == DisplayedMinute) return;
            DisplayedMinute = time;
            hour.rotation = Quaternion.AngleAxis((time % 720) * .5f, Vector3.left) * hourRest;
            minute.rotation = Quaternion.AngleAxis((time % 60) * 6f, Vector3.left) * minuteRest;
        }
    }
}

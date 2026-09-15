using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The imported fair and its physical owners, retained with the City root.</summary>
    public sealed class CityFairWorld : MonoBehaviour
    {
        private static readonly string[] Goods = { "Bread", "Fruit", "Pottery", "WoodenToys" };
        private readonly List<Transform> lampAnchors = new List<Transform>();
        private readonly List<Collider> rainShelters = new List<Collider>();
        private readonly List<GameObject> stalls = new List<GameObject>();
        private readonly List<GameObject> goods = new List<GameObject>();
        private GameObject organ, bell;
        public CityFairPlan Plan { get; private set; }
        public IReadOnlyList<Transform> LampAnchors => lampAnchors;
        public IReadOnlyList<Collider> RainShelters => rainShelters;
        public IReadOnlyList<GameObject> Stalls => stalls;
        public IReadOnlyList<GameObject> GoodsDisplays => goods;
        public CityFairInteraction Organ { get; private set; }
        public CityFairInteraction Bell { get; private set; }
        public CityFairVendors Vendors { get; private set; }
        public CityFairChildren Children { get; private set; }

        internal void Build(CityFairPlan plan)
        {
            Plan = plan;
            foreach (CityFairStall stall in plan.Stalls)
            {
                GameObject shell = Place("Stall", stall.Position, stall.Rotation, true);
                shell.name = stall.Id;
                stalls.Add(shell);
                goods.Add(Place(Goods[stall.GoodsIndex], stall.Position, stall.Rotation, false));
                var shelter = new GameObject(stall.Id + " canopy rain exclusion");
                shelter.layer = 2;
                shelter.transform.SetParent(shell.transform, false);
                shelter.transform.localPosition = new Vector3(0f, 2.55f, 0f);
                var volume = shelter.AddComponent<BoxCollider>();
                volume.isTrigger = true;
                volume.size = new Vector3(2.95f, .30f, 2.35f);
                rainShelters.Add(volume);
            }
            organ = Place("Organ", plan.OrganPosition, plan.OrganRotation, false);
            Box(organ.transform, new Vector3(0f, .65f, -.04f), new Vector3(1.05f, 1.30f, .72f));
            bell = Place("Bell", plan.BellPosition, plan.BellRotation, false);
            // Only the stand blocks movement. The front rope and hand dock remain clear.
            Box(bell.transform, new Vector3(0f, 1f, -.18f), new Vector3(.72f, 2f, .16f));
            for (int index = 0; index < plan.Benches.Length; index++)
                Place("Bench", plan.BenchPositions[index], plan.BenchRotations[index], true);
            for (int index = 0; index < plan.ClutterPositions.Length; index++)
                Place("Clutter", plan.ClutterPositions[index], plan.ClutterRotations[index], true);
            BuildGarlands();
            Vendors = CityFairVendors.Build(transform, plan);
            Children = CityFairChildren.Build(transform, plan);
        }

        public void InstallInteractions(PlayerRuntime player, Camera camera)
        {
            if (Organ != null) return;
            var controller = player.GameObject.GetComponent<PlayerAnimatedInteractionController>();
            if (controller == null) controller = player.GameObject.AddComponent<PlayerAnimatedInteractionController>();
            if (!controller.IsInitialized) controller.Initialize(player, camera);
            Organ = Attach(organ, CityFairInteractionKind.Organ, new Vector3(.54f, 0f, 1.08f),
                "CrankPivot", "CrankGripAnchor", null, player, controller);
            Bell = Attach(bell, CityFairInteractionKind.Bell, new Vector3(.47f, 0f, .76f),
                "RopePivot", "RopeGripAnchor", "BellSwingPivot", player, controller);
        }

        private CityFairInteraction Attach(GameObject item, CityFairInteractionKind kind, Vector3 localDock,
            string mechanismName, string gripName, string bellName, PlayerRuntime player,
            PlayerAnimatedInteractionController controller)
        {
            Vector3 dock = item.transform.TransformPoint(localDock);
            dock.y = Plan.SampleGroundY(dock) + PlayerFactory.GroundedRootOffset;
            Transform grip = CityFairAssetProvider.FindPart(item, gripName);
            Vector3 facing = Vector3.ProjectOnPlane(-item.transform.forward, Vector3.up).normalized;
            var actionPlan = PlayerDoorActionPlan.CreateStationary(grip.position, dock, facing);
            var action = CityFairInteraction.Attach(item.transform, kind, player, controller, actionPlan,
                CityFairAssetProvider.FindPart(item, mechanismName), grip,
                bellName == null ? null : CityFairAssetProvider.FindPart(item, bellName));
            var offer = new GameObject(kind + " interaction reach");
            offer.transform.SetParent(item.transform, false);
            offer.transform.localPosition = new Vector3(.2f, 1.1f, .65f);
            var volume = offer.AddComponent<BoxCollider>();
            volume.isTrigger = true;
            volume.size = new Vector3(1.5f, 1.7f, 1.6f);
            return action;
        }

        private void BuildGarlands()
        {
            for (int spanIndex = 0; spanIndex < Plan.Garlands.Length; spanIndex++)
            {
                CityFairGarland span = Plan.Garlands[spanIndex];
                GameObject garland = CityFairAssetProvider.CreateGarland(transform, span.Start, span.End);
                Transform[] parts = garland.GetComponentsInChildren<Transform>(true);
                foreach (Transform part in parts)
                {
                    if (!part.name.StartsWith("Fixture_", StringComparison.Ordinal) ||
                        part.GetComponent<MeshFilter>() != null) continue;
                    // A semantic FBX anchor may still inherit its unit factor.
                    // Keep the particle size and offset on the metre wrapper.
                    Vector3 bulbPosition = part.position - garland.transform.up * .183f;
                    CityLightHalo.CreateAlwaysBurning(transform,
                        transform.InverseTransformPoint(bulbPosition), .10f, .30f,
                        new Color(3.2f, 1.6f, .55f, .12f), new Color(2f, 1f, .4f, .035f));
                    // The outer spans light their two counters from real bulbs
                    // in front of the awnings; the middle span lights the square.
                    bool pooled = spanIndex == 1 ? part.name == "Fixture_06" :
                        part.name == "Fixture_03" || part.name == "Fixture_09";
                    if (!pooled) continue;
                    var lightAnchor = new GameObject("Fair garland pooled light").transform;
                    lightAnchor.SetParent(transform, false);
                    lightAnchor.position = bulbPosition;
                    lightAnchor.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                    lampAnchors.Add(lightAnchor);
                }
            }
        }

        private GameObject Place(string kind, Vector3 position, Quaternion rotation, bool collision)
        {
            GameObject instance = CityFairAssetProvider.Instantiate(kind, transform, position, rotation);
            if (collision) AddMeshColliders(instance);
            return instance;
        }

        private static void AddMeshColliders(GameObject instance)
        {
            foreach (MeshFilter filter in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
            }
        }

        private static void Box(Transform parent, Vector3 center, Vector3 size)
        {
            BoxCollider collider = parent.gameObject.AddComponent<BoxCollider>();
            collider.center = center;
            collider.size = size;
        }
    }
}

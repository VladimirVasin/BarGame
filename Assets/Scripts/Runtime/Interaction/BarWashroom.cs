using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>Hosts the shared toilet actions in the bar's real, continuously walked room.</summary>
    [DisallowMultipleComponent]
    public sealed class BarWashroom : MonoBehaviour
    {
        public const string ResidueScope = "bar-washroom";
        public BathroomInteractionHost Host { get; private set; }
        public HomeToiletInteraction Toilet { get; private set; }
        public HomeToiletPlungeInteraction Plunge { get; private set; }
        public HomeToiletChoiceInteraction Choice { get; private set; }
        public InteriorHingedDoorInteraction Door { get; private set; }
        public Transform FixtureFrame => Host?.transform;

        public static BarWashroom Create(Transform parent, Transform room, PlayerRuntime player,
            PlayerCameraFollow follow, InteractionPromptView prompt, IntoxicationHudView hud,
            PlayerAnimatedInteractionController controller)
        {
            if (parent == null || room == null || player.GameObject == null || follow == null || controller == null)
                throw new ArgumentException("The bar washroom requires the composed room and live hero services.");
            var carrier = new GameObject("Bar Washroom Interactions");
            carrier.transform.SetParent(parent, false);
            var washroom = carrier.AddComponent<BarWashroom>();
            washroom.Build(room, player, follow, prompt, hud, controller);
            return washroom;
        }

        private void Build(Transform room, PlayerRuntime player, PlayerCameraFollow follow,
            InteractionPromptView prompt, IntoxicationHudView hud, PlayerAnimatedInteractionController controller)
        {
            // Map the existing metre-authored fixture and clips into the north annex:
            // Home water (4.05,.4373,1.4) becomes bar water (-3.85,.4373,6.35).
            var frame = new GameObject("Bar Washroom Toilet Frame").transform;
            frame.SetParent(transform, false);
            frame.localPosition = new Vector3(-2.45f, 0f, 2.30f);
            frame.localRotation = Quaternion.Euler(0f, -90f, 0f);
            Transform pedestal = CreateFixture(frame, "ToiletPedestal", new Vector3(4.15f, .24f, 1.40f),
                new Color(.50f, .48f, .39f));
            var body = pedestal.gameObject.AddComponent<BoxCollider>();
            body.center = pedestal.GetComponent<MeshFilter>().sharedMesh.bounds.center;
            body.size = pedestal.GetComponent<MeshFilter>().sharedMesh.bounds.size;
            Transform bowl = CreateFixture(frame, "ToiletBowl", new Vector3(4.05f, .49f, 1.40f),
                new Color(.66f, .64f, .53f));
            Transform seat = CreateFixture(frame, "ToiletSeat", new Vector3(4.05f, .62f, 1.40f),
                new Color(.33f, .31f, .24f));
            HomeToiletLid lid = HomeToiletLid.Create(frame);
            Transform water = HomeToiletLid.CreateWater(frame).transform;
            Transform flush = Require(room, "Bar Washroom Toilet Flush");
            var anchors = new ToiletFixtureAnchors(frame, lid, bowl, water, seat, flush);
            Host = new BathroomInteractionHost(frame, room, player, follow, prompt, hud,
                RetroAudioService.EnsureInstalled(), anchors, ResidueScope + ":" + room.name, transform.parent,
                candidate => !IsWashroomReceiver(candidate, room, player.GameObject.transform));
            Toilet = Child("Bar Washroom Small Toilet").AddComponent<HomeToiletInteraction>();
            Toilet.Initialize(Host);
            Plunge = Child("Bar Washroom Seated Toilet").AddComponent<HomeToiletPlungeInteraction>();
            Plunge.Initialize(Host);
            GameObject choice = Child("Bar Washroom Toilet Choice");
            choice.transform.position = anchors.EntryPosition;
            var trigger = choice.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = .30f;
            trigger.center = Vector3.up * .8f;
            Choice = choice.AddComponent<HomeToiletChoiceInteraction>();
            Choice.Initialize(Host, promptRadius: .65f, minimumFacingDot: .5f);

            Transform pivot = Require(room, "Bar Washroom Door");
            BoxCollider leaf = pivot.GetComponent<BoxCollider>();
            if (leaf == null) throw new InvalidOperationException("The washroom requires its solid authored door leaf.");
            Transform outsideHandle = Contact(pivot, "Bar Washroom Outside Handle", new Vector3(.91f, 1.01f, -.075f));
            Transform insideHandle = Contact(pivot, "Bar Washroom Inside Handle", new Vector3(.91f, 1.01f, .075f));
            Door = pivot.gameObject.AddComponent<InteriorHingedDoorInteraction>();
            Door.Initialize(player, controller, pivot, leaf, outsideHandle, insideHandle,
                transform.TransformPoint(new Vector3(-4.20f, PlayerFactory.GroundedRootOffset, 3.25f)),
                transform.TransformPoint(new Vector3(-3.35f, PlayerFactory.GroundedRootOffset, 4.55f)), -90f);
        }

        private GameObject Child(string label)
        {
            var child = new GameObject(label);
            child.transform.SetParent(transform, false);
            return child;
        }

        private static Transform CreateFixture(Transform frame, string resource, Vector3 local, Color tint)
        {
            var part = new GameObject("Bar Washroom " + resource);
            part.transform.SetParent(frame, false);
            part.transform.localPosition = local;
            part.AddComponent<MeshFilter>().sharedMesh = HomeUrineResources.Mesh(resource);
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = RuntimePrimitiveFactory.DefaultMaterial;
            RuntimePrimitiveFactory.SetColor(renderer, tint);
            return part.transform;
        }

        private static Transform Contact(Transform pivot, string label, Vector3 position)
        {
            var grip = new GameObject(label).transform;
            grip.SetParent(pivot, false);
            grip.localPosition = position;
            return grip;
        }

        private static Transform Require(Transform root, string label)
        {
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
                if (candidate.name == label) return candidate;
            throw new InvalidOperationException("Missing authored washroom part: " + label);
        }

        private static bool IsWashroomReceiver(Transform candidate, Transform room, Transform hero)
        {
            if (candidate == hero || candidate.IsChildOf(hero)) return false;
            for (Transform cursor = candidate; cursor != null; cursor = cursor.parent)
            {
                if (cursor.name == "Bar Washroom Bowl Effects") return false;
                if (cursor.name.StartsWith("Bar Washroom ", StringComparison.Ordinal)) return true;
                if (cursor == room) break;
            }
            return false;
        }
    }
}

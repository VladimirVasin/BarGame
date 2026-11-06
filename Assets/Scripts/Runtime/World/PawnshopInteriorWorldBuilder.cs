using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    public sealed class PawnshopInteriorWorldResult
    {
        public Transform Root { get; }
        public Transform Model { get; }
        public IReadOnlyList<BoxCollider> SolidColliders { get; }
        private readonly Dictionary<string, Transform> anchors;

        internal PawnshopInteriorWorldResult(Transform root, Transform model,
            List<BoxCollider> colliders, Dictionary<string, Transform> modelAnchors)
        {
            Root = root; Model = model; SolidColliders = colliders.AsReadOnly(); anchors = modelAnchors;
        }

        public Transform GetAnchor(string name) => anchors.TryGetValue("ANCHOR_" + name, out var anchor)
            ? anchor : throw new InvalidOperationException("Missing pawnshop anchor: " + name);
    }

    public static class PawnshopInteriorWorldBuilder
    {
        public const string ResourcePath = "Pawnshop/PawnshopInterior3D";

        public static PawnshopInteriorWorldResult Build(Transform parent, PawnshopInteriorLayoutPlan plan)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            PawnshopInteriorLayoutValidator.ValidateOrThrow(plan);
            var template = Resources.Load<GameObject>(ResourcePath);
            if (template == null) throw new InvalidOperationException("Missing authored pawnshop interior model.");
            var root = new GameObject("Pawnshop Interior").transform;
            root.SetParent(parent, false);
            try
            {
                Transform model = Object.Instantiate(template, root, false).transform;
                var anchors = new Dictionary<string, Transform>(StringComparer.Ordinal);
                foreach (Transform part in model.GetComponentsInChildren<Transform>(true))
                    if (part.name.StartsWith("ANCHOR_", StringComparison.Ordinal)) anchors.Add(part.name, part);
                var colliders = new List<BoxCollider>(12);
                Box("Floor", new Vector3(0f, -.1f, 0f), new Vector3(7.7f, .2f, 8.7f), true);
                Box("Ceiling", new Vector3(0f, 3.075f, 0f), new Vector3(7.7f, .15f, 8.7f));
                Box("Left wall", new Vector3(-3.775f, 1.5f, 0f), new Vector3(.15f, 3f, 8.7f));
                Box("Right wall", new Vector3(3.775f, 1.5f, 0f), new Vector3(.15f, 3f, 8.7f));
                Box("Rear wall", new Vector3(0f, 1.5f, 4.275f), new Vector3(7.4f, 3f, .15f));
                Box("Front left jamb", new Vector3(-2.2f, 1.5f, -4.275f), new Vector3(3f, 3f, .15f));
                Box("Front right jamb", new Vector3(2.2f, 1.5f, -4.275f), new Vector3(3f, 3f, .15f));
                Box("Front lintel", new Vector3(0f, 2.675f, -4.275f), new Vector3(1.4f, .65f, .15f));
                BoundsBox("Counter", plan.CounterBounds);
                BoundsBox("Secure staff barrier", plan.SecurePartitionBounds);
                BoundsBox("Left display", plan.LeftDisplayBounds);
                BoundsBox("Right display", plan.RightDisplayBounds);
                var result = new PawnshopInteriorWorldResult(root, model, colliders, anchors);
                ValidateAnchor(result, "Spawn", plan.PlayerSpawn - Vector3.up * PlayerFactory.GroundedRootOffset);
                ValidateAnchor(result, "Clerk", plan.ClerkPosition);
                ValidateAnchor(result, "Trade", plan.TradePosition);
                ValidateAnchor(result, "DoorPivot", plan.DoorPivot);
                ValidateAnchor(result, "DoorHandle", plan.DoorHandle);
                return result;

                void BoundsBox(string name, Bounds bounds) => Box(name, bounds.center, bounds.size);
                void Box(string name, Vector3 centre, Vector3 size, bool floor = false)
                {
                    var host = new GameObject("Pawnshop Collision " + name);
                    host.transform.SetParent(root, false);
                    host.transform.localPosition = centre;
                    var collider = host.AddComponent<BoxCollider>(); collider.size = size;
                    colliders.Add(collider);
                    if (floor) FootstepGround.Stamp(host, FootstepGroundKind.Concrete);
                }
            }
            catch
            {
                if (Application.isPlaying) Object.Destroy(root.gameObject);
                else Object.DestroyImmediate(root.gameObject);
                throw;
            }
        }

        private static void ValidateAnchor(PawnshopInteriorWorldResult world, string name, Vector3 expected)
        {
            Vector3 actual = world.Root.InverseTransformPoint(world.GetAnchor(name).position);
            if (Vector3.Distance(actual, expected) > .015f)
                throw new InvalidOperationException("Pawnshop model/plan anchor mismatch: " + name + " at " + actual);
        }
    }
}

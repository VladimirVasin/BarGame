using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public sealed class MountainRoadBridgeWorldResult
    {
        internal MountainRoadBridgeWorldResult(
            GameObject root,
            GameObject structuralDeck,
            GameObject structuralBeams,
            GameObject abutments,
            IList<Transform> piers,
            IList<Transform> rails,
            IList<Collider> railColliders,
            IDictionary<string, Transform> semanticObjects)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            StructuralDeck = structuralDeck ??
                throw new ArgumentNullException(nameof(structuralDeck));
            StructuralBeams = structuralBeams ??
                throw new ArgumentNullException(nameof(structuralBeams));
            Abutments = abutments ??
                throw new ArgumentNullException(nameof(abutments));
            Piers = new ReadOnlyCollection<Transform>(
                new List<Transform>(
                    piers ?? throw new ArgumentNullException(nameof(piers))));
            Rails = new ReadOnlyCollection<Transform>(
                new List<Transform>(
                    rails ?? throw new ArgumentNullException(nameof(rails))));
            RailColliders = new ReadOnlyCollection<Collider>(
                new List<Collider>(
                    railColliders ??
                    throw new ArgumentNullException(
                        nameof(railColliders))));
            SemanticObjects = new ReadOnlyDictionary<string, Transform>(
                new Dictionary<string, Transform>(
                    semanticObjects ??
                    throw new ArgumentNullException(
                        nameof(semanticObjects)),
                    StringComparer.Ordinal));

            RendererCount = Root.GetComponentsInChildren<Renderer>(true)
                .Length;
            PhysicalColliders = new ReadOnlyCollection<Collider>(
                CollectEnabledColliders(Root));
            ActiveColliderCount = PhysicalColliders.Count;
        }

        public GameObject Root { get; }
        public GameObject StructuralDeck { get; }
        public GameObject StructuralBeams { get; }
        public GameObject Abutments { get; }
        public IReadOnlyList<Transform> Piers { get; }
        public IReadOnlyList<Transform> Rails { get; }
        public IReadOnlyList<Collider> RailColliders { get; }
        public IReadOnlyList<Collider> PhysicalColliders { get; }
        public IReadOnlyDictionary<string, Transform> SemanticObjects { get; }
        public int RendererCount { get; }
        public int ActiveColliderCount { get; }

        private static List<Collider> CollectEnabledColliders(GameObject root)
        {
            Collider[] colliders =
                root.GetComponentsInChildren<Collider>(true);
            var result = new List<Collider>(colliders.Length);
            for (int index = 0; index < colliders.Length; index++)
            {
                if (colliders[index].enabled)
                {
                    result.Add(colliders[index]);
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Materializes the structure below the route's existing continuous
    /// asphalt. Its open rails use separate continuous collision ribbons so
    /// the silhouette stays light without leaving vehicle-sized gaps.
    /// </summary>
    public static class MountainRoadBridgeWorldBuilder
    {
        public const float StructuralDeckSurfaceClearance = 0.035f;
        public const float RailViewingGapLength = 2.4f;

        private const float RailPostThickness = 0.16f;
        private const float RailBeamThickness = 0.13f;
        private const float RailPostSpacing = 2.75f;
        private const float RailColliderEndOverlap = 0.24f;
        private const float GirderWidth = 0.30f;
        private const float CrossBeamThickness = 0.22f;
        private const float PierCapHeight = 0.48f;
        private const float PierCapDepth = 1.05f;
        private const float PierLegWidth = 0.72f;
        private const float PierFootingHeight = 0.48f;
        private const float PierFootingEmbed = 0.42f;
        private const int MaximumRailIntervals = 24;

        // The bridge borrows three packaged sheets rather than printing its
        // own: the deck, abutments and piers are concrete, the girders and
        // crossbeams are oxidised steel and the open rails are painted
        // rail steel. Each batch bakes its UVs at its own recipe's pitch,
        // so the pitch is never restated as a literal here.
        private static float ConcreteMetersPerTile =>
            MountainRoadSurfaceAppearance.GetRecipe(
                MountainRoadSurfaceKind.Concrete).MetersPerTile;

        private static float IronMetersPerTile =>
            MountainRoadSurfaceAppearance.GetRecipe(
                MountainRoadSurfaceKind.RustedIron).MetersPerTile;

        private static readonly Color AgedConcrete =
            new Color(0.285f, 0.30f, 0.285f, 1f);
        private static readonly Color DarkConcrete =
            new Color(0.225f, 0.24f, 0.23f, 1f);
        private static readonly Color OxidizedSteel =
            new Color(0.205f, 0.225f, 0.215f, 1f);
        private static readonly Color RailSteel =
            new Color(0.245f, 0.275f, 0.26f, 1f);

        public static MountainRoadBridgeWorldResult Build(
            Transform parent,
            MountainRoadBridgeDescriptor bridge)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            MountainRoadBridgeValidator.ValidateOrThrow(bridge);

            var root = new GameObject(
                "High Gorge Bridge - " + bridge.StableId);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = bridge.Center;

            Vector3 span = bridge.End - bridge.Start;
            Quaternion bridgeRotation = Quaternion.LookRotation(
                span.normalized,
                Vector3.up);
            Quaternion yawRotation = Quaternion.LookRotation(
                bridge.Forward,
                Vector3.up);
            Vector3 deckUp = bridgeRotation * Vector3.up;
            float girderHeight = Mathf.Clamp(
                bridge.DeckThickness * 0.82f,
                0.52f,
                0.78f);

            var semanticObjects = new Dictionary<string, Transform>(
                StringComparer.Ordinal)
            {
                [bridge.StableId] = root.transform
            };

            GameObject structuralDeck = BuildStructuralDeck(
                root.transform,
                bridge,
                span,
                bridgeRotation,
                deckUp);
            semanticObjects[bridge.StableId + "-deck"] =
                structuralDeck.transform;

            GameObject structuralBeams = BuildStructuralBeams(
                root.transform,
                bridge,
                span,
                bridgeRotation,
                deckUp,
                girderHeight);
            GameObject abutments = BuildAbutments(
                root.transform,
                bridge,
                span,
                yawRotation,
                deckUp,
                girderHeight);
            semanticObjects[bridge.StableId + "-abutments"] =
                abutments.transform;

            var piers = new List<Transform>(2);
            for (int index = 0; index < 2; index++)
            {
                float t = index == 0 ? 0.34f : 0.66f;
                Transform pier = BuildPier(
                    root.transform,
                    bridge,
                    t,
                    index,
                    yawRotation,
                    deckUp,
                    girderHeight);
                piers.Add(pier);
                semanticObjects[
                    bridge.StableId + $"-pier-{index + 1:00}"] = pier;
            }

            var rails = new List<Transform>(2);
            var railColliders = new List<Collider>(2);
            BuildRail(
                root.transform,
                bridge,
                span,
                bridgeRotation,
                deckUp,
                -1,
                out Transform leftRail,
                out Collider leftCollider);
            rails.Add(leftRail);
            railColliders.Add(leftCollider);
            semanticObjects[bridge.StableId + "-rail-left"] = leftRail;

            BuildRail(
                root.transform,
                bridge,
                span,
                bridgeRotation,
                deckUp,
                1,
                out Transform rightRail,
                out Collider rightCollider);
            rails.Add(rightRail);
            railColliders.Add(rightCollider);
            semanticObjects[bridge.StableId + "-rail-right"] = rightRail;

            var result = new MountainRoadBridgeWorldResult(
                root,
                structuralDeck,
                structuralBeams,
                abutments,
                piers,
                rails,
                railColliders,
                semanticObjects);
            MountainRoadBridgeValidator.ValidateBuiltWorldOrThrow(result);
            return result;
        }

        /// <summary>
        /// The slab under the road. It is one oriented box in a combined
        /// batch rather than a scaled cube, for the reason the girders and
        /// the piers already are: a cube's per-face 0..1 UVs would stretch
        /// one tile of concrete over the whole fifty-metre span, while the
        /// batch bakes true metre-scale UVs per face. The slab keeps its
        /// place in the world, its single enabled collider and its single
        /// renderer; only its transform stops carrying the offset, which is
        /// now baked into the mesh.
        /// </summary>
        private static GameObject BuildStructuralDeck(
            Transform parent,
            MountainRoadBridgeDescriptor bridge,
            Vector3 span,
            Quaternion bridgeRotation,
            Vector3 deckUp)
        {
            var slab = new[]
            {
                new RuntimeOrientedBox(
                    -deckUp *
                    (StructuralDeckSurfaceClearance +
                     bridge.DeckThickness * 0.5f),
                    bridgeRotation,
                    new Vector3(
                        bridge.DeckWidth,
                        bridge.DeckThickness,
                        span.magnitude + 0.12f))
            };
            GameObject deck =
                RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                    "Physical Sloped Structural Deck",
                    parent,
                    slab,
                    AgedConcrete,
                    true,
                    ConcreteMetersPerTile,
                    RuntimeWorldUvMode.BoxProjected);
            MountainRoadSurfaceAppearance.ApplyCombined(
                deck.GetComponent<Renderer>(),
                MountainRoadSurfaceKind.Concrete,
                AgedConcrete);
            return deck;
        }

        private static GameObject BuildStructuralBeams(
            Transform parent,
            MountainRoadBridgeDescriptor bridge,
            Vector3 span,
            Quaternion bridgeRotation,
            Vector3 deckUp,
            float girderHeight)
        {
            var boxes = new List<RuntimeOrientedBox>(20);
            float girderLateral = bridge.DeckWidth * 0.34f;
            float girderDrop = StructuralDeckSurfaceClearance +
                                bridge.DeckThickness +
                                girderHeight * 0.5f;
            for (int girder = -1; girder <= 1; girder++)
            {
                boxes.Add(new RuntimeOrientedBox(
                    bridge.Right * (girder * girderLateral) -
                    deckUp * girderDrop,
                    bridgeRotation,
                    new Vector3(
                        GirderWidth,
                        girderHeight,
                        span.magnitude - 0.45f)));
            }

            int crossBeamIntervals = Mathf.Clamp(
                Mathf.CeilToInt(bridge.Length / 4.5f),
                6,
                14);
            float crossBeamDrop = StructuralDeckSurfaceClearance +
                                  bridge.DeckThickness +
                                  girderHeight * 0.72f;
            for (int index = 0; index <= crossBeamIntervals; index++)
            {
                float t = index / (float)crossBeamIntervals;
                boxes.Add(new RuntimeOrientedBox(
                    span * (t - 0.5f) - deckUp * crossBeamDrop,
                    bridgeRotation,
                    new Vector3(
                        bridge.DeckWidth + 0.18f,
                        CrossBeamThickness,
                        0.32f)));
            }

            GameObject beams =
                RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                    "Batched Steel Girders And Crossbeams",
                    parent,
                    boxes,
                    OxidizedSteel,
                    false,
                    IronMetersPerTile,
                    RuntimeWorldUvMode.BoxProjected);
            MountainRoadSurfaceAppearance.ApplyCombined(
                beams.GetComponent<Renderer>(),
                MountainRoadSurfaceKind.RustedIron,
                OxidizedSteel);
            return beams;
        }

        private static GameObject BuildAbutments(
            Transform parent,
            MountainRoadBridgeDescriptor bridge,
            Vector3 span,
            Quaternion yawRotation,
            Vector3 deckUp,
            float girderHeight)
        {
            float planarSpan = new Vector2(span.x, span.z).magnitude;
            float depth = Mathf.Clamp(
                bridge.AbutmentBlendLength * 0.22f,
                1.1f,
                1.6f);
            float inset = Mathf.Clamp01(depth * 0.5f / planarSpan);
            float height = Mathf.Clamp(
                (Mathf.Min(bridge.Start.y, bridge.End.y) -
                 bridge.GorgeFloorY) * 0.14f,
                2.8f,
                4.4f);
            var boxes = new List<RuntimeOrientedBox>(2);
            float[] samples = { inset, 1f - inset };
            for (int index = 0; index < samples.Length; index++)
            {
                Vector3 surface = Vector3.Lerp(
                    bridge.Start,
                    bridge.End,
                    samples[index]);
                Vector3 supportTop = surface - deckUp *
                    (StructuralDeckSurfaceClearance +
                     bridge.DeckThickness +
                     girderHeight);
                Vector3 center = supportTop -
                                 Vector3.up * (height * 0.5f) -
                                 bridge.Center;
                boxes.Add(new RuntimeOrientedBox(
                    center,
                    yawRotation,
                    new Vector3(
                        bridge.DeckWidth + 1.1f,
                        height,
                        depth)));
            }

            GameObject abutments =
                RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                    "Physical Concrete Abutments",
                    parent,
                    boxes,
                    DarkConcrete,
                    true,
                    ConcreteMetersPerTile,
                    RuntimeWorldUvMode.BoxProjected);
            MountainRoadSurfaceAppearance.ApplyCombined(
                abutments.GetComponent<Renderer>(),
                MountainRoadSurfaceKind.Concrete,
                DarkConcrete);
            return abutments;
        }

        private static Transform BuildPier(
            Transform parent,
            MountainRoadBridgeDescriptor bridge,
            float t,
            int index,
            Quaternion yawRotation,
            Vector3 deckUp,
            float girderHeight)
        {
            Vector3 surface = Vector3.Lerp(bridge.Start, bridge.End, t);
            Vector3 supportTop = surface - deckUp *
                (StructuralDeckSurfaceClearance +
                 bridge.DeckThickness +
                 girderHeight);
            float pierBaseY = bridge.GorgeFloorY - PierFootingEmbed;
            float supportHeight = supportTop.y - pierBaseY;
            float legTop = supportHeight - PierCapHeight;
            float legHeight = legTop - PierFootingHeight;
            float legOffset = bridge.DeckWidth * 0.31f;

            Transform pierRoot = new GameObject(
                $"Grounded Gorge Pier {index + 1:00}").transform;
            pierRoot.SetParent(parent, false);
            pierRoot.localPosition = new Vector3(
                surface.x - bridge.Center.x,
                pierBaseY - bridge.Center.y,
                surface.z - bridge.Center.z);
            pierRoot.localRotation = yawRotation;

            var boxes = new List<RuntimeOrientedBox>(5)
            {
                new RuntimeOrientedBox(
                    new Vector3(
                        0f,
                        supportHeight - PierCapHeight * 0.5f,
                        0f),
                    Quaternion.identity,
                    new Vector3(
                        bridge.DeckWidth + 0.92f,
                        PierCapHeight,
                        PierCapDepth))
            };
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * legOffset;
                boxes.Add(new RuntimeOrientedBox(
                    new Vector3(
                        x,
                        PierFootingHeight + legHeight * 0.5f,
                        0f),
                    Quaternion.identity,
                    new Vector3(
                        PierLegWidth,
                        legHeight,
                        PierLegWidth)));
                boxes.Add(new RuntimeOrientedBox(
                    new Vector3(
                        x,
                        PierFootingHeight * 0.5f,
                        0f),
                    Quaternion.identity,
                    new Vector3(
                        PierLegWidth * 1.75f,
                        PierFootingHeight,
                        PierLegWidth * 1.75f)));
            }

            GameObject bent =
                RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                    "Physical Pier Bent And Footings",
                    pierRoot,
                    boxes,
                    AgedConcrete,
                    true,
                    ConcreteMetersPerTile,
                    RuntimeWorldUvMode.BoxProjected);
            MountainRoadSurfaceAppearance.ApplyCombined(
                bent.GetComponent<Renderer>(),
                MountainRoadSurfaceKind.Concrete,
                AgedConcrete);
            return pierRoot;
        }

        private static void BuildRail(
            Transform parent,
            MountainRoadBridgeDescriptor bridge,
            Vector3 span,
            Quaternion bridgeRotation,
            Vector3 deckUp,
            int side,
            out Transform railRoot,
            out Collider continuousCollider)
        {
            string sideName = side < 0 ? "Left" : "Right";
            float lateral = bridge.DeckWidth * 0.5f -
                            RailPostThickness * 0.5f;
            railRoot = new GameObject(
                sideName + " Open Bridge Rail").transform;
            railRoot.SetParent(parent, false);
            railRoot.localPosition = bridge.Right * (side * lateral);

            int intervals = Mathf.Clamp(
                Mathf.CeilToInt(bridge.Length / RailPostSpacing),
                2,
                MaximumRailIntervals);
            var boxes = new List<RuntimeOrientedBox>(intervals + 5);
            for (int index = 0; index <= intervals; index++)
            {
                float t = index / (float)intervals;
                float along = (t - 0.5f) * span.magnitude;
                if (Mathf.Abs(along) <
                    RailViewingGapLength * 0.5f +
                    RailPostThickness)
                {
                    continue;
                }

                boxes.Add(new RuntimeOrientedBox(
                    span * (t - 0.5f) +
                    Vector3.up * (bridge.RailHeight * 0.5f),
                    Quaternion.identity,
                    new Vector3(
                        RailPostThickness,
                        bridge.RailHeight,
                        RailPostThickness)));
            }

            float[] railHeights =
            {
                bridge.RailHeight * 0.52f,
                bridge.RailHeight - RailBeamThickness * 0.5f
            };
            float railSegmentLength =
                (span.magnitude - RailViewingGapLength) * 0.5f;
            Vector3 railAxis = span.normalized;
            for (int index = 0; index < railHeights.Length; index++)
            {
                for (int half = -1; half <= 1; half += 2)
                {
                    float along = RailViewingGapLength * 0.5f +
                                  railSegmentLength * 0.5f;
                    boxes.Add(new RuntimeOrientedBox(
                        railAxis * (half * along) +
                        Vector3.up * railHeights[index],
                        bridgeRotation,
                        new Vector3(
                            RailBeamThickness,
                            RailBeamThickness,
                            railSegmentLength)));
                }
            }

            GameObject railBatch =
                RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                    "Batched Open Rail Posts And Beams",
                    railRoot,
                    boxes,
                    RailSteel,
                    false,
                    IronMetersPerTile,
                    RuntimeWorldUvMode.BoxProjected);
            MountainRoadSurfaceAppearance.ApplyCombined(
                railBatch.GetComponent<Renderer>(),
                MountainRoadSurfaceKind.RustedIron,
                RailSteel);

            var colliderObject = new GameObject(
                "Continuous Physical Rail Collider");
            colliderObject.transform.SetParent(railRoot, false);
            colliderObject.transform.localPosition =
                deckUp * (bridge.RailHeight * 0.5f);
            colliderObject.transform.localRotation = bridgeRotation;
            var boxCollider = colliderObject.AddComponent<BoxCollider>();
            boxCollider.size = new Vector3(
                RailPostThickness,
                bridge.RailHeight,
                span.magnitude + RailColliderEndOverlap * 2f);
            continuousCollider = boxCollider;
        }
    }
}

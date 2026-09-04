using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Places the Blender-authored drink-service assemblies from a validated
    /// local plan. Unity owns state, physics and per-drink colour only; every
    /// visible bottle, vessel, menu page and pour stream comes from the shared
    /// bar service prop pack.
    /// </summary>
    public static class BarDrinkServiceWorldBuilder
    {
        private static readonly BarDrinkVesselKind[] VesselKinds =
        {
            BarDrinkVesselKind.Tumbler,
            BarDrinkVesselKind.Pint,
            BarDrinkVesselKind.WineGlass,
            BarDrinkVesselKind.ShotGlass,
            BarDrinkVesselKind.Snifter
        };

        private static readonly Color GlassColor =
            new Color(0.62f, 0.82f, 0.86f, 0.24f);
        public static BarDrinkServiceView Build(
            Transform parent,
            BarDrinkServicePlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (plan.BottleSlots.Count !=
                BarDrinkServicePlan.RequiredBottleCount)
            {
                throw new ArgumentException(
                    "Bar drink world requires exactly four bottle slots.",
                    nameof(plan));
            }

            var serviceObject = new GameObject("Bar Drink Service");
            serviceObject.transform.SetParent(parent, false);
            Transform serviceRoot = serviceObject.transform;
            BarDrinkServiceView serviceView =
                serviceObject.AddComponent<BarDrinkServiceView>();

            BarDrinkMenuPresentation menu = BuildMenu(
                serviceRoot,
                plan,
                parent.Find("MenuDock"));

            var bottles = new List<BarDrinkBottleView>(
                BarDrinkServicePlan.RequiredBottleCount);
            for (int index = 0; index < plan.BottleSlots.Count; index++)
            {
                BarDrinkBottleSlotPlan slotPlan = plan.BottleSlots[index];
                BarDrinkPresentation presentation =
                    BarDrinkPresentationCatalog.Get(slotPlan.DrinkId);
                bottles.Add(BuildBottle(
                    serviceRoot,
                    slotPlan,
                    presentation));
            }

            var vessels = new List<BarDrinkVesselView>(VesselKinds.Length);
            for (int index = 0; index < VesselKinds.Length; index++)
            {
                vessels.Add(BuildVessel(serviceRoot, VesselKinds[index]));
            }

            BarServicePropInstance stream =
                BarServicePropFactory.CreatePourStream(serviceRoot);
            if (!stream.TryGetRenderer(
                    "service_pour_stream",
                    out Renderer streamRenderer))
            {
                throw new InvalidOperationException(
                    "The authored bar service stream has no renderer.");
            }

            streamRenderer.sharedMaterial =
                BarDrinkServiceResources.LiquidMaterial;
            RuntimePrimitiveFactory.SetColor(streamRenderer, Color.white);
            SetTransparentRenderer(streamRenderer);

            BarBeerTapRuntimeBinding beerTap = BindBeerTap(
                parent,
                serviceRoot,
                plan.BeerTap);

            serviceView.Initialize(
                plan,
                bottles,
                vessels,
                stream.transform,
                streamRenderer,
                menu,
                beerTap);
            return serviceView;
        }

        private static BarDrinkBottleView BuildBottle(
            Transform parent,
            BarDrinkBottleSlotPlan slotPlan,
            BarDrinkPresentation presentation)
        {
            var slotObject = new GameObject(
                $"Bar Drink Slot {slotPlan.Id}");
            slotObject.transform.SetParent(parent, false);
            slotObject.transform.localPosition = slotPlan.Pose.Position;
            slotObject.transform.localRotation = slotPlan.Pose.Rotation;

            BarServicePropInstance authored =
                BarServicePropFactory.CreateBottle(
                    slotObject.transform,
                    presentation.BottleStyle);
            GameObject bottleObject = authored.gameObject;
            bottleObject.name =
                $"Bar Drink Bottle {presentation.StableId}";
            Transform bottleRoot = bottleObject.transform;
            var renderers = new List<Renderer>();
            var colors = new List<Color>();
            ConfigureBottleAppearance(
                authored,
                presentation,
                renderers,
                colors);
            Transform mouth = RequireAnchor(
                authored,
                "service_bottle_mouth:" + presentation.BottleStyle);
            Bounds localBounds = CalculateLocalBounds(
                bottleRoot,
                renderers);

            Collider solidCollider;
            if (presentation.BottleStyle ==
                BarDrinkBottleStyle.VodkaBottle)
            {
                BoxCollider solidBox =
                    bottleObject.AddComponent<BoxCollider>();
                solidBox.center = localBounds.center;
                solidBox.size = Vector3.Scale(
                    localBounds.size,
                    new Vector3(0.94f, 0.96f, 0.94f));
                solidCollider = solidBox;
            }
            else
            {
                CapsuleCollider solidCapsule =
                    bottleObject.AddComponent<CapsuleCollider>();
                solidCapsule.direction = 1;
                solidCapsule.center = localBounds.center;
                solidCapsule.radius = Mathf.Max(
                    0.01f,
                    Mathf.Min(localBounds.size.x, localBounds.size.z) *
                    0.47f);
                solidCapsule.height = Mathf.Max(
                    localBounds.size.y * 0.98f,
                    solidCapsule.radius * 2f);
                solidCollider = solidCapsule;
            }

            BoxCollider selectionTrigger =
                bottleObject.AddComponent<BoxCollider>();
            selectionTrigger.center = localBounds.center;
            selectionTrigger.size = localBounds.size +
                new Vector3(0.09f, 0.07f, 0.09f);
            selectionTrigger.isTrigger = true;

            Rigidbody body = bottleObject.AddComponent<Rigidbody>();
            body.mass = 0.55f;
            body.useGravity = false;
            body.isKinematic = true;
            body.detectCollisions = true;
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode =
                CollisionDetectionMode.ContinuousSpeculative;

            mouth.name = "Bottle Mouth Anchor";

            BarDrinkBottleView bottleView =
                bottleObject.AddComponent<BarDrinkBottleView>();
            bottleView.Initialize(
                presentation.DrinkId,
                slotPlan.Id,
                mouth,
                renderers,
                colors,
                solidCollider,
                selectionTrigger,
                body);
            return bottleView;
        }

        /// <summary>
        /// Builds the same authored bottle silhouette the service
        /// shelf uses, visuals only -- no colliders, physics or view
        /// state -- for props such as the patrons' hand-held bottles.
        /// Returns the bottle's total local height.
        /// </summary>
        internal static float BuildBottleVisual(
            Transform root,
            BarDrinkPresentation presentation)
        {
            BarServicePropInstance authored =
                BarServicePropFactory.CreateBottle(
                    root,
                    presentation.BottleStyle);
            var renderers = new List<Renderer>();
            var colors = new List<Color>();
            ConfigureBottleAppearance(
                authored,
                presentation,
                renderers,
                colors);
            Transform mouth = RequireAnchor(
                authored,
                "service_bottle_mouth:" + presentation.BottleStyle);
            return mouth.localPosition.y;
        }

        private static void ConfigureBottleAppearance(
            BarServicePropInstance authored,
            BarDrinkPresentation presentation,
            ICollection<Renderer> renderers,
            ICollection<Color> colors)
        {
            RegisterAuthoredPart(
                RequireRenderer(authored, "service_bottle_body"),
                presentation.BottleColor,
                BarSurfaceKind.BottleGlass,
                renderers,
                colors,
                true);
            RegisterAuthoredPart(
                RequireRenderer(authored, "service_bottle_closure"),
                ResolveClosureColor(presentation.BottleStyle),
                BarSurfaceKind.PaintedMetal,
                renderers,
                colors,
                true);
            RegisterAuthoredPart(
                RequireRenderer(authored, "service_bottle_label"),
                presentation.LabelColor,
                BarSurfaceKind.Paper,
                renderers,
                colors,
                true);

            if (renderers.Count != authored.Renderers.Count)
            {
                throw new InvalidOperationException(
                    $"The authored {presentation.BottleStyle} bottle has " +
                    "an unexpected visible part contract.");
            }
        }

        private static Color ResolveClosureColor(
            BarDrinkBottleStyle style)
        {
            switch (style)
            {
                case BarDrinkBottleStyle.WaterBottle:
                    return new Color(0.74f, 0.78f, 0.69f);
                case BarDrinkBottleStyle.BeerLongneck:
                    return new Color(0.57f, 0.48f, 0.31f);
                case BarDrinkBottleStyle.WineBottle:
                    return new Color(0.48f, 0.30f, 0.16f);
                case BarDrinkBottleStyle.VodkaBottle:
                    return new Color(0.52f, 0.58f, 0.57f);
                case BarDrinkBottleStyle.CognacBottle:
                    return new Color(0.38f, 0.19f, 0.08f);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(style),
                        style,
                        "Unsupported bottle style.");
            }
        }

        private static BarDrinkVesselView BuildVessel(
            Transform parent,
            BarDrinkVesselKind kind)
        {
            BarServicePropInstance authored =
                BarServicePropFactory.CreateVessel(parent, kind);
            GameObject vesselObject = authored.gameObject;
            vesselObject.name = $"Bar Drink Vessel {kind}";
            Renderer glassRenderer = RequireRenderer(
                authored,
                "service_vessel_shell");
            glassRenderer.sharedMaterial =
                BarDrinkServiceResources.GlassMaterial;
            RuntimePrimitiveFactory.SetColor(glassRenderer, GlassColor);
            SetTransparentRenderer(glassRenderer);

            Renderer liquidRenderer = RequireRenderer(
                authored,
                "service_vessel_liquid");
            liquidRenderer.sharedMaterial =
                BarDrinkServiceResources.LiquidMaterial;
            RuntimePrimitiveFactory.SetColor(liquidRenderer, Color.white);
            SetTransparentRenderer(liquidRenderer);

            var liquidObject = new GameObject("Liquid Fill");
            liquidObject.transform.SetParent(vesselObject.transform, false);
            Transform liquidBase = RequireAnchor(
                authored,
                "service_vessel_liquid_base:" + kind);
            liquidObject.transform.localPosition = liquidBase.localPosition;
            liquidRenderer.transform.SetParent(
                liquidObject.transform,
                true);
            Transform pourTarget = RequireAnchor(
                authored,
                "service_vessel_target:" + kind);
            pourTarget.name = "Pour Target";
            Transform gripAnchor = RequireAnchor(
                authored,
                "service_vessel_grip:" + kind);
            gripAnchor.name = "Vessel Grip Anchor";
            Transform drinkRimAnchor = RequireAnchor(
                authored,
                "service_vessel_drink_rim:" + kind);
            drinkRimAnchor.name = "Vessel Drink Rim Anchor";

            Renderer highlightRenderer = null;
            if (authored.TryGetRenderer(
                    "service_vessel_highlight",
                    out Renderer authoredHighlight))
            {
                highlightRenderer = authoredHighlight;
                RuntimePrimitiveFactory.SetColor(
                    highlightRenderer,
                    new Color(1f, 0.72f, 0.04f, 1f));
                highlightRenderer.shadowCastingMode = ShadowCastingMode.Off;
                highlightRenderer.receiveShadows = false;
                highlightRenderer.enabled = false;
            }

            BarDrinkVesselView view =
                vesselObject.AddComponent<BarDrinkVesselView>();
            view.Initialize(
                kind,
                glassRenderer,
                liquidObject.transform,
                liquidRenderer,
                pourTarget,
                gripAnchor,
                drinkRimAnchor,
                highlightRenderer);
            return view;
        }

        private static BarBeerTapRuntimeBinding BindBeerTap(
            Transform room,
            Transform serviceRoot,
            BarBeerTapServicePlan plan)
        {
            bool authored = true;
            Transform serverDock = ResolveTapAnchor(
                room,
                serviceRoot,
                BarBeerTapServicePlan.ServerDockAnchorName,
                plan.ServerPose,
                ref authored);
            Transform vesselDock = ResolveTapAnchor(
                room,
                serviceRoot,
                BarBeerTapServicePlan.VesselDockAnchorName,
                plan.VesselPose,
                ref authored);
            Transform spout = ResolveTapAnchor(
                room,
                serviceRoot,
                BarBeerTapServicePlan.SpoutAnchorName,
                plan.SpoutPose,
                ref authored);
            Transform handlePivot = ResolveTapAnchor(
                room,
                serviceRoot,
                BarBeerTapServicePlan.HandlePivotAnchorName,
                plan.HandlePivotPose,
                ref authored);
            Transform handleGrip = ResolveTapAnchor(
                room,
                serviceRoot,
                BarBeerTapServicePlan.HandleGripAnchorName,
                plan.HandleGripPose,
                ref authored);
            Transform handleRoot = room.Find(
                BarBeerTapServicePlan.HandlePartName);
            authored &= handleRoot != null;
            return new BarBeerTapRuntimeBinding(
                serverDock,
                vesselDock,
                spout,
                handlePivot,
                handleGrip,
                handleRoot,
                authored);
        }

        private static Transform ResolveTapAnchor(
            Transform room,
            Transform serviceRoot,
            string anchorName,
            BarDrinkServicePose fallback,
            ref bool authored)
        {
            Transform anchor = room.Find(anchorName);
            if (anchor != null)
            {
                return anchor;
            }

            authored = false;
            var fallbackObject = new GameObject(
                anchorName + " Runtime Fallback");
            Transform fallbackAnchor = fallbackObject.transform;
            fallbackAnchor.SetParent(serviceRoot, false);
            fallbackAnchor.localPosition = fallback.Position;
            fallbackAnchor.localRotation = fallback.Rotation;
            return fallbackAnchor;
        }

        private static BarDrinkMenuPresentation BuildMenu(
            Transform parent,
            BarDrinkServicePlan plan,
            Transform authoredDock)
        {
            return BarDrinkMenuPresentation.CreateAndBind(
                parent,
                plan,
                authoredDock);
        }

        private static Renderer RequireRenderer(
            BarServicePropInstance authored,
            string role)
        {
            if (authored != null &&
                authored.TryGetRenderer(role, out Renderer renderer) &&
                renderer != null)
            {
                return renderer;
            }

            throw new InvalidOperationException(
                $"The authored bar service group has no renderer role " +
                $"'{role}'.");
        }

        private static Transform RequireAnchor(
            BarServicePropInstance authored,
            string role)
        {
            if (authored != null &&
                authored.TryGetAnchor(role, out Transform anchor) &&
                anchor != null)
            {
                return anchor;
            }

            throw new InvalidOperationException(
                $"The authored bar service group has no anchor role " +
                $"'{role}'.");
        }

        private static void RegisterAuthoredPart(
            Renderer renderer,
            Color color,
            BarSurfaceKind surface,
            ICollection<Renderer> renderers,
            ICollection<Color> colors,
            bool castsShadows)
        {
            Color displayColor = BarSurfaceAppearance.CreateDisplayTint(
                color,
                surface);
            RuntimePrimitiveFactory.SetColor(renderer, displayColor);
            renderer.shadowCastingMode = castsShadows
                ? ShadowCastingMode.On
                : ShadowCastingMode.Off;
            renderer.receiveShadows = castsShadows;
            renderers.Add(renderer);
            colors.Add(displayColor);
        }

        private static Bounds CalculateLocalBounds(
            Transform root,
            IReadOnlyList<Renderer> renderers)
        {
            bool hasPoint = false;
            Bounds result = default;
            for (int rendererIndex = 0;
                 rendererIndex < renderers.Count;
                 rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                if (renderer == null)
                {
                    continue;
                }

                Bounds world = renderer.bounds;
                for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
                {
                    Vector3 corner = world.center + new Vector3(
                        (cornerIndex & 1) == 0
                            ? -world.extents.x
                            : world.extents.x,
                        (cornerIndex & 2) == 0
                            ? -world.extents.y
                            : world.extents.y,
                        (cornerIndex & 4) == 0
                            ? -world.extents.z
                            : world.extents.z);
                    Vector3 local = root.InverseTransformPoint(corner);
                    if (!hasPoint)
                    {
                        result = new Bounds(local, Vector3.zero);
                        hasPoint = true;
                    }
                    else
                    {
                        result.Encapsulate(local);
                    }
                }
            }

            if (!hasPoint || result.size.sqrMagnitude < 0.000001f)
            {
                throw new InvalidOperationException(
                    "The authored bottle has no measurable renderer bounds.");
            }

            return result;
        }

        private static void SetTransparentRenderer(Renderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

    }
}

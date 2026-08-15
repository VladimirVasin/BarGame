using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Builds the complete physical drink-service set from a validated local
    /// plan. No generated object owns a unique Material or a realtime Light.
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
        private static readonly Color DarkWoodColor =
            new Color(0.11f, 0.045f, 0.025f);
        private static readonly Color LeatherColor =
            new Color(0.31f, 0.055f, 0.052f);
        private static readonly Color BrassColor =
            new Color(0.68f, 0.44f, 0.14f);

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
                    "Bar drink world requires exactly nine bottle slots.",
                    nameof(plan));
            }

            var serviceObject = new GameObject("Bar Drink Service");
            serviceObject.transform.SetParent(parent, false);
            Transform serviceRoot = serviceObject.transform;
            BarDrinkServiceView serviceView =
                serviceObject.AddComponent<BarDrinkServiceView>();

            BuildServiceStool(serviceRoot, plan);
            BuildMenu(serviceRoot, plan);

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

            GameObject stream = RuntimePrimitiveFactory.CreateCylinder(
                "Bar Drink Pour Stream",
                serviceRoot,
                Vector3.zero,
                Vector3.one,
                Color.white,
                BarDrinkServiceResources.LiquidMaterial,
                false);
            Renderer streamRenderer = stream.GetComponent<Renderer>();
            SetTransparentRenderer(streamRenderer);

            serviceView.Initialize(
                plan,
                bottles,
                vessels,
                stream.transform,
                streamRenderer);
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

            var bottleObject = new GameObject(
                $"Bar Drink Bottle {presentation.StableId}");
            bottleObject.transform.SetParent(slotObject.transform, false);
            Transform bottleRoot = bottleObject.transform;
            var renderers = new List<Renderer>();
            var colors = new List<Color>();
            BottleDimensions dimensions = BuildBottleGeometry(
                bottleRoot,
                presentation,
                renderers,
                colors);

            Collider solidCollider;
            if (presentation.BottleStyle ==
                BarDrinkBottleStyle.VodkaBottle)
            {
                BoxCollider solidBox =
                    bottleObject.AddComponent<BoxCollider>();
                solidBox.center = new Vector3(
                    0f,
                    dimensions.TotalHeight * 0.48f,
                    0f);
                solidBox.size = new Vector3(
                    dimensions.Width * 0.94f,
                    dimensions.TotalHeight * 0.96f,
                    dimensions.Depth * 0.94f);
                solidCollider = solidBox;
            }
            else
            {
                CapsuleCollider solidCapsule =
                    bottleObject.AddComponent<CapsuleCollider>();
                solidCapsule.direction = 1;
                solidCapsule.center = new Vector3(
                    0f,
                    dimensions.TotalHeight * 0.5f,
                    0f);
                solidCapsule.radius =
                    Mathf.Min(dimensions.Width, dimensions.Depth) * 0.5f;
                solidCapsule.height = dimensions.TotalHeight;
                solidCollider = solidCapsule;
            }

            BoxCollider selectionTrigger =
                bottleObject.AddComponent<BoxCollider>();
            selectionTrigger.center = new Vector3(
                0f,
                dimensions.TotalHeight * 0.5f,
                0f);
            selectionTrigger.size = new Vector3(
                dimensions.Width + 0.09f,
                dimensions.TotalHeight + 0.07f,
                dimensions.Depth + 0.09f);
            selectionTrigger.isTrigger = true;

            Rigidbody body = bottleObject.AddComponent<Rigidbody>();
            body.mass = 0.55f;
            body.useGravity = false;
            body.isKinematic = true;
            body.detectCollisions = true;
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode =
                CollisionDetectionMode.ContinuousSpeculative;

            var mouthObject = new GameObject("Bottle Mouth Anchor");
            mouthObject.transform.SetParent(bottleRoot, false);
            mouthObject.transform.localPosition =
                Vector3.up * dimensions.TotalHeight;

            BarDrinkBottleView bottleView =
                bottleObject.AddComponent<BarDrinkBottleView>();
            bottleView.Initialize(
                presentation.DrinkId,
                slotPlan.Id,
                mouthObject.transform,
                renderers,
                colors,
                solidCollider,
                selectionTrigger,
                body);
            return bottleView;
        }

        /// <summary>
        /// Builds the same authored bottle silhouette the service
        /// shelf uses, visuals only — no colliders, physics or view
        /// state — for props such as the patrons' hand-held bottles.
        /// Returns the bottle's total local height.
        /// </summary>
        internal static float BuildBottleVisual(
            Transform root,
            BarDrinkPresentation presentation)
        {
            var renderers = new List<Renderer>();
            var colors = new List<Color>();
            BottleDimensions dimensions = BuildBottleGeometry(
                root,
                presentation,
                renderers,
                colors);
            return dimensions.TotalHeight;
        }

        private static BottleDimensions BuildBottleGeometry(
            Transform root,
            BarDrinkPresentation presentation,
            ICollection<Renderer> renderers,
            ICollection<Color> colors)
        {
            Color bottleColor = presentation.BottleColor;
            Color labelColor = presentation.LabelColor;
            BottleDimensions dimensions;
            switch (presentation.BottleStyle)
            {
                case BarDrinkBottleStyle.WaterBottle:
                    dimensions = new BottleDimensions(0.21f, 0.21f, 0.68f);
                    AddCylinder(root, "Body", 0.21f, 0.48f, 0.24f,
                        bottleColor, renderers, colors);
                    AddCylinder(root, "Shoulder", 0.18f, 0.08f, 0.52f,
                        Shade(bottleColor, 0.95f), renderers, colors);
                    AddCylinder(root, "Neck", 0.085f, 0.10f, 0.61f,
                        Shade(bottleColor, 1.05f), renderers, colors);
                    AddCylinder(root, "Cap", 0.098f, 0.04f, 0.66f,
                        new Color(0.74f, 0.78f, 0.69f), renderers, colors);
                    break;
                case BarDrinkBottleStyle.BeerLongneck:
                    dimensions = new BottleDimensions(0.21f, 0.21f, 0.72f);
                    AddCylinder(root, "Body", 0.21f, 0.44f, 0.22f,
                        bottleColor, renderers, colors);
                    AddCylinder(root, "Shoulder", 0.17f, 0.07f, 0.475f,
                        Shade(bottleColor, 0.96f), renderers, colors);
                    AddCylinder(root, "Long Neck", 0.083f, 0.20f, 0.61f,
                        Shade(bottleColor, 1.03f), renderers, colors);
                    AddCylinder(root, "Crown Cap", 0.098f, 0.035f, 0.7025f,
                        new Color(0.57f, 0.48f, 0.31f), renderers, colors);
                    break;
                case BarDrinkBottleStyle.WineBottle:
                    dimensions = new BottleDimensions(0.23f, 0.23f, 0.82f);
                    AddCylinder(root, "Body", 0.23f, 0.50f, 0.25f,
                        bottleColor, renderers, colors);
                    AddCylinder(root, "Shoulder", 0.18f, 0.07f, 0.535f,
                        Shade(bottleColor, 0.97f), renderers, colors);
                    AddCylinder(root, "Neck", 0.08f, 0.23f, 0.685f,
                        Shade(bottleColor, 1.04f), renderers, colors);
                    AddCylinder(root, "Cork", 0.088f, 0.035f, 0.8025f,
                        new Color(0.48f, 0.30f, 0.16f), renderers, colors);
                    break;
                case BarDrinkBottleStyle.VodkaBottle:
                    dimensions = new BottleDimensions(0.25f, 0.18f, 0.67f);
                    AddBox(root, "Square Body", new Vector3(0.25f, 0.44f, 0.18f),
                        0.22f, bottleColor, renderers, colors);
                    AddBox(root, "Square Shoulder", new Vector3(0.21f, 0.07f, 0.16f),
                        0.475f, Shade(bottleColor, 0.96f), renderers, colors);
                    AddCylinder(root, "Neck", 0.105f, 0.14f, 0.58f,
                        Shade(bottleColor, 1.04f), renderers, colors);
                    AddCylinder(root, "Metal Cap", 0.12f, 0.05f, 0.645f,
                        new Color(0.52f, 0.58f, 0.57f), renderers, colors);
                    break;
                case BarDrinkBottleStyle.CognacBottle:
                    dimensions = new BottleDimensions(0.30f, 0.24f, 0.61f);
                    AddCylinder(root, "Broad Body", 0.30f, 0.36f, 0.18f,
                        bottleColor, renderers, colors, 0.80f);
                    AddCylinder(root, "Shoulder", 0.24f, 0.08f, 0.40f,
                        Shade(bottleColor, 0.97f), renderers, colors, 0.84f);
                    AddCylinder(root, "Neck", 0.10f, 0.13f, 0.505f,
                        Shade(bottleColor, 1.04f), renderers, colors);
                    AddCylinder(root, "Stopper", 0.125f, 0.04f, 0.59f,
                        new Color(0.38f, 0.19f, 0.08f), renderers, colors);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(presentation),
                        presentation.BottleStyle,
                        "Unsupported bottle style.");
            }

            float bodyMidpoint = Mathf.Min(
                dimensions.TotalHeight * 0.34f,
                0.25f);
            AddBox(
                root,
                "Front Label",
                new Vector3(
                    dimensions.Width * 0.76f,
                    Mathf.Min(0.18f, dimensions.TotalHeight * 0.27f),
                    0.018f),
                bodyMidpoint,
                labelColor,
                renderers,
                colors,
                -dimensions.Depth * 0.5f - 0.011f);
            AddBox(
                root,
                "Label Band",
                new Vector3(
                    dimensions.Width * 0.52f,
                    0.026f,
                    0.021f),
                bodyMidpoint + 0.035f,
                Shade(labelColor, 0.45f),
                renderers,
                colors,
                -dimensions.Depth * 0.5f - 0.013f);
            return dimensions;
        }

        private static BarDrinkVesselView BuildVessel(
            Transform parent,
            BarDrinkVesselKind kind)
        {
            var vesselObject = new GameObject($"Bar Drink Vessel {kind}");
            vesselObject.transform.SetParent(parent, false);

            GameObject glass = CreateMeshObject(
                "Glass",
                vesselObject.transform,
                BarDrinkServiceMeshLibrary.GetGlassMesh(kind),
                BarDrinkServiceResources.GlassMaterial,
                GlassColor);
            SetTransparentRenderer(glass.GetComponent<Renderer>());

            var liquidObject = new GameObject("Liquid Fill");
            liquidObject.transform.SetParent(vesselObject.transform, false);
            liquidObject.transform.localPosition = Vector3.up *
                BarDrinkServiceMeshLibrary.GetLiquidBaseHeight(kind);
            GameObject liquid = CreateMeshObject(
                "Liquid Volume",
                liquidObject.transform,
                BarDrinkServiceMeshLibrary.GetLiquidMesh(kind),
                BarDrinkServiceResources.LiquidMaterial,
                Color.white);
            Renderer liquidRenderer = liquid.GetComponent<Renderer>();
            SetTransparentRenderer(liquidRenderer);

            var targetObject = new GameObject("Pour Target");
            targetObject.transform.SetParent(vesselObject.transform, false);
            targetObject.transform.localPosition = Vector3.up *
                (BarDrinkServiceMeshLibrary.GetVesselHeight(kind) - 0.025f);

            BarDrinkVesselView view =
                vesselObject.AddComponent<BarDrinkVesselView>();
            view.Initialize(
                kind,
                glass.GetComponent<Renderer>(),
                liquidObject.transform,
                liquidRenderer,
                targetObject.transform);
            return view;
        }

        private static void BuildServiceStool(
            Transform parent,
            BarDrinkServicePlan plan)
        {
            var stoolObject = new GameObject("Bar Drink Service Stool");
            stoolObject.transform.SetParent(parent, false);
            stoolObject.transform.localPosition = plan.ServiceStoolPosition;
            Transform stool = stoolObject.transform;
            RuntimePrimitiveFactory.CreateCylinder(
                "Stool Floor Plate",
                stool,
                Vector3.up * 0.035f,
                new Vector3(0.42f, 0.035f, 0.42f),
                DarkWoodColor,
                false);
            RuntimePrimitiveFactory.CreateCylinder(
                "Stool Stem",
                stool,
                Vector3.up * 0.43f,
                new Vector3(0.105f, 0.39f, 0.105f),
                BrassColor,
                false);
            RuntimePrimitiveFactory.CreateCylinder(
                "Stool Seat",
                stool,
                Vector3.up * 0.86f,
                new Vector3(0.48f, 0.075f, 0.48f),
                LeatherColor,
                false);
            RuntimePrimitiveFactory.CreateCylinder(
                "Stool Foot Ring",
                stool,
                Vector3.up * 0.30f,
                new Vector3(0.52f, 0.025f, 0.52f),
                BrassColor,
                false);
        }

        private static void BuildMenu(
            Transform parent,
            BarDrinkServicePlan plan)
        {
            var menuObject = new GameObject("Physical Bar Drink Menu");
            menuObject.transform.SetParent(parent, false);
            menuObject.transform.localPosition = plan.MenuPose.Position;
            menuObject.transform.localRotation = plan.MenuPose.Rotation;
            Transform menu = menuObject.transform;
            RuntimePrimitiveFactory.CreateBox(
                "Menu Board",
                menu,
                Vector3.up * 0.018f,
                new Vector3(0.42f, 0.036f, 0.29f),
                DarkWoodColor,
                false);
            for (int index = 0; index < 4; index++)
            {
                RuntimePrimitiveFactory.CreateBox(
                    $"Menu Line {index + 1}",
                    menu,
                    new Vector3(
                        -0.025f + (index % 2) * 0.055f,
                        0.039f,
                        -0.09f + index * 0.055f),
                    new Vector3(
                        index % 2 == 0 ? 0.28f : 0.21f,
                        0.009f,
                        0.018f),
                    index == 0 ? BrassColor : new Color(0.74f, 0.65f, 0.45f),
                    false);
            }
        }

        private static GameObject CreateMeshObject(
            string name,
            Transform parent,
            Mesh mesh,
            Material material,
            Color color)
        {
            var result = new GameObject(name);
            result.transform.SetParent(parent, false);
            result.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = result.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            RuntimePrimitiveFactory.SetColor(renderer, color);
            return result;
        }

        private static void AddCylinder(
            Transform root,
            string name,
            float diameter,
            float height,
            float centerY,
            Color color,
            ICollection<Renderer> renderers,
            ICollection<Color> colors,
            float depthScale = 1f)
        {
            GameObject part = RuntimePrimitiveFactory.CreateCylinder(
                name,
                root,
                Vector3.up * centerY,
                new Vector3(
                    diameter,
                    height * 0.5f,
                    diameter * depthScale),
                color,
                false);
            Register(part, color, renderers, colors);
        }

        private static void AddBox(
            Transform root,
            string name,
            Vector3 size,
            float centerY,
            Color color,
            ICollection<Renderer> renderers,
            ICollection<Color> colors,
            float centerZ = 0f)
        {
            GameObject part = RuntimePrimitiveFactory.CreateBox(
                name,
                root,
                new Vector3(0f, centerY, centerZ),
                size,
                color,
                false);
            Register(part, color, renderers, colors);
        }

        private static void Register(
            GameObject part,
            Color color,
            ICollection<Renderer> renderers,
            ICollection<Color> colors)
        {
            Renderer renderer = part.GetComponent<Renderer>();
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderers.Add(renderer);
            colors.Add(color);
        }

        private static void SetTransparentRenderer(Renderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static Color Shade(Color color, float multiplier)
        {
            return new Color(
                color.r * multiplier,
                color.g * multiplier,
                color.b * multiplier,
                color.a);
        }

        private readonly struct BottleDimensions
        {
            public BottleDimensions(float width, float depth, float totalHeight)
            {
                Width = width;
                Depth = depth;
                TotalHeight = totalHeight;
            }

            public float Width { get; }
            public float Depth { get; }
            public float TotalHeight { get; }
        }
    }
}

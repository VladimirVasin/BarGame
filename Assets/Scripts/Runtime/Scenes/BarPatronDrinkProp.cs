using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    public enum BarPatronDrinkPropKind
    {
        Bottle,
        Pint,
        WineGlass,
        ShotGlass,
        Snifter
    }

    /// <summary>
    /// One visit-owned authored drink. Offsets and bounds use rotation-only
    /// root space in physical metres, including the visual child's scale.
    /// Resolve a point as rootPosition + rootRotation * localPoint.
    /// </summary>
    public sealed class BarPatronDrinkProp
    {
        internal BarPatronDrinkProp(
            Transform root,
            Transform mouth,
            BarDrinkVesselView vessel,
            BarPatronDrinkPropKind kind,
            DrinkId drinkId,
            Vector3 gripLocalPosition,
            Vector3 rimLocalPosition,
            Bounds physicalLocalBounds)
        {
            Root = root != null ? root : throw new ArgumentNullException(nameof(root));
            Mouth = mouth != null ? mouth : throw new ArgumentNullException(nameof(mouth));
            Vessel = vessel;
            Kind = kind;
            DrinkId = drinkId;
            GripLocalPosition = gripLocalPosition;
            RimLocalPosition = rimLocalPosition;
            PhysicalLocalBounds = physicalLocalBounds;
            BaseLocalPosition = new Vector3(0f, physicalLocalBounds.min.y, 0f);
            OpeningLocalAxis = Vector3.up;
            HandleLocalAxis = IsBottle ? Vector3.forward : Vector3.right;
            GripKind = NpcHandPose.GripKindForVessel(ToVesselKind(kind));

            switch (kind)
            {
                case BarPatronDrinkPropKind.Bottle:
                    MaxSipTiltDegrees = 90f;
                    SipTorsoLeanBackDegrees = 32f;
                    SipHeadLeanBackDegrees = 19f;
                    break;
                case BarPatronDrinkPropKind.Pint:
                    MaxSipTiltDegrees = 60f;
                    SipTorsoLeanBackDegrees = 16f;
                    SipHeadLeanBackDegrees = 10f;
                    break;
                case BarPatronDrinkPropKind.WineGlass:
                    MaxSipTiltDegrees = 55f;
                    SipTorsoLeanBackDegrees = 10f;
                    SipHeadLeanBackDegrees = 6f;
                    break;
                case BarPatronDrinkPropKind.ShotGlass:
                    MaxSipTiltDegrees = 90f;
                    SipTorsoLeanBackDegrees = 28f;
                    SipHeadLeanBackDegrees = 17f;
                    break;
                case BarPatronDrinkPropKind.Snifter:
                    MaxSipTiltDegrees = 50f;
                    SipTorsoLeanBackDegrees = 12f;
                    SipHeadLeanBackDegrees = 7f;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        public Transform Root { get; }
        public Transform Mouth { get; }
        public BarDrinkVesselView Vessel { get; }
        public BarPatronDrinkPropKind Kind { get; }
        public DrinkId DrinkId { get; }
        public NpcHandGripKind GripKind { get; }
        public Vector3 GripLocalPosition { get; }
        public Vector3 RimLocalPosition { get; }
        public Vector3 BaseLocalPosition { get; }
        public Bounds PhysicalLocalBounds { get; }
        public Vector3 OpeningLocalAxis { get; }
        public Vector3 HandleLocalAxis { get; }
        public float MaxSipTiltDegrees { get; }
        public float SipTorsoLeanBackDegrees { get; }
        public float SipHeadLeanBackDegrees { get; }
        public bool IsBottle => Kind == BarPatronDrinkPropKind.Bottle;
        public float GripToLipDistance => Vector3.Distance(GripLocalPosition, RimLocalPosition);
        public float GripToBaseDistance => Vector3.Distance(GripLocalPosition, BaseLocalPosition);

        public static BarPatronDrinkProp Create(
            Transform parent,
            BarPatronDrinkPropKind kind,
            BarDrinkPresentation drink)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            BarDrinkVesselKind vesselKind = ToVesselKind(kind);
            Transform root = new GameObject($"Patron Drink {kind}").transform;
            root.SetParent(parent, false);
            try
            {
                BarDrinkVesselView vessel = null;
                Transform mouth;
                Vector3 grip;
                if (kind == BarPatronDrinkPropKind.Bottle)
                {
                    root.name = $"Patron Bottle {drink.StableId}";
                    Transform rig = new GameObject("Bottle Rig").transform;
                    rig.SetParent(root, false);
                    rig.localScale = Vector3.one * BarDrinkServiceView.CarriedBottleScale;
                    float height = BarDrinkServiceWorldBuilder.BuildBottleVisual(rig, drink);
                    float gripHeightShare = BarDrinkServiceView.ResolveBottleGripHeightShare(drink.BottleStyle);
                    rig.localPosition = Vector3.down *
                        (height * BarDrinkServiceView.CarriedBottleScale * gripHeightShare);
                    mouth = new GameObject("Bottle Mouth Anchor").transform;
                    mouth.SetParent(rig, false);
                    mouth.localPosition = Vector3.up * height;
                    grip = Vector3.zero;
                }
                else
                {
                    vessel = BarDrinkServiceWorldBuilder.BuildVessel(root, vesselKind);
                    vessel.ConfigureLiquid(drink.LiquidColor, drink.TargetFill);
                    vessel.SetFillProgress(1f);
                    vessel.SetInteractionHighlight(false);
                    mouth = vessel.DrinkRimAnchor;
                    grip = InRotationSpace(root, vessel.GripWorldPosition);
                }

                return new BarPatronDrinkProp(root, mouth, vessel, kind, drink.DrinkId, grip,
                    InRotationSpace(root, mouth.position), MeasureBounds(root,
                        vessel != null ? vessel.InteractionHighlightRenderer : null));
            }
            catch
            {
                Object.DestroyImmediate(root.gameObject);
                throw;
            }
        }

        private static BarDrinkVesselKind ToVesselKind(BarPatronDrinkPropKind kind)
        {
            switch (kind)
            {
                case BarPatronDrinkPropKind.Bottle: return BarDrinkVesselKind.None;
                case BarPatronDrinkPropKind.Pint: return BarDrinkVesselKind.Pint;
                case BarPatronDrinkPropKind.WineGlass: return BarDrinkVesselKind.WineGlass;
                case BarPatronDrinkPropKind.ShotGlass: return BarDrinkVesselKind.ShotGlass;
                case BarPatronDrinkPropKind.Snifter: return BarDrinkVesselKind.Snifter;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private static Vector3 InRotationSpace(Transform root, Vector3 worldPoint) =>
            Quaternion.Inverse(root.rotation) * (worldPoint - root.position);

        private static Bounds MeasureBounds(Transform root, Renderer highlight)
        {
            Bounds result = default;
            bool found = false;
            Quaternion inverseRotation = Quaternion.Inverse(root.rotation);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == highlight) continue;
                Bounds bounds = renderer.localBounds;
                Matrix4x4 toWorld = renderer.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 signs = new Vector3((corner & 1) == 0 ? -1f : 1f,
                        (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                    Vector3 worldPoint = toWorld.MultiplyPoint3x4(
                        bounds.center + Vector3.Scale(bounds.extents, signs));
                    Vector3 point = inverseRotation * (worldPoint - root.position);
                    if (!found) { result = new Bounds(point, Vector3.zero); found = true; }
                    else result.Encapsulate(point);
                }
            }

            if (!found) throw new InvalidOperationException("An authored patron drink has no visible bounds.");
            return result;
        }
    }
}

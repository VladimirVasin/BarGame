using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Composes the authored Nighthawks-inspired cafe with plan-owned
    /// collision, causal sound, realtime light and the bespoke service cast.
    /// The imported FBX remains passive presentation data.
    /// </summary>
    public static class MountainRoadCafeWorldBuilder
    {
        public const string EntranceAnchorId =
            "terminal-cafe-entrance";
        public const string CounterAnchorId =
            "terminal-cafe-counter";
        public const string GlassAnchorId =
            "terminal-cafe-glass";
        public const string LonePatronAnchorId =
            "terminal-cafe-npc-lone";
        public const string PairFirstAnchorId =
            "terminal-cafe-npc-pair-a";
        public const string PairSecondAnchorId =
            "terminal-cafe-npc-pair-b";
        public const string AttendantAnchorId =
            "terminal-cafe-npc-attendant";

        public const int MaximumRealtimeLights = 3;
        public const int TableauNpcCount = 4;

        // Published for the shared terminal seat planner. These values are
        // also authored into HeroSeat and Stool.01 in the deterministic FBX.
        public static readonly float[] StoolRightOffsets =
        {
            -1.50f,
            -0.38f,
            0.75f,
            1.80f,
            3.00f
        };

        public const int EmptyStoolIndex = 1;
        public const float StoolForward = -2.18f;
        public const float StoolSeatTopAboveFloor = 0.8175f;
        public const float StoolSeatThickness = 0.055f;
        public const float StoolSeatDiameter = 0.48f;

        public static MountainRoadCafeWorldResult Build(
            Transform parent,
            MountainRoadCafePlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            ValidatePlan(plan);

            var root = new GameObject("Nighthawks Mountain Cafe");
            root.transform.SetParent(parent, false);
            Transform physicalRoot = CreateRoot(root.transform, "Physical Cafe");
            Transform dressingRoot = CreateRoot(root.transform, "Cafe Dressing");
            Transform npcRoot = CreateRoot(root.transform, "Silent Cafe Tableau");
            Transform lightingRoot = CreateRoot(
                root.transform,
                "Always-On Cafe Lighting");

            var semanticAnchors = new Dictionary<string, Transform>(
                StringComparer.Ordinal)
            {
                { plan.StableId, root.transform }
            };

            MountainRoadCafeAssetRegistry model =
                MountainRoadCafeModelResources.Instantiate(
                    dressingRoot,
                    plan);
            BindModelSemantics(model, semanticAnchors);

            MountainRoadCafeCollisionWorldResult collision =
                MountainRoadCafeCollisionWorldBuilder.Build(
                    physicalRoot,
                    plan);
            MountainRoadCafeCastController cast =
                MountainRoadCafeCastFactory.Create(
                    npcRoot,
                    MountainRoadCafeCastPlan.Create(plan),
                    semanticAnchors,
                    StableSeed(plan.StableId));
            if (cast == null)
            {
                throw new InvalidOperationException(
                    "The authored mountain cafe cast is unavailable.");
            }

            MountainRoadCafeServicePresentation service =
                MountainRoadCafeServicePresentation.CreateAndBind(
                    model,
                    cast);
            List<Light> lights = BuildLights(
                plan,
                model,
                lightingRoot);
            MountainRoadCafeSoundscape soundscape =
                MountainRoadCafeSoundscape.Create(
                    root.transform,
                    semanticAnchors,
                    StableSeed(plan.StableId));

            return new MountainRoadCafeWorldResult(
                plan,
                root,
                physicalRoot.gameObject,
                dressingRoot.gameObject,
                npcRoot.gameObject,
                lightingRoot.gameObject,
                soundscape,
                lights,
                semanticAnchors,
                cast,
                model,
                collision,
                service);
        }

        private static void ValidatePlan(MountainRoadCafePlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (string.IsNullOrWhiteSpace(plan.StableId) ||
                plan.FootprintXZ == null ||
                plan.FootprintXZ.Count != 5 ||
                plan.Height < 4f ||
                plan.DoorWidth < 1.59f)
            {
                throw new ArgumentException(
                    "The mountain cafe requires its stable five-sided, " +
                    "4 metre high, 1.6 metre entrance plan.",
                    nameof(plan));
            }
        }

        private static Transform CreateRoot(
            Transform parent,
            string name)
        {
            var child = new GameObject(name);
            child.layer = parent.gameObject.layer;
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static void BindModelSemantics(
            MountainRoadCafeAssetRegistry model,
            IDictionary<string, Transform> semantics)
        {
            AddAnchor(model, semantics, EntranceAnchorId, "DoorThreshold");
            AddAnchor(model, semantics, CounterAnchorId, "CounterCorner");
            AddAnchor(model, semantics, GlassAnchorId, "GlassCorner");
            AddAnchor(
                model,
                semantics,
                MountainRoadCafeSoundscape.RefrigeratorAnchorId,
                "Audio.Fridge");
            AddAnchor(
                model,
                semantics,
                MountainRoadCafeSoundscape.FixtureAnchorId,
                "Audio.Fixture");
            AddAnchor(
                model,
                semantics,
                MountainRoadCafeSoundscape.BoilerAnchorId,
                "Audio.Boiler");
        }

        private static void AddAnchor(
            MountainRoadCafeAssetRegistry model,
            IDictionary<string, Transform> semantics,
            string stableId,
            string authoredName)
        {
            if (!model.TryGetAnchor(authoredName, out Transform anchor) ||
                anchor == null)
            {
                throw new InvalidOperationException(
                    $"Cafe model has no required '{authoredName}' anchor.");
            }

            semantics.Add(stableId, anchor);
        }

        private static List<Light> BuildLights(
            MountainRoadCafePlan plan,
            MountainRoadCafeAssetRegistry model,
            Transform parent)
        {
            Transform warm = RequireAnchor(model, "Light.WarmCounter");
            Transform cold = RequireAnchor(model, "Light.ColdService");
            Transform wash = RequireAnchor(model, "Light.ExteriorWash");

            // The fixtures are authored data; these are the points their
            // cones have to serve. The warm practical reads the sleeping-head
            // contact frame. The cold practical physically sits above the
            // stove, so its angular bisector must cover both the task surface
            // straight below and the complete four-person counter tableau.
            // This keeps one causal visible source instead of adding a fourth
            // Light or leaving a glowing fixture whose beam points elsewhere.
            // Both interior keys stay shadowless: the sleeper's folded arms
            // otherwise occlude almost his complete face in the contact pose.
            Vector3 lonePatronReadingPoint =
                plan.Center -
                plan.Right * 1.50f -
                plan.Forward * 2.18f +
                Vector3.up * 1.13f;
            Vector3 castFillPoint =
                plan.Center +
                plan.Right * 0.76f -
                plan.Forward * 1.68f +
                Vector3.up * 1.25f;
            Vector3 coldDirection = (
                Vector3.down +
                (castFillPoint - cold.position).normalized).normalized;

            // The invisible sulphur wash now fills the same counter band as
            // well as the apron. Its slight forward bias is deliberate: a
            // broader vertical cone would also catch the raised terrace and
            // erase the summit's black back edge.
            Vector3 washDirection = (
                plan.Right * 0.04f -
                Vector3.up -
                plan.Forward * 0.19f).normalized;

            return new List<Light>(MaximumRealtimeLights)
            {
                CreateSpotLight(
                    "Sulphur Counter Light",
                    parent,
                    warm.position,
                    (lonePatronReadingPoint - warm.position).normalized,
                    new Color(1f, 0.72f, 0.32f),
                    60f,
                    11f,
                    110f,
                    42f,
                    false),
                CreateSpotLight(
                    "Cold Service Light",
                    parent,
                    cold.position,
                    coldDirection,
                    new Color(0.46f, 0.77f, 0.71f),
                    53f,
                    14f,
                    110f,
                    100f,
                    false),
                CreateSpotLight(
                    "Sulphur Facade Wash",
                    parent,
                    wash.position,
                    washDirection,
                    new Color(1f, 0.78f, 0.45f),
                    8.5f,
                    20f,
                    128f,
                    122f,
                    false)
            };
        }

        private static Transform RequireAnchor(
            MountainRoadCafeAssetRegistry model,
            string name)
        {
            if (!model.TryGetAnchor(name, out Transform anchor) ||
                anchor == null)
            {
                throw new InvalidOperationException(
                    $"Cafe model has no required '{name}' anchor.");
            }

            return anchor;
        }

        private static Light CreateSpotLight(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 direction,
            Color color,
            float intensity,
            float range,
            float spotAngle,
            float innerSpotAngle,
            bool shadows)
        {
            var lightObject = new GameObject(name);
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.position = position;
            lightObject.transform.rotation = Quaternion.LookRotation(
                direction,
                Vector3.forward);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.spotAngle = spotAngle;
            light.innerSpotAngle = innerSpotAngle;
            light.shadows = shadows
                ? LightShadows.Hard
                : LightShadows.None;
            light.shadowStrength = shadows ? 0.66f : 0f;
            light.shadowBias = 0.04f;
            light.shadowNormalBias = 0.25f;
            light.renderMode = LightRenderMode.ForcePixel;
            light.bounceIntensity = shadows ? 0.08f : 0f;
            light.enabled = true;
            return light;
        }

        private static int StableSeed(string stableId)
        {
            unchecked
            {
                int hash = 17;
                string id = stableId ?? string.Empty;
                for (int index = 0; index < id.Length; index++)
                {
                    hash = hash * 31 + id[index];
                }

                return hash;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BarPromenade
{
    /// <summary>
    /// Places the bar interior.
    ///
    /// This used to compose the room out of `89` `RuntimePrimitiveFactory`
    /// boxes and cylinders. It is now a placer: the geometry is one
    /// authored model built by `tools/build-bar-3d-model.py`, and what
    /// remains here is everything a passive model cannot carry - the
    /// district tint, the collision, the interactive jukebox, the turning
    /// fan, and one pendant instanced per light anchor.
    ///
    /// Two placement properties are deliberate, because interaction and
    /// collision code depend on them:
    ///
    /// * every part is a DIRECT child of the room under its authored
    ///   semantic name. The model's
    ///   own hierarchy is flattened away after the tints are applied.
    /// * collision is authored, not taken from the meshes. The model
    ///   declares a box per collider in its manifest, so traversal remains
    ///   data-owned while visible geometry is free to be re-cut.
    /// </summary>
    public static class BarInteriorWorldBuilder
    {
        private const string DistrictDressName = "District Identity";
        private const string CeilingFanName = "Slow Ceiling Fan";
        private const string JukeboxName = "Bar Jukebox";
        private const string PracticalGroup = "prefab:Practical";

        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId =
            Shader.PropertyToID("_Color");

        //  Reparenting KEEPS the world transform, and that is load
        //  bearing. An imported FBX carries its unit factor on the
        //  authoring root - a hundred - and stores the vertices at a
        //  hundredth of the metres they were authored in. Lifting a part
        //  out of that root with `worldPositionStays: false` drops the
        //  factor, and the entire room silently becomes a hundredth of
        //  its size - with correct anchors and correct collision, because
        //  neither comes from the meshes. `BarAssetSetup` measures the
        //  imported model against the manifest, and
        //  `BarModelContractTests` measures the PLACED room, because the
        //  prefab can be right while the placer is wrong.
        private const bool KeepWorld = true;

        public static Transform Build(
            Transform parent,
            BarInteriorLayoutPlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            Transform room = new GameObject(
                $"Interior {plan.BarId}").transform;
            room.SetParent(parent, false);

            GameObject prefab = BarModelResources.LoadInteriorPrefab();
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "The bar interior model is missing. Run " +
                    "tools/build-bar-3d-model.py through Blender, then " +
                    "Bar Promenade/Bar/Build Runtime Prefabs.");
            }

            GameObject instance = Object.Instantiate(prefab, room);
            instance.name = "Model";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            BarAssetRegistry registry =
                instance.GetComponent<BarAssetRegistry>();
            if (registry == null)
            {
                throw new InvalidOperationException(
                    "The bar interior prefab has no BarAssetRegistry.");
            }

            //  Read while the model's hierarchy is still intact, and
            //  passed down rather than parked in a static field: this
            //  builder holds no state between calls.
            Vector3 fanPivot = AnchorPosition(
                registry, room, "ceiling_fan_pivot",
                new Vector3(0f, 4.35f, 0.75f));
            Vector3 jukeboxPivot = AnchorPosition(
                registry, room, "jukebox_pivot",
                new Vector3(6.4f, 0f, -6.78f));

            PreserveAuthoredAnchors(room, registry);
            ApplySurfaces(registry, plan);
            BuildPracticals(room, registry, plan);
            Organise(room, registry, plan, fanPivot, jukeboxPivot);

            //  After `Organise`, so the sets it destroyed leave no
            //  collision behind, and so the boxes hang off the room
            //  rather than off a model part.
            AddColliders(room, registry);
            Object.DestroyImmediate(instance);
            return room;
        }

        /// <summary>
        /// Keeps Blender's semantic positions after the imported wrapper is
        /// flattened and destroyed. Each marker is a direct, unit-scale child
        /// named exactly as the manifest anchor (`HeroSeat`, `MenuDock`, ...),
        /// so interaction code never has to interpret FBX scale or axis data.
        /// </summary>
        private static void PreserveAuthoredAnchors(
            Transform room,
            BarAssetRegistry registry)
        {
            foreach (BarAnchorBinding binding in registry.Anchors)
            {
                if (binding == null || binding.Anchor == null ||
                    string.IsNullOrWhiteSpace(binding.AnchorName))
                {
                    continue;
                }

                var marker = new GameObject(binding.AnchorName);
                marker.transform.SetParent(room, false);
                marker.transform.localPosition =
                    room.InverseTransformPoint(binding.Anchor.position);
                marker.transform.localRotation =
                    Quaternion.Inverse(room.rotation) *
                    binding.Anchor.rotation;
            }
        }

        // -------------------------------------------------- surfaces --

        /// <summary>
        /// Gives every part its district tint and authored surface sheet.
        ///
        /// The same property-block path `BarSurfaceAppearance.ApplyAuthored`
        /// takes for a primitive, so a district tint is still a
        /// `_BaseColor` and not something baked into an asset. That is
        /// the whole reason the model imports with
        /// `materialImportMode = None`.
        /// </summary>
        private static void ApplySurfaces(
            BarAssetRegistry registry,
            BarInteriorLayoutPlan plan)
        {
            BarDistrictIdentity identity = plan.DistrictIdentity;
            var properties = new MaterialPropertyBlock();

            foreach (BarPartBinding binding in registry.Parts)
            {
                Renderer renderer = binding?.Renderer;
                if (renderer == null)
                {
                    continue;
                }

                if (TryApplyCafeStoolSurface(binding))
                {
                    continue;
                }

                ApplyTint(
                    renderer,
                    properties,
                    binding.Tint.Resolve(identity),
                    binding.Sheet,
                    !binding.Emissive);
            }
        }

        private static bool TryApplyCafeStoolSurface(
            BarPartBinding binding)
        {
            switch (binding.Role)
            {
                case "stool_leg":
                case "hero_stool_legs":
                case "hero_stool_footring":
                    MountainRoadCafeSurfaceAppearance.Apply(
                        binding.Renderer,
                        MountainRoadCafeSurfaceKind.MetalDetail);
                    return true;
                case "stool_seat":
                case "hero_stool_seat":
                    MountainRoadCafeSurfaceAppearance.Apply(
                        binding.Renderer,
                        MountainRoadCafeSurfaceKind.CounterDetail);
                    return true;
                default:
                    return false;
            }
        }

        private static void ApplyTint(
            Renderer renderer,
            MaterialPropertyBlock properties,
            Color tint,
            string sheet,
            bool sheeted)
        {
            properties.Clear();
            if (sheeted &&
                BarSurfaceAppearance.TryResolveSheet(
                    sheet,
                    out BarSurfaceKind kind))
            {
                BarSurfaceAppearance.ApplyAuthored(renderer, kind, tint);
                return;
            }

            properties.SetColor(BaseColorId, tint);
            properties.SetColor(ColorId, tint);
            renderer.SetPropertyBlock(properties);
        }

        // ------------------------------------------------- collision --

        /// <summary>
        /// Puts the authored collision boxes into the room.
        ///
        /// Each box gets its OWN child of the room, never a component on
        /// the model part it describes. The manifest writes collision in
        /// room-space metres - the very numbers the primitives carried -
        /// while a part's transform carries the FBX unit factor of a
        /// hundred and the Blender-to-Unity axis conversion of ninety
        /// degrees about X. `BoxCollider.center` and `size` are read in
        /// that local space, so hanging them on the part turned the
        /// floor into a 2200x1600x24 m slab tipped on its side and sunk
        /// twelve metres: the room had no ground, the hero fell through
        /// it forever, and the chase camera - whose probe now started
        /// inside that slab, so `SphereCast` reported a distance of zero
        /// - collapsed onto his head. A child of the room is unrotated
        /// and unit-scaled, so the numbers mean what they say.
        /// `WireJukebox` already places its box this way.
        ///
        /// A box, never a mesh collider: the model's geometry is
        /// chamfered and tapered now, and a mesh collider would make the
        /// room's traversal depend on how the art was cut. The boxes are
        /// the ones the primitives carried.
        /// </summary>
        private static void AddColliders(
            Transform room,
            BarAssetRegistry registry)
        {
            foreach (BarPartBinding binding in registry.Parts)
            {
                Renderer renderer = binding?.Renderer;
                if (renderer == null || binding.Colliders.Count == 0)
                {
                    continue;
                }

                string partName = renderer.gameObject.name;
                for (int index = 0; index < binding.Colliders.Count; index++)
                {
                    BarColliderSpec spec = binding.Colliders[index];
                    var holder = new GameObject(
                        binding.Colliders.Count == 1
                            ? $"{partName} Collision"
                            : $"{partName} Collision {index + 1}");
                    holder.transform.SetParent(room, false);
                    holder.transform.localPosition = spec.Center;
                    BoxCollider collider =
                        holder.AddComponent<BoxCollider>();
                    collider.size = spec.Size;
                }
            }
        }

        // -------------------------------------------------- variants --

        /// <summary>
        /// Sorts the model's parts into the room.
        ///
        /// Grouping is DATA, not hierarchy: the model is one flat sheet of
        /// parts each labelled with the group it belongs to, and the
        /// containers the room needs are built here. That is deliberate -
        /// an empty exported to FBX carries a unit-scale factor back with
        /// it, and meshes parented to one arrive a hundred times too
        /// small. It is also simply the better split: which parts share a
        /// parent at runtime is the room's business, not the model's.
        ///
        /// Five things happen. Semantic anchors have already been copied
        /// directly under the room. Unselected activity sets and district
        /// dressings are destroyed. The surviving dressing is gathered
        /// under "District Identity", the name the room has always
        /// published it under. The fan and the jukebox are gathered under
        /// their pivots, which is what lets one turn and the other be
        /// interacted with. Everything else becomes a direct child of the
        /// room under its authored name, because interactions, audio, the
        /// drink service and several tests address parts by
        /// `room.Find(name)`.
        /// </summary>
        private static void Organise(
            Transform room,
            BarAssetRegistry registry,
            BarInteriorLayoutPlan plan,
            Vector3 fanPivot,
            Vector3 jukeboxPivot)
        {
            string keepActivity =
                BarAssetRegistry.ActivityGroupPrefix +
                NormalizeActivity(plan.Activity);
            string keepDistrict =
                BarAssetRegistry.DistrictGroupPrefix +
                plan.DistrictIdentity.Mood;

            Transform dress = null;
            Transform fan = null;
            Transform jukebox = null;

            foreach (BarPartBinding binding in registry.Parts)
            {
                Renderer renderer = binding?.Renderer;
                if (renderer == null)
                {
                    continue;
                }

                Transform part = renderer.transform;
                string group = binding.Group ?? string.Empty;

                if (group.StartsWith(
                        BarAssetRegistry.PrefabGroupPrefix,
                        StringComparison.Ordinal))
                {
                    //  A template, already cloned per light anchor.
                    Object.DestroyImmediate(part.gameObject);
                    continue;
                }

                if (group.StartsWith(
                        BarAssetRegistry.ActivityGroupPrefix,
                        StringComparison.Ordinal))
                {
                    if (!string.Equals(
                            group, keepActivity, StringComparison.Ordinal))
                    {
                        Object.DestroyImmediate(part.gameObject);
                        continue;
                    }

                    part.SetParent(room, KeepWorld);
                    continue;
                }

                if (group.StartsWith(
                        BarAssetRegistry.DistrictGroupPrefix,
                        StringComparison.Ordinal))
                {
                    if (!string.Equals(
                            group, keepDistrict, StringComparison.Ordinal))
                    {
                        Object.DestroyImmediate(part.gameObject);
                        continue;
                    }

                    dress = dress ?? NewContainer(room, DistrictDressName);
                    part.SetParent(dress, KeepWorld);
                    continue;
                }

                if (string.Equals(
                        group,
                        BarAssetRegistry.PivotGroupPrefix + CeilingFanName,
                        StringComparison.Ordinal))
                {
                    fan = fan ?? NewContainer(room, CeilingFanName);
                    part.SetParent(fan, KeepWorld);
                    continue;
                }

                if (string.Equals(
                        group,
                        BarAssetRegistry.PivotGroupPrefix + JukeboxName,
                        StringComparison.Ordinal))
                {
                    jukebox = jukebox ?? NewContainer(room, JukeboxName);
                    part.SetParent(jukebox, KeepWorld);
                    continue;
                }

                part.SetParent(room, KeepWorld);
            }

            if (fan != null)
            {
                fan.localPosition = fanPivot;
                fan.gameObject.AddComponent<BarCeilingFan>();
            }

            if (jukebox != null)
            {
                jukebox.localPosition = jukeboxPivot;
                jukebox.localRotation = Quaternion.Euler(0f, -90f, 0f);
                WireJukebox(jukebox);
            }
        }

        private static Transform NewContainer(Transform room, string name)
        {
            var container = new GameObject(name);
            container.transform.SetParent(room, false);
            return container.transform;
        }

        private static string NormalizeActivity(BarActivityKind activity)
        {
            //  `None` is not an authored set; the room falls back to the
            //  cocktail cart, exactly as the builder's switch did.
            return activity == BarActivityKind.None
                ? nameof(BarActivityKind.Cocktail)
                : activity.ToString();
        }

        // ------------------------------------------------ practicals --

        /// <summary>
        /// One pendant per light anchor, from a single authored template.
        ///
        /// Authored once rather than seven times so the layout plan stays
        /// the only place a light's position is written down. The cable
        /// is stretched to reach the ceiling from whatever height its
        /// anchor hangs at.
        /// </summary>
        private static void BuildPracticals(
            Transform room,
            BarAssetRegistry registry,
            BarInteriorLayoutPlan plan)
        {
            Transform cable = null;
            Transform shade = null;
            Transform bulb = null;
            foreach (BarPartBinding binding in registry.Parts)
            {
                if (binding?.Renderer == null ||
                    !string.Equals(
                        binding.Group,
                        PracticalGroup,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                switch (binding.Role)
                {
                    case "practical_cable":
                        cable = binding.Renderer.transform;
                        break;
                    case "practical_shade":
                        shade = binding.Renderer.transform;
                        break;
                    case "practical_bulb":
                        bulb = binding.Renderer.transform;
                        break;
                }
            }

            if (cable == null || shade == null || bulb == null)
            {
                throw new InvalidOperationException(
                    "The bar model's practical template is incomplete.");
            }

            //  Read while the template still hangs under the authoring
            //  root, because that is where its unit factor lives.
            Vector3 unit = shade.lossyScale;

            //  And which of the template's own axes points at the
            //  ceiling. It is not Unity's Y: the parts arrive carrying
            //  the Blender-to-Unity axis conversion, ninety degrees
            //  about X, so a lamp's height runs along its local Z.
            //  Derived from the template rather than written down here,
            //  because the importer's convention is not this file's to
            //  assume - and when it was assumed, every pendant in the
            //  bar hung sideways and the flex stretched thicker instead
            //  of longer.
            Vector3 localUp = cable.InverseTransformDirection(Vector3.up);

            BarDistrictIdentity identity = plan.DistrictIdentity;
            var properties = new MaterialPropertyBlock();
            for (int index = 0; index < plan.LightAnchors.Count; index++)
            {
                BarInteriorLightAnchor anchor = plan.LightAnchors[index];
                bool counterPendant =
                    anchor.Kind == BarInteriorLightKind.CounterPendant;
                Color bulbColor = counterPendant
                    ? identity.PendantColor
                    : anchor.Color;
                float cableHeight =
                    Mathf.Max(0.2f, plan.RoomHeight - anchor.Position.y);

                Transform placedCable = Clone(
                    cable, room, $"Practical Cable {index + 1}",
                    anchor.Position,
                    StretchAlong(unit, localUp, cableHeight));
                ApplyTint(
                    placedCable.GetComponent<Renderer>(),
                    properties,
                    identity.DarkWoodTint,
                    "PaintedMetal",
                    true);

                Transform placedShade = Clone(
                    shade, room, $"Practical Shade {index + 1}",
                    anchor.Position,
                    unit);
                ApplyTint(
                    placedShade.GetComponent<Renderer>(),
                    properties,
                    index % 2 == 0
                        ? identity.MetalTint
                        : identity.DarkWoodTint,
                    "PaintedMetal",
                    true);

                Transform placedBulb = Clone(
                    bulb, room, $"Practical Bulb {index + 1}",
                    anchor.Position,
                    unit);
                ApplyTint(
                    placedBulb.GetComponent<Renderer>(),
                    properties,
                    bulbColor * 2.2f,
                    string.Empty,
                    false);
            }
        }

        /// <summary>
        /// Scales <paramref name="unit"/> by <paramref name="length"/>
        /// along whichever local axis points at the ceiling, leaving the
        /// other two at the unit factor.
        ///
        /// Written component-wise off the measured axis rather than as
        /// `unit.y * length`, because the axis that reads as "up" here is
        /// the model's, not Unity's: with the wrong one the flex grew
        /// four centimetres thicker instead of a metre longer.
        /// </summary>
        private static Vector3 StretchAlong(
            Vector3 unit,
            Vector3 localUp,
            float length)
        {
            return new Vector3(
                unit.x * Mathf.Lerp(1f, length, Mathf.Abs(localUp.x)),
                unit.y * Mathf.Lerp(1f, length, Mathf.Abs(localUp.y)),
                unit.z * Mathf.Lerp(1f, length, Mathf.Abs(localUp.z)));
        }

        /// <summary>
        /// Copies one template part out of the model and into the room.
        ///
        /// The clone inherits the template's WORLD scale and its WORLD
        /// ROTATION, neither of them the identity. An imported FBX keeps
        /// its unit factor on the authoring root - a hundred - and writes
        /// the vertices at a hundredth of the metres they were authored
        /// in, so a part lifted out and set to unit scale is a hundredth
        /// of its size. A 0.58 m lampshade becomes six millimetres: still
        /// present, still correctly positioned, still the right colour,
        /// and invisible. The same root carries the Blender-to-Unity axis
        /// conversion, ninety degrees about X, so a part lifted out and
        /// set to the identity rotation lies on its side: every pendant
        /// in the bar hung horizontally, its shade a disc facing sideways
        /// and its flex pointing into the room instead of at the ceiling.
        /// </summary>
        private static Transform Clone(
            Transform source,
            Transform room,
            string name,
            Vector3 position,
            Vector3 scale)
        {
            GameObject copy = Object.Instantiate(source.gameObject, room);
            copy.name = name;
            copy.transform.localPosition = position;
            copy.transform.localRotation =
                Quaternion.Inverse(room.rotation) * source.rotation;
            copy.transform.localScale = scale;
            return copy.transform;
        }

        private static void WireJukebox(Transform jukebox)
        {
            BoxCollider solid = jukebox.gameObject.AddComponent<BoxCollider>();
            solid.center = new Vector3(0f, 0.85f, 0f);
            solid.size = new Vector3(0.62f, 1.75f, 0.98f);

            var trigger = new GameObject("Jukebox Trigger");
            trigger.transform.SetParent(jukebox, false);
            trigger.transform.localPosition = new Vector3(0.75f, 0.9f, 0f);
            BoxCollider triggerCollider =
                trigger.AddComponent<BoxCollider>();
            triggerCollider.isTrigger = true;
            triggerCollider.size = new Vector3(1.2f, 1.8f, 1.5f);

            Transform panel = jukebox.Find("Jukebox Glow Panel");
            if (panel == null)
            {
                throw new InvalidOperationException(
                    "The bar jukebox model has no glow panel to light.");
            }

            BarJukeboxInteraction interaction =
                trigger.AddComponent<BarJukeboxInteraction>();
            interaction.Initialize(
                panel.GetComponent<Renderer>(),
                new Color(1.35f, 0.78f, 0.30f, 1f));
        }

        /// <summary>
        /// Where an anchor sits in the ROOM's frame.
        ///
        /// Read through world space, never as `localPosition`. The
        /// anchor's parent is the imported authoring root, which carries
        /// the FBX unit factor of 100, so its local coordinates are a
        /// hundredth of the metres it was authored in: the jukebox anchor
        /// reads `(0.064, 0, -0.068)` and the jukebox lands in the middle
        /// of the floor instead of against the front wall. Only a
        /// rendered frame showed that.
        /// </summary>
        private static Vector3 AnchorPosition(
            BarAssetRegistry registry,
            Transform room,
            string role,
            Vector3 fallback)
        {
            return registry.TryGetAnchor(role, out Transform anchor)
                ? room.InverseTransformPoint(anchor.position)
                : fallback;
        }
    }

    [DisallowMultipleComponent]
    public sealed class BarCeilingFan : MonoBehaviour
    {
        [SerializeField] private float degreesPerSecond = 14f;

        private void Update()
        {
            transform.Rotate(
                Vector3.up,
                degreesPerSecond * Time.deltaTime,
                Space.Self);
        }
    }
}

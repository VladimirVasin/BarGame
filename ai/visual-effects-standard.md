# Visual effects standard

Accepted user decision `2026-10-21`: automatically review all sixteen effects
in future scene/world/art work. This permanent audit is neither all-on by
default nor a new approval gate.

## When and how to audit

Triggers: scene/prefab/model/material/shader/lighting/camera/environment
planning or changes, including generators/importers.
Read both world bibles and accepted architecture. Inspect actual assets,
enabled runtime owners, composition, settings and checks. Shader
capability, a preference key or a plan alone does not prove active coverage.

Use a compact working matrix:

| Effect | Actual coverage | Gap or reason not needed | Proposed action | Performance bound | Evidence |
| --- | --- | --- | --- | --- | --- |

Consider all sixteen, including absence and reasoned omissions. Project
statuses remain Current/Partial/Planned/Deferred; not-needed is applicability,
not a fifth status. Trivial work needs only a grouped summary, not sixteen
user-facing rows. Do not keep growing per-scene inventories duplicating code;
record lasting decisions and update existing documents when facts change.

## Six scene-shaping effects

1. **Global illumination:** reflected colour/energy, baking and object probes;
   distinguish baked GI/APV from finite source-derived bounce.
2. **SSAO:** contact and recess readability; control radius, strength and halos
   on faces, thin members, vegetation and moving geometry.
3. **Height/depth fog:** depth layers, visibility and shelters; preserve
   canonical haze and readable routes.
4. **Volumetric light/god rays:** existing sources, finite support and
   structural/depth/shadow occlusion; separate lit air from particles/glow.
5. **Shadows:** appropriate softness, stable cascades/range/resolution and bias;
   preserve accepted local hard, soft and shadowless exceptions.
6. **Colour grading/tonemapping:** palette, skin/shadow/highlight detail;
   compare actual materials without erasing district identity.

## Ten material, motion and accent effects

7. **Reflection probes:** receiver coverage/filtering, capture exclusions,
   invalidation, cube count/resolution and lifetime.
8. **Stylized water:** depth colour, Fresnel, shoreline foam, moving normals,
   sky/local reflection and shallow caustics, each assessed separately by water type.
9. **Roughness/smoothness and wetness:** variation/causal wear; preserve
   pigment, mean response and the area's wet/dry contract.
10. **Bevels/weighted normals:** edge highlights/face stability without
    needless geometry, softened silhouettes or changed contacts.
11. **Vertex colour gradients:** subtle material/vegetation variation; inspect
    actual COLOR/UV channels and shader consumption before assuming support.
12. **Wind and interaction:** shared weather, rooted vegetation, free branches,
    cloth and nearby body response; distinguish surface grass from real stems.
13. **Decals:** causal moisture, moss, wear, cracks and dirt; choose existing
    atlas/material masks or bounded geometry/projectors for the actual workload.
14. **Ambient particles:** dust, rain/snow, smoke, leaves and natural sources;
    assess pollen/fireflies by place.
15. **Rim lighting/Fresnel:** source-dependent silhouette readability; assess
    whether an independent glow would introduce a new style or story signal.
16. **Selective outlines:** current target and prompt/gaze/visibility rules,
    thin distance-aware presentation; distinguish interaction affordances from
    permanent hero/world outlines.

## Implementation boundaries

Reuse owners, global passes, material families, buffers and capture lifecycles.
Implement appropriate work within the authorized task without
routine confirmation; larger/unrelated changes remain proposals. The standing
audit does not authorize mass rollout or rendering rewrites. Canon deviations
follow existing accepted-decision/story-registry rules; no exception is granted here.

For requested rollout, cover all appropriate instances through shared type/area
profiles. A pilot is not project-wide completion. Bound active work spatially
and by distance, not permanently to one showcase group; explain exclusions.

Budget CPU/GPU, memory/GC and loading from design onward: bound populations,
passes, textures, probes, update/capture rates and nearby interaction sources.
Cache references/share assets/reuse buffers. Distance/culling may scale
presentation while time/input/collision/simulation remain correct. Preserve
pause/disable/dormancy/return/unload cleanup. Compare measured views before
claiming gains; label unmeasured costs.

GI/APV in runtime composition require matching bake geometry/data, stable
lightmap/probe bindings, shader sampling and bounded scenario/data loading.
Ambient fill, painted light and local bounce are not baked GI.
Reserve vertex channels; preserve relevant UVs, topology, anchors and asset
`.meta` files. Deformation must agree in forward, shadow, depth and normals,
plus any active GBuffer/motion/meta paths; expand bounds and retain rooted,
rig, camera and physical contact continuity.

Effects need ordinary material/weather/light causes; respect local darkness,
clean interiors, water, palette and narrative silence. Add no fresh clues,
supernatural signals or text as polish. Absence is not a canon prohibition;
identify the actual local rule before requiring an exception.

## Completion check

- All sixteen reviewed; actual coverage/gaps and scope verified.
- Shared ownership, budgets, vertex/visibility/contact and cleanup retained.
- Follow FAST: one focused primary check with reviewed capture for appearance
  changes, within the existing shared-framework allowance. Full suites/build/
  smoke need their existing AGENTS.md authorization.
- Briefly report assessment, changes, evidence, intentional omissions/larger
  proposals and unmeasured costs. Update facts; delete temporary captures.

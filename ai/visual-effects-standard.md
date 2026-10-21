# Visual effects standard

Accepted user decision `2026-10-21`: automatically review all three groups
(24 categories), including post-processing, for every scene change and future
world/art work without a user reminder. This permanent audit is neither all-on by
default nor a new approval gate.

## When and how to audit

Triggers: every scene change; prefab/model/material/shader/lighting/camera/
environment planning or changes, including generators/importers.
Read both bibles/accepted architecture; inspect live assets, owners,
composition, settings and focused checks. Shader support/preferences/plans
do not prove coverage.

Use a compact working matrix:

| Effect | Coverage | Gap/fit | Action | Cost/bound | Proof |
| --- | --- | --- | --- | --- | --- |

Assess each of the 24 categories: coverage, gaps, suitability and improvements. Use
project statuses from ai/README.md; applicability is separate. Trivial work
needs a grouped summary. Update lasting facts/decisions; avoid per-scene
inventories duplicating code.

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

## Eight post-processing categories

17. **Bloom:** justified bright sources/highlights; tune threshold/spread without
    washing out albedo, silhouettes or nearby detail.
18. **Depth of field:** scene bands, inspection/cinematic focus; keep gameplay,
    contacts and UI clear. Reuse shared scoped owners/settings/focus cleanup.
19. **Anti-aliasing:** actual URP/renderer/camera/target path; compare MSAA/SMAA/
    TAA on thin/moving detail, vegetation/previews. TAA needs correct motion/history.
20. **Vignette:** restrained intensity; check combined scene/state strength and
    corner/route readability, including intoxication.
21. **Film grain + dithering:** grain by style; technical dither only for proven
    real sky/fog/dark banding within native/canon constraints, before crisp UI.
    Distinguish it from retro quantization; inspect the final output.
22. **Lens flares:** justified existing bright sources, occlusion/camera response;
    respect cloudy/dark areas and narrative silence.
23. **Motion blur:** camera/genre, correct motion and readability; if used,
    provide a saved player off switch.
24. **Chromatic aberration/lens distortion:** brief justified state/optics effects;
    retain shared owners/settings and sober-state clarity.

## Implementation boundaries

Reuse owners/passes/assets/buffers. Follow AGENTS.md optimization/lifecycle
rules; bound work/loading, measure gains and label unknown costs.
Implement within scope; larger work is a proposal, no mass rollout/rewrite/
canon override. Rollout uses shared type/area profiles, spatial/range bounds
and justified exclusions. A pilot is not project-wide completion.
Baked GI/APV needs matching bake geometry/data, bindings/sampling and bounded loading;
ambient/painted/local bounce is not baked GI.
Reserve channels/UVs/topology/anchors/metas. Deformation must match active
forward/shadow/depth/normal/GBuffer/motion/meta passes; expand bounds and retain
root/rig/camera/physical contacts and pause/disable/dormancy/return/unload cleanup.
Preserve ordinary causes, local darkness/cleanliness/water/palette/silence. No
new clues/supernatural signals/text as polish. Absence is not a canon ban.

## Completion check

- Review all three groups for coverage/gaps, suitability and scoped action.
- Follow FAST/shared-framework allowance: focused check/reviewed captures for
  appearance changes; broader checks need existing AGENTS.md authorization.
- Report assessment/changes/evidence/omissions/unknown costs; update facts and
  delete temporary captures.

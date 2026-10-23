# Visual effects standard

Accepted user decision `2026-10-21`: every scene change/world/art task reviews
all three groups (24 categories), including SSR and SSGI/SCGI.
No default all-on or new approval gate.

## Audit

Covers prefab/model/material/shader/lighting/camera/environment plans,
generators and importers. Read bibles/architecture; inspect assets, owners,
composition, settings and checks. Dormant package code, shader support
or preferences do not prove an active compatible provider or receiver coverage.

Working matrix:

| Effect | Coverage | Gap/fit | Action | Cost/bound | Proof |
| --- | --- | --- | --- | --- | --- |

Assess coverage/gap/fit/improvement per category; use ai/README.md statuses,
with applicability separate. Group trivial work; record lasting facts.

## Six scene-shaping effects

1. **Global illumination:** distinguish baked GI/APV, finite source-derived
   bounce, SSGI (screen-space) and SCGI (surface cache). Assess changing sources,
   doors, material sampling, SSGI visibility/history, leaks, cache/update/memory
   bounds; replace existing bounce to avoid duplicate energy.
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

7. **Reflection probes + SSR:** receiver coverage, wetness/roughness and filtering;
   SSR blends to probes without double energy. Check offscreen/hidden
   sources, edges, motion/history/cuts; exclude preview/capture
   cameras. Bound tracing, cube captures/resolution, invalidation and lifetime.
8. **Stylized water:** depth colour, Fresnel, shoreline foam, moving normals,
   sky/local reflection and shallow caustics, each assessed separately by water type.
9. **Roughness/smoothness and wetness:** variation/causal wear; preserve
   pigment, mean response and the area's wet/dry contract.
10. **Bevels/weighted normals:** edge highlights/face stability without
    needless geometry, softened silhouettes or changed contacts.
11. **Vertex colour gradients:** subtle material/vegetation variation; inspect
    COLOR/UV channels and shader consumption.
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
19. **Anti-aliasing:** actual renderer/camera/target; compare MSAA/SMAA/TAA on
    moving detail, vegetation/previews. TAA needs correct motion/history.
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

Reuse owners/passes/assets/buffers; follow AGENTS.md optimization/lifecycle.
Measure gains; label unknown costs. Scope rollout by shared type/area profiles,
spatial bounds and exclusions. Pilots are not project-wide completion or canon
overrides. Preview providers need proven pipeline/platform/shader compatibility;
global batching/buffer changes need a focused gate, not silent activation.
Baked GI/APV needs matching geometry/data, bindings/sampling and bounded loading;
ambient/painted/local bounce is not baked GI.
Preserve channels/UVs/topology/anchors/metas. Deformation matches forward/shadow/
depth/normal/GBuffer/motion/meta passes; retain bounds, root/rig/camera/contacts
and pause/disable/dormancy/return/unload cleanup.
Preserve ordinary causes, local darkness/cleanliness/water/palette/silence. No
new clues/supernatural signals/text as polish. Absence is not a canon ban.

## Completion check

- Review all groups: coverage/gaps, fit and scoped action.
- Follow FAST: focused check/reviewed frames for appearance changes.
- Report changes/proof/omissions/unknown costs; update facts; delete captures.

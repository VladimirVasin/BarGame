#ifndef BARPROMENADE_MOUNTAIN_WIND_SWAY_INCLUDED
#define BARPROMENADE_MOUNTAIN_WIND_SWAY_INCLUDED

// The wind that reaches the conifers on the mountain road.
//
// Every crown on that road is two stacked cones merged into ONE mesh per
// layer - 92, 142 and 186 trees - so there is no transform to rotate and no
// per-tree object to animate. The bend has to happen per vertex, and it has
// to happen identically in the forward pass and in the shadow pass, or the
// shadow the car's headlights throw across the asphalt stands still under a
// crown that is moving.
//
// WHAT THE MESH CARRIES, AND WHY IT IS ALL IN UV0.
// The four passes that need this displacement do not agree on their vertex
// attributes. Their Attributes structs intersect in exactly two semantics:
//
//   LitForwardPass       positionOS normalOS tangentOS texcoord lightmapUVx2
//   ShadowCasterPass     positionOS normalOS           texcoord
//   DepthOnlyPass        position                      texcoord
//   LitDepthNormalsPass  positionOS normal   tangentOS texcoord
//
// No COLOR anywhere, and no TEXCOORD1 in three of the four. So a second UV
// set or a vertex-colour channel would silently read as zero in the shadow
// pass - which is precisely the pass that must not disagree. Everything
// therefore rides POSITION and TEXCOORD0.
//
// MountainRoadSceneryMeshFactory bakes the crown's V as the height ABOVE
// THAT TREE'S OWN BASE (not above the world origin) at the needle sheet's
// metre pitch. That one choice yields all three quantities this file needs:
//
//   aboveBase = uv.y * metresPerTile     - the lever the crown bends on
//   baseY     = positionWS.y - aboveBase - how high the tree itself stands,
//                                          which is how hard the wind blows
//   phase     = f(positionWS.xz)         - who is out of step with whom
//
// PARAMETERS RIDE GLOBALS, for the same non-negotiable reason the vertex
// snap's do: UnityPerMaterial must stay byte-identical to stock URP Lit or
// the SRP Batcher stops batching every renderer in the game. Unlike the
// snap's, these are written with a plain Shader.SetGlobalVector rather than
// on a command buffer. The snap could not afford that - it is pushed on
// EVERY camera, including the inventory preview and the fountain's cubemap
// probe, which must not inherit the gameplay camera's grid. This global is
// read by exactly one material, on geometry that exists only in the
// mountain-road scene and that neither of those cameras ever draws.
//
// An unset global reads as zero, which means strength zero, which means an
// exact passthrough - so material thumbnails and asset previews get the
// still tree for free.

// x, z: horizontal wind direction, unit length. y: strength 0..1.
// w: time in seconds (the field's own clock, not _Time).
float4 _MountainWindParams;

// x: world Y at the foot of the climb. y: world Y at its summit.
// z: metres of tip travel at full strength and full altitude, for a tree of
//    ReferenceTreeHeight. w: metres per unit of the crown's V.
float4 _MountainWindProfile;

// The height the lever is normalised against. A taller tree therefore
// travels further at its tip and a sapling barely moves, which is what a
// stand of mixed conifers actually does.
#define MOUNTAIN_WIND_REFERENCE_HEIGHT 12.0

// About 25 m, so neighbouring trees are out of step while one crown - 2.6 to
// 7.6 m across - deforms slightly instead of sliding rigidly.
#define MOUNTAIN_WIND_PHASE_PER_METRE float2(0.041, 0.037)

float3 MountainWindOffsetWS(float3 positionWS, float2 uv)
{
    float strength = _MountainWindParams.y;

    // Load-bearing early out, exactly as the snap's is: with no wind set,
    // this shader must be a bit-for-bit passthrough of stock URP Lit.
    if (!(strength > 0.0))
    {
        return (float3)0.0;
    }

    float aboveBase = max(0.0, uv.y * _MountainWindProfile.w);
    float baseY = positionWS.y - aboveBase;
    float climb = saturate(
        (baseY - _MountainWindProfile.x) /
        max(1e-3, _MountainWindProfile.y - _MountainWindProfile.x));

    // A cantilever deflects as the square of the distance from its root, so
    // the skirt at the bottom of the crown barely leaves the trunk it grows
    // out of and the tip carries the whole gesture.
    float reference = MOUNTAIN_WIND_REFERENCE_HEIGHT;
    float lever = (aboveBase * aboveBase) / (reference * reference);
    float amplitude = _MountainWindProfile.z * strength * lever *
                      lerp(0.40, 1.00, climb);

    float time = _MountainWindParams.w;
    float phase = dot(positionWS.xz, MOUNTAIN_WIND_PHASE_PER_METRE);
    float wave = sin(time * 1.35 + phase) * 0.62 +
                 sin(time * 2.90 + phase * 1.7) * 0.28;

    float3 forward = float3(_MountainWindParams.x, 0.0, _MountainWindParams.z);
    float3 lateral = float3(-forward.z, 0.0, forward.x);

    // Lean downwind and never back past upright, a cross-wind shiver on top,
    // and a small sink so the crown does not appear to stretch as it bends.
    return forward * (amplitude * (0.55 + 0.45 * wave)) +
           lateral * (amplitude * 0.22 * sin(time * 3.7 + phase * 2.3)) +
           float3(0.0, -amplitude * 0.18 * abs(wave), 0.0);
}

// Displaces through world space and back rather than assuming the crown
// batches are built at the origin. They are today; a parent that ever moves
// or scales would otherwise bend the forest sideways with no error anywhere.
void MountainWindDisplace(inout float4 positionOS, float2 uv)
{
    float3 positionWS = TransformObjectToWorld(positionOS.xyz);
    positionWS += MountainWindOffsetWS(positionWS, uv);
    positionOS.xyz = TransformWorldToObject(positionWS);
}

#endif

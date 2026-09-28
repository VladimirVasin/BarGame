#ifndef BAR_PROMENADE_GROUND_LAYERS_INCLUDED
#define BAR_PROMENADE_GROUND_LAYERS_INCLUDED

// Linear companion maps: R smoothness multiplier, G shallow relief,
// B cavity tendency; junction atlas A holds the authored footprint.
// These are measured from each existing albedo family.
TEXTURE2D(_GroundResponse); SAMPLER(sampler_GroundResponse);
TEXTURE2D(_GroundSubstrateMap); SAMPLER(sampler_GroundSubstrateMap);
TEXTURE2D(_GroundSubstrateResponse); SAMPLER(sampler_GroundSubstrateResponse);
TEXTURE2D(_GroundAsphaltMap); SAMPLER(sampler_GroundAsphaltMap);
TEXTURE2D(_GroundAsphaltResponse); SAMPLER(sampler_GroundAsphaltResponse);
TEXTURE2D(_GroundJunctionAtlas); SAMPLER(sampler_GroundJunctionAtlas);
TEXTURE2D(_GroundJunctionResponse); SAMPLER(sampler_GroundJunctionResponse);
float4 _GroundJunctionBounds[4];
float _GroundJunctions;
float _GroundKind;
float _GroundWetness;
float _GroundRoadCoordinates;
float _GroundVertexData;
float _GroundCityClimate;
float _CityGroundWetness;
half4 _GroundSubstrateColor;
half4 _GroundAsphaltColor;

float GroundHash(float2 p)
{
    float3 q = frac(float3(p.xyx) * 0.1031);
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}

float GroundNoise(float2 p)
{
    float2 cell = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(GroundHash(cell), GroundHash(cell + float2(1, 0)), f.x),
        lerp(GroundHash(cell + float2(0, 1)), GroundHash(cell + 1), f.x), f.y);
}

void ApplyGroundAsphalt(float2 plane, half upward, float4 road, float roadFootprint,
    inout half3 albedo, inout half smoothness)
{
    if (_GroundRoadCoordinates > 0.5 && road.z > 0.1)
    {
        float side = abs(road.x) / max(0.1, road.z);
        float grit = smoothstep(0.72, 1.0,
            side + (GroundNoise(plane * 2.1) - 0.5) * 0.11);
        float wheels = 1.0 - smoothstep(0.08, 0.27, abs(side - 0.47));
        float span = 14.0;
        float block = floor((road.y + road.w * span) / span);
        float selector = GroundHash(float2(block, floor(road.w * 83.0)));
        float centre = (selector - 0.5) * road.z * 1.35;
        float2 delta = float2(road.x - centre,
            frac((road.y + road.w * span) / span) * span - 6.5);
        float2 halfSize = float2(0.45 + selector * 0.7, 0.8 + selector * 1.2);
        float edge = max(abs(delta.x) - halfSize.x, abs(delta.y) - halfSize.y);
        edge += (GroundNoise(plane * 6.0) - 0.5) * 0.06;
        float aa = max(roadFootprint, 0.018);
        float patch = (1.0 - smoothstep(-aa, aa, edge)) * step(0.30, selector);
        float seam = (1.0 - smoothstep(0.025, 0.055 + aa, abs(edge))) * step(0.30, selector);
        albedo *= (1.0 - patch * 0.23 - seam * 0.13 + wheels * 0.035);
        // Same neutral aggregate, locally denser along the true kerb.
        albedo = lerp(albedo, albedo * 1.16, grit * upward);
        smoothness *= lerp(1.0, 0.35, grit);
        smoothness = lerp(smoothness, min(0.56h, smoothness * 1.12h), patch);
    }
    smoothness = min(smoothness, 0.55h);
}

void ApplyGroundLayers(float2 uv, float3 world, half3 normal,
    float4 road, half4 vertexData, inout SurfaceData surface, out half relief)
{
    half3 response = SAMPLE_TEXTURE2D(_GroundResponse, sampler_GroundResponse, uv).rgb;
    half upward = saturate(normal.y * 4.0);
    float2 plane = abs(normal.y) > 0.5 ? world.xz :
        (abs(normal.x) > abs(normal.z) ? world.zy : world.xy);
    float2 groundDx = ddx(world.xz), groundDy = ddy(world.xz);
    float roadFootprint = length(fwidth(road.xy)) + length(fwidth(plane)) * 0.36;
    half broad = GroundNoise(plane * 0.19);
    half wetness = saturate(max(_GroundWetness, _CityGroundWetness * _GroundCityClimate));
    half damp = smoothstep(0.30h, 0.75h, broad) * wetness;
    // The street registry already owns the road's tint and scalar. Other
    // city grounds share its clock on the GPU, without thousands of entries.
    if (_GroundCityClimate > 0.5)
    {
        surface.albedo *= lerp(1.0h, 0.88h, wetness);
        half wetMaximum = (_GroundKind > 3.5 && _GroundKind < 6.5) ? 0.32h : 0.20h;
        surface.smoothness = lerp(surface.smoothness, wetMaximum, wetness);
    }
    surface.albedo *= lerp(0.95h, 1.04h, broad);
    surface.smoothness *= response.r;
    surface.smoothness *= lerp(0.65h, 1.0h, damp);
    relief = (response.g - 0.5h) * 0.0015h;

    if (_GroundKind < 0.5) // Asphalt: patches and edges belong to the road frame.
    {
        ApplyGroundAsphalt(plane, upward, road, roadFootprint, surface.albedo, surface.smoothness);
    }
    else if (_GroundKind > 8.5 && _GroundKind < 9.5) // Snow: consumes actual pressing/edge data.
    {
        half pressure = vertexData.r * _GroundVertexData;
        half exposed = saturate(vertexData.g * _GroundVertexData);
        surface.albedo *= lerp(1.0h, 0.93h, pressure);
        surface.smoothness = lerp(surface.smoothness, 0.16h * response.r, pressure);
        relief *= 0.3h;
        // Deep snow has no visible substrate; skip its texture/noise work.
        [branch] if (exposed > 0.0h)
        {
            half3 soil = SAMPLE_TEXTURE2D_GRAD(_GroundSubstrateMap, sampler_GroundSubstrateMap,
                world.xz / 5.0, groundDx / 5.0, groundDy / 5.0).rgb;
            half3 soilResponse = SAMPLE_TEXTURE2D_GRAD(_GroundSubstrateResponse,
                sampler_GroundSubstrateResponse, world.xz / 5.0, groundDx / 5.0, groundDy / 5.0).rgb;
            soil *= _GroundSubstrateColor.rgb * lerp(0.95h, 1.04h, broad) *
                (1.0h - soilResponse.b * lerp(0.025h, 0.085h, damp));
            half3 asphalt = SAMPLE_TEXTURE2D_GRAD(_GroundAsphaltMap, sampler_GroundAsphaltMap,
                world.xz / 3.5, groundDx / 3.5, groundDy / 3.5).rgb *
                _GroundAsphaltColor.rgb * lerp(0.95h, 1.04h, broad);
            half3 asphaltResponse = SAMPLE_TEXTURE2D_GRAD(_GroundAsphaltResponse,
                sampler_GroundAsphaltResponse, world.xz / 3.5, groundDx / 3.5, groundDy / 3.5).rgb;
            half asphaltSmoothness = 0.045h * asphaltResponse.r * lerp(0.65h, 1.0h, damp);
            ApplyGroundAsphalt(plane, upward, road, roadFootprint, asphalt, asphaltSmoothness);
            half asphaltWeight = saturate(vertexData.b * _GroundVertexData);
            half3 substrate = lerp(soil, asphalt, asphaltWeight);
            half substrateSmoothness = lerp(0.03h * soilResponse.r * lerp(0.65h, 1.0h, damp),
                asphaltSmoothness, asphaltWeight);
            half substrateHeight = lerp(soilResponse.g, asphaltResponse.g, asphaltWeight);
            if (_GroundJunctions > 0.5)
            {
                // Sample the SAME painted atlas as the underlying terrain. Future
                // mask edits cannot make the snow reveal the old procedural mask.
                [unroll] for (int index = 0; index < 4; index++)
                {
                    float4 bounds = _GroundJunctionBounds[index];
                    float2 junctionPoint = (world.xz - bounds.xy) / max(bounds.zw, 0.001);
                    if (all(junctionPoint >= 0) && all(junctionPoint <= 1))
                    {
                        float2 atlasUv = (float2(index % 2, index / 2) * 1024.0 +
                            4.5 + junctionPoint * 1015.0) / 2048.0;
                        half3 junctionColor = SAMPLE_TEXTURE2D_LOD(_GroundJunctionAtlas, sampler_GroundJunctionAtlas, atlasUv, 0).rgb *
                            lerp(0.95h, 1.04h, broad);
                        half4 junction = SAMPLE_TEXTURE2D_LOD(_GroundJunctionResponse,
                            sampler_GroundJunctionResponse, atlasUv, 0);
                        substrate = lerp(substrate, junctionColor, junction.a);
                        substrateSmoothness = lerp(substrateSmoothness,
                            0.045h * junction.r * lerp(0.65h, 1.0h, damp), junction.a);
                        substrateHeight = lerp(substrateHeight, junction.g, junction.a);
                    }
                }
            }
            surface.albedo = lerp(surface.albedo, substrate, exposed);
            surface.smoothness = lerp(surface.smoothness, substrateSmoothness, exposed);
            relief = lerp(relief, (substrateHeight - 0.5h) * 0.0015h, exposed);
        }
    }
    else
    {
        // Existing joints/grain stay registered to their colour sheet.
        half joints = response.b * lerp(0.025h, 0.085h, damp);
        surface.albedo *= 1.0h - joints;
        if (_GroundKind > 6.5 && _GroundKind < 7.5) relief *= 0.4h; // sand
        if (_GroundKind > 2.5 && _GroundKind < 3.5) relief *= 0.5h; // lawn
        if (_GroundKind > 9.5) relief = 0; // paint / soft silt
    }
    surface.metallic = 0;
}

half3 GroundReliefNormal(float3 world, half3 normal, half relief)
{
    float3 dx = ddx(world), dy = ddy(world);
    float3 rx = cross(dy, normal), ry = cross(normal, dx);
    float determinant = dot(dx, rx);
    float3 gradient = (ddx(relief) * rx + ddy(relief) * ry) /
        (abs(determinant) > 1e-8 ? determinant : 1.0);
    // Keep subpixel grain quiet in the PS1 image and at grazing angles.
    gradient *= saturate(1.0 - max(length(dx), length(dy)) * 1.5);
    gradient /= max(1.0, length(gradient) / 0.10);
    return normalize(normal - gradient);
}
#endif

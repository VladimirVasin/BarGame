#ifndef BAR_PROMENADE_CITY_GROUND_JOINTS_INCLUDED
#define BAR_PROMENADE_CITY_GROUND_JOINTS_INCLUDED

// Packed spatial bins and five-vector recipes owned by one City ground
// composition. Coordinates are in that composition's frame, including Home.
StructuredBuffer<float4> _CityJointData;
// Ground response and both arrays repeat and filter the same way. Sharing
// the existing sampler keeps the fully lit Metal variant within 16 slots.
TEXTURE2D_ARRAY(_CityJointColor);
TEXTURE2D_ARRAY(_CityJointResponse);
float4 _CityJointGrid;
float4x4 _CityJointFrame;
float _CityJointCellsPerMetre;

struct CityJointSample
{
    half3 color;
    half smoothness;
    half relief;
    half receiver;
    half fallback;
};

CityJointSample CityJointRead(uint record, float2 position, float2 dx, float2 dy,
    half weather, half broad)
{
    float4 u = _CityJointData[record], v = _CityJointData[record + 1];
    float4 dry = _CityJointData[record + 2], wet = _CityJointData[record + 3];
    float4 physics = _CityJointData[record + 4];
    float2 uv = float2(dot(position, u.xy) + u.z, dot(position, v.xy) + v.z);
    float2 uvDx = float2(dot(dx, u.xy), dot(dx, v.xy));
    float2 uvDy = float2(dot(dy, u.xy), dot(dy, v.xy));
    if (v.w > .5) uv.y = GroundClampedV(uv.y, abs(uvDx.y) + abs(uvDy.y));
    half3 response = SAMPLE_TEXTURE2D_ARRAY_GRAD(_CityJointResponse, sampler_GroundResponse,
        uv, u.w, uvDx, uvDy).rgb;
    half4 pigment = lerp(dry, wet, weather * physics.y);
    half film = weather * max(physics.x, physics.y);
    half damp = smoothstep(.30h, .75h, broad) * film;
    half climate = weather * physics.x;
    half maximum = physics.w > 3.5h && physics.w < 6.5h ? .32h : .20h;
    CityJointSample result;
    result.color = SAMPLE_TEXTURE2D_ARRAY_GRAD(_CityJointColor, sampler_GroundResponse,
        uv, u.w, uvDx, uvDy).rgb * pigment.rgb * lerp(1.h, .88h, climate) * lerp(.95h, 1.04h, broad);
    if (physics.w > .5h) result.color *= 1.h - response.b * lerp(.025h, .085h, damp);
    result.smoothness = lerp(pigment.a, maximum, climate) * response.r * lerp(.65h, 1.h, damp);
    if (physics.w < .5h) result.smoothness = min(.55h, result.smoothness);
    result.relief = physics.w > 9.5h ? 0.h :
        (response.g - .5h) * .0015h * (physics.w > 6.5h && physics.w < 7.5h ? .4h : 1.h);
    result.receiver = weather * physics.z;
    result.fallback = lerp(1.h, result.receiver, physics.z);
    return result;
}

void CityJointAccumulate(uint a, uint b, float weight, float2 p, float2 dx, float2 dy,
    half weather, half broad, inout float3 pigment, inout float4 response)
{
    CityJointSample x = CityJointRead(a, p, dx, dy, weather, broad);
    CityJointSample y = CityJointRead(b, p, dx, dy, weather, broad);
    pigment += (x.color + y.color) * weight;
    response += float4(x.smoothness + y.smoothness, x.relief + y.relief,
        x.receiver + y.receiver, x.fallback + y.fallback) * weight;
}

void ApplyCityGroundJoints(float3 world, half3 normal, inout SurfaceData surface,
    inout half relief, inout half receiver, inout half fallback)
{
    float3 p = mul(_CityJointFrame, float4(world, 1.f)).xyz;
    float2 dx = ddx(p.xz), dy = ddy(p.xz);
    [branch] if (_CityJointCellsPerMetre < .25 || _CityJointPreviewOff > .5 ||
        (_GroundKind >= 8.5 && _GroundKind < 10.5) || normal.y < .2h) return;
    int2 cell = int2(floor(p.xz * _CityJointCellsPerMetre) - _CityJointGrid.xy);
    if (any(cell < 0) || any(cell >= int2(_CityJointGrid.zw))) return;
    float4 header = _CityJointData[cell.y * (int)_CityJointGrid.z + cell.x];
    uint count = (uint)header.y;
    [branch] if (count == 0) return;
    uint start = (uint)header.x, payload = (uint)header.z;
    half weather = saturate(_CityGroundWetness);
    half broad = GroundNoise(world.xz * .19);
    float edgeVariation = lerp(.76, 1.19, GroundNoise(p.xz * 2.7) * .7 + GroundNoise(p.xz * 7.9) * .3);
    float envelope = 0, total = 0, pendingWeight = 0;
    uint pendingA = 0, pendingB = 0;
    float3 pigment = 0;
    float4 response = 0, packed = 0;
    [loop] for (uint i = 0; i < count; i++)
    {
        if ((i & 3u) == 0u) packed = _CityJointData[start + (i >> 2u)];
        uint address = payload + (uint)packed[i & 3u];
        float4 a = _CityJointData[address], b = _CityJointData[address + 1u];
        float2 edge = b.xz - a.xz;
        float t = saturate(dot(p.xz - a.xz, edge) / max(1e-8, dot(edge, edge)));
        float3 nearest = lerp(a.xyz, b.xyz, t);
        float3 delta = p - nearest;
        // A true seam always evaluates to one. Noise breaks the shoulder of
        // the band while both faces evaluate the same value at the contact.
        float distance = length(delta.xz) / a.w;
        if (distance >= 1) continue;
        float4 slopes = _CityJointData[address + 3u];
        float height = min(abs(delta.y - dot(slopes.xy, delta.xz)),
            abs(delta.y - dot(slopes.zw, delta.xz)));
        float weight = (1 - smoothstep(.015, .048, height)) *
            (1 - smoothstep(0, 1, distance * lerp(edgeVariation, 1, distance)));
        if (weight <= .0001) continue;
        envelope = max(envelope, weight);
        float4 info = _CityJointData[address + 2u];
        weight *= min(1, info.y / a.w);
        uint r0 = payload + (uint)b.w, r1 = payload + (uint)info.x;
        uint first = min(r0, r1), last = max(r0, r1);
        if (pendingWeight > 0 && (pendingA != first || pendingB != last))
        {
            CityJointAccumulate(pendingA, pendingB, pendingWeight, p.xz, dx, dy, weather, broad, pigment, response);
            pendingWeight = 0;
        }
        pendingA = first; pendingB = last;
        pendingWeight += weight;
        total += 2 * weight;
    }
    if (total <= 0) return;
    CityJointAccumulate(pendingA, pendingB, pendingWeight, p.xz, dx, dy, weather, broad, pigment, response);
    response /= total;
    surface.albedo = lerp(surface.albedo, pigment / total, envelope);
    surface.smoothness = lerp(surface.smoothness, response.x, envelope);
    relief = lerp(relief, response.y, envelope);
    receiver = lerp(receiver, response.z, envelope);
    fallback = lerp(fallback, response.w, envelope);
}
#endif

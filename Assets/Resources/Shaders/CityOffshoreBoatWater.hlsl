// Finite working-light patch and stern wash on the sea's own displaced surface.
// Inputs default to zero on every non-sea material. The island owns other slots.
half3 OffshoreBoatWater(float3 positionWS, half3 normalWS, float3 viewDir,
    float4 hull, float4 course, float4 lamp, float4 beam)
{
    if (hull.w <= 0 && lamp.w <= 0) return 0;
    float cameraDistance = distance(_WorldSpaceCameraPos, positionWS);
    float fade = 1 - smoothstep(42.0, 47.4, cameraDistance);
    float2 delta = positionWS.xz - hull.xz;
    float aft = -dot(delta, course.xy) - course.z;
    float across = abs(dot(delta, float2(-course.y, course.x)));
    float width = 0.20 + saturate(aft / 5.5) * 0.55;
    float wash = smoothstep(0.0, 0.7, aft) * (1 - smoothstep(2.0, 5.5, aft)) *
        (1 - smoothstep(width * 0.25, width, across));
    float breakup = saturate(0.46 + sin(positionWS.x * 7.2 + positionWS.z * 4.7 - _Time.y * 1.8) * 0.3 +
        sin(positionWS.z * 11.1 - _Time.y * 1.4) * 0.22);
    half3 result = half3(0.16h, 0.18h, 0.16h) * wash * breakup * hull.w * 0.22h;

    float3 fromLamp = positionWS - lamp.xyz;
    float lengthToLamp = length(fromLamp);
    float alignment = dot(fromLamp / max(lengthToLamp, 0.01), beam.xyz);
    float cone = smoothstep(0.980, 0.996, alignment);
    float reach = 1 - smoothstep(beam.w * 0.72, beam.w, lengthToLamp);
    float3 halfway = normalize(-fromLamp / max(lengthToLamp, 0.01) + viewDir);
    float glimmer = 0.24 + 0.76 * pow(saturate(dot(normalWS, halfway)), 10);
    result += half3(0.46h, 0.30h, 0.12h) * cone * reach * glimmer *
        (0.55 + 0.45 * breakup) * lamp.w * 0.34;
    // Already hazed presentation light, like the visible cone overhead. It has
    // a short physical reach, and never lights the shore or adds a real Light.
    return result * fade;
}

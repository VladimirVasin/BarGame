#ifndef BAR_PROMENADE_CITY_WIND_FIELD_INCLUDED
#define BARPROMENADE_CITY_WIND_FIELD_INCLUDED

float4 _CityWind;
float4 _CityPreviousWind;
float4 _CityWindProfile;
float4x4 _CityWindWorldToCity;

float3 CityWindCoordinates(float3 positionWS)
{
    return mul(_CityWindWorldToCity, float4(positionWS, 1)).xyz;
}

float CityWindStrength(float strength, float height)
{
    float climb = smoothstep(0, 1, (height - _CityWindProfile.x) * _CityWindProfile.y);
    // Approach the authored maximum without flattening every crest gust at a hard clamp.
    float exposed = 1 - pow(1 - saturate(strength), 1 + _CityWindProfile.z);
    return lerp(strength, exposed, climb * climb);
}

float CityWindPhase(float2 cityXZ)
{
    return dot(cityXZ, float2(.17, .11));
}

float CityWindPulse(float phase, float time)
{
    return .62 + .24 * sin(time * 1.5 + phase) + .14 * sin(time * 3.1 + phase * 1.7);
}

#endif

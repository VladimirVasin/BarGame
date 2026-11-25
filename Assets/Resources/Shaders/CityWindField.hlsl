#ifndef BAR_PROMENADE_CITY_WIND_FIELD_INCLUDED
#define BARPROMENADE_CITY_WIND_FIELD_INCLUDED

float4 _CityWind;
float4 _CityPreviousWind;
float4x4 _CityWindWorldToCity;

float3 CityWindCoordinates(float3 positionWS)
{
    return mul(_CityWindWorldToCity, float4(positionWS, 1)).xyz;
}

float CityWindPhase(float2 cityXZ)
{
    return dot(cityXZ, float2(.17, .11));
}

#endif
